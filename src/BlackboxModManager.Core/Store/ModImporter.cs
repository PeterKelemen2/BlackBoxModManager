using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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

		/// <summary>
		/// What the user has to know about this import. It holds the notes of the directory
		/// and the notes that the game decision added. Report these lines.
		/// </summary>
		public IReadOnlyList<string> Notes => this.Mod.Manifest.Notes;

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
		/// Imports one archive or one directory for one game. Pass a name in displayName to
		/// override the name that the source gives.
		///
		/// <b>The manifest of a Binary mod decides the game, not the game argument.</b> A
		/// manifest that names Most Wanted produces a Most Wanted mod, and the result then
		/// carries a note. A drop-in mod names no game, so it takes the game argument.
		/// </summary>
		public ModImportResult Import(string source, GameINT game, string displayName = null,
			IProgress<ImportProgress> progress = null, CancellationToken cancellation = default)
		{
			if (String.IsNullOrWhiteSpace(source)) throw new ArgumentException("The source is empty.", nameof(source));

			if (game == GameINT.None) throw new ArgumentOutOfRangeException(nameof(game), "GameINT.None is not a game.");

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

				progress?.Report(new ImportProgress(ImportStage.Unpack));

				if (isDirectory) CopyTree(full, scratch, progress, cancellation);
				else Unpack(full, scratch, progress, cancellation);

				cancellation.ThrowIfCancellationRequested();

				progress?.Report(new ImportProgress(ImportStage.Inspect));

				string contentRoot = ModClassifier.FindContentRoot(scratch);
				ModContent content = ModClassifier.Classify(contentRoot, progress);

				if (content.Files.Count == 0)
				{
					throw new ModImportException($"The source {full} holds no file.", full);
				}

				// A copy and an extraction both carry the read-only flag across. A read-only
				// file in the store blocks a later removal of the mod.
				FileTree.ClearReadOnly(contentRoot);

				var notes = new List<string>(content.Notes);

				var manifest = new ModManifest
				{
					Name = String.IsNullOrWhiteSpace(displayName) ? NameOf(full, isDirectory) : displayName.Trim(),
					Kind = content.Kind,
					Game = GameOf(content, contentRoot, game, notes).ToString(),
					Source = Path.GetFileName(full),
					Imported = DateTimeOffset.UtcNow,
					FileCount = content.Files.Count,
					TotalBytes = content.TotalBytes,
					Notes = notes,
				};

				progress?.Report(new ImportProgress(ImportStage.Store, 0, content.Files.Count));

				// The last safe point. Adopt moves the scratch directory into the store, and
				// a stop inside that move would leave half a mod in the library.
				cancellation.ThrowIfCancellationRequested();

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
			catch (OperationCanceledException)
			{
				// A cancel is what the user asked for. The finally block below removes the
				// scratch directory, so the store holds no new mod.
				throw;
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

		private static void Unpack(string archivePath, string scratch,
			IProgress<ImportProgress> progress, CancellationToken cancellation)
		{
			if (!ArchiveExtractor.LooksLikeArchive(archivePath))
			{
				// A single file that is not an archive is still a mod. One .asi plugin
				// arrives that way.
				File.Copy(archivePath, Path.Combine(scratch, Path.GetFileName(archivePath)), true);
				return;
			}

			ArchiveExtractor.Extract(archivePath, scratch, progress, cancellation);
		}

		/// <summary>
		/// Decides which game a mod belongs to, and adds a note when the answer surprises the
		/// user.
		///
		/// An ASI mod and a loose-file mod name no game, so both take the game that the caller
		/// gave. A Binary mod names its own game, and this application trusts the manifest.
		///
		/// <b>A Binary mod that names no game still enters the store.</b> The import stores a
		/// file, and it does not install anything. VariantReader refuses such a mod at deploy
		/// time with a message that names the variant. A refused import would leave the user
		/// with a file and no way to look at it.
		/// </summary>
		private static GameINT GameOf(ModContent content, string contentRoot, GameINT wanted,
			List<string> notes)
		{
			if (content.Kind != ModKind.Binary) return wanted;

			var named = new List<GameINT>();

			try
			{
				ModPackage package = ModPackageReader.Read(contentRoot);

				foreach (ModVariant variant in package.Variants)
				{
					if (variant.Game != GameINT.None && !named.Contains(variant.Game)) named.Add(variant.Game);
				}
			}
			catch (Exception)
			{
				// A manifest that does not read leaves the game unknown. The note below says
				// so, and the deploy reports the real problem.
			}

			if (named.Count == 0)
			{
				notes.Add($"No manifest of this mod names a game. The import files it under {wanted}. " +
					"A deploy refuses it until a manifest names a game.");
				return wanted;
			}

			if (named.Count > 1)
			{
				notes.Add($"The manifests of this mod name {named.Count} games: " +
					$"{String.Join(", ", named)}. The import files it under {named[0]}.");
			}
			else if (named[0] != wanted)
			{
				notes.Add($"The manifest names {named[0]} and not {wanted}. The import files the mod " +
					$"under {named[0]}. Switch the game to {named[0]} to see it.");
			}

			return named[0];
		}

		private static string NameOf(string source, bool isDirectory)
		{
			return isDirectory || !ArchiveExtractor.LooksLikeArchive(source)
				? Path.GetFileName(source)
				: Path.GetFileNameWithoutExtension(source);
		}

		/// <summary>
		/// Copies a directory into the scratch directory, and it reports each file.
		///
		/// The walk is flat and not recursive, so the count of the files is known before the
		/// first copy. The window needs that count to draw a bar. An empty directory of the
		/// source still reaches the target, because the first loop creates every directory.
		/// </summary>
		private static void CopyTree(string source, string target,
			IProgress<ImportProgress> progress = null, CancellationToken cancellation = default)
		{
			Directory.CreateDirectory(target);

			foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
			{
				Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
			}

			var files = new List<string>(Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories));
			var reporter = new StageReporter(progress, ImportStage.Unpack);
			int done = 0;

			foreach (string file in files)
			{
				cancellation.ThrowIfCancellationRequested();

				File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);

				++done;

				reporter.File(done, files.Count, Path.GetFileName(file));
			}
		}
	}
}
