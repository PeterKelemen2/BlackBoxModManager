using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Thrown when a deploy or a revert stops. The game directory is untouched unless the
	/// message says otherwise.
	/// </summary>
	public sealed class DeployServiceException : Exception
	{
		public DeployServiceException(string message, Exception inner = null) : base(message, inner) { }
	}

	/// <summary>
	/// What one deploy produced.
	/// </summary>
	public sealed class DeployResult
	{
		public DeployReport Report { get; }

		public VerificationResult Verification { get; }

		public ReplicationReport Staging { get; }

		/// <summary>What the conflict check found before the deploy wrote anything.</summary>
		public ConflictReport Conflicts { get; }

		public DeployResult(DeployReport report, VerificationResult verification, ReplicationReport staging,
			ConflictReport conflicts)
		{
			this.Report = report;
			this.Verification = verification;
			this.Staging = staging;
			this.Conflicts = conflicts;
		}
	}

	/// <summary>
	/// Runs one deploy from end to end.
	///
	/// The order is fixed and every step depends on the one before it.
	///
	/// 1. Take the vanilla snapshot, once, before the first deploy.
	/// 2. Build the staging copy from the vanilla copy.
	/// 3. Let each engine put its mods into the staging copy, in load order.
	/// 4. Verify the staging copy.
	/// 5. Swap the staging copy into the game directory.
	///
	/// <b>No step writes into the game directory.</b> Only the swap changes it, and only
	/// after the verify passes.
	///
	/// Run one deploy at a time, on one background thread. The library statics of Nikki are
	/// process-global, and step 6 loads containers inside this same flow. See defect 8.
	/// </summary>
	public sealed class DeployService
	{
		private readonly ModStore _store;
		private readonly IReadOnlyList<IDeployEngine> _engines;
		private readonly string _workRootOverride;
		private readonly BinaryInstall _binary;

		/// <summary>
		/// The engines in the order that a deploy runs them.
		///
		/// The link engine runs first. It puts drop-in files into the staging copy, and one
		/// of those files can be a container that the container engine then loads. The
		/// reverse order would load the container of the game and then overwrite the result.
		///
		/// The Binary kind goes to one router, because two engines apply it and this class
		/// groups the mods by kind. See BinaryRouteEngine.
		/// </summary>
		public DeployService(ModStore store, BinaryInstall binary = null, string workRootOverride = null)
			: this(store, new IDeployEngine[] { new LinkDeployEngine(), new BinaryRouteEngine() },
				binary, workRootOverride) { }

		public DeployService(ModStore store, IReadOnlyList<IDeployEngine> engines,
			BinaryInstall binary = null, string workRootOverride = null)
		{
			this._store = store ?? throw new ArgumentNullException(nameof(store));
			this._engines = engines ?? throw new ArgumentNullException(nameof(engines));
			this._binary = binary;
			this._workRootOverride = workRootOverride;
		}

		public GameWorkspace WorkspaceOf(GameInstall install) => new GameWorkspace(install, this._workRootOverride);

		/// <summary>
		/// Applies one profile to one game install.
		/// </summary>
		public DeployResult Deploy(GameInstall install, Profile profile, bool fullVerify = false,
			Action<string> log = null, CancellationToken cancellation = default)
		{
			if (install is null) throw new ArgumentNullException(nameof(install));
			if (profile is null) throw new ArgumentNullException(nameof(profile));

			Action<string> write = log ?? (line => { });
			GameWorkspace workspace = this.WorkspaceOf(install);
			var timing = new DeployTiming();

			if (timing.CountsCompression)
			{
				write($"{DeployTiming.CompressionSwitch} is on. The native compressor counts every call.");
			}

			if (!workspace.SharesVolumeWithGame())
			{
				write($"The workspace {workspace.Root} sits on another volume than the game. " +
					"Every build and every swap copies every byte.");
			}

			// Ask for the rights before anything reads or copies a file. The swap is the only
			// step that needs them and it is the last step, so without this check a deploy does
			// every minute of its work and then fails. See AccessPreflight.
			AccessPreflight.Check(workspace, write);

			// Decide the route of every Binary mod before anything reads or copies a file. The
			// staging copy depends on the answer, and every later reader must get the same
			// answer. See BinaryRoutePlan.
			BinaryRoutePlan routes = BinaryRoutePlan.Build(profile, this._store);

			if (routes.ModIds.Count > 0)
			{
				write(routes.ToString());

				foreach (string line in routes.Describe(this._store)) write(line);
			}

			// Read the variants and resolve every script one time. The conflict check and the
			// command gate both need them, and each resolve reads every file of the append
			// graph. One real mod appends 158 files.
			IReadOnlyList<EnabledVariant> variants;
			var scripts = new ScriptResolutionCache(workspace.StagingDirectory);

			using (timing.Measure("read the variants"))
			{
				variants = VariantReader.Read(profile, this._store, install.Game, write);
			}

			cancellation.ThrowIfCancellationRequested();

			// Read the conflicts first. The check writes nothing, and a user who sees the
			// list before the deploy can still change the load order.
			ConflictReport conflicts;

			using (timing.Measure("check the conflicts"))
			{
				conflicts = ConflictPreflight.Run(variants, workspace.StagingDirectory, write, scripts, routes);
			}

			cancellation.ThrowIfCancellationRequested();

			// Settle the ASI loader before anything writes. A contest with no stored answer
			// stops the deploy here, where the game directory is still untouched.
			ProxyPlan proxies = this.PlanLoaders(profile);
			IReadOnlyList<LoaderChoice> loaders = LoaderPreflight.Settle(proxies, write);

			VanillaSnapshot snapshot;

			using (timing.Measure("record the vanilla copy"))
			{
				snapshot = this.EnsureVanilla(workspace, write);
			}

			using (timing.Measure("check the baseline"))
			{
				CheckBaseline(workspace, snapshot, fullVerify, write);
			}

			cancellation.ThrowIfCancellationRequested();

			write("Build the staging copy.");
			ReplicationReport staging;

			// A hard link shares its content with the vanilla copy and with the live install.
			// The container engine breaks that share for every file that it writes, because it
			// reads the script and knows the list. Binary reads nothing to us and writes where
			// it wants, so the only safe answer is a copy that shares nothing. See defect 16.
			bool linkFiles = !routes.UsesCli;

			if (!linkFiles)
			{
				write($"The staging copy holds a private copy of every file, because {routes.CliCount} " +
					"mods deploy through Binary. Binary writes in place, and a hard link would reach " +
					"the vanilla copy and the game.");
			}

			using (timing.Measure("build the staging copy"))
			{
				staging = TreeReplicator.Build(
					workspace.VanillaDirectory, workspace.StagingDirectory, write, linkFiles);
			}

			var context = new DeployContext(
				install, workspace.StagingDirectory, profile, this._store, this._binary, write, proxies,
				workspace.VanillaDirectory, snapshot, variants, scripts, timing, cancellation, routes);

			DeployReport report;

			try
			{
				using (timing.Measure("run the engines"))
				{
					report = this.RunEngines(context, profile, write, loaders);
				}
			}
			catch (OperationCanceledException)
			{
				timing.Write(write);

				write("The deploy stopped because the user canceled it. The game directory did not change, " +
					"and the staging copy holds the part that ran.");

				throw;
			}
			finally
			{
				write($"The scripts resolved {scripts.Misses} times and answered {scripts.Hits} " +
					"more requests from the cache.");
			}

			VerificationResult verification;

			using (timing.Measure("verify the staging copy"))
			{
				verification = StagingVerifier.Verify(
					workspace.StagingDirectory, snapshot, report, this._store, fullVerify, write);
			}

			if (!verification.IsClean)
			{
				timing.Write(write);

				throw new DeployServiceException(
					$"The verify found {verification.Problems.Count} problems, so the swap did not run. " +
					$"The game directory did not change. The first problem is: {verification.Problems[0]}");
			}

			using (timing.Measure("swap the game directory"))
			{
				GameSwap.Swap(workspace, workspace.StagingDirectory, write);
			}

			workspace.WriteState(new WorkspaceState
			{
				DeployedProfile = profile.Name,
				Deployed = DateTimeOffset.UtcNow,
				DeployedFileCount = report.FileCount,
				DeployedFingerprint = ProfileFingerprint.Of(profile),
			});

			timing.Write(write);

			write(report.Summary());
			write("The deploy finished.");

			return new DeployResult(report, verification, staging, conflicts);
		}

		/// <summary>
		/// Reports the fields that the enabled variants disagree about. It writes nothing, so
		/// the UI can call it whenever the selection changes.
		/// </summary>
		public ConflictReport CheckConflicts(GameInstall install, Profile profile, Action<string> log = null)
		{
			if (install is null) throw new ArgumentNullException(nameof(install));
			if (profile is null) throw new ArgumentNullException(nameof(profile));

			IReadOnlyList<EnabledVariant> variants = VariantReader.Read(
				profile, this._store, install.Game, null);

			// The staging directory need not exist yet. The sandbox test compares paths and
			// it reads no file.
			return ConflictPreflight.Run(variants, this.WorkspaceOf(install).StagingDirectory, log,
				null, BinaryRoutePlan.Build(profile, this._store));
		}

		/// <summary>
		/// Puts the vanilla state back into the game directory.
		/// </summary>
		public void Revert(GameInstall install, Action<string> log = null)
		{
			if (install is null) throw new ArgumentNullException(nameof(install));

			Action<string> write = log ?? (line => { });
			GameWorkspace workspace = this.WorkspaceOf(install);

			if (!workspace.HasVanilla)
			{
				throw new DeployServiceException(
					$"The workspace {workspace.Root} holds no vanilla copy, so a revert has nothing to restore. " +
					"This application has never deployed to this install.");
			}

			// A revert swaps too, so it needs the same rights as a deploy.
			AccessPreflight.Check(workspace, write);

			// Build the replacement first. A revert must never move the vanilla copy
			// itself, because a failure would then leave no baseline.
			write("Build the vanilla copy for the swap.");
			TreeReplicator.Build(workspace.VanillaDirectory, workspace.StagingDirectory, write);

			GameSwap.Swap(workspace, workspace.StagingDirectory, write);

			workspace.WriteState(new WorkspaceState());

			write("The game directory holds the vanilla state again.");
		}

		/// <summary>
		/// Reads the vanilla state of the install, once. Later deploys reuse the result.
		///
		/// A snapshot of an install that already carries mods would record those mods as the
		/// vanilla state. The state file answers that question, and a first run against a
		/// modded install has no way to know. Say so, and let the user decide.
		/// </summary>
		public VanillaSnapshot EnsureVanilla(GameWorkspace workspace, Action<string> log = null)
		{
			if (workspace is null) throw new ArgumentNullException(nameof(workspace));

			Action<string> write = log ?? (line => { });

			if (workspace.HasVanilla)
			{
				VanillaSnapshot stored = workspace.ReadSnapshot();

				if (stored != null) return stored;

				write("The snapshot file did not read. Take the snapshot again from the vanilla copy.");

				VanillaSnapshot rebuilt = SnapshotReader.Create(workspace.VanillaDirectory, write);
				SnapshotReader.Save(workspace.SnapshotFile, rebuilt);

				return rebuilt;
			}

			workspace.Create();

			write($"Read the vanilla state of {workspace.Install.Root}. This runs once.");
			VanillaSnapshot snapshot = SnapshotReader.Create(workspace.Install.Root, write);

			write("Build the vanilla copy.");
			TreeReplicator.Build(workspace.Install.Root, workspace.VanillaDirectory, write);

			// Point the snapshot at the copy. Every later compare runs against a directory
			// that this application owns.
			snapshot.Root = workspace.VanillaDirectory;
			SnapshotReader.Save(workspace.SnapshotFile, snapshot);

			workspace.WriteState(new WorkspaceState());

			return snapshot;
		}

		/// <summary>
		/// Stops the deploy when the vanilla copy no longer holds what the snapshot recorded.
		///
		/// The quick check compares the length of every file. It costs one directory walk, and
		/// it catches a file that a past deploy rewrote or truncated. The full check reads
		/// every byte of the install, so the user turns it on when a deploy gave a result that
		/// nobody can explain.
		///
		/// The container engine reads the content of the files that it writes, whatever this
		/// setting says. That is the small list where the exact answer matters.
		/// </summary>
		private static void CheckBaseline(GameWorkspace workspace, VanillaSnapshot snapshot,
			bool fullVerify, Action<string> write)
		{
			write(fullVerify
				? "Check the vanilla copy against its record. The full check reads every byte."
				: "Check the vanilla copy against its record.");

			IReadOnlyList<SnapshotDifference> drift = SnapshotReader.Compare(
				snapshot, workspace.VanillaDirectory, fullVerify);

			if (drift.Count == 0) return;

			throw new DeployServiceException(BaselineVerifier.Describe(drift));
		}

		/// <summary>
		/// Lets each engine deploy the mods that it claims, in load order, and joins the
		/// reports.
		/// </summary>
		private DeployReport RunEngines(DeployContext context, Profile profile, Action<string> write,
			IReadOnlyList<LoaderChoice> loaders = null)
		{
			var files = new List<DeployedFile>();
			var overrides = new List<DeployOverride>();
			var methods = new Dictionary<LinkKind, int>();
			var containers = new List<ContainerWrite>();
			var settings = new List<SettingsWrite>();
			var scriptWrites = new List<ScriptWrite>();
			string note = String.Empty;

			IReadOnlyList<InstalledMod> enabled = this.ResolveEnabled(profile);

			if (enabled.Count == 0)
			{
				write("The profile enables no mod. The staging copy holds the vanilla state.");
				return new DeployReport(files, overrides, methods, note);
			}

			// Group the mods by engine and keep the load order inside each group. A mod
			// whose kind no engine claims stops the deploy.
			foreach (IDeployEngine engine in this._engines)
			{
				var mine = new List<InstalledMod>();

				foreach (InstalledMod mod in enabled)
				{
					if (engine.Kinds.Contains(mod.Kind)) mine.Add(mod);
				}

				if (mine.Count == 0) continue;

				write($"The {engine.Name} deploys {mine.Count} mods.");
				DeployReport report = engine.Deploy(context, mine);

				files.AddRange(report.Files);
				overrides.AddRange(report.Overrides);
				containers.AddRange(report.Containers);
				settings.AddRange(report.Settings);
				scriptWrites.AddRange(report.ScriptWrites);

				foreach (KeyValuePair<LinkKind, int> entry in report.Methods)
				{
					methods[entry.Key] = methods.TryGetValue(entry.Key, out int count)
						? count + entry.Value
						: entry.Value;
				}

				if (note.Length == 0) note = report.MethodNote;
			}

			return new DeployReport(files, overrides, methods, note, containers, settings, loaders,
				scriptWrites, context.Routes);
		}

		/// <summary>
		/// Reads which mod supplies each ASI loader file. It writes nothing, so the window can
		/// call it whenever the selection changes.
		///
		/// A mod that the store no longer holds is not an error here. <c>ResolveEnabled</c>
		/// reports that case with a message that names the mod, and this call runs first.
		/// </summary>
		public ProxyPlan PlanLoaders(Profile profile)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));

			var mods = new List<InstalledMod>();

			foreach (string id in profile.EnabledInOrder())
			{
				InstalledMod mod = this._store.Find(id);

				if (mod != null) mods.Add(mod);
			}

			return LoaderPreflight.Plan(profile, mods);
		}

		/// <summary>
		/// Reads the enabled mods of a profile out of the store, in load order.
		/// </summary>
		private IReadOnlyList<InstalledMod> ResolveEnabled(Profile profile)
		{
			var found = new List<InstalledMod>();
			var unclaimed = new List<string>();

			foreach (string id in profile.EnabledInOrder())
			{
				InstalledMod mod = this._store.Find(id);

				if (mod is null)
				{
					throw new DeployServiceException(
						$"The profile \"{profile.Name}\" enables the mod \"{id}\", which the store no longer holds. " +
						"Remove the mod from the profile, then deploy again.");
				}

				bool claimed = false;

				foreach (IDeployEngine engine in this._engines)
				{
					if (!engine.Kinds.Contains(mod.Kind)) continue;

					claimed = true;
					break;
				}

				if (!claimed)
				{
					unclaimed.Add($"\"{mod.Name}\" of kind {mod.Kind}");
					continue;
				}

				found.Add(mod);
			}

			if (unclaimed.Count > 0)
			{
				throw new DeployServiceException(
					$"The profile \"{profile.Name}\" enables {String.Join(", ", unclaimed)}. " +
					"No engine in this build deploys that kind. Switch those mods off, then deploy again.");
			}

			return found;
		}
	}
}
