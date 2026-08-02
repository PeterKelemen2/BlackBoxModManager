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
		/// </summary>
		public static int Extract(string archivePath, string targetDirectory)
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
				? ExtractZip(archivePath, target)
				: ExtractOther(archivePath, target);
		}

		/// <summary>
		/// Reads a zip through the base class library.
		///
		/// This method does not call ZipFile.ExtractToDirectory. That method stops on the
		/// first entry that it dislikes, and it gives no way to skip one bad name in an
		/// archive that is otherwise good.
		/// </summary>
		private static int ExtractZip(string archivePath, string target)
		{
			int written = 0;

			try
			{
				using ZipArchive archive = ZipFile.OpenRead(archivePath);

				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					// A directory entry carries an empty name. Its files create it.
					if (String.IsNullOrEmpty(entry.Name)) continue;

					string path = SafePath(archivePath, target, entry.FullName);

					FileTree.CreateParent(path);

					using Stream source = entry.Open();
					using FileStream destination = File.Create(path);
					source.CopyTo(destination);

					++written;
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
		/// Reads a rar or a 7z through SharpCompress.
		///
		/// ArchiveFactory reads the header, so a file with the wrong extension still opens.
		/// </summary>
		private static int ExtractOther(string archivePath, string target)
		{
			int written = 0;

			try
			{
				using IArchive archive = ArchiveFactory.Open(archivePath);

				foreach (IArchiveEntry entry in archive.Entries)
				{
					if (entry.IsDirectory) continue;
					if (String.IsNullOrEmpty(entry.Key)) continue;

					string path = SafePath(archivePath, target, entry.Key);

					FileTree.CreateParent(path);

					using Stream source = entry.OpenEntryStream();
					using FileStream destination = File.Create(path);
					source.CopyTo(destination);

					++written;
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
