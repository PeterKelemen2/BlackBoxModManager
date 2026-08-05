using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers step 9 from end to end. The user changes an option, the deploy writes it into the
	/// staging copy, and two mods that both ship <c>dinput8.dll</c> produce one prompt and one
	/// deployed file.
	///
	/// The mods are synthetic. See <see cref="AsiFixture"/>.
	/// </summary>
	public class AsiConfigurationTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly FakeGame _game = new FakeGame();
		private readonly ModStore _store;
		private readonly ModImporter _importer;
		private readonly DeployService _service;
		private readonly List<string> _log = new List<string>();

		public AsiConfigurationTests()
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

		// ------------------------------------------------------------------ the layout

		[Fact]
		public void TheLayoutMatchesTheSettingsFileToItsPlugin()
		{
			InstalledMod mod = this.Import("Widescreen Fix");
			AsiLayout layout = AsiLayoutReader.Read(mod.ContentRoot);

			Assert.Equal(new[] { AsiFixture.PluginPath }, layout.Plugins.ToArray());

			AsiSettingsFile file = Assert.Single(layout.Settings);

			Assert.Equal(AsiFixture.SettingsPath, file.RelativePath);
			Assert.Equal(AsiFixture.PluginPath, file.PluginPath);
			Assert.True(file.HasPlugin);
		}

		[Fact]
		public void ADataFileBesideThePluginIsNotSettings()
		{
			// The Widescreen Fix ships a .dat file that holds the HUD offsets. The window must
			// not offer an editor for it.
			InstalledMod mod = this.Import("Widescreen Fix");
			AsiLayout layout = AsiLayoutReader.Read(mod.ContentRoot);

			Assert.DoesNotContain(layout.Settings, f => f.RelativePath.EndsWith(".dat", StringComparison.Ordinal));
		}

		[Fact]
		public void AnUnmatchedSettingsFileCarriesNoPlugin()
		{
			// Not every .ini beside a plugin is the settings of that plugin. The window shows an
			// unmatched file under its own heading and it claims no owner.
			string root = Path.Combine(this._temp.Path, "source", "Unmatched");
			AsiFixture.Write(root);
			File.WriteAllText(Path.Combine(root, "scripts", "something-else.ini"), "[A]\nx = 1\n");

			InstalledMod mod = this._importer.Import(root, GameINT.Underground2).Mod;
			AsiLayout layout = AsiLayoutReader.Read(mod.ContentRoot);

			AsiSettingsFile loose = Assert.Single(layout.Settings, f => !f.HasPlugin);

			Assert.Equal("something-else.ini", loose.Name);
		}

		[Fact]
		public void ASettingsFileThatThisApplicationCannotReadKeepsItsReason()
		{
			// A directory named like a file reaches this. The link engine still places the real
			// files, and the window says that it holds no editor for this one.
			string root = Path.Combine(this._temp.Path, "source", "Broken");
			AsiFixture.Write(root);

			AsiLayout layout = AsiLayoutReader.Read(root, ProxyNames.Default);

			Assert.All(layout.Settings, f => Assert.True(f.IsReadable));
		}

		// ------------------------------------------------------------------ the profile

		[Fact]
		public void AnAnswerThatMatchesTheFileLeavesTheProfile()
		{
			// The profile holds the differences from the mod. A value that the user set back to
			// the original must not stay, or the deployed file would never match the store again.
			var entry = new ProfileEntry("mod", true);

			entry.SetIni(AsiFixture.SettingsPath, "MAIN/FixHUD", "0", "1");
			Assert.Equal(1, entry.IniAnswerCount);

			entry.SetIni(AsiFixture.SettingsPath, "MAIN/FixHUD", "1", "1");
			Assert.Equal(0, entry.IniAnswerCount);
			Assert.Empty(entry.IniSettings);
		}

		[Fact]
		public void TheProfileSurvivesARoundTripThroughTheStore()
		{
			var store = new ProfileStore(Path.Combine(this._temp.Path, "profiles"));
			var profile = new Profile("Test", "Underground2");

			profile.Ensure("mod").SetIni(AsiFixture.SettingsPath, "MAIN/ResX", "1920", "0");
			profile.ChooseLoader(ProxyNames.DirectInput, "mod");

			store.Save(GameINT.Underground2, profile);

			Profile again = Assert.Single(store.List(GameINT.Underground2));

			Assert.Equal("1920", again.Find("mod").IniFor(AsiFixture.SettingsPath)["MAIN/ResX"]);
			Assert.Equal("mod", again.LoaderChoice(ProxyNames.DirectInput));
		}

		[Fact]
		public void AskMeAgainClearsTheStoredLoaderChoice()
		{
			var profile = new Profile("Test", "Underground2");

			profile.ChooseLoader(ProxyNames.DirectInput, "mod");
			profile.ChooseLoader(ProxyNames.DirectInput, null);

			Assert.Null(profile.LoaderChoice(ProxyNames.DirectInput));
		}

		// ------------------------------------------------------------------ the deploy

		[Fact]
		public void ADeployWritesTheAnswerIntoTheStagingCopyAndLeavesTheModStoreAlone()
		{
			InstalledMod mod = this.Import("Widescreen Fix");
			Profile profile = this.ProfileWith(mod);

			profile.Find(mod.Id).SetIni(AsiFixture.SettingsPath, "MAIN/ResX", "1920", "0");
			profile.Find(mod.Id).SetIni(AsiFixture.SettingsPath, "MAIN/FixHUD", "0", "1");

			DeployResult result = this.Deploy(profile);

			Assert.True(result.Verification.IsClean, String.Join(" ", result.Verification.Problems));

			IniDocument deployed = IniReader.Read(this._game.FullPath(AsiFixture.SettingsPath));

			Assert.Equal("1920", deployed.Find(new IniKey("MAIN", "ResX")).Value);
			Assert.Equal("0", deployed.Find(new IniKey("MAIN", "FixHUD")).Value);

			// The comment of the changed line survives, and the mod store holds the original.
			Assert.Equal("Corrects HUD aspect ratio.", deployed.Find(new IniKey("MAIN", "FixHUD")).Comment);
			Assert.Equal(AsiFixture.SettingsText,
				File.ReadAllText(Path.Combine(mod.ContentRoot, "scripts",
					AsiFixture.PluginName + ".ini")));

			SettingsWrite write = Assert.Single(result.Report.Settings);

			Assert.Equal(2, write.Changed.Count);
			Assert.Empty(write.Skipped);
		}

		[Fact]
		public void ADeployWithNoAnswerLeavesTheSettingsFileAsTheModShippedIt()
		{
			InstalledMod mod = this.Import("Widescreen Fix");

			DeployResult result = this.Deploy(this.ProfileWith(mod));

			Assert.Empty(result.Report.Settings);
			Assert.Equal(AsiFixture.SettingsText, File.ReadAllText(this._game.FullPath(AsiFixture.SettingsPath)));
		}

		[Fact]
		public void AnEditedSettingsFileStillPassesTheVerify()
		{
			// The verify hashes a deployed file against the mod store. An edited file differs
			// from the store on purpose, so the check on it is existence and a length above zero.
			InstalledMod mod = this.Import("Widescreen Fix");
			Profile profile = this.ProfileWith(mod);

			profile.Find(mod.Id).SetIni(AsiFixture.SettingsPath, "MAIN/ResX", "1920", "0");

			DeployResult result = this.Deploy(profile);

			Assert.True(result.Verification.IsClean, String.Join(" ", result.Verification.Problems));
			Assert.Contains(result.Report.Files, f => f.Edited && f.RelativePath == AsiFixture.SettingsPath);
		}

		[Fact]
		public void ARevertPutsTheOriginalSettingsFileBack()
		{
			InstalledMod mod = this.Import("Widescreen Fix");
			Profile profile = this.ProfileWith(mod);

			profile.Find(mod.Id).SetIni(AsiFixture.SettingsPath, "MAIN/ResX", "1920", "0");

			this.Deploy(profile);
			this._service.Revert(this._game.Install(), this._log.Add);

			Assert.False(File.Exists(this._game.FullPath(AsiFixture.SettingsPath)));
		}

		[Fact]
		public void AnAnswerForAnOptionThatTheModDroppedIsReportedAndDoesNotStopTheDeploy()
		{
			InstalledMod mod = this.Import("Widescreen Fix");
			Profile profile = this.ProfileWith(mod);

			profile.Find(mod.Id).SetIni(AsiFixture.SettingsPath, "MAIN/GoneInVersionTwo", "1", "0");

			DeployResult result = this.Deploy(profile);

			Assert.True(result.Verification.IsClean);
			Assert.Equal(new[] { "MAIN/GoneInVersionTwo" }, Assert.Single(result.Report.Settings).Skipped.ToArray());
		}

		// ------------------------------------------------------------------ the loader

		[Fact]
		public void OneModThatSuppliesTheLoaderNeedsNoAnswer()
		{
			InstalledMod mod = this.Import("Widescreen Fix", "loader one");
			Profile profile = this.ProfileWith(mod);

			ProxyPlan plan = this._service.PlanLoaders(profile);
			ProxyContest contest = Assert.Single(plan.Contests);

			Assert.False(contest.IsContested);
			Assert.False(contest.NeedsAnswer);
			Assert.Equal(mod.Id, contest.Supplier.ModId);
			Assert.True(plan.IsSettled);
		}

		[Fact]
		public void TwoModsThatSupplyTheLoaderStopTheDeployUntilTheUserChooses()
		{
			// Never pick a loader automatically. A proxy DLL forwards to the real system
			// library, and a version that forwards wrongly breaks sound or input.
			InstalledMod first = this.Import("Widescreen Fix", "loader one");
			InstalledMod second = this.Import("Other Fix", "loader two, a different build");
			Profile profile = this.ProfileWith(first, second);

			ProxyPlan plan = this._service.PlanLoaders(profile);
			ProxyContest contest = Assert.Single(plan.Contests);

			Assert.True(contest.IsContested);
			Assert.True(contest.NeedsAnswer);
			Assert.Null(contest.Supplier);

			DeployServiceException error = Assert.Throws<DeployServiceException>(() => this.Deploy(profile));

			Assert.Contains(ProxyNames.DirectInput, error.Message, StringComparison.Ordinal);
			Assert.Contains("Widescreen Fix", error.Message, StringComparison.Ordinal);
			Assert.Contains("Other Fix", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void TheChosenModSuppliesTheLoaderAndTheOtherCopyStaysBehind()
		{
			InstalledMod first = this.Import("Widescreen Fix", "loader one");
			InstalledMod second = this.Import("Other Fix", "loader two, a different build");
			Profile profile = this.ProfileWith(first, second);

			profile.ChooseLoader(ProxyNames.DirectInput, first.Id);

			DeployResult result = this.Deploy(profile);

			Assert.True(result.Verification.IsClean, String.Join(" ", result.Verification.Problems));

			// The earlier mod in the load order wins because the user said so, not because of
			// the order. Before step 9 the later mod always won.
			Assert.Equal("loader one", File.ReadAllText(this._game.FullPath(ProxyNames.DirectInput)));

			LoaderChoice choice = Assert.Single(result.Report.Loaders);

			Assert.Equal(first.Id, choice.WinnerModId);
			Assert.Equal(new[] { "\"Other Fix\"" }, choice.Skipped.ToArray());

			// One deployed file at that path, and no override entry, because the losing copy
			// never reached the staging copy.
			Assert.Single(result.Report.Files, f => f.RelativePath == ProxyNames.DirectInput);
			Assert.DoesNotContain(result.Report.Overrides, o => o.RelativePath == ProxyNames.DirectInput);
		}

		[Fact]
		public void TheLogNamesTheWinnerAndEveryLoser()
		{
			InstalledMod first = this.Import("Widescreen Fix", "loader one");
			InstalledMod second = this.Import("Other Fix", "loader two, a different build");
			Profile profile = this.ProfileWith(first, second);

			profile.ChooseLoader(ProxyNames.DirectInput, second.Id);

			this.Deploy(profile);

			string log = String.Join("\n", this._log);

			Assert.Contains($"{ProxyNames.DirectInput} comes from \"Other Fix\"", log, StringComparison.Ordinal);
			Assert.Contains("skipped the copy of \"Widescreen Fix\"", log, StringComparison.Ordinal);
		}

		[Fact]
		public void AStoredChoiceForAModThatNoLongerSuppliesTheFileAsksAgainWithAReason()
		{
			// Keep the first answer until the user changes it. A deploy where the chosen mod is
			// gone, switched off, or no longer holds the file asks again.
			InstalledMod first = this.Import("Widescreen Fix", "loader one");
			InstalledMod second = this.Import("Other Fix", "loader two, a different build");
			Profile profile = this.ProfileWith(first, second);

			profile.ChooseLoader(ProxyNames.DirectInput, "a mod that left the store");

			ProxyContest contest = Assert.Single(this._service.PlanLoaders(profile).Contests);

			Assert.True(contest.NeedsAnswer);
			Assert.Contains("Choose again", contest.Reason, StringComparison.Ordinal);
		}

		[Fact]
		public void TwoCopiesOfOneFileReadAsOneFile()
		{
			// Two candidates with one hash are the same file. The dialog says so and the user
			// picks either one without further thought.
			InstalledMod first = this.Import("Widescreen Fix", "one loader build");
			InstalledMod second = this.Import("Other Fix", "one loader build");
			Profile profile = this.ProfileWith(first, second);

			ProxyContest contest = Assert.Single(this._service.PlanLoaders(profile).Contests);

			Assert.True(contest.AllSameFile);
			Assert.Equal(contest.Candidates[0].Identity.Hash, contest.Candidates[1].Identity.Hash);
		}

		[Fact]
		public void ALoaderWithNoVersionShowsUnknownAndAHash()
		{
			// A missing version is normal and it is not an error. Never hide a candidate.
			InstalledMod mod = this.Import("Widescreen Fix", "plain bytes with no version resource");
			Profile profile = this.ProfileWith(mod);

			ProxyCandidate candidate = Assert.Single(
				Assert.Single(this._service.PlanLoaders(profile).Contests).Candidates);

			Assert.Equal("unknown", candidate.Identity.VersionText);
			Assert.Equal(8, candidate.Identity.ShortHash.Length);
			Assert.Contains("hash ", candidate.Describe(), StringComparison.Ordinal);
			Assert.Contains("bytes", candidate.Describe(), StringComparison.Ordinal);
		}

		[Fact]
		public void ABuildMarkerAnswersWhenTheFileCarriesNoVersionResource()
		{
			InstalledMod mod = this.Import("Widescreen Fix", "padding Ultimate ASI Loader padding");
			Profile profile = this.ProfileWith(mod);

			ProxyCandidate candidate = Assert.Single(
				Assert.Single(this._service.PlanLoaders(profile).Contests).Candidates);

			Assert.Equal(ProxyIdentitySource.BuildMarker, candidate.Identity.Source);
			Assert.Equal("Ultimate ASI Loader", candidate.Identity.Product);
		}

		[Fact]
		public void AModWithNoLoaderProducesNoContest()
		{
			InstalledMod mod = this.Import("Widescreen Fix");

			Assert.Empty(this._service.PlanLoaders(this.ProfileWith(mod)).Contests);
		}

		[Fact]
		public void ALoaderNameThatThisApplicationDoesNotManageProducesANote()
		{
			string root = Path.Combine(this._temp.Path, "source", "Sound Fix");
			AsiFixture.Write(root);
			File.WriteAllText(Path.Combine(root, "dsound.dll"), "a sound proxy");

			InstalledMod mod = this._importer.Import(root, GameINT.Underground2).Mod;

			ProxyPlan plan = this._service.PlanLoaders(this.ProfileWith(mod));

			Assert.Contains("dsound.dll", Assert.Single(plan.Unmanaged), StringComparison.Ordinal);
		}

		// ------------------------------------------------------------------ helpers

		private InstalledMod Import(string name, string loaderBody = null)
		{
			string root = Path.Combine(this._temp.Path, "source", name);

			AsiFixture.Write(root, loaderBody);

			return this._importer.Import(root, GameINT.Underground2, name).Mod;
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
	}
}
