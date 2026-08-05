using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Endscript.Helpers;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the merged load of step 6.1 and the conflict check of step 6.4.
	///
	/// Every test reads the real example mods and builds no container, so the whole file runs
	/// on native Linux with no Wine and no game.
	/// </summary>
	public class MergedLoadTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public MergedLoadTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose() => this._temp.Dispose();

		private InstalledMod Import(string source) => this._importer.Import(source, GameINT.Underground2).Mod;

		/// <summary>
		/// Builds a profile that switches on the named variants of one mod.
		/// </summary>
		private static Profile ProfileWith(InstalledMod mod, params string[] variants)
		{
			var profile = new Profile("Test", nameof(GameINT.Underground2));
			ProfileEntry entry = profile.Ensure(mod.Id);
			entry.Enabled = true;

			foreach (string variant in variants) entry.Selections.Ensure(variant).Enabled = true;

			return profile;
		}

		private IReadOnlyList<EnabledVariant> Read(Profile profile) =>
			VariantReader.Read(profile, this._store, GameINT.Underground2);

		/// <summary>
		/// The two options of the camera mod, by the names that the script gives them.
		/// The option name is the quoted string in the combobox line, not the file name of
		/// the block that it appends.
		/// </summary>
		private const string CameraInstall = "Install Camera Mod [NFSMW TO U2]";

		private const string CameraRestore = "Restore original camera settings";

		/// <summary>
		/// Writes the game files that the manifest links name. Every manifest of both example
		/// mods carries the same four, and a missing one stops the build on purpose.
		/// </summary>
		private string Staging()
		{
			string root = Path.Combine(this._temp.Path, "staging");

			foreach (string file in new[]
			{
				@"GLOBAL\attributes.bin", @"GLOBAL\fe_attrib.bin",
				@"LANGUAGES\Labels_Global.bin", @"LANGUAGES\Labels.bin",
			})
			{
				string path = Path.Combine(root, file.Replace('\\', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, "game data");
			}

			return root;
		}

		// ---------------------------------------------------------------- the variants

		[Fact]
		public void OnlyTheVariantsThatTheProfileSwitchesOnApply()
		{
			InstalledMod mod = this.Import(ExampleMods.OneLap);

			IReadOnlyList<EnabledVariant> variants = this.Read(ProfileWith(mod, "1 Lap URL Races"));

			Assert.Single(variants);
			Assert.Equal("1 Lap URL Races", variants[0].Variant.Name);
			Assert.Equal(1, variants[0].Order);
		}

		[Fact]
		public void AVariantThatKeepsItsAnswersStaysOffWhenTheFlagIsOff()
		{
			InstalledMod mod = this.Import(ExampleMods.OneLap);
			Profile profile = ProfileWith(mod, "1 Lap URL Races");

			// The user switched this one off and the answers stay in the file.
			profile.Find(mod.Id).Selections.Ensure("1 Lap SUV Races").Enabled = false;

			IReadOnlyList<EnabledVariant> variants = this.Read(profile);

			Assert.Single(variants);
			Assert.Equal("1 Lap URL Races", variants[0].Variant.Name);
		}

		[Fact]
		public void AModWithNoVariantSwitchedOnStopsTheDeploy()
		{
			InstalledMod mod = this.Import(ExampleMods.OneLap);
			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(mod.Id).Enabled = true;

			DeployServiceException error = Assert.Throws<DeployServiceException>(() => this.Read(profile));

			Assert.Contains("no variant switched on", error.Message);
		}

		[Fact]
		public void AVariantNameThatTheModNoLongerHoldsStopsTheDeploy()
		{
			InstalledMod mod = this.Import(ExampleMods.OneLap);

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => this.Read(ProfileWith(mod, "1 Lap MOON Races")));

			Assert.Contains("no longer holds it", error.Message);
		}

		[Fact]
		public void TheLoadOrderFollowsTheProfileEntryOrder()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			InstalledMod camera = this.Import(ExampleMods.Camera);

			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(camera.Id).Enabled = true;
			profile.Find(camera.Id).Selections.Ensure("Install").Enabled = true;
			profile.Ensure(lap.Id).Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap URL Races").Enabled = true;

			IReadOnlyList<EnabledVariant> variants = this.Read(profile);

			Assert.Equal(2, variants.Count);
			Assert.Equal(camera.Id, variants[0].Mod.Id);
			Assert.Equal(lap.Id, variants[1].Mod.Id);
		}

		// ---------------------------------------------------------------- the union

		/// <summary>
		/// Both example mods declare GLOBAL\GLOBALB.LZC. The union must hold it once.
		/// A second entry makes AddNew throw, or it builds two containers for one file.
		/// </summary>
		[Fact]
		public void TheUnionHoldsOneEntryPerContainer()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			InstalledMod camera = this.Import(ExampleMods.Camera);

			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(lap.Id).Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap URL Races").Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap SUV Races").Enabled = true;
			profile.Ensure(camera.Id).Enabled = true;
			profile.Find(camera.Id).Selections.Ensure("Install").Enabled = true;

			MergedLoad merged = MergedLaunch.Build(this.Read(profile), this.Staging());

			Assert.Equal(new[] { @"GLOBAL\GLOBALA.BUN", @"GLOBAL\GLOBALB.LZC" }, merged.Files);

			// Three variants asked for GLOBALB.LZC. The union names all three.
			Assert.Equal(3, merged.Contributors[@"GLOBAL\GLOBALB.LZC"].Count);
		}

		[Fact]
		public void TheUnionKeepsTheSpellingOfTheManifest()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);

			MergedLoad merged = MergedLaunch.Build(
				this.Read(ProfileWith(lap, "1 Lap URL Races")), this.Staging());

			// The library matches this name as plain text. A normalized spelling would make
			// every command of the script fail its lookup.
			Assert.Contains(@"GLOBAL\GLOBALB.LZC", merged.Files);
		}

		[Fact]
		public void TheMergedManifestPointsAtTheStagingCopy()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);

			string staging = this.Staging();
			MergedLoad merged = MergedLaunch.Build(
				this.Read(ProfileWith(lap, "1 Lap URL Races")), staging);

			Assert.Equal(Path.TrimEndingDirectorySeparator(staging), merged.Launch.Directory);
			Assert.Equal(nameof(Endscript.Enums.eUsage.Modder), merged.Launch.Usage);
			Assert.Equal(nameof(GameINT.Underground2), merged.Launch.Game);

			// Every variant brings its own script, so the merged manifest names none.
			Assert.Equal(String.Empty, merged.Launch.Endscript);
		}

		/// <summary>
		/// Every link of both example mods is Absolute, so it resolves against the staging
		/// directory. The merged manifest stores the resolved path, because one synthetic
		/// manifest cannot hold the ThisDir of several mods.
		/// </summary>
		[Fact]
		public void TheUnionResolvesEveryLinkToAFullPath()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			Profile profile = ProfileWith(lap, "1 Lap URL Races");

			MergedLoad merged = MergedLaunch.Build(this.Read(profile), this.Staging());

			Assert.Equal(4, merged.Launch.Links.Count);

			foreach (SubLoader link in merged.Launch.Links)
			{
				Assert.True(Path.IsPathRooted(link.File), $"The link {link.File} is not a full path.");
				Assert.True(File.Exists(link.File), $"The link {link.File} does not exist.");
			}
		}

		/// <summary>
		/// A missing link file is normal. Binary writes the same four links into every
		/// manifest of one game, and a vanilla Underground 2 install holds only
		/// LANGUAGES\Labels.bin of them. Every loader in Nikki returns for a file that does
		/// not exist, so the deploy leaves the link out and says so once.
		/// </summary>
		[Fact]
		public void AMissingLinkFileProducesANoteAndNotAFailure()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);

			MergedLoad merged = MergedLaunch.Build(
				this.Read(ProfileWith(lap, "1 Lap URL Races", "1 Lap SUV Races")),
				Path.Combine(this._temp.Path, "empty"));

			Assert.Empty(merged.Launch.Links);
			Assert.Equal(4, merged.Notes.Count);
			Assert.Contains(merged.Notes, note => note.Contains("attributes.bin"));

			// The union still holds the containers, so the deploy can still run.
			Assert.Contains(@"GLOBAL\GLOBALB.LZC", merged.Files);
		}

		/// <summary>
		/// Two variants of one mod name the same links. The note has to appear once.
		/// </summary>
		[Fact]
		public void ASkippedLinkProducesOneNoteForEveryVariant()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			InstalledMod camera = this.Import(ExampleMods.Camera);

			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(lap.Id).Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap URL Races").Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap ALL Races").Enabled = true;
			profile.Ensure(camera.Id).Enabled = true;
			profile.Find(camera.Id).Selections.Ensure("Install").Enabled = true;

			MergedLoad merged = MergedLaunch.Build(
				this.Read(profile), Path.Combine(this._temp.Path, "empty"));

			// Four links, three variants, and four notes.
			Assert.Equal(4, merged.Notes.Count);
		}

		// ---------------------------------------------------------------- the conflicts

		/// <summary>
		/// The ALL variant is the union of the other four, so ALL beside URL writes the same
		/// field the same value. That is not a conflict, and reporting it would teach a user
		/// to ignore the list.
		/// </summary>
		[Fact]
		public void TwoVariantsThatAgreeProduceNoConflict()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);

			ConflictReport report = ConflictPreflight.Run(
				this.Read(ProfileWith(lap, "1 Lap ALL Races", "1 Lap URL Races")));

			Assert.True(report.IsClean);
			Assert.Equal(2, report.CheckedVariants);
			Assert.True(report.KeyedEdits > 0);
		}

		/// <summary>
		/// The two example mods edit different managers, CarTypeInfos against GCareers, so
		/// the success criterion of the brief needs this to report nothing.
		/// </summary>
		[Fact]
		public void TheTwoExampleModsDoNotConflict()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			InstalledMod camera = this.Import(ExampleMods.Camera);

			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(lap.Id).Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap URL Races").Enabled = true;
			profile.Ensure(camera.Id).Enabled = true;
			profile.Find(camera.Id).Selections.Ensure("Install").Enabled = true;

			ConflictReport report = ConflictPreflight.Run(this.Read(profile));

			Assert.True(report.IsClean);
			Assert.Empty(report.Unchecked);
		}

		/// <summary>
		/// The later variant in the load order wins, because every mod applies to one loaded
		/// profile in order and the last write wins.
		/// </summary>
		[Fact]
		public void TheLaterVariantWinsAConflict()
		{
			InstalledMod camera = this.Import(ExampleMods.Camera);

			// The camera mod holds one combobox. Its two options write the same fields with
			// different values, so two copies of the mod with different answers disagree.
			InstalledMod other = this._importer.Import(ExampleMods.Camera, GameINT.Underground2, "Camera again").Mod;

			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(camera.Id).Enabled = true;
			VariantSelection first = profile.Find(camera.Id).Selections.Ensure("Install");
			first.Enabled = true;
			first.Choose(0, CameraRestore);

			profile.Ensure(other.Id).Enabled = true;
			VariantSelection second = profile.Find(other.Id).Selections.Ensure("Install");
			second.Enabled = true;
			second.Choose(0, CameraInstall);

			ConflictReport report = ConflictPreflight.Run(this.Read(profile));

			Assert.NotEmpty(report.Conflicts);
			Assert.All(report.Conflicts, entry => Assert.Contains("Camera again", entry.Winner));
			Assert.All(report.Conflicts, entry => Assert.Contains(camera.Name, entry.Loser));
		}

		[Fact]
		public void TheConflictCheckNeverThrowsForAVariantThatItCannotRead()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			IReadOnlyList<EnabledVariant> variants = this.Read(ProfileWith(lap, "1 Lap URL Races"));

			// Take the script away. The check has to report the variant and carry on.
			File.Delete(Path.Combine(lap.ContentRoot, "MOD", "URL.end"));

			ConflictReport report = ConflictPreflight.Run(variants);

			Assert.Single(report.Unchecked);
			Assert.True(report.IsClean);
		}

		// ---------------------------------------------------------------- the command gate

		/// <summary>
		/// The gate sits inside the deploy engine and not only in the preflight. A caller that
		/// skips the preflight must not be able to skip the rule.
		/// </summary>
		[Fact]
		public void TheGateStopsAModThatUsesARefusedCommand()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			IReadOnlyList<EnabledVariant> variants = this.Read(ProfileWith(lap, "1 Lap URL Races"));

			this.AppendLine(lap, Path.Combine("MOD", "URL.end"), "stop_errors true");

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => CommandGate.Check(variants, this.Staging()));

			Assert.Contains("stop_errors", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void TheGateStopsAModThatWritesOutsideStaging()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			IReadOnlyList<EnabledVariant> variants = this.Read(ProfileWith(lap, "1 Lap URL Races"));

			this.AppendLine(lap, Path.Combine("MOD", "URL.end"), "erase_file absolute ..\\..\\important.txt");

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => CommandGate.Check(variants, this.Staging()));

			Assert.Contains("outside the staging copy", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void TheGateLetsTheExampleModsThrough()
		{
			InstalledMod lap = this.Import(ExampleMods.OneLap);
			InstalledMod camera = this.Import(ExampleMods.Camera);

			var profile = new Profile("Test", nameof(GameINT.Underground2));
			profile.Ensure(lap.Id).Enabled = true;
			profile.Find(lap.Id).Selections.Ensure("1 Lap URL Races").Enabled = true;
			profile.Ensure(camera.Id).Enabled = true;
			profile.Find(camera.Id).Selections.Ensure("Install").Enabled = true;

			CommandGate.Check(this.Read(profile), this.Staging());
		}

		/// <summary>Adds one line to the end of a script of an imported mod.</summary>
		private void AppendLine(InstalledMod mod, string relative, string line)
		{
			string path = Path.Combine(mod.ContentRoot, relative);

			File.AppendAllText(path, line + Environment.NewLine);
		}
	}
}
