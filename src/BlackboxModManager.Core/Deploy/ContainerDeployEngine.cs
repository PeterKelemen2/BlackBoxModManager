using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using Endscript.Commands;
using Endscript.Core;
using Endscript.Helpers;
using Endscript.Interfaces;
using Endscript.Profiles;
using Nikki.Core;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Applies Binary mods to the containers of the staging copy.
	///
	/// <b>One pass for each variant, in load order.</b> Each pass builds a new BaseProfile,
	/// loads the containers that the manifest of that one variant names, runs the script of
	/// that variant, and saves. The next pass reads what the last pass wrote. The edits
	/// composite through the disk, so no mod overwrites the container of another mod. This
	/// is what Binary 2.8.3 does, and every Binary mod is written for it.
	///
	/// <b>One shared profile for every mod does not work.</b> The command <c>delete</c>
	/// saves a container and then removes it from the profile. A mod that ends with
	/// <c>delete</c> leaves the next mod with no container to edit, and the next mod fails
	/// with "was never loaded". See defect 18.
	///
	/// <b>Never call Load twice on one profile.</b> Load adds a container per call, and Save
	/// then writes one file twice from two states. See defect 6. Each pass gets a new
	/// profile, so that rule holds.
	///
	/// The whole loop holds LibraryGate and runs on the calling thread. The hash list
	/// statics are process-global and LoadHashList resets the key map of Nikki. See defect 8.
	/// </summary>
	public sealed class ContainerDeployEngine : IDeployEngine
	{
		public string Name => "container engine";

		public IReadOnlySet<ModKind> Kinds { get; } = new HashSet<ModKind> { ModKind.Binary };

		public DeployReport Deploy(DeployContext context, IReadOnlyList<InstalledMod> mods)
		{
			if (context is null) throw new ArgumentNullException(nameof(context));
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			if (context.Binary is null)
			{
				throw new DeployServiceException(
					"A Binary mod edits the containers of the game, and that needs the hash lists of a " +
					"Binary 2.8.3 install. Set the Binary install directory, then deploy again.");
			}

			if (!Directory.Exists(context.StagingDirectory))
			{
				throw new DeployServiceException(
					$"The staging directory {context.StagingDirectory} does not exist.");
			}

			// The live install must never receive a write. Prove it here, where the message
			// can still name the cause.
			if (FileTree.IsSameOrInside(context.StagingDirectory, context.Game.Root))
			{
				throw new DeployServiceException(
					$"The staging directory {context.StagingDirectory} is the game install, or sits inside it. " +
					"A deploy writes only to a staging copy.");
			}

			GameINT game = context.Game.Game;

			IReadOnlyList<EnabledVariant> variants = VariantReader.Read(
				context.Profile, context.Store, game, context.Log);

			if (variants.Count == 0)
			{
				context.Log("No variant of a Binary mod is on. The container engine has nothing to do.");
				return new DeployReport(null, null, null, null);
			}

			// Classify every command before anything writes. A refused command and a path
			// outside the staging copy both stop the deploy here. See step 8.
			GateResult gate = CommandGate.Check(variants, context.StagingDirectory, context.Log);

			// The union covers every container that any enabled mod loads. The engine loads
			// one variant at a time, and it uses this union for two other jobs. It makes
			// every container private before the first pass, and it reports the containers
			// that the deploy rewrote.
			MergedLoad merged = MergedLaunch.Build(variants, context.StagingDirectory, strict: false);

			foreach (string note in merged.Notes) context.Log(note);

			context.Log($"The enabled mods name {merged.Files.Count} containers: {String.Join(", ", merged.Files)}.");

			this.Prepare(context, merged, gate);

			// One gate covers the statics, every load, every script, and every save.
			using (LibraryGate.Enter())
			{
				return this.RunPasses(context, merged, variants, gate);
			}
		}

		/// <summary>
		/// Makes every container that the deploy can write private, and confirms that each
		/// container of the merged load exists.
		///
		/// <b>This is the step that protects the install of the user.</b> TreeReplicator
		/// builds the staging copy with hard links, so a staging container, the vanilla
		/// container, and the live container are one file with three names. Nikki writes a
		/// container with FileMode.Create, which keeps the share and reaches all three names.
		/// MakePrivate breaks the share first.
		///
		/// <b>The manifest list is not the whole list.</b> A script creates a container with
		/// <c>new</c> and writes it back with <c>delete</c>, and no manifest names that
		/// container. A script also writes a file that is no container at all. The command
		/// <c>unlock_memory</c> writes a header over five memory files, and <c>move_file</c>
		/// and <c>copy_file</c> write a target. The gate reads every command and reports both
		/// lists. Cover them, or a mod of this shape rewrites the vanilla baseline and the
		/// game of the user. See defect 16.
		/// </summary>
		private void Prepare(DeployContext context, MergedLoad merged, GateResult gate)
		{
			CheckBaseline(context, merged, gate);

			var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int privateCount = 0;

			foreach (string file in merged.Files)
			{
				string path = ModPath.Resolve(context.StagingDirectory, file);

				if (!File.Exists(path))
				{
					// CheckFiles throws for this from inside the library, and its message
					// names the path and no mod.
					string owners = String.Join(", ", merged.Contributors[file]);

					throw new DeployServiceException(
						$"The mods {owners} need the container {file} and the game does not hold it. " +
						$"The deploy looked at {path}.");
				}

				StagingFiles.MakePrivate(path);
				done.Add(Path.GetFullPath(path));
				++privateCount;
			}

			int extra = 0;

			foreach (string file in gate.Containers)
			{
				string path = ModPath.Resolve(context.StagingDirectory, file);

				if (!done.Add(Path.GetFullPath(path))) continue;

				// A container that does not exist yet is normal here. The command "new
				// override" creates one, and a new file shares nothing.
				if (!File.Exists(path)) continue;

				StagingFiles.MakePrivate(path);
				++privateCount;
				++extra;
			}

			int files = 0;

			foreach (string path in gate.WritePaths)
			{
				if (!done.Add(Path.GetFullPath(path))) continue;

				// The gate already proved that this path stays inside the staging copy. A
				// path that does not exist yet needs no call, because a new file shares
				// nothing.
				if (!File.Exists(path)) continue;

				StagingFiles.MakePrivate(path);
				++privateCount;
				++files;
			}

			context.Log($"The staging copy holds {privateCount} private files, " +
				"so a write cannot reach the vanilla copy or the game.");

			if (extra > 0)
			{
				context.Log($"  {extra} of them are containers that a script creates and no manifest names.");
			}

			if (files > 0)
			{
				context.Log($"  {files} of them are files that a filesystem command writes.");
			}
		}

		/// <summary>
		/// Stops the deploy when the vanilla copy no longer holds what the snapshot recorded.
		///
		/// <b>A deploy against a changed baseline gives a wrong result.</b> Every Binary mod
		/// reads the container before it writes it. A container that already carries the edits
		/// of a past run makes the script report "already exists" for every name that it adds,
		/// and the user cannot see why.
		///
		/// The check covers the files that this deploy writes, and it reads their content. A
		/// hash of the whole install costs seconds for every deploy and answers nothing more
		/// about these files.
		/// </summary>
		private static void CheckBaseline(DeployContext context, MergedLoad merged, GateResult gate)
		{
			if (context.Baseline is null || String.IsNullOrWhiteSpace(context.VanillaDirectory)) return;

			var paths = new List<string>(merged.Files.Count + gate.Containers.Count + gate.WritePaths.Count);

			paths.AddRange(merged.Files);
			paths.AddRange(gate.Containers);

			foreach (string full in gate.WritePaths)
			{
				string relative = BaselineVerifier.RelativeTo(context.StagingDirectory, full);

				if (relative != null) paths.Add(relative);
			}

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				context.Baseline, context.VanillaDirectory, paths);

			if (drift.Count > 0) throw new DeployServiceException(BaselineVerifier.Describe(drift));

			context.Log("The vanilla copy still matches its record for every file that this deploy writes.");
		}

		/// <summary>
		/// Runs one load, apply and save pass for each variant, in load order.
		/// </summary>
		private DeployReport RunPasses(DeployContext context, MergedLoad merged,
			IReadOnlyList<EnabledVariant> variants, GateResult gate)
		{
			GameINT game = context.Game.Game;

			// Nikki writes MainLog.txt into the current directory. Point that at our own
			// data before any container work. See defect 9.
			string before = Directory.GetCurrentDirectory();
			Directory.CreateDirectory(AppPaths.LogDirectory);
			Directory.SetCurrentDirectory(AppPaths.LogDirectory);

			try
			{
				// Both statics must hold a value before the first Load. Load calls
				// LoadHashList first, and Save writes CustomHashList as its last step. The
				// statics are process-global, so one call covers every pass. See defect 7.
				ProfileHashLists.Apply(context.Binary, game);

				(string main, string custom) = ProfileHashLists.Current(game);
				context.Log($"The main hash list is {main}.");
				context.Log($"The custom hash list is {custom}.");

				// The gate reads the variants in load order and returns one script for each.
				// A pass that took the script of another mod would edit the wrong containers,
				// so prove the pairing before anything writes.
				if (gate.Scripts.Count != variants.Count)
				{
					throw new DeployServiceException(
						$"The command gate read {gate.Scripts.Count} scripts for {variants.Count} variants. " +
						"The deploy stopped before it changed anything.");
				}

				for (int i = 0; i < variants.Count; ++i)
				{
					RunOne(context, variants[i], gate.Scripts[i], game);
				}

				IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);

				context.Log($"The container engine applied {variants.Count} variants and rewrote " +
					$"{containers.Count} containers.");

				return new DeployReport(null, null, null, null, containers);
			}
			finally
			{
				try
				{
					Directory.SetCurrentDirectory(before);
				}
				catch (Exception)
				{
					// The working directory of the process stays under our own data. That is
					// harmless, because every path that this application uses is absolute.
				}
			}
		}

		/// <summary>
		/// One pass. It loads the containers of one variant, runs the script of that variant,
		/// and saves.
		///
		/// The profile is new for each pass, so the load reads what the last pass wrote.
		/// </summary>
		private static void RunOne(DeployContext context, EnabledVariant variant,
			ResolvedScript resolved, GameINT game)
		{
			MergedLoad load = MergedLaunch.Build(new[] { variant }, context.StagingDirectory);

			context.Log($"Pass {variant.Order}: {variant.Label}. " +
				$"Load {load.Files.Count} containers: {String.Join(", ", load.Files)}.");

			BaseProfile profile = BaseProfile.NewProfile(game, load.Launch.Directory);
			string[] loadErrors = profile.Load(load.Launch);

			Fail(context, $"The load for \"{variant.Label}\" reported", loadErrors);

			// A Load that reports nothing may have loaded nothing. Load returns an empty
			// array at once when Files is empty, and it drops a container that failed.
			if (profile.Count != load.Files.Count)
			{
				throw new DeployServiceException(
					$"The variant \"{variant.Label}\" names {load.Files.Count} containers and the profile " +
					$"holds {profile.Count}. The deploy stopped before it changed anything.");
			}

			Apply(context, profile, variant, resolved);

			string[] saveErrors = profile.Save();

			Fail(context, $"The save for \"{variant.Label}\" reported", saveErrors);
		}

		/// <summary>
		/// Runs the script of one variant against the profile of that pass.
		///
		/// The commands come from the gate, which already walked the append graph and parsed
		/// the script. A second parse reads every appended file again for no new answer.
		/// </summary>
		private static void Apply(DeployContext context, BaseProfile profile, EnabledVariant variant,
			ResolvedScript resolved)
		{
			Launch manifest = variant.Variant.Manifest;
			string scriptPath = ModPath.Resolve(manifest.ThisDir, manifest.Endscript);

			if (!File.Exists(scriptPath))
			{
				throw new DeployServiceException(
					$"The variant \"{variant.Label}\" names the script {manifest.Endscript} and it is not at {scriptPath}.");
			}

			BaseCommand[] commands = resolved.Commands;

			if (commands.Length == 0)
			{
				throw new DeployServiceException(
					$"The variant \"{variant.Label}\" holds no command. The deploy stopped before it " +
					"changed anything.");
			}

			context.Log($"  {variant.Label} runs {commands.Length} commands.");

			// The third argument becomes Path.GetDirectoryName(launcher) inside
			// CollectionMap, and every command that reads a file resolves against that.
			// Pass the full path of the script. A bare file name gives an empty directory,
			// and those commands then read from the working directory of the process.
			var manager = new EndScriptManager(profile, commands, scriptPath);

			// Without CommandChase the jump targets stay unresolved and every selectable
			// fails with a message that names nothing.
			manager.CommandChase();

			var resolver = new SelectionResolver(variant.Variant, variant.Selection);
			int pause = 0;

			while (!Step(manager, variant))
			{
				var selectable = (ISelectable)manager.CurrentCommand;

				// Resolve validates the range and throws a message that names the mod, the
				// script, and the line. An out-of-range Choice inside ProcessScript reads as
				// "Unable to find end to a selectable statement". See defect 5.
				int choice = resolver.Resolve(selectable, pause);

				context.Log($"  question {pause} \"{selectable.Description}\" answered " +
					$"\"{selectable.Options[choice].Name}\".");

				selectable.Choice = choice;
				++pause;
			}

			foreach (ResolverNote note in resolver.Notes) context.Log($"  {note}");

			// A script can reach its end and still report errors. Treat any entry as a
			// failed deploy.
			var errors = new List<string>();

			foreach (EndError error in manager.Errors)
			{
				errors.Add($"{error.Filename} line {error.Index}: {error.Error} ({error.Line})");
			}

			if (errors.Count > 0)
			{
				throw new DeployServiceException(
					$"The variant \"{variant.Label}\" reported {errors.Count} script errors. " +
					$"{String.Join(" ", errors)}");
			}
		}

		/// <summary>
		/// Calls ProcessScript once and turns its exception into a message that names the
		/// variant. ProcessScript throws rather than returning a code.
		/// </summary>
		private static bool Step(EndScriptManager manager, EnabledVariant variant)
		{
			try
			{
				return manager.ProcessScript();
			}
			catch (ModSelectionException)
			{
				throw;
			}
			catch (Exception ex)
			{
				BaseCommand command = manager.CurrentCommand;

				string where = command is null
					? "an unknown place in the script"
					: $"{command.Filename} line {command.Index}: \"{command.Line}\"";

				throw new DeployServiceException(
					$"The variant \"{variant.Label}\" stopped at {where}. {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Surfaces the strings that Load or Save returned, and stops the deploy when the
		/// list holds anything.
		/// </summary>
		private static void Fail(DeployContext context, string what, string[] errors)
		{
			if (errors is null || errors.Length == 0)
			{
				context.Log($"{what} no error.");
				return;
			}

			foreach (string error in errors) context.Log($"  {error}");

			throw new DeployServiceException(
				$"{what} {errors.Length} errors. {String.Join(" ", errors)}");
		}
	}
}
