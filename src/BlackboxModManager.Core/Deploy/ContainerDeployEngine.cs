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
	/// <b>Load once. Apply every enabled mod. Save once.</b> This is the most important rule
	/// in the project, and this class is the whole of it. Every enabled variant runs against
	/// one loaded BaseProfile before one Save, so the edits composite at the collection and
	/// the entry level. No mod overwrites the container of another mod, and container
	/// merging never becomes a problem that somebody has to solve.
	///
	/// <b>One pass per mod breaks it.</b> Load adds a container per call, and Save then
	/// writes one file twice from two states. See defect 6.
	///
	/// The whole pass holds LibraryGate and runs on the calling thread. The hash list
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

			MergedLoad merged = MergedLaunch.Build(variants, context.StagingDirectory);

			foreach (string note in merged.Notes) context.Log(note);

			context.Log($"The merged load names {merged.Files.Count} containers: {String.Join(", ", merged.Files)}.");

			this.Prepare(context, merged);

			// One gate covers the statics, the load, every script, and the save.
			using (LibraryGate.Enter())
			{
				return this.RunPass(context, merged, variants);
			}
		}

		/// <summary>
		/// Makes every container of the merged load private and confirms that it exists.
		///
		/// <b>This is the step that protects the install of the user.</b> TreeReplicator
		/// builds the staging copy with hard links, so a staging container, the vanilla
		/// container, and the live container are one file with three names. Save writes a
		/// container in place, and that write would reach all three. MakePrivate breaks the
		/// share first.
		/// </summary>
		private void Prepare(DeployContext context, MergedLoad merged)
		{
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
			}

			context.Log($"The staging copy holds {merged.Files.Count} private containers, " +
				"so a write cannot reach the vanilla copy or the game.");
		}

		/// <summary>
		/// The single pass. It loads, applies every variant in load order, and saves.
		/// </summary>
		private DeployReport RunPass(DeployContext context, MergedLoad merged,
			IReadOnlyList<EnabledVariant> variants)
		{
			GameINT game = context.Game.Game;

			// Nikki writes MainLog.txt into the current directory. Point that at our own
			// data before any container work. See defect 9.
			string before = Directory.GetCurrentDirectory();
			Directory.CreateDirectory(AppPaths.LogDirectory);
			Directory.SetCurrentDirectory(AppPaths.LogDirectory);

			try
			{
				// Both statics must hold a value before Load. Load calls LoadHashList first,
				// and Save writes CustomHashList as its last step. See defect 7.
				ProfileHashLists.Apply(context.Binary, game);

				(string main, string custom) = ProfileHashLists.Current(game);
				context.Log($"The main hash list is {main}.");
				context.Log($"The custom hash list is {custom}.");

				BaseProfile profile = BaseProfile.NewProfile(game, merged.Launch.Directory);

				context.Log("Load the containers. This runs once for the whole deploy.");
				string[] loadErrors = profile.Load(merged.Launch);

				Fail(context, "The load reported", loadErrors);

				// A Load that reports nothing may have loaded nothing. Load returns an empty
				// array at once when Files is empty, and it drops a container that failed.
				if (profile.Count != merged.Files.Count)
				{
					throw new DeployServiceException(
						$"The merged load names {merged.Files.Count} containers and the profile holds " +
						$"{profile.Count}. The deploy stopped before it changed anything.");
				}

				foreach (SynchronizedDatabase database in profile) context.Log($"  loaded {database.Filename}");

				foreach (EnabledVariant variant in variants) Apply(context, profile, variant);

				context.Log("Save the containers. This runs once, after every mod applied.");
				string[] saveErrors = profile.Save();

				Fail(context, "The save reported", saveErrors);

				var containers = new List<ContainerWrite>(merged.Files.Count);

				foreach (string file in merged.Files)
				{
					containers.Add(new ContainerWrite(file, merged.Contributors[file]));
				}

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
		/// Runs the script of one variant against the loaded profile.
		///
		/// The manager gets the same profile instance every time. That is what makes the
		/// single pass work.
		/// </summary>
		private static void Apply(DeployContext context, BaseProfile profile, EnabledVariant variant)
		{
			Launch manifest = variant.Variant.Manifest;
			string scriptPath = ModPath.Resolve(manifest.ThisDir, manifest.Endscript);

			if (!File.Exists(scriptPath))
			{
				throw new DeployServiceException(
					$"The variant \"{variant.Label}\" names the script {manifest.Endscript} and it is not at {scriptPath}.");
			}

			// The library parser splices every append inline and keeps no cycle guard.
			ScriptAppendGraph.Walk(scriptPath);

			BaseCommand[] commands = ScriptReader.Parse(scriptPath);

			context.Log($"Apply {variant.Order}: {variant.Label}, {commands.Length} commands.");

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
