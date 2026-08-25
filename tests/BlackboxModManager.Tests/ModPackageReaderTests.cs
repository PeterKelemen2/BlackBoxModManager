using System;
using System.IO;
using System.Linq;
using BlackboxModManager.Core.Mods;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers 4.1 variant discovery and 4.2 option extraction.
	/// </summary>
	public class ModPackageReaderTests
	{
		// ------------------------------------------------------------------ the 1 Lap mod

		[ExampleModsFact]
		public void TheOneLapFolderIsOnePackageWithFiveVariants()
		{
			// A folder with five manifests is one package with five variants. It is not
			// five unrelated mods.
			//
			// The roadmap and the brief both say "all four 1 Lap manifests". The folder
			// holds five: ALL, CIRCUIT, STREET, SUV, and URL.
			ModPackage package = ModPackageReader.Read(ExampleMods.OneLap);

			Assert.Equal(5, package.Variants.Count);
			Assert.Empty(package.Problems);
			Assert.True(package.IsInstallable);
			Assert.Equal(ExampleMods.OneLapFolder, package.Name);
		}

		[ExampleModsFact]
		public void TheOneLapVariantsCarryTheirManifestNames()
		{
			ModPackage package = ModPackageReader.Read(ExampleMods.OneLap);

			Assert.Equal(
				new[]
				{
					"1 Lap ALL Races", "1 Lap CIRCUIT Races", "1 Lap STREET Races",
					"1 Lap SUV Races", "1 Lap URL Races",
				},
				package.Variants.Select(v => v.Name).ToArray());
		}

		[ExampleModsFact]
		public void EveryOneLapVariantIsInstallableAndNamesUnderground2()
		{
			ModPackage package = ModPackageReader.Read(ExampleMods.OneLap);

			Assert.All(package.Variants, v =>
			{
				Assert.Equal(ModVariantState.Ok, v.State);
				Assert.Equal(GameINT.Underground2, v.Game);
			});
		}

		[ExampleModsFact]
		public void TheOneLapVariantsAskNoQuestion()
		{
			ModPackage package = ModPackageReader.Read(ExampleMods.OneLap);

			Assert.All(package.Variants, v => Assert.Empty(v.OptionSets));
		}

		[ExampleModsFact]
		public void TheReaderFindsAManifestByItsHeaderAndNotByItsExtension()
		{
			// The folder holds five VERSN1 manifests and five VERSN2 scripts under MOD.
			// Every one of the ten uses the .end extension.
			ModPackage package = ModPackageReader.Read(ExampleMods.OneLap);

			Assert.Equal(10, Directory.GetFiles(ExampleMods.OneLap, "*.end", SearchOption.AllDirectories).Length);
			Assert.Equal(5, package.Variants.Count);
		}

		// ------------------------------------------------------------------ the camera mod

		[ExampleModsFact]
		public void TheCameraFolderIsOnePackageWithOneVariant()
		{
			ModPackage package = ModPackageReader.Read(ExampleMods.Camera);

			Assert.Single(package.Variants);
			Assert.Empty(package.Problems);
			Assert.Equal("Install", package.Variants[0].Name);
		}

		[ExampleModsFact]
		public void TheCameraVariantAsksOneComboboxQuestion()
		{
			ModVariant variant = ModPackageReader.Read(ExampleMods.Camera).Variants[0];

			ModOptionSet set = Assert.Single(variant.OptionSets);

			Assert.Equal(0, set.Ordinal);
			Assert.Equal(ModOptionKind.Combobox, set.Kind);
			Assert.Equal("Choose option you needeed", set.Description);
			Assert.Equal("script.end", set.SourceFile);
			Assert.Equal(2, set.SourceLine);
		}

		[ExampleModsFact]
		public void TheCameraOptionNamesComeFromTheScriptInOrder()
		{
			// The names carry spaces and brackets. A plain split on a space would break
			// them, which is why the tokenizer of the library toggles on quotes.
			ModVariant variant = ModPackageReader.Read(ExampleMods.Camera).Variants[0];
			ModOptionSet set = variant.OptionSets[0];

			Assert.Equal(
				new[] { "Install Camera Mod [NFSMW TO U2]", "Restore original camera settings" },
				set.Options.Select(o => o.Name).ToArray());

			Assert.Equal(new[] { 0, 1 }, set.Options.Select(o => o.Index).ToArray());
		}

		[ExampleModsFact]
		public void FindLocatesAnOptionByName()
		{
			ModOptionSet set = ModPackageReader.Read(ExampleMods.Camera).Variants[0].OptionSets[0];

			Assert.Equal(1, set.Find("Restore original camera settings").Index);
			Assert.Null(set.Find("no such option"));
		}

		// ------------------------------------------------------------------ failures

		[Fact]
		public void AMissingDirectoryBecomesAPackageProblemAndNotAnException()
		{
			ModPackage package = ModPackageReader.Read(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

			Assert.Empty(package.Variants);
			Assert.Single(package.Problems);
			Assert.False(package.IsInstallable);
		}

		[Fact]
		public void AFolderWithNoManifestReportsThat()
		{
			using var temp = new TempDirectory();
			File.WriteAllText(Path.Combine(temp.Path, "readme.txt"), "nothing here");

			ModPackage package = ModPackageReader.Read(temp.Path);

			Assert.Empty(package.Variants);
			Assert.Contains(package.Problems, p => p.Contains("[VERSN1]", StringComparison.Ordinal));
		}

		[Fact]
		public void AnUnknownVersionHeaderBecomesAPackageProblemThatNamesTheFile()
		{
			using var temp = new TempDirectory();
			temp.WriteManifest("Good.end", "Underground2", "Script.end");
			File.WriteAllText(Path.Combine(temp.Path, "Strange.end"), "[VERSN7]\nwho knows\n");
			temp.WriteScript("Script.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			ModPackage package = ModPackageReader.Read(temp.Path);

			Assert.Single(package.Variants);
			Assert.Contains(package.Problems, p => p.Contains("Strange.end", StringComparison.Ordinal)
				&& p.Contains("VERSN7", StringComparison.Ordinal));
		}

		[Fact]
		public void AVersion2ScriptAndAVersion3MenuAreNotProblems()
		{
			// Both sit beside a manifest by design.
			using var temp = new TempDirectory();
			temp.WriteManifest("Good.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");
			File.WriteAllText(Path.Combine(temp.Path, "Menu.end"), "[VERSN3]\n<xml/>\n");

			ModPackage package = ModPackageReader.Read(temp.Path);

			Assert.Empty(package.Problems);
		}

		[Fact]
		public void AnUnsupportedGameMarksTheVariantAndDoesNotInstallItHopefully()
		{
			using var temp = new TempDirectory();
			temp.WriteManifest("Alien.end", "SomeOtherGame", "Script.end");
			temp.WriteScript("Script.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];

			Assert.Equal(ModVariantState.UnsupportedGame, variant.State);
			Assert.False(variant.IsInstallable);
			Assert.Contains("SomeOtherGame", variant.Problem, StringComparison.Ordinal);
		}

		[Fact]
		public void AMissingScriptMarksTheVariantAndLeavesItsSiblingsAlone()
		{
			using var temp = new TempDirectory();
			temp.WriteManifest("Broken.end", "Underground2", "NoSuchScript.end");
			temp.WriteManifest("Working.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			ModPackage package = ModPackageReader.Read(temp.Path);

			Assert.Equal(ModVariantState.BadScript, package.Find("Broken").State);
			Assert.Equal(ModVariantState.Ok, package.Find("Working").State);
			Assert.True(package.IsInstallable);
		}

		[Fact]
		public void ACheckboxProducesTheTwoFixedNames()
		{
			// The two block names are fixed. Display text is a UI concern.
			using var temp = new TempDirectory();
			temp.WriteManifest("Box.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end",
				"checkbox \"Turn the thing on\"",
				"disabled",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 0",
				"enabled",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
				"end");

			ModOptionSet set = ModPackageReader.Read(temp.Path).Variants[0].OptionSets[0];

			Assert.Equal(ModOptionKind.Checkbox, set.Kind);
			Assert.Equal("Turn the thing on", set.Description);
			Assert.Equal(new[] { "disabled", "enabled" }, set.Options.Select(o => o.Name).ToArray());
		}

		[Fact]
		public void AnIfStatementNeverBecomesAQuestion()
		{
			// IfStatementCommand carries ISelectable and never pauses. ProcessScript
			// evaluates it inline, so the user must never see it as an option.
			using var temp = new TempDirectory();
			temp.WriteManifest("Cond.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end",
				"if collection_exists GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT",
				"do",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT A 1",
				"else",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT A 2",
				"end");

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];

			Assert.Equal(ModVariantState.Ok, variant.State);
			Assert.Empty(variant.OptionSets);
		}
	}
}
