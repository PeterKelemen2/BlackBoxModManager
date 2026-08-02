using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Store;
using Endscript.Core;
using Endscript.Helpers;
using Xunit;
using Nikki.Core;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// A throwaway install of any game in the catalog. It writes the executable and every
	/// marker of the descriptor, so the validator passes it.
	///
	/// The files hold text. No test here reads container data, so every one runs on native
	/// Linux with no Wine and no game.
	/// </summary>
	internal sealed class FakeInstall : IDisposable
	{
		public string Root { get; }

		public GameDefinition Definition { get; }

		public FakeInstall(GameINT game)
		{
			this.Definition = GameCatalog.Demand(game);
			this.Root = Path.Combine(Path.GetTempPath(), $"install-test-{Guid.NewGuid():N}",
				this.Definition.DisplayName);

			Directory.CreateDirectory(this.Root);

			this.Write(this.Definition.Executable);

			foreach (string marker in this.Definition.MarkerFiles) this.Write(marker);

			foreach (string container in this.Definition.ContainerFiles) this.Write(container);

			foreach (string marker in this.Definition.MarkerDirectories)
			{
				Directory.CreateDirectory(Path.Combine(this.Root, marker));
			}
		}

		public void Write(string relative)
		{
			string full = Path.Combine(this.Root, relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			File.WriteAllText(full, "text, and not container data");
		}

		public void Delete(string relative)
		{
			File.Delete(Path.Combine(this.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
		}

		public void Dispose()
		{
			try
			{
				Core.Files.FileTree.Delete(Path.GetDirectoryName(this.Root));
			}
			catch (Exception)
			{
				// A leftover temporary directory does not fail a test run.
			}
		}
	}

	/// <summary>
	/// Step 7. The catalog, the detection across games, and the game scope of the store.
	/// </summary>
	public sealed class GameProfileTests
	{
		// ------------------------------------------------------------ the catalog

		[Fact]
		public void TheCatalogHoldsTheGamesThatAListingConfirmed()
		{
			var games = new List<GameINT>();

			foreach (GameDefinition definition in GameCatalog.All) games.Add(definition.Game);

			Assert.Contains(GameINT.Underground2, games);
			Assert.Contains(GameINT.MostWanted, games);
			Assert.Contains(GameINT.Prostreet, games);
		}

		/// <summary>
		/// A target with no descriptor must show up in Absent. That list is what the window
		/// reads to name the games that it does not manage.
		/// </summary>
		[Fact]
		public void EveryTargetIsEitherInTheCatalogOrInAbsent()
		{
			foreach (GameINT game in Core.HashListPaths.SupportedGames)
			{
				bool managed = GameCatalog.Find(game) != null;
				bool absent = new List<GameINT>(GameCatalog.Absent).Contains(game);

				Assert.True(managed ^ absent, $"{game} is in both lists or in neither.");
			}
		}

		[Fact]
		public void AGameWithNoDescriptorThrowsFromDemand()
		{
			foreach (GameINT game in GameCatalog.Absent)
			{
				Assert.Throws<ArgumentOutOfRangeException>(() => GameCatalog.Demand(game));
				Assert.Null(GameCatalog.Find(game));
			}
		}

		/// <summary>
		/// Identify returns one game per directory only while no two descriptors share an
		/// executable name. The lookup ignores letter case, so the comparison does too.
		/// </summary>
		[Fact]
		public void NoTwoGamesShareAnExecutableName()
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (GameDefinition definition in GameCatalog.All)
			{
				Assert.True(seen.Add(definition.Executable),
					$"Two games name the executable {definition.Executable}.");
			}
		}

		[Fact]
		public void EveryDescriptorNamesMarkersAndHints()
		{
			foreach (GameDefinition definition in GameCatalog.All)
			{
				Assert.False(String.IsNullOrWhiteSpace(definition.Executable));
				Assert.NotEmpty(definition.MarkerFiles);
				Assert.NotEmpty(definition.MarkerDirectories);
				Assert.NotEmpty(definition.DirectoryHints);
			}
		}

		// ------------------------------------------------------------ detection

		[Fact]
		public void TheValidatorPassesEveryGameOfTheCatalog()
		{
			foreach (GameDefinition definition in GameCatalog.All)
			{
				using var install = new FakeInstall(definition.Game);

				GameInstallStatus status = GameInstallValidator.Validate(definition.Game, install.Root);

				Assert.True(status.IsUsable, status.Message);
				Assert.Equal(definition.Game, status.Install.Game);
			}
		}

		[Fact]
		public void IdentifyNamesTheGameOfADirectory()
		{
			using var install = new FakeInstall(GameINT.MostWanted);

			IReadOnlyList<GameDefinition> found = GameInstallLocator.Identify(install.Root);

			Assert.Single(found);
			Assert.Equal(GameINT.MostWanted, found[0].Game);
		}

		/// <summary>
		/// Six games look alike from the outside. The message must name the real game, or the
		/// user reads "no executable" and does not know why.
		/// </summary>
		[Fact]
		public void TheValidatorNamesTheGameThatTheDirectoryReallyHolds()
		{
			using var install = new FakeInstall(GameINT.MostWanted);

			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, install.Root);

			Assert.False(status.IsUsable);
			Assert.Equal(GameInstallCheck.ExecutableMissing, status.Check);
			Assert.Contains("Most Wanted", status.Message);
		}

		[Fact]
		public void FindAllReturnsOneEntryPerGame()
		{
			IReadOnlyDictionary<GameINT, IReadOnlyList<string>> found = GameInstallLocator.FindAll();

			Assert.Equal(GameCatalog.All.Count, found.Count);

			foreach (GameDefinition definition in GameCatalog.All)
			{
				Assert.True(found.ContainsKey(definition.Game));
				Assert.NotNull(found[definition.Game]);
			}
		}

		[Fact]
		public void ResolveAllReturnsOneEntryPerGame()
		{
			string file = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");

			try
			{
				var service = new GameInstallService(file);

				Assert.Equal(GameCatalog.All.Count, service.ResolveAll().Count);
			}
			finally
			{
				if (File.Exists(file)) File.Delete(file);
			}
		}

		/// <summary>
		/// A missing container blocks nothing. Only a Binary mod needs one, and an install
		/// that takes drop-in mods alone is still usable.
		/// </summary>
		[Fact]
		public void AMissingContainerIsReportedAndNotRejected()
		{
			using var install = new FakeInstall(GameINT.MostWanted);

			GameInstall game = GameInstallValidator.Validate(GameINT.MostWanted, install.Root).Install;

			Assert.Empty(game.MissingContainers());

			install.Delete(install.Definition.ContainerFiles[0]);

			Assert.True(GameInstallValidator.Validate(GameINT.MostWanted, install.Root).IsUsable);
			Assert.Single(game.MissingContainers());
		}

		// ------------------------------------------------------------ the game scope

		private static ModStore Store(out string root)
		{
			root = Path.Combine(Path.GetTempPath(), $"store-test-{Guid.NewGuid():N}");
			return new ModStore(Path.Combine(root, "mods"));
		}

		private static ModImporter Importer(ModStore store, string root)
		{
			return new ModImporter(store, Path.Combine(root, "import"));
		}

		private static string Plugin(string root, string name)
		{
			string directory = Path.Combine(root, "source", name);
			Directory.CreateDirectory(Path.Combine(directory, "scripts"));
			File.WriteAllText(Path.Combine(directory, "scripts", "plugin.asi"), "the plugin");

			return directory;
		}

		[Fact]
		public void TheImportFilesADropInModUnderTheGameThatTheCallerNamed()
		{
			ModStore store = Store(out string root);

			try
			{
				ModImportResult result = Importer(store, root).Import(
					Plugin(root, "Plugin"), GameINT.Carbon);

				Assert.Equal(GameINT.Carbon, result.Mod.Game);
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		/// <summary>
		/// The manifest decides. Both example mods name Underground 2, so an import that asks
		/// for Most Wanted still produces an Underground 2 mod, plus a note.
		/// </summary>
		[Fact]
		public void TheImportTakesTheGameFromTheManifest()
		{
			ModStore store = Store(out string root);

			try
			{
				ModImportResult result = Importer(store, root).Import(
					ExampleMods.Camera, GameINT.MostWanted);

				Assert.Equal(GameINT.Underground2, result.Mod.Game);
				Assert.Contains(result.Notes, note => note.Contains("Underground2"));
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		[Fact]
		public void TheStoreListsOnlyTheModsOfOneGame()
		{
			ModStore store = Store(out string root);

			try
			{
				ModImporter importer = Importer(store, root);

				importer.Import(Plugin(root, "For Carbon"), GameINT.Carbon);
				importer.Import(Plugin(root, "For Most Wanted"), GameINT.MostWanted);

				Assert.Equal(2, store.List().Count);
				Assert.Single(store.List(GameINT.Carbon));
				Assert.Equal("For Carbon", store.List(GameINT.Carbon)[0].Name);
				Assert.Empty(store.List(GameINT.Underground2));
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		/// <summary>
		/// The store held mods with no game before metadata version 2. Hiding one of them
		/// would read as a store that lost it.
		/// </summary>
		[Fact]
		public void AModWithNoGameShowsUnderEveryGame()
		{
			ModStore store = Store(out string root);

			try
			{
				InstalledMod mod = Importer(store, root).Import(Plugin(root, "Old"), GameINT.Carbon).Mod;

				mod.Manifest.Game = null;
				store.Save(mod);

				Assert.Null(store.Find(mod.Id).Game);
				Assert.Single(store.List(GameINT.Carbon));
				Assert.Single(store.List(GameINT.Underground2));
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		[Fact]
		public void AssignGivesADropInModAGame()
		{
			ModStore store = Store(out string root);

			try
			{
				InstalledMod mod = Importer(store, root).Import(Plugin(root, "Old"), GameINT.Carbon).Mod;

				store.Assign(mod, GameINT.Prostreet);

				Assert.Equal(GameINT.Prostreet, store.Find(mod.Id).Game);
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		[Fact]
		public void AssignRefusesABinaryMod()
		{
			ModStore store = Store(out string root);

			try
			{
				InstalledMod mod = Importer(store, root).Import(
					ExampleMods.Camera, GameINT.Underground2).Mod;

				Assert.Throws<ArgumentException>(() => store.Assign(mod, GameINT.MostWanted));
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		// ------------------------------------------------------------ deploy and revert

		/// <summary>
		/// A drop-in mod deploys and reverts on every game of the catalog.
		///
		/// The link engine and the staging code read a descriptor and nothing else, so this
		/// covers the new games without a mod sample for them. A Binary mod of Most Wanted or
		/// of ProStreet needs a real sample. See the Results section of 07-game-profiles.md.
		/// </summary>
		[Theory]
		[InlineData(GameINT.MostWanted)]
		[InlineData(GameINT.Prostreet)]
		[InlineData(GameINT.Underground2)]
		public void ADropInModDeploysAndRevertsOnEveryGame(GameINT game)
		{
			using var install = new FakeInstall(game);
			ModStore store = Store(out string root);

			try
			{
				InstalledMod mod = Importer(store, root).Import(Plugin(root, "Plugin"), game).Mod;

				var profile = new Core.Profiles.Profile("Test", game.ToString());
				profile.Ensure(mod.Id).Enabled = true;

				var service = new Core.Deploy.DeployService(store);
				GameInstall target = GameInstallValidator.Validate(game, install.Root).Install;

				Core.Deploy.DeployResult result = service.Deploy(target, profile, true, line => { });

				Assert.True(result.Verification.IsClean);
				Assert.Single(result.Report.Files);
				Assert.True(File.Exists(Path.Combine(install.Root, "scripts", "plugin.asi")));

				service.Revert(target, line => { });

				Assert.False(File.Exists(Path.Combine(install.Root, "scripts", "plugin.asi")));
				Assert.True(File.Exists(Path.Combine(install.Root, install.Definition.Executable)));
			}
			finally
			{
				Core.Files.FileTree.Delete(root);
			}
		}

		// ------------------------------------------------------------ the link audit

		[Fact]
		public void TheExampleModsMatchTheExpectedLinkSetOfUnderground2()
		{
			GameDefinition definition = GameCatalog.Demand(GameINT.Underground2);

			foreach (string path in new[] { ExampleMods.Camera, ExampleMods.OneLap })
			{
				ModPackage package = ModPackageReader.Read(path);

				Assert.True(ManifestLinkAudit.HasExpectation(definition));
				Assert.Empty(ManifestLinkAudit.Run(package, definition));
			}
		}

		/// <summary>
		/// A game with no recorded expectation reports nothing. Silence there means "not
		/// checked" and never "clean", so the caller has to read HasExpectation.
		/// </summary>
		[Fact]
		public void AGameWithNoExpectedSetReportsNothing()
		{
			GameDefinition definition = GameCatalog.Demand(GameINT.MostWanted);

			Assert.False(ManifestLinkAudit.HasExpectation(definition));
			Assert.Empty(ManifestLinkAudit.Compare("any", Manifest(Extra()), definition));
		}

		[Fact]
		public void AnExtraLinkIsADeviation()
		{
			GameDefinition definition = GameCatalog.Demand(GameINT.Underground2);
			List<SubLoader> links = Expected(definition);
			links.Add(Extra()[0]);

			IReadOnlyList<LinkDeviation> found = ManifestLinkAudit.Compare(
				"the variant", Manifest(links), definition);

			Assert.Single(found);
			Assert.NotNull(found[0].Extra);
			Assert.Null(found[0].Missing);
			Assert.Contains("GLOBAL\\extra.bin", found[0].ToString());
		}

		[Fact]
		public void AMissingLinkIsADeviation()
		{
			GameDefinition definition = GameCatalog.Demand(GameINT.Underground2);
			List<SubLoader> links = Expected(definition);
			links.RemoveAt(0);

			IReadOnlyList<LinkDeviation> found = ManifestLinkAudit.Compare(
				"the variant", Manifest(links), definition);

			Assert.Single(found);
			Assert.Null(found[0].Extra);
			Assert.NotNull(found[0].Missing);
		}

		/// <summary>
		/// Two manifests spell one path differently. The audit must not call that a deviation.
		/// </summary>
		[Fact]
		public void TheAuditIgnoresLetterCaseAndTheSeparator()
		{
			GameDefinition definition = GameCatalog.Demand(GameINT.Underground2);
			List<SubLoader> links = new List<SubLoader>();

			foreach (ManifestLink link in definition.ExpectedLinks)
			{
				links.Add(new SubLoader
				{
					LoadType = link.LoadType.ToUpperInvariant(),
					PathType = link.PathType,
					File = link.File.Replace('\\', '/').ToLowerInvariant(),
				});
			}

			Assert.Empty(ManifestLinkAudit.Compare("the variant", Manifest(links), definition));
		}

		private static List<SubLoader> Expected(GameDefinition definition)
		{
			var links = new List<SubLoader>();

			foreach (ManifestLink link in definition.ExpectedLinks)
			{
				links.Add(new SubLoader
				{
					LoadType = link.LoadType,
					PathType = link.PathType,
					File = link.File,
				});
			}

			return links;
		}

		private static List<SubLoader> Extra()
		{
			return new List<SubLoader>
			{
				new SubLoader { LoadType = "Attributes", PathType = "Absolute", File = @"GLOBAL\extra.bin" },
			};
		}

		private static Launch Manifest(List<SubLoader> links)
		{
			return new Launch { Links = links };
		}
	}
}
