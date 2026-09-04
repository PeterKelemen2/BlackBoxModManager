using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using BlackboxModManager.Core.Store;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the two guards of an import.
	///
	/// <b>An archive comes from the internet, so its entry names are not trustworthy.</b> The
	/// listing guard refuses a name that leaves the target directory, and it refuses an entry
	/// that names a link. A real link in the target directory lets a later entry write
	/// outside the target, and no name test sees that write. See step 19, Part 5.
	///
	/// A cancel is the other rule. The user presses Cancel, and the import stops and adds no
	/// mod to the store. See step 19, Part 4.
	/// </summary>
	public class ArchiveGuardTests
	{
		/// <summary>
		/// Writes an archive that holds one file and one symbolic link to the parent
		/// directory.
		///
		/// The file is a tar, and the name carries the 7z extension. SharpCompress opens an
		/// archive by its header, so the reader that the guard uses still reads it. A tar is
		/// the one link-carrying format that the base class library writes, and a test needs
		/// no external program for it.
		/// </summary>
		private static string WriteArchiveWithALink(string path)
		{
			using FileStream file = File.Create(path);
			using var writer = new TarWriter(file, TarEntryFormat.Pax);

			var plain = new PaxTarEntry(TarEntryType.RegularFile, "readme.txt");
			plain.DataStream = new MemoryStream(new byte[] { 0x61 });
			writer.WriteEntry(plain);

			var link = new PaxTarEntry(TarEntryType.SymbolicLink, "escape")
			{
				LinkName = "../../..",
			};
			writer.WriteEntry(link);

			return path;
		}

		[Fact]
		public void AnArchiveThatHoldsALinkDoesNotExtract()
		{
			using var temp = new TempDirectory();

			string archive = WriteArchiveWithALink(temp.File("linked.7z"));
			string target = Path.Combine(temp.Path, "out");

			ArchiveReadException error = Assert.Throws<ArchiveReadException>(
				() => ArchiveExtractor.Extract(archive, target));

			// The message has to name the entry, so that the user can read the archive and
			// see what this application refused.
			Assert.Contains("escape", error.Message);
			Assert.Contains("names a link", error.Message);

			// The guard runs before any extractor writes a file.
			Assert.False(File.Exists(Path.Combine(target, "readme.txt")));
		}

		[Fact]
		public void AnArchiveThatWritesOutsideTheTargetDoesNotExtract()
		{
			using var temp = new TempDirectory();

			string archive = temp.File("escape.7z");

			using (FileStream file = File.Create(archive))
			using (var writer = new TarWriter(file, TarEntryFormat.Pax))
			{
				var entry = new PaxTarEntry(TarEntryType.RegularFile, "../outside.txt");
				entry.DataStream = new MemoryStream(new byte[] { 0x61 });
				writer.WriteEntry(entry);
			}

			ArchiveReadException error = Assert.Throws<ArchiveReadException>(
				() => ArchiveExtractor.Extract(archive, Path.Combine(temp.Path, "out")));

			Assert.Contains("outside.txt", error.Message);
			Assert.False(File.Exists(Path.Combine(temp.Path, "outside.txt")));
		}

		/// <summary>
		/// A canceled import adds no mod. The scratch directory carries the whole import
		/// until the last step, so a stop leaves the store as it was.
		/// </summary>
		[Fact]
		public void ACanceledImportAddsNoMod()
		{
			using var temp = new TempDirectory();

			var store = new ModStore(Path.Combine(temp.Path, "mods"));
			var importer = new ModImporter(store, Path.Combine(temp.Path, "import"));

			string source = Path.Combine(temp.Path, "a-mod");
			Directory.CreateDirectory(source);
			File.WriteAllText(Path.Combine(source, "plugin.asi"), "a plugin");

			using var cancel = new CancellationTokenSource();
			cancel.Cancel();

			Assert.Throws<OperationCanceledException>(
				() => importer.Import(source, GameINT.Underground2, null, null, cancel.Token));

			Assert.Empty(store.List());
		}
	}
}
