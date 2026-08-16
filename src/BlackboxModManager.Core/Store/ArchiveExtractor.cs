using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BlackboxModManager.Core.Files;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// Thrown when an archive does not open, or when it holds an unsafe entry.
	/// </summary>
	public sealed class ArchiveReadException : Exception
	{
		public string ArchivePath { get; }

		public ArchiveReadException(string message, string archivePath, Exception inner = null)
			: base(message, inner)
		{
			this.ArchivePath = archivePath;
		}
	}

	/// <summary>
	/// Extracts a mod archive into a directory.
	///
	/// System.IO.Compression reads zip. SharpCompress reads rar and 7z. Both paths write
	/// through the same guard, because an archive comes from the internet and its entry
	/// names are not trustworthy.
	/// </summary>
	public static class ArchiveExtractor
	{
		/// <summary>The extensions that this class opens.</summary>
		public static IReadOnlySet<string> Extensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".zip", ".rar", ".7z",
		};

		public static bool LooksLikeArchive(string path)
		{
			return !String.IsNullOrWhiteSpace(path) && Extensions.Contains(Path.GetExtension(path));
		}

		/// <summary>
		/// Extracts every entry into the target directory. It creates the target directory.
		/// It returns the number of files that it wrote.
		///
		/// The progress argument takes one report for each file. <see cref="ProgressGap"/>
		/// limits how often the report goes out.
		/// </summary>
		public static int Extract(string archivePath, string targetDirectory,
			IProgress<ImportProgress> progress = null)
		{
			if (String.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("The archive path is empty.", nameof(archivePath));
			if (String.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentException("The target directory is empty.", nameof(targetDirectory));

			if (!File.Exists(archivePath))
			{
				throw new ArchiveReadException($"The archive {archivePath} does not exist.", archivePath);
			}

			string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
			Directory.CreateDirectory(target);

			return String.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase)
				? ExtractZip(archivePath, target, progress)
				: ExtractOther(archivePath, target, progress);
		}

		/// <summary>
		/// Reads a zip through the base class library.
		///
		/// This method does not call ZipFile.ExtractToDirectory. That method stops on the
		/// first entry that it dislikes, and it gives no way to skip one bad name in an
		/// archive that is otherwise good.
		/// </summary>
		private static int ExtractZip(string archivePath, string target,
			IProgress<ImportProgress> progress)
		{
			int written = 0;
			var reporter = new StageReporter(progress, ImportStage.Unpack);

			try
			{
				using ZipArchive archive = ZipFile.OpenRead(archivePath);

				int total = 0;
				foreach (ZipArchiveEntry counted in archive.Entries)
				{
					if (!String.IsNullOrEmpty(counted.Name)) ++total;
				}

				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					// A directory entry carries an empty name. Its files create it.
					if (String.IsNullOrEmpty(entry.Name)) continue;

					string path = SafePath(archivePath, target, entry.FullName);

					FileTree.CreateParent(path);

					using (Stream source = entry.Open())
					using (FileStream destination = File.Create(path))
					{
						source.CopyTo(destination);
					}

					++written;

					reporter.File(written, total, entry.Name);
				}
			}
			catch (ArchiveReadException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new ArchiveReadException($"The zip archive {archivePath} did not read. {ex.Message}", archivePath, ex);
			}

			return written;
		}

		/// <summary>
		/// Reads a rar or a 7z.
		///
		/// The listing comes from SharpCompress, and the files come from 7-Zip. SharpCompress
		/// reads a header in milliseconds, and it gives the guard below every entry name
		/// before one byte reaches the disk. 7-Zip then writes the files, because SharpCompress
		/// needs half an hour for a big solid 7z. See
		/// docs/roadmap/98-known-upstream-defects.md, defect 14.
		///
		/// A build with no 7-Zip beside it reads the files through SharpCompress instead. The
		/// result is the same, and a solid archive of many files takes minutes.
		/// </summary>
		private static int ExtractOther(string archivePath, string target,
			IProgress<ImportProgress> progress)
		{
			int total = ReadListing(archivePath, target);

			return SevenZipTool.Exists
				? SevenZipTool.Extract(archivePath, target, total, progress)
				: ExtractWithLibrary(archivePath, target, total, progress);
		}

		/// <summary>
		/// Reads the listing of the archive, and it returns the number of files.
		///
		/// This is the guard of the whole path. It refuses an entry name that writes outside
		/// the target directory, and it refuses an archive that needs a password. Both tests
		/// run before any extractor writes a file.
		///
		/// ArchiveFactory reads the header, so a file with the wrong extension still opens.
		/// </summary>
		private static int ReadListing(string archivePath, string target)
		{
			int total = 0;

			try
			{
				using IArchive archive = ArchiveFactory.Open(archivePath);

				foreach (IArchiveEntry entry in archive.Entries)
				{
					if (entry.IsDirectory) continue;
					if (String.IsNullOrEmpty(entry.Key)) continue;

					if (entry.IsEncrypted)
					{
						throw new ArchiveReadException(
							$"The archive {archivePath} needs a password. This application does not open one.",
							archivePath);
					}

					// The result goes nowhere. The call throws for a name that leaves the
					// target directory, and that is the point of it.
					SafePath(archivePath, target, entry.Key);

					++total;
				}
			}
			catch (ArchiveReadException)
			{
				throw;
			}
			catch (SharpCompress.Common.CryptographicException ex)
			{
				throw new ArchiveReadException(
					$"The archive {archivePath} needs a password. This application does not open one.",
					archivePath, ex);
			}
			catch (Exception ex)
			{
				throw new ArchiveReadException($"The archive {archivePath} did not read. {ex.Message}", archivePath, ex);
			}

			return total;
		}

		/// <summary>
		/// Reads a rar or a 7z through SharpCompress. This runs when 7-Zip is not there.
		/// </summary>
		private static int ExtractWithLibrary(string archivePath, string target, int total,
			IProgress<ImportProgress> progress)
		{
			int written = 0;
			var reporter = new StageReporter(progress, ImportStage.Unpack);

			try
			{
				using IArchive archive = ArchiveFactory.Open(archivePath);

				foreach (IArchiveEntry entry in archive.Entries)
				{
					if (entry.IsDirectory) continue;
					if (String.IsNullOrEmpty(entry.Key)) continue;

					string path = SafePath(archivePath, target, entry.Key);

					FileTree.CreateParent(path);

					using (Stream source = entry.OpenEntryStream())
					using (FileStream destination = File.Create(path))
					{
						source.CopyTo(destination);
					}

					++written;

					reporter.File(written, total, Path.GetFileName(entry.Key));
				}
			}
			catch (ArchiveReadException)
			{
				throw;
			}
			catch (SharpCompress.Common.CryptographicException ex)
			{
				throw new ArchiveReadException(
					$"The archive {archivePath} needs a password. This application does not open one.",
					archivePath, ex);
			}
			catch (Exception ex)
			{
				throw new ArchiveReadException($"The archive {archivePath} did not read. {ex.Message}", archivePath, ex);
			}

			return written;
		}

		/// <summary>
		/// Turns an entry name into a full path under the target directory.
		///
		/// An entry name comes from the internet. A name such as ..\..\windows\system32\x
		/// writes outside the target. Resolve the name and reject any result that leaves
		/// the target directory.
		/// </summary>
		private static string SafePath(string archivePath, string target, string entryName)
		{
			string relative = entryName.Replace('\\', '/').TrimStart('/');
			string full;

			try
			{
				full = Path.GetFullPath(Path.Combine(target, relative));
			}
			catch (Exception ex)
			{
				throw new ArchiveReadException(
					$"The archive {archivePath} holds the entry name \"{entryName}\", which is not a valid path. {ex.Message}",
					archivePath, ex);
			}

			if (!FileTree.IsSameOrInside(full, target) || String.Equals(full, target, StringComparison.OrdinalIgnoreCase))
			{
				throw new ArchiveReadException(
					$"The archive {archivePath} holds the entry name \"{entryName}\", which writes outside the target directory. " +
					"This application does not extract that archive.",
					archivePath);
			}

			return full;
		}
	}
}
