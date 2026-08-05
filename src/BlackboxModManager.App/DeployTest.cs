using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using Nikki.Core;

namespace BlackboxModManager.App
{
	/// <summary>
	/// Installs both example mods into one game directory, in one load, apply, and save
	/// pass, and then reverts.
	///
	/// This is the success criterion of the project brief, driven with no window. It runs the
	/// same Core code that the window runs, so a pass here is a pass for the application.
	///
	/// <b>Pass a scratch copy of the game, never a live install.</b> The swap replaces the
	/// directory that this test gets. tools/run-deploy-test.sh makes the copy.
	///
	/// Start it with:
	/// BlackboxModManager.exe --deploytest &lt;gameDir&gt; &lt;binaryDir&gt; &lt;modsRoot&gt;
	/// </summary>
	internal static class DeployTest
	{
		public const string Switch = "--deploytest";

		private const string OneLapFolder = "NFSU2 - 1 Lap URL And Other Races v2.0";
		private const string CameraFolder = "3822ca-NFSUG2 - Camera MOD MW to U2 ver.1.0";

		private const string OneLapVariant = "1 Lap URL Races";
		private const string CameraVariant = "Install";

		/// <summary>
		/// The option name of the camera mod. The name is the quoted string of the combobox
		/// line, not the file name of the block that it appends.
		/// </summary>
		private const string CameraOption = "Install Camera Mod [NFSMW TO U2]";

		private static readonly List<string> Report = new List<string>();
		private static int _failed;

