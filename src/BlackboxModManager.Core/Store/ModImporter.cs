using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using Nikki.Core;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// What one import produced.
	/// </summary>
	public sealed class ModImportResult
	{
		public InstalledMod Mod { get; }

		public ModContent Content { get; }

		internal ModImportResult(InstalledMod mod, ModContent content)
		{
			this.Mod = mod;
			this.Content = content;
		}
	}

	/// <summary>
	/// Thrown when an import cannot produce a mod.
	/// </summary>
	public sealed class ModImportException : Exception
	{
		/// <summary>The archive or the directory that the import read.</summary>
		public string SourcePath { get; }

		public ModImportException(string message, string source, Exception inner = null)
			: base(message, inner)
		{
			this.SourcePath = source;
		}
	}

	/// <summary>
	/// Puts a mod into the store.
	///
	/// The import reads an archive or a directory, extracts it to a scratch directory,
	/// classifies it, and only then moves it into the store. A failed import therefore
	/// leaves no half mod behind.
	///
	/// The import never reads the game directory and never writes to it.
	/// </summary>
	public sealed class ModImporter
	{
		private readonly ModStore _store;
		private readonly string _scratchRoot;

		public ModImporter() : this(new ModStore(), AppPaths.ImportDirectory) { }

		public ModImporter(ModStore store, string scratchRoot)
		{
			this._store = store ?? throw new ArgumentNullException(nameof(store));

			if (String.IsNullOrWhiteSpace(scratchRoot)) throw new ArgumentException("The scratch root is empty.", nameof(scratchRoot));

			this._scratchRoot = Path.GetFullPath(scratchRoot);
		}

		/// <summary>
		/// Imports one archive or one directory. Pass a name in displayName to override the
		/// name that the source gives.
		/// </summary>
		public ModImportResult Import(string source, string displayName = null)
		{
			if (String.IsNullOrWhiteSpace(source)) throw new ArgumentException("The source is empty.", nameof(source));

			string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
			bool isDirectory = Directory.Exists(full);

			if (!isDirectory && !File.Exists(full))
			{
				throw new ModImportException($"The source {full} does not exist.", full);
			}

			// The scratch directory sits under the same root as the store, so the move at
			// the end stays on one volume.
			string scratch = Path.Combine(this._scratchRoot, Guid.NewGuid().ToString("N"));

			try
			{
				Directory.CreateDirectory(scratch);

				if (isDirectory) CopyTree(full, scratch);
				else Unpack(full, scratch);

				string contentRoot = ModClassifier.FindContentRoot(scratch);
				ModContent content = ModClassifier.Classify(contentRoot);

				if (content.Files.Count == 0)
				{
					throw new ModImportException($"The source {full} holds no file.", full);
				}

				// A copy and an extraction both carry the read-only flag across. A read-only
				// file in the store blocks a later removal of the mod.
				FileTree.ClearReadOnly(contentRoot);

				var manifest = new ModManifest
				{
					Name = String.IsNullOrWhiteSpace(displayName) ? NameOf(full, isDirectory) : displayName.Trim(),
					Kind = content.Kind,
					Game = GameOf(content, contentRoot),
					Source = Path.GetFileName(full),
					Imported = DateTimeOffset.UtcNow,
					FileCount = content.Files.Count,
					TotalBytes = content.TotalBytes,
					Notes = new List<string>(content.Notes),
				};

				InstalledMod mod = this._store.Adopt(contentRoot, manifest);

				return new ModImportResult(mod, content);
			}
			catch (ModImportException)
			{
				throw;
			}
			catch (ArchiveReadException ex)
			{
				throw new ModImportException(ex.Message, full, ex);
			}
			catch (Exception ex)
			{
				throw new ModImportException($"The import of {full} failed. {ex.Message}", full, ex);
			}
			finally
			{
				try
				{
					FileTree.Delete(scratch);
				}
				catch (Exception)
				{
					// A scratch directory that stays behind wastes disk space and breaks
					// nothing. The name carries a GUID, so it collides with nothing.
				}
			}
		}

		private static void Unpack(string archivePath, string scratch)
		{
			if (!ArchiveExtractor.LooksLikeArchive(archivePath))
			{
				// A single file that is not an archive is still a mod. One .asi plugin
				// arrives that way.
				File.Copy(archivePath, Path.Combine(scratch, Path.GetFileName(archivePath)), true);
				return;
			}

			ArchiveExtractor.Extract(archivePath, scratch);
		}

		/// <summary>
		/// Reads the game out of a Binary mod. An ASI mod and a loose-file mod name no game,
		/// so this returns null for both. The user assigns those.
		/// </summary>
		private static string GameOf(ModContent content, string contentRoot)
		{
			if (content.Kind != ModKind.Binary) return null;

			try
			{
				ModPackage package = ModPackageReader.Read(contentRoot);

				foreach (ModVariant variant in package.Variants)
				{
					if (variant.Game != GameINT.None) return variant.Game.ToString();
				}
			}
			catch (Exception)
			{
				// A manifest that does not read leaves the game unknown. The import still
				// succeeds, and step 6 reports the real problem when the user enables it.
			}

			return null;
		}

		private static string NameOf(string source, bool isDirectory)
		{
			return isDirectory || !ArchiveExtractor.LooksLikeArchive(source)
				? Path.GetFileName(source)
				: Path.GetFileNameWithoutExtension(source);
		}

		private static void CopyTree(string source, string target)
		{
			Directory.CreateDirectory(target);

			foreach (string file in Directory.EnumerateFiles(source))
			{
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
			}

			foreach (string child in Directory.EnumerateDirectories(source))
			{
				CopyTree(child, Path.Combine(target, Path.GetFileName(child)));
			}
		}
	}
}
