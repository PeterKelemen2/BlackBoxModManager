using System;
using System.IO;
using System.IO.Compression;
using BlackboxModManager.Core.Store;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the mod store and the import of step 5.3.
	/// </summary>
	public class ModStoreTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public ModStoreTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose() => this._temp.Dispose();

		private string MakeSource(string name, params (string Path, string Content)[] files)
		{
			string root = Path.Combine(this._temp.Path, "source", name);

			foreach ((string path, string content) in files)
			{
				string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllText(full, content);
			}

			return root;
		}

		[Fact]
		public void AnAsiPluginImportsAsAnAsiMod()
		{
			string source = this.MakeSource("Widescreen Fix", ("scripts/widescreen.asi", "plugin"));

			ModImportResult result = this._importer.Import(source);

			Assert.Equal(ModKind.Asi, result.Mod.Kind);
			Assert.Equal("Widescreen Fix", result.Mod.Name);
			Assert.Equal("widescreen-fix", result.Mod.Id);
			Assert.True(File.Exists(Path.Combine(result.Mod.ContentRoot, "scripts", "widescreen.asi")));
		}

		[Fact]
		public void AFolderWithNoMarkerImportsAsLooseFiles()
		{
			string source = this.MakeSource("Texture Pack", ("CARS/car.bin", "a car"));

			ModImportResult result = this._importer.Import(source);

			Assert.Equal(ModKind.LooseFiles, result.Mod.Kind);
			Assert.Single(result.Content.Files);
		}

		[Fact]
		public void AManifestImportsAsABinaryMod()
		{
			var mod = new TempDirectory();

			try
			{
				mod.WriteManifest("Install.end", "Underground2", "script.end");
				mod.WriteScript("script.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4");

				ModImportResult result = this._importer.Import(mod.Path);

				Assert.Equal(ModKind.Binary, result.Mod.Kind);
				Assert.Single(result.Content.Manifests);
				Assert.Equal("Underground2", result.Mod.Manifest.Game);
			}
			finally
			{
				mod.Dispose();
			}
		}

		/// <summary>
		/// An archive wraps its content in one directory. That wrapper is not part of the
		/// game path, so the import drops it.
		/// </summary>
		[Fact]
		public void TheImportDropsAWrapperDirectory()
		{
			string source = this.MakeSource("Wrapped", ("Wrapped v1.0/scripts/plugin.asi", "plugin"));

			ModImportResult result = this._importer.Import(source);

			Assert.Equal(new[] { "scripts/plugin.asi" }, result.Content.Files);
		}

		/// <summary>
		/// A level that holds a file stops the walk. A wrong guess there shifts every
		/// deployed file by one directory.
		/// </summary>
		[Fact]
		public void TheImportKeepsADirectoryThatSitsBesideAFile()
		{
			string source = this.MakeSource("Mixed",
				("readme.txt", "read me"),
				("GLOBAL/GLOBALA.BUN", "container"));

			ModImportResult result = this._importer.Import(source);

			Assert.Contains("readme.txt", result.Content.Files);
			Assert.Contains("GLOBAL/GLOBALA.BUN", result.Content.Files);
		}

		[Fact]
		public void TwoModsOfOneNameGetTwoIdentifiers()
		{
			string first = this.MakeSource("Same Name", ("a.asi", "one"));
			InstalledMod one = this._importer.Import(first).Mod;

			string second = Path.Combine(this._temp.Path, "second", "Same Name");
			Directory.CreateDirectory(second);
			File.WriteAllText(Path.Combine(second, "b.asi"), "two");
			InstalledMod other = this._importer.Import(second).Mod;

			Assert.NotEqual(one.Id, other.Id);
			Assert.Equal(2, this._store.List().Count);
		}

		[Fact]
		public void TheStoreReadsBackWhatTheImportWrote()
		{
			string source = this.MakeSource("Round Trip", ("scripts/x.asi", "plugin"));
			InstalledMod written = this._importer.Import(source).Mod;

			InstalledMod read = this._store.Find(written.Id);

			Assert.NotNull(read);
			Assert.Equal(written.Name, read.Name);
			Assert.Equal(ModKind.Asi, read.Kind);
		}

		[Fact]
		public void RemoveTakesTheModAndItsFiles()
		{
			string source = this.MakeSource("Throwaway", ("x.asi", "plugin"));
			InstalledMod mod = this._importer.Import(source).Mod;

			this._store.Remove(mod.Id);

			Assert.Null(this._store.Find(mod.Id));
			Assert.False(Directory.Exists(mod.Root));
		}

		[Fact]
		public void AnEmptySourceFails()
		{
			string source = Path.Combine(this._temp.Path, "empty");
			Directory.CreateDirectory(source);

			Assert.Throws<ModImportException>(() => this._importer.Import(source));
		}

		[Fact]
		public void AZipArchiveImports()
		{
			string archive = Path.Combine(this._temp.Path, "Zipped Mod.zip");

			using (var stream = new FileStream(archive, FileMode.Create))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				using var writer = new StreamWriter(zip.CreateEntry("scripts/zipped.asi").Open());
				writer.Write("plugin");
			}

			ModImportResult result = this._importer.Import(archive);

			Assert.Equal("Zipped Mod", result.Mod.Name);
			Assert.Equal(ModKind.Asi, result.Mod.Kind);
		}

		/// <summary>
		/// An entry name comes from the internet. A name that walks out of the target
		/// directory must stop the extraction.
		/// </summary>
		[Fact]
		public void AnEntryThatWritesOutsideTheTargetStopsTheExtraction()
		{
			string archive = Path.Combine(this._temp.Path, "Evil.zip");

			using (var stream = new FileStream(archive, FileMode.Create))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				using var writer = new StreamWriter(zip.CreateEntry("../escaped.asi").Open());
				writer.Write("plugin");
			}

			string target = Path.Combine(this._temp.Path, "extract");

			ArchiveReadException error = Assert.Throws<ArchiveReadException>(
				() => ArchiveExtractor.Extract(archive, target));

			Assert.Contains("outside the target directory", error.Message);
			Assert.False(File.Exists(Path.Combine(this._temp.Path, "escaped.asi")));
		}

		[Theory]
		[InlineData("NFSU2 - 1 Lap URL", "nfsu2-1-lap-url")]
		[InlineData("  ", "mod")]
		[InlineData("///", "mod")]
		[InlineData("Mod v1.0", "mod-v1.0")]
		public void TheSlugKeepsOnlySafeCharacters(string name, string expected)
		{
			Assert.Equal(expected, ModStore.Slug(name));
		}
	}
}
