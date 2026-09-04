using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
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
			IProgress<ImportProgress> progress = null, CancellationToken cancellation = default)
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
				? ExtractZip(archivePath, target, progress, cancellation)
				: ExtractOther(archivePath, target, progress, cancellation);
		}

		/// <summary>
		/// Reads a zip through the base class library.
		///
		/// This method does not call ZipFile.ExtractToDirectory. That method stops on the
		/// first entry that it dislikes, and it gives no way to skip one bad name in an
		/// archive that is otherwise good.
		/// </summary>
		private static int ExtractZip(string archivePath, string target,
			IProgress<ImportProgress> progress, CancellationToken cancellation)
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
					cancellation.ThrowIfCancellationRequested();

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
			catch (OperationCanceledException)
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
			IProgress<ImportProgress> progress, CancellationToken cancellation)
		{
			int total = ReadListing(archivePath, target);

			int written = SevenZipTool.Exists
				? SevenZipTool.Extract(archivePath, target, total, progress, cancellation)
				: ExtractWithLibrary(archivePath, target, total, progress, cancellation);

			// The listing and the extractor are two readers of one archive, so the listing
			// cannot prove what the extractor wrote. Read the disk instead.
			RefuseLinks(archivePath, target);

			return written;
		}

		/// <summary>
		/// Reads the listing of the archive, and it returns the number of files.
		///
		/// This is the first guard of the path. It refuses an entry name that writes outside
		/// the target directory, it refuses an archive that needs a password, and it refuses
		/// an entry that names a link. Every test runs before any extractor writes a file.
		///
		/// <b>This guard reads the archive with SharpCompress, and 7-Zip writes the files.</b>
		/// Two readers of one archive can disagree, so this method proves nothing about what
		/// 7-Zip writes. <c>SevenZipTool</c> passes the switches that make 7-Zip write a link
		/// entry as a plain file, and <see cref="RefuseLinks"/> then walks the target. This
		/// method stays the whole guard of <see cref="ExtractWithLibrary"/> alone.
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

					if (IsLink(entry))
					{
						throw new ArchiveReadException(
							$"The archive {archivePath} holds the entry \"{entry.Key}\", which names a link " +
							"and not a file. A link lets a later entry write outside the target directory. " +
							"This application does not extract that archive.",
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
			catch (OperationCanceledException)
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
			IProgress<ImportProgress> progress, CancellationToken cancellation)
		{
			int written = 0;
			var reporter = new StageReporter(progress, ImportStage.Unpack);

			try
			{
				using IArchive archive = ArchiveFactory.Open(archivePath);

				foreach (IArchiveEntry entry in archive.Entries)
				{
					cancellation.ThrowIfCancellationRequested();

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
			catch (OperationCanceledException)
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
		/// True when one entry of the listing names a link and not a file.
		///
		/// Three archive formats say it three ways. A tar entry carries the target of the
		/// link in <c>LinkTarget</c>. A Windows 7-Zip entry sets the reparse-point bit of
		/// the file attributes. A p7zip entry stores the unix mode in the high half of the
		/// same field and sets bit 0x8000 to say so.
		///
		/// <b>SharpCompress reports neither field for every archive.</b> The base entry
		/// throws for <c>Attrib</c>, and the 7z reader hides the high half of a p7zip mode.
		/// So this test finds some links and not all of them, and <see cref="RefuseLinks"/>
		/// holds the answer that the disk gives.
		/// </summary>
		private static bool IsLink(IArchiveEntry entry)
		{
			// FILE_ATTRIBUTE_REPARSE_POINT.
			const int ReparsePoint = 0x400;

			// Bit 0x8000 says that the high half holds a unix mode. S_IFMT is 0xF000 of that
			// mode, and S_IFLNK is 0xA000.
			const int UnixModeFollows = 0x8000;
			const int UnixTypeMask = unchecked((int)0xF0000000);
			const int UnixLink = unchecked((int)0xA0000000);

			if (!String.IsNullOrEmpty(Read(() => entry.LinkTarget))) return true;

			int? attributes = ReadAttributes(entry);

			if (attributes is null) return false;

			if ((attributes.Value & ReparsePoint) != 0) return true;

			return (attributes.Value & UnixModeFollows) != 0
				&& (attributes.Value & UnixTypeMask) == UnixLink;
		}

		private static int? ReadAttributes(IArchiveEntry entry)
		{
			try
			{
				return entry.Attrib;
			}
			catch (Exception)
			{
				// The base entry of SharpCompress throws NotImplementedException here. A
				// format that reports no attributes answers nothing about a link.
				return null;
			}
		}

		private static string Read(Func<string> value)
		{
			try
			{
				return value();
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Walks the target directory and refuses the import when it finds a link.
		///
		/// This is the last guard, and it reads the disk. The listing guard and the
		/// extractor are two readers of one archive, so only the disk says what the
		/// extraction wrote.
		///
		/// The walk does not descend into a link. A link to a parent directory would make
		/// the walk run forever.
		/// </summary>
		private static void RefuseLinks(string archivePath, string target)
		{
			foreach (FileSystemInfo entry in new DirectoryInfo(target).EnumerateFileSystemInfos())
			{
				if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
				{
					throw new ArchiveReadException(
						$"The archive {archivePath} wrote the link {entry.FullName} into the target directory. " +
						"A link lets a write reach a place outside the target. " +
						"This application does not import that archive.",
						archivePath);
				}

				if (entry is DirectoryInfo child) RefuseLinks(archivePath, child.FullName);
			}
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
