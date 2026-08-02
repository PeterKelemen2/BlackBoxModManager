using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using Xunit;
using Nikki.Core;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the deploy path of steps 5.5 and 5.6 from end to end.
	///
	/// Every test builds a game directory, a mod store, and a profile, and then deploys and
	/// reverts. The files hold text, so the whole run needs no game and no Wine.
	/// </summary>
	public class DeployPipelineTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly FakeGame _game = new FakeGame();
		private readonly ModStore _store;
		private readonly ModImporter _importer;
		private readonly DeployService _service;
		private readonly List<string> _log = new List<string>();

		public DeployPipelineTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
			this._service = new DeployService(this._store);
		}

		public void Dispose()
		{
			this._game.Dispose();
			this._temp.Dispose();
		}

		private InstalledMod Import(string name, params (string Path, string Content)[] files)
		{
			string root = Path.Combine(this._temp.Path, "source", name);

			foreach ((string path, string content) in files)
			{
				string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllText(full, content);
			}

			return this._importer.Import(root, GameINT.Underground2).Mod;
		}

		private Profile ProfileWith(params InstalledMod[] mods)
		{
			var profile = new Profile("Test", "Underground2");

			foreach (InstalledMod mod in mods) profile.Ensure(mod.Id).Enabled = true;

			return profile;
		}

		private DeployResult Deploy(Profile profile)
		{
			return this._service.Deploy(this._game.Install(), profile, true, this._log.Add);
		}

		// ------------------------------------------------------------------ the snapshot

		[Fact]
		public void TheSnapshotIgnoresTheBackupFilesOfBinary()
		{
			VanillaSnapshot snapshot = SnapshotReader.Create(this._game.Root);

			Assert.Contains("GLOBAL/GLOBALA.BUN", snapshot.Files.Keys);
			Assert.DoesNotContain("GLOBAL/GLOBALA.BUN.bacc", snapshot.Files.Keys);
		}

		[Fact]
		public void TheSnapshotFindsAChangeOfContentAtTheSameLength()
		{
			VanillaSnapshot snapshot = SnapshotReader.Create(this._game.Root);

			// The same length, and different content. A check on size and time would miss
			// this. A hash does not.
			this._game.Write("GLOBAL/GLOBALA.BUN", "container A");

			IReadOnlyList<SnapshotDifference> differences = SnapshotReader.Compare(snapshot, this._game.Root);

			Assert.Single(differences);
			Assert.Equal(SnapshotDifferenceKind.Changed, differences[0].Kind);
			Assert.Equal("GLOBAL/GLOBALA.BUN", differences[0].RelativePath);
		}

		[Fact]
		public void ASnapshotWritesAndReadsBack()
		{
			string path = Path.Combine(this._temp.Path, "snapshot.json");
			VanillaSnapshot written = SnapshotReader.Create(this._game.Root);

			SnapshotReader.Save(path, written);
			VanillaSnapshot read = SnapshotReader.Load(path);

			Assert.Equal(written.Count, read.Count);
			Assert.Equal(written.Files["GLOBAL/GLOBALA.BUN"].Hash, read.Files["global/globala.bun"].Hash);
		}

		// ------------------------------------------------------------------ the deploy

		[Fact]
		public void ADeployPutsTheModFilesIntoTheGameDirectory()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));

			DeployResult result = this.Deploy(this.ProfileWith(mod));

			Assert.True(this._game.Has("scripts/plugin.asi"));
			Assert.Equal("the plugin", this._game.Read("scripts/plugin.asi"));
			Assert.Single(result.Report.Files);
			Assert.True(result.Verification.IsClean);
		}

		[Fact]
		public void ADeployKeepsEveryFileThatNoModTouched()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));

			this.Deploy(this.ProfileWith(mod));

			Assert.Equal("container a", this._game.Read("GLOBAL/GLOBALA.BUN"));
			Assert.Equal("a track", this._game.Read("TRACKS/track.bin"));
			Assert.True(this._game.Has("SPEED2.EXE"));
		}

		[Fact]
		public void AModFileReplacesTheGameFileOfTheSamePath()
		{
			InstalledMod mod = this.Import("Override", ("GLOBAL/GLOBALA.BUN", "the mod container"));

			DeployResult result = this.Deploy(this.ProfileWith(mod));

			Assert.Equal("the mod container", this._game.Read("GLOBAL/GLOBALA.BUN"));
			Assert.True(result.Report.Files[0].OverridesGameFile);
		}

		/// <summary>
		/// Load order is the whole conflict rule. The later mod wins, and the report names
		/// the pair.
		/// </summary>
		[Fact]
		public void TheLaterModInTheLoadOrderWins()
		{
			InstalledMod first = this.Import("First", ("GLOBAL/GLOBALA.BUN", "from the first mod"));
			InstalledMod second = this.Import("Second", ("GLOBAL/GLOBALA.BUN", "from the second mod"));

			DeployResult result = this.Deploy(this.ProfileWith(first, second));

			Assert.Equal("from the second mod", this._game.Read("GLOBAL/GLOBALA.BUN"));
			Assert.Single(result.Report.Overrides);
			Assert.Equal(first.Id, result.Report.Overrides[0].LoserModId);
			Assert.Equal(second.Id, result.Report.Overrides[0].WinnerModId);
		}

		[Fact]
		public void AChangeOfTheLoadOrderChangesTheWinner()
		{
			InstalledMod first = this.Import("First", ("GLOBAL/GLOBALA.BUN", "from the first mod"));
			InstalledMod second = this.Import("Second", ("GLOBAL/GLOBALA.BUN", "from the second mod"));

			Profile profile = this.ProfileWith(first, second);
			profile.Move(second.Id, -1);

			this.Deploy(profile);

			Assert.Equal("from the first mod", this._game.Read("GLOBAL/GLOBALA.BUN"));
		}

		[Fact]
		public void ADeployReportsTheMethodThatItUsed()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));

			DeployResult result = this.Deploy(this.ProfileWith(mod));

			Assert.Single(result.Report.Methods);
			Assert.Contains("The deploy put 1 files in place", result.Report.Summary());
		}

		/// <summary>
		/// A hard link shares the content. A configuration file that the game writes must
		/// therefore reach the game directory as a copy.
		/// </summary>
		[Fact]
		public void AWritableFileReachesTheGameAsACopy()
		{
			InstalledMod mod = this.Import("Settings", ("scripts/plugin.ini", "setting=1"));

			DeployResult result = this.Deploy(this.ProfileWith(mod));

			Assert.Equal(LinkKind.Copy, result.Report.Files[0].Kind);

			// Write through the deployed name. The mod store must not change.
			this._game.Write("scripts/plugin.ini", "setting=2");

			Assert.Equal("setting=1", File.ReadAllText(
				Path.Combine(mod.ContentRoot, "scripts", "plugin.ini")));
		}

		[Fact]
		public void ASecondDeployStartsFromTheVanillaStateAgain()
		{
			InstalledMod first = this.Import("First", ("GLOBAL/GLOBALA.BUN", "from the first mod"));
			InstalledMod second = this.Import("Second", ("CARS/car.bin", "a modded car"));

			this.Deploy(this.ProfileWith(first));
			this.Deploy(this.ProfileWith(second));

			// The first mod is no longer enabled, so its change must be gone.
			Assert.Equal("container a", this._game.Read("GLOBAL/GLOBALA.BUN"));
			Assert.Equal("a modded car", this._game.Read("CARS/car.bin"));
		}

		[Fact]
		public void AProfileWithNoEnabledModGivesTheVanillaState()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));

			this.Deploy(this.ProfileWith(mod));
			this.Deploy(new Profile("Empty", "Underground2"));

			Assert.False(this._game.Has("scripts/plugin.asi"));
			Assert.Equal("container a", this._game.Read("GLOBAL/GLOBALA.BUN"));
		}

		// ------------------------------------------------------------------ the revert

		[Fact]
		public void ARevertPutsTheVanillaStateBack()
		{
			InstalledMod mod = this.Import("Override",
				("GLOBAL/GLOBALA.BUN", "the mod container"),
				("scripts/plugin.asi", "the plugin"));

			this.Deploy(this.ProfileWith(mod));
			this._service.Revert(this._game.Install(), this._log.Add);

			Assert.Equal("container a", this._game.Read("GLOBAL/GLOBALA.BUN"));
			Assert.False(this._game.Has("scripts/plugin.asi"));

			GameWorkspace workspace = this._service.WorkspaceOf(this._game.Install());
			VanillaSnapshot snapshot = workspace.ReadSnapshot();

			Assert.Empty(SnapshotReader.Compare(snapshot, this._game.Root));
			Assert.True(workspace.ReadState().IsVanilla);
		}

		[Fact]
		public void ARevertWithNoVanillaCopyFails()
		{
			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => this._service.Revert(this._game.Install()));

			Assert.Contains("holds no vanilla copy", error.Message);
		}

		[Fact]
		public void TheStateFileNamesTheProfileThatIsDeployed()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));

			this.Deploy(this.ProfileWith(mod));

			WorkspaceState state = this._service.WorkspaceOf(this._game.Install()).ReadState();

			Assert.Equal("Test", state.DeployedProfile);
			Assert.False(state.IsVanilla);
			Assert.Equal(1, state.DeployedFileCount);
		}

		// ------------------------------------------------------------------ the guards

		[Fact]
		public void TheWorkspaceSitsOutsideTheGameDirectory()
		{
			GameWorkspace workspace = this._service.WorkspaceOf(this._game.Install());

			Assert.False(FileTree.IsSameOrInside(workspace.Root, this._game.Root));
			Assert.EndsWith(GameWorkspace.WorkspaceSuffix, workspace.Root);
		}

		/// <summary>
		/// A Binary mod edits containers, and that needs the hash lists of a Binary install.
		/// This DeployService holds none, so the deploy must stop before it writes.
		/// </summary>
		[Fact]
		public void ABinaryModWithNoBinaryInstallStopsTheDeploy()
		{
			var mod = new TempDirectory();

			try
			{
				mod.WriteManifest("Install.end", "Underground2", "script.end");
				mod.WriteScript("script.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4");

				InstalledMod binary = this._importer.Import(mod.Path, GameINT.Underground2).Mod;
				Assert.Equal(ModKind.Binary, binary.Kind);

				Profile profile = this.ProfileWith(binary);
				profile.Find(binary.Id).Selections.Ensure("Install").Enabled = true;

				DeployServiceException error = Assert.Throws<DeployServiceException>(() => this.Deploy(profile));

				Assert.Contains("Binary install", error.Message);

				// The game directory must be untouched.
				Assert.Equal("container a", this._game.Read("GLOBAL/GLOBALA.BUN"));
			}
			finally
			{
				mod.Dispose();
			}
		}

		[Fact]
		public void AProfileThatEnablesAModThatLeftTheStoreStopsTheDeploy()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));
			Profile profile = this.ProfileWith(mod);
			this._store.Remove(mod.Id);

			Assert.Throws<DeployServiceException>(() => this.Deploy(profile));
		}

		/// <summary>
		/// The staging copy shares its content with the vanilla copy. A writer that does not
		/// break the share first would edit the baseline. Step 6 depends on this.
		/// </summary>
		[Fact]
		public void MakePrivateBreaksTheShareWithTheVanillaCopy()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));
			this.Deploy(this.ProfileWith(mod));

			GameWorkspace workspace = this._service.WorkspaceOf(this._game.Install());
			string live = FileTree.Combine(this._game.Root, "GLOBAL/GLOBALA.BUN");
			string vanilla = FileTree.Combine(workspace.VanillaDirectory, "GLOBAL/GLOBALA.BUN");

			StagingFiles.MakePrivate(live);
			File.WriteAllText(live, "an edit that step 6 makes");

			Assert.Equal("container a", File.ReadAllText(vanilla));
		}

		[Fact]
		public void TheReadOnlyFilesOfTheGameSurviveTheWholeRun()
		{
			InstalledMod mod = this.Import("Plugin", ("scripts/plugin.asi", "the plugin"));

			this.Deploy(this.ProfileWith(mod));
			this._service.Revert(this._game.Install(), this._log.Add);

			Assert.Equal("a library", this._game.Read("server.dll"));
		}
	}
}
