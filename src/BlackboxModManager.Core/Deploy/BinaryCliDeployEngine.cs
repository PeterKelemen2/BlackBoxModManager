using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using Endscript.Core;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Applies Binary mods by running the Binary 2.8.3 executable of the user.
	///
	/// <b>This is the fallback route, and the container engine stays the default.</b> The
	/// command gate refuses a command that this application does not run, and a mod that needs
	/// such a command cannot deploy through the native route at all. Binary 2.8.3 pins the same
	/// commits of Nikki and Endscript that we build, so it runs that mod and it gives the same
	/// bytes.
	///
	/// <b>One process for each variant, in load order.</b> The edits composite through the
	/// disk, exactly as they do in the native route. One process for every variant together
	/// would break the mod that follows a <c>delete</c>. See defect 18.
	///
	/// Three properties of the Binary command line shape this class. The brief records all
	/// three, and none of them is a choice that we can make differently.
	///
	/// 1. <b>The manifest must say Modder.</b> LoadProfile throws on any other value, so the
	///    route writes a manifest of its own and never hands over the manifest of the mod.
	/// 2. <b>The exit code is always zero.</b> Binary writes a parse error and an apply error
	///    with Console.WriteLine and returns. So the route reads EndError.log instead.
	/// 3. <b>A question blocks the run.</b> Binary reads an answer from its own console, and
	///    this application cannot reach that console. So the route hands over a script that
	///    asks nothing. See ScriptEmitter.
	/// </summary>
	public sealed class BinaryCliDeployEngine : IDeployEngine
	{
		/// <summary>The file that Binary writes a parse error and an apply error into.</summary>
		public const string ErrorLogName = "EndError.log";

		/// <summary>The file that the library of Binary writes its own trace into.</summary>
		public const string MainLogName = "MainLog.txt";

		/// <summary>The name of the manifest that this route writes.</summary>
		public const string ManifestFileName = "launch.end";

		/// <summary>
		/// How long one variant may take.
		///
		/// A large container mod takes minutes. A run that passes this limit is a run that
		/// waits for something, and the most likely something is a question on a console that
		/// we cannot reach.
		/// </summary>
		public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

		private readonly IProcessRunner _runner;
		private readonly TimeSpan _timeout;

		public BinaryCliDeployEngine(IProcessRunner runner = null, TimeSpan? timeout = null)
		{
			this._runner = runner ?? new ProcessRunner();
			this._timeout = timeout ?? DefaultTimeout;
		}

		public string Name => "Binary CLI engine";

		public IReadOnlySet<ModKind> Kinds { get; } = new HashSet<ModKind> { ModKind.Binary };

		public DeployReport Deploy(DeployContext context, IReadOnlyList<InstalledMod> mods)
		{
			if (context is null) throw new ArgumentNullException(nameof(context));
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			if (context.Binary is null)
			{
				throw new DeployServiceException(
					"The Binary route runs the Binary 2.8.3 executable, and no Binary install is set. " +
					"Set the Binary install directory, then deploy again.");
			}

			if (!Directory.Exists(context.StagingDirectory))
			{
				throw new DeployServiceException(
					$"The staging directory {context.StagingDirectory} does not exist.");
			}

			// The live install must never receive a write. This route hands a directory to
			// another program, so prove it here, where the message can still name the cause.
			if (FileTree.IsSameOrInside(context.StagingDirectory, context.Game.Root))
			{
				throw new DeployServiceException(
					$"The staging directory {context.StagingDirectory} is the game install, or sits inside it. " +
					"A deploy writes only to a staging copy.");
			}

			IReadOnlyList<EnabledVariant> variants = BinaryVariantScope.Of(context, mods);

			if (variants.Count == 0)
			{
				context.Log("No variant of a Binary mod takes the CLI route. That engine has nothing to do.");
				return new DeployReport(null, null, null, null);
			}

			string executable = Path.Combine(context.Binary.Root, BinaryInstallValidator.ExecutableName);

			if (!File.Exists(executable))
			{
				throw new DeployServiceException(
					$"The Binary install at {context.Binary.Root} holds no {BinaryInstallValidator.ExecutableName}. " +
					"Set the Binary install directory again.");
			}

			context.Log($"The CLI route runs {executable} one time for each of {variants.Count} variants.");

			// Read every command before anything runs. A command that writes outside the
			// staging copy stops the deploy. A command that this application cannot run does
			// not, because Binary runs it. That is the reason this route exists.
			GateResult gate = CommandGate.Check(
				variants, context.StagingDirectory, context.Log, context.Scripts, refuseUnsupported: false);

			MergedLoad merged = MergedLaunch.Build(variants, context.StagingDirectory, strict: false);

			foreach (string note in merged.Notes) context.Log(note);

			CheckBaseline(context, merged, gate);

			// Record what already differs from the vanilla state. Every later difference
			// belongs to Binary. See BuildObservedWrites.
			IReadOnlySet<string> before = Differences(context);

			for (int i = 0; i < variants.Count; ++i)
			{
				context.Cancellation.ThrowIfCancellationRequested();

				EnabledVariant variant = variants[i];

				using (context.Timing.Measure($"binary {variant.Order} {variant.Label}"))
				{
					this.RunOne(context, variant, gate.Scripts[i], executable);
				}
			}

			context.Cancellation.ThrowIfCancellationRequested();

			IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);
			var writes = new List<ScriptWrite>(
				ContainerReportBuilder.BuildScriptWrites(context.StagingDirectory, gate, containers));

			writes.AddRange(BuildObservedWrites(context, before, containers, writes, variants));

			return new DeployReport(null, null, null, String.Empty, containers, null, null, writes);
		}

		/// <summary>
		/// Runs Binary one time for one variant.
		/// </summary>
		private void RunOne(DeployContext context, EnabledVariant variant, ResolvedScript resolved,
			string executable)
		{
			context.Log($"  The mod \"{variant.Label}\" deploys through Binary " +
				$"{context.Binary.Version?.ToString() ?? "of an unknown version"}.");

			string work = this.PrepareWorkDirectory(variant);
			string launcher = ModPath.Resolve(variant.Variant.Manifest.ThisDir, variant.Variant.Manifest.Endscript);
			string generated = null;

			try
			{
				if (resolved.Answers.Count > 0)
				{
					generated = WriteGeneratedScript(context, variant, resolved, launcher);
					launcher = generated;
				}

				string manifest = WriteManifest(context, variant, launcher, work);
				ProcessResult result = this.Start(context, executable, manifest, launcher, work);

				this.Interpret(context, variant, work, result);
			}
			finally
			{
				Remove(context, generated);
			}
		}

		/// <summary>
		/// Writes the script that answers every question of the variant, and returns its path.
		///
		/// <b>The file goes beside the launcher of the mod and nowhere else.</b> Seventeen
		/// commands read a file relative to the directory of the launcher, and the parser
		/// resolves an append against that same directory. A file in a scratch directory would
		/// break every one of those paths.
		/// </summary>
		private static string WriteGeneratedScript(DeployContext context, EnabledVariant variant,
			ResolvedScript resolved, string launcher)
		{
			if (resolved.IsApproximate)
			{
				throw new DeployServiceException(
					$"The mod \"{variant.Label}\" asks {resolved.Answers.Count} questions, and an 'if' " +
					"command encloses part of its script. The Binary route answers a question by writing " +
					"a script that holds the chosen commands only, and it cannot do that for an 'if', " +
					"because the branch depends on the containers of the game. Deploy this mod with the " +
					"container engine instead.");
			}

			string directory = Path.GetDirectoryName(launcher);

			if (String.IsNullOrWhiteSpace(directory))
			{
				throw new DeployServiceException(
					$"The launcher script of the mod \"{variant.Label}\" has no directory. " +
					$"The deploy looked at {launcher}.");
			}

			string path = Path.Combine(directory, ScriptEmitter.GeneratedFileName);

			File.WriteAllText(path, ScriptEmitter.Emit(resolved), new UTF8Encoding(false));

			context.Log($"    The mod answers {resolved.Answers.Count} questions: " +
				$"{String.Join(", ", resolved.Answers)}. Binary reads an answer from its own console, " +
				$"so the route wrote {ScriptEmitter.CountOf(resolved)} commands to " +
				$"{ScriptEmitter.GeneratedFileName} and hands that file over.");

			return path;
		}

		/// <summary>
		/// Writes the manifest that Binary loads, and returns its path.
		///
		/// MergedLaunch already builds what this route needs. It sets Usage to Modder, it points
		/// Directory at the staging copy, and it resolves every link to a full path. This method
		/// adds the one field that the merged manifest leaves empty on purpose.
		///
		/// <b>Endscript holds a full path.</b> Path.Combine returns a rooted second argument
		/// unchanged, so the manifest resolves the script whatever it uses as the base. That is
		/// what lets the manifest sit in a scratch directory while the script stays beside the
		/// files that it reads.
		/// </summary>
		private static string WriteManifest(DeployContext context, EnabledVariant variant,
			string launcher, string work)
		{
			MergedLoad load = MergedLaunch.Build(new[] { variant }, context.StagingDirectory);

			foreach (string note in load.Notes) context.Log($"    {note}");

			Launch launch = load.Launch;
			launch.Endscript = Path.GetFullPath(launcher);

			string path = Path.Combine(work, ManifestFileName);

			Launch.Serialize(path, launch);

			context.Log($"    The manifest names {load.Files.Count} containers and " +
				$"{launch.Links?.Count ?? 0} links. Binary loads them from the staging copy.");

			return path;
		}

		/// <summary>
		/// Starts Binary and waits for it.
		/// </summary>
		private ProcessResult Start(DeployContext context, string executable, string manifest,
			string launcher, string work)
		{
			// The first argument is the usage mode. Binary parses it and never reads it, because
			// the Usage field of the manifest decides. The shape is positional, so pass it.
			var arguments = new[] { "modder", manifest, launcher };
			var request = new ProcessRequest(executable, arguments, work, this._timeout);

			try
			{
				return this._runner.Run(request, context.Cancellation);
			}
			catch (ProcessStartException exception)
			{
				throw new DeployServiceException(
					$"{exception.Message} Binary 2.8.3 needs the .NET Core 3.1 Desktop runtime, and this " +
					"application does not supply it. Install that runtime, or set the profile back to the " +
					"container engine.", exception);
			}
		}

		/// <summary>
		/// Decides whether one run passed, and stops the deploy when it did not.
		///
		/// <b>The exit code answers nothing.</b> Binary returns zero for a run that applied no
		/// edit. It writes the reason into EndError.log, so that file is the verdict. A run that
		/// passes leaves the file absent or empty.
		/// </summary>
		private void Interpret(DeployContext context, EnabledVariant variant, string work,
			ProcessResult result)
		{
			string error = ReadLog(Path.Combine(work, ErrorLogName));

			if (error.Length > 0)
			{
				throw new DeployServiceException(
					$"Binary refused the mod \"{variant.Label}\". Binary reported: {error}");
			}

			if (result.TimedOut)
			{
				throw new DeployServiceException(
					$"Binary ran for longer than {this._timeout.TotalMinutes:F0} minutes for the mod " +
					$"\"{variant.Label}\", so the deploy ended it. The most likely cause is a question. " +
					"Binary reads an answer from its own console, and this application cannot reach it.");
			}

			string output = result.StandardOutput.Trim();

			if (output.Length > 0)
			{
				// Binary calls AllocConsole before it writes, so this pipe often stays empty.
				// Show whatever did arrive, because it is the only live account of the run.
				foreach (string line in output.Split('\n')) context.Log($"    Binary said: {line.TrimEnd()}");
			}

			context.Log($"    Binary finished in {result.Duration.TotalSeconds:F1} seconds. " +
				$"It wrote no error. The log of the run stays in {work}.");
		}

		/// <summary>
		/// Reads one log file of Binary and returns its text, or an empty string.
		/// </summary>
		private static string ReadLog(string path)
		{
			try
			{
				if (!File.Exists(path)) return String.Empty;

				return File.ReadAllText(path).Trim();
			}
			catch (Exception)
			{
				// A log that we cannot read tells us nothing, and it is not a failure of its own.
				return String.Empty;
			}
		}

		/// <summary>
		/// Makes an empty directory for one variant to run in.
		///
		/// <b>The directory must start empty.</b> Binary appends to EndError.log, so a file that
		/// a past run left would read as a failure of this run.
		/// </summary>
		private string PrepareWorkDirectory(EnabledVariant variant)
		{
			string name = $"{variant.Order:D2}-{Safe(variant.Label)}";
			string path = Path.Combine(AppPaths.BinaryCliDirectory, name);

			FileTree.Delete(path);
			Directory.CreateDirectory(path);

			return path;
		}

		/// <summary>
		/// Deletes the generated script. A file that stays would change what the mod holds.
		/// </summary>
		private static void Remove(DeployContext context, string path)
		{
			if (String.IsNullOrEmpty(path)) return;

			try
			{
				if (File.Exists(path)) File.Delete(path);
			}
			catch (Exception exception)
			{
				context.Log($"    warning: the deploy could not delete {path}. {exception.Message} " +
					"Delete the file by hand. The mod store holds a file that the mod did not ship.");
			}
		}

		/// <summary>
		/// The relative paths of the staging copy that differ from the vanilla record.
		///
		/// The compare reads lengths and no content. A container that Binary rewrites almost
		/// always changes length, and the pass that follows the deploy hashes everything that
		/// matters anyway. This call only has to tell one set of files from another.
		/// </summary>
		private static IReadOnlySet<string> Differences(DeployContext context)
		{
			var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			if (context.Baseline is null) return found;

			foreach (SnapshotDifference difference in SnapshotReader.Compare(
				context.Baseline, context.StagingDirectory, hashContent: false))
			{
				found.Add(difference.RelativePath);
			}

			return found;
		}

		/// <summary>
		/// Reports every file that Binary changed and that the command parse did not name.
		///
		/// <b>The verify needs this list.</b> Its first pass reports every staged file that
		/// differs from the vanilla state and that no mod claimed. The parse names what the
		/// script says it writes, and Binary runs commands that this application does not
		/// classify. So a file that only Binary knows about would stop a deploy that did what
		/// the mod asked for. See defect 16.
		///
		/// The difference between the two lists is worth a log line of its own. It is the only
		/// measure of how much of a Binary run this application can predict.
		/// </summary>
		private static IReadOnlyList<ScriptWrite> BuildObservedWrites(DeployContext context,
			IReadOnlySet<string> before, IReadOnlyList<ContainerWrite> containers,
			IReadOnlyList<ScriptWrite> predicted, IReadOnlyList<EnabledVariant> variants)
		{
			if (context.Baseline is null) return Array.Empty<ScriptWrite>();

			var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (ContainerWrite write in containers) known.Add(PathKey.Normalize(write.RelativePath));
			foreach (ScriptWrite write in predicted) known.Add(PathKey.Normalize(write.RelativePath));

			var owners = new List<string>(variants.Count);
			foreach (EnabledVariant variant in variants) owners.Add(variant.Label);

			var extra = new List<ScriptWrite>();

			foreach (string path in Differences(context))
			{
				if (before.Contains(path)) continue;
				if (!known.Add(PathKey.Normalize(path))) continue;

				extra.Add(new ScriptWrite(path, owners));
			}

			if (extra.Count == 0)
			{
				context.Log("  Binary changed no file that the command parse did not name.");
				return extra;
			}

			var names = new List<string>();

			for (int i = 0; i < extra.Count && i < 8; ++i) names.Add(extra[i].RelativePath);

			string tail = extra.Count > names.Count ? ", and more" : String.Empty;

			context.Log($"  Binary changed {extra.Count} files that the command parse did not name: " +
				$"{String.Join(", ", names)}{tail}. The deploy reports them, so the verify accepts them.");

			return extra;
		}

		/// <summary>
		/// Stops the deploy when the vanilla copy no longer holds what the snapshot recorded.
		///
		/// A deploy against a changed baseline gives a wrong result. Every Binary mod reads a
		/// container before it writes it, and a container that already carries the edits of a
		/// past run makes the script report "already exists" for every name that it adds.
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

			context.Log("  The vanilla copy still matches its record for every file that this route names.");
		}

		/// <summary>
		/// Turns a label into a directory name. A mod name holds a slash and a colon.
		/// </summary>
		private static string Safe(string label)
		{
			var text = new StringBuilder(label.Length);

			foreach (char letter in label)
			{
				text.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), letter) >= 0 ? '-' : letter);
			}

			return text.ToString().Trim();
		}
	}
}
