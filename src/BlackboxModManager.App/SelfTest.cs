using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using Nikki.Core;

namespace BlackboxModManager.App
{
	/// <summary>
	/// Runs the deploy path against a directory tree that this class builds, and reports
	/// what happened.
	///
	/// This exists because the unit tests run on native Linux and the application runs
	/// under Wine. The two platforms differ in the parts that matter most here: hard links,
	/// letter case, and a directory move. This check answers those questions on the
	/// platform that the user runs.
	///
	/// It drives the public API of the Core library only. It holds no logic that the
	/// application does not use.
	///
	/// Start it with: BlackboxModManager.exe --selftest &lt;directory&gt;
	/// </summary>
	internal static class SelfTest
	{
		public const string Switch = "--selftest";

		private static readonly List<string> Report = new List<string>();
		private static int _failed;

		public static int Run(string workRoot)
		{
			string root = Path.Combine(Path.GetFullPath(workRoot), $"selftest-{Guid.NewGuid():N}");

			Line($"The self test runs in {root}.");
			Line($"The process architecture is {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}.");
			Line($"The operating system reports {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");

			try
			{
				Directory.CreateDirectory(root);
				Check(root);
			}
			catch (Exception ex)
			{
				++_failed;
				Line($"The self test stopped on an exception. {ex}");
			}
			finally
			{
				try
				{
					FileTree.Delete(root);
				}
				catch (Exception ex)
				{
					Line($"The cleanup left {root} behind. {ex.Message}");
				}
			}

			Line(_failed == 0 ? "PASSED. Every check passed." : $"FAILED. {_failed} checks failed.");

			// A window application has no console on Windows. Write the report to a file
			// too, so that the result survives a run with no terminal.
			string log = Path.Combine(Path.GetFullPath(workRoot), "selftest.log");

			try
			{
				File.WriteAllText(log, String.Join(Environment.NewLine, Report));
				Console.WriteLine($"The report is at {log}.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"The report did not reach {log}. {ex.Message}");
			}

			return _failed == 0 ? 0 : 1;
		}

		private static void Check(string root)
		{
			// ------------------------------------------------------------ the game

			string game = Path.Combine(root, "game", "Need for Speed Underground 2");
			Write(game, "SPEED2.EXE", "the game");
			Write(game, "GLOBAL/GLOBALA.BUN", "container a");
			Write(game, "GLOBAL/GlobalB.lzc", "container b");
			Write(game, "GLOBAL/GLOBALA.BUN.bacc", "the backup of another tool");
			Write(game, "CARS/car.bin", "a car");
			Write(game, "TRACKS/track.bin", "a track");
			Write(game, "FRONTEND/front.bin", "a menu");

			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, game);
			Expect(status.IsUsable, $"The validator accepts the game directory. {status.Message}");

			if (!status.IsUsable) return;

			GameInstall install = status.Install;

			// ------------------------------------------------------------ the store

			var store = new ModStore(Path.Combine(root, "mods"));
			var importer = new ModImporter(store, Path.Combine(root, "import"));

			string first = Path.Combine(root, "source", "First Mod");
			Write(first, "scripts/plugin.asi", "the plugin");
			Write(first, "GLOBAL/GLOBALA.BUN", "from the first mod");

			string second = Path.Combine(root, "source", "Second Mod");
			Write(second, "GLOBAL/GLOBALA.BUN", "from the second mod");
			Write(second, "scripts/plugin.ini", "setting=1");

			InstalledMod one = importer.Import(first).Mod;
			InstalledMod two = importer.Import(second).Mod;

			Expect(one.Kind == ModKind.Asi, $"The first mod is an ASI mod. It is {one.Kind}.");
			Expect(two.Kind == ModKind.LooseFiles, $"The second mod holds loose files. It is {two.Kind}.");

			// ------------------------------------------------------------ the deploy

			var profile = new Profile("Self test", GameINT.Underground2.ToString());
			profile.Ensure(one.Id).Enabled = true;
			profile.Ensure(two.Id).Enabled = true;

			var service = new DeployService(store);
			GameWorkspace workspace = service.WorkspaceOf(install);

			Line($"The workspace is {workspace.Root}.");
			Expect(workspace.SharesVolumeWithGame(), "The workspace shares the volume of the game.");

			DeployResult result = service.Deploy(install, profile, true, Line);

			Expect(result.Verification.IsClean, "The verify found no problem.");
			Expect(Read(game, "scripts/plugin.asi") == "the plugin", "The game holds the plugin of the first mod.");
			Expect(Read(game, "GLOBAL/GLOBALA.BUN") == "from the second mod",
				"The later mod in the load order wins.");
			Expect(Read(game, "GLOBAL/GlobalB.lzc") == "container b", "A file that no mod touched did not change.");
			Expect(result.Report.Overrides.Count == 1, $"The report names 1 collision. It names {result.Report.Overrides.Count}.");

			Line(result.Report.Summary());

			foreach (KeyValuePair<LinkKind, int> method in result.Report.Methods)
			{
				Line($"The deploy used {method.Key} for {method.Value} files.");
			}

			Expect(result.Report.Methods.ContainsKey(LinkKind.HardLink),
				"The deploy used a hard link for at least one file.");

			// A file that the game writes must be a private copy, whatever the probe allows.
			string ini = FileTree.Combine(game, "scripts/plugin.ini");
			File.WriteAllText(ini, "setting=2");
			Expect(File.ReadAllText(FileTree.Combine(two.ContentRoot, "scripts/plugin.ini")) == "setting=1",
				"A write to a deployed configuration file leaves the mod store alone.");

			// The staging copy shares its content with the vanilla copy. Step 6 depends on
			// this call to break the share before it writes a container.
			string live = FileTree.Combine(game, "GLOBAL/GlobalB.lzc");
			StagingFiles.MakePrivate(live);
			File.WriteAllText(live, "an edit that step 6 makes");
			Expect(File.ReadAllText(FileTree.Combine(workspace.VanillaDirectory, "GLOBAL/GlobalB.lzc")) == "container b",
				"MakePrivate breaks the share with the vanilla copy.");

			// ------------------------------------------------------------ the revert

			service.Revert(install, Line);

			Expect(Read(game, "GLOBAL/GLOBALA.BUN") == "container a", "The revert put the game container back.");
			Expect(Read(game, "GLOBAL/GlobalB.lzc") == "container b", "The revert undid the edit of step 6.");
			Expect(!File.Exists(FileTree.Combine(game, "scripts/plugin.asi")), "The revert removed the plugin.");

			VanillaSnapshot snapshot = workspace.ReadSnapshot();
			IReadOnlyList<SnapshotDifference> differences = SnapshotReader.Compare(snapshot, game);

			Expect(differences.Count == 0, $"The game matches the vanilla snapshot. It differs in {differences.Count} files.");

			foreach (SnapshotDifference difference in differences) Line($"  {difference}");

			Expect(workspace.ReadState().IsVanilla, "The state file reports the vanilla state.");
			Expect(!snapshot.Files.ContainsKey("GLOBAL/GLOBALA.BUN.bacc"), "The snapshot ignores the .bacc file.");
		}

		// ---------------------------------------------------------------- helpers

		private static void Write(string root, string relative, string content)
		{
			string full = FileTree.Combine(root, relative);
			FileTree.CreateParent(full);
			File.WriteAllText(full, content);
		}

		private static string Read(string root, string relative)
		{
			string full = FileTree.Combine(root, relative);

			return File.Exists(full) ? File.ReadAllText(full) : "(the file does not exist)";
		}

		private static void Expect(bool condition, string what)
		{
			if (!condition) ++_failed;

			Line($"{(condition ? "ok  " : "FAIL")}  {what}");
		}

		private static void Line(string text)
		{
			Report.Add(text);
			Console.WriteLine(text);
		}
	}
}