		public static int Run(string gameDirectory, string binaryDirectory, string modsRoot, bool revert = true)
		{
			Line($"The deploy test runs against {gameDirectory}.");
			Line($"The Binary install is {binaryDirectory}.");
			Line($"The example mods are at {modsRoot}.");
			Line($"The application data is at {AppPaths.Root}.");

			try
			{
				Check(gameDirectory, binaryDirectory, modsRoot, revert);
			}
			catch (Exception ex)
			{
				++_failed;
				Line($"The deploy test stopped on an exception. {ex}");
			}

			Line(_failed == 0 ? "PASSED. Every check passed." : $"FAILED. {_failed} checks failed.");

			string log = Path.Combine(AppPaths.LogDirectory, "deploytest.log");

			try
			{
				Directory.CreateDirectory(AppPaths.LogDirectory);
				File.WriteAllText(log, String.Join(Environment.NewLine, Report));
				Console.WriteLine($"The report is at {log}.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"The report did not reach {log}. {ex.Message}");
			}

			return _failed == 0 ? 0 : 1;
		}

		private static void Check(string gameDirectory, string binaryDirectory, string modsRoot, bool revert)
		{
			// ------------------------------------------------------------ the installs

			GameInstallStatus game = GameInstallValidator.Validate(GameINT.Underground2, gameDirectory);
			Expect(game.IsUsable, $"The game directory validates. {game.Message}");

			if (!game.IsUsable) return;

			BinaryInstallStatus binary = BinaryInstallValidator.Validate(binaryDirectory);
			Expect(binary.IsUsable, $"The Binary install validates. {binary.Message}");

			if (!binary.IsUsable) return;

			if (binary.VersionWarning.Length > 0) Line(binary.VersionWarning);

			GameInstall install = game.Install;

			// ------------------------------------------------------------ the mods

			// A fresh store for every run. A leftover mod would change the load order.
			var store = new ModStore(Path.Combine(AppPaths.Root, "deploytest-mods"));
			FileTree.Delete(store.Root);

			var importer = new ModImporter(store, Path.Combine(AppPaths.Root, "deploytest-import"));

			InstalledMod camera = importer.Import(Path.Combine(modsRoot, CameraFolder), GameINT.Underground2).Mod;
			InstalledMod lap = importer.Import(Path.Combine(modsRoot, OneLapFolder), GameINT.Underground2).Mod;

			Expect(camera.Kind == ModKind.Binary, $"The camera mod is a Binary mod. It is {camera.Kind}.");
			Expect(lap.Kind == ModKind.Binary, $"The 1 Lap mod is a Binary mod. It is {lap.Kind}.");

			// ------------------------------------------------------------ the profile

			// The camera mod applies first and the 1 Lap mod second. The two edit different
			// managers, so the order changes nothing here. It still has to be explicit.
			var profile = new Profile("Deploy test", nameof(GameINT.Underground2));

			ProfileEntry cameraEntry = profile.Ensure(camera.Id);
			cameraEntry.Enabled = true;
			VariantSelection cameraSelection = cameraEntry.Selections.Ensure(CameraVariant);
			cameraSelection.Enabled = true;
			cameraSelection.Choose(0, CameraOption);

			ProfileEntry lapEntry = profile.Ensure(lap.Id);
			lapEntry.Enabled = true;
			lapEntry.Selections.Ensure(OneLapVariant).Enabled = true;

			// ------------------------------------------------------------ the conflicts

			var service = new DeployService(store, binary.Install);

			ConflictReport conflicts = service.CheckConflicts(install, profile, Line);

			Expect(conflicts.IsClean,
				$"The two mods report no conflict. They report {conflicts.Conflicts.Count}.");
			Expect(conflicts.Unchecked.Count == 0,
				$"The check read every variant. It could not read {conflicts.Unchecked.Count}.");
			Expect(conflicts.CheckedVariants == 2,
				$"The check read 2 variants. It read {conflicts.CheckedVariants}.");
			Expect(conflicts.CanDeploy,
				$"The check refuses no command. It refuses {conflicts.Rejections.Count} commands and " +
				$"it found {conflicts.Escapes.Count} paths outside staging.");
			Expect(conflicts.Warnings.Count == 0,
				$"Both mods use classified commands only. The check warns {conflicts.Warnings.Count} times.");

			// ------------------------------------------------------------ the deploy

			GameWorkspace workspace = service.WorkspaceOf(install);
			Line($"The workspace is {workspace.Root}.");

			DeployResult result = service.Deploy(install, profile, true, Line);

			Expect(result.Verification.IsClean, "The verify found no problem.");

			foreach (string problem in result.Verification.Problems) Line($"  problem: {problem}");

			Expect(result.Report.Containers.Count == 2,
				$"The deploy rewrote 2 containers. It rewrote {result.Report.Containers.Count}.");

			foreach (ContainerWrite container in result.Report.Containers) Line($"  container: {container}");

			// One load for both mods. Two loads would build two container objects for
			// GLOBALB.LZC and the edits of the first mod would vanish. See defect 6.
			var names = new List<string>();
			foreach (ContainerWrite container in result.Report.Containers) names.Add(container.RelativePath);

			Expect(names.Contains(@"GLOBAL\GLOBALB.LZC"), "The deploy rewrote GLOBALB.LZC.");
			Expect(names.Contains(@"GLOBAL\GLOBALA.BUN"), "The deploy rewrote GLOBALA.BUN.");

			// GLOBALB.LZC carries the edits of both mods, so its content must differ from
			// the vanilla state.
			VanillaSnapshot snapshot = workspace.ReadSnapshot();
			Expect(snapshot != null, "The workspace holds a vanilla snapshot.");

			if (snapshot is null) return;

			Expect(Changed(snapshot, install.Root, "GLOBAL/GlobalB.lzc"),
				"The container GlobalB.lzc in the game differs from the vanilla state.");

			WorkspaceState state = workspace.ReadState();
			Expect(state.DeployedProfile == "Deploy test",
				$"The state file names the profile. It names \"{state.DeployedProfile}\".");

			// The game has to start from this directory. Only a run of the game proves that
			// the container still loads, and this test cannot do that.
			Line($"Start {install.ExecutablePath} to confirm that the game reads the result.");

			// ------------------------------------------------------------ the revert

			if (!revert)
			{
				Line("The test keeps the deploy in place. Start the game to confirm the result.");
				Line("Run the revert later through the window, or run this test again.");
				return;
			}

			service.Revert(install, Line);

			IReadOnlyList<SnapshotDifference> differences = SnapshotReader.Compare(snapshot, install.Root);

			Expect(differences.Count == 0,
				$"The revert matches the vanilla snapshot. It differs in {differences.Count} files.");

			int shown = 0;

			foreach (SnapshotDifference difference in differences)
			{
				if (++shown > 20) break;

				Line($"  {difference}");
			}

			Expect(workspace.ReadState().IsVanilla, "The state file reports the vanilla state.");
		}

		/// <summary>
		/// True when the file on the disk differs from the snapshot entry.
		/// </summary>
		private static bool Changed(VanillaSnapshot snapshot, string root, string relative)
		{
			if (!snapshot.Files.TryGetValue(relative, out SnapshotEntry entry))
			{
				Line($"The snapshot holds no entry for {relative}.");
				return false;
			}

			string path = ModPath.Resolve(root, relative);

			if (!File.Exists(path))
			{
				Line($"The file {path} does not exist.");
				return false;
			}

			var info = new FileInfo(path);
			Line($"{relative} was {entry.Length} bytes and it is {info.Length} bytes.");

			return info.Length != entry.Length || FileHash.Compute(path) != entry.Hash;
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
