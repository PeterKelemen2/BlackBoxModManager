using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// What kind of mod a directory holds. The kind decides which engine deploys it.
	/// </summary>
	public enum ModKind
	{
		/// <summary>
		/// Files that override the files of the game. The link engine puts them in place.
		/// This is the fallback. A directory that shows no other sign gets this kind.
		/// </summary>
		LooseFiles = 0,

		/// <summary>
		/// A plugin that an ASI loader reads. It is a drop-in file, so the link engine
		/// deploys it in the same way as a loose file.
		/// </summary>
		Asi,

		/// <summary>
		/// A VERSN1 manifest and a VERSN2 script. The container engine deploys it.
		/// <b>Step 6 adds that engine.</b>
		/// </summary>
		Binary,
	}

	/// <summary>
	/// What one mod directory holds.
	/// </summary>
	public sealed class ModContent
	{
		public ModKind Kind { get; }

		/// <summary>Every file, relative to the content root, with a forward slash separator.</summary>
		public IReadOnlyList<string> Files { get; }

		/// <summary>The VERSN1 manifests, relative to the content root.</summary>
		public IReadOnlyList<string> Manifests { get; }

		/// <summary>The ASI plugins, relative to the content root.</summary>
		public IReadOnlyList<string> AsiFiles { get; }

		public long TotalBytes { get; }

		/// <summary>
		/// What the user has to know about this directory. A mixed mod produces one entry.
		/// An empty directory produces one.
		/// </summary>
		public IReadOnlyList<string> Notes { get; }

		public ModContent(ModKind kind, IReadOnlyList<string> files, IReadOnlyList<string> manifests,
			IReadOnlyList<string> asiFiles, long totalBytes, IReadOnlyList<string> notes)
		{
			this.Kind = kind;
			this.Files = files ?? Array.Empty<string>();
			this.Manifests = manifests ?? Array.Empty<string>();
			this.AsiFiles = asiFiles ?? Array.Empty<string>();
			this.TotalBytes = totalBytes;
			this.Notes = notes ?? Array.Empty<string>();
		}
	}

	/// <summary>
	/// Reads a directory and decides what kind of mod it holds.
	///
	/// The rule has three steps. A VERSN1 manifest makes it a Binary mod. An .asi file
	/// makes it an ASI mod. Everything else is loose files.
	/// </summary>
	public static class ModClassifier
	{
		public const string AsiExtension = ".asi";

		/// <summary>
		/// Reads every file of the directory and decides what kind of mod they make.
		///
		/// This opens each file to test it, so a mod of a thousand files takes seconds. The
		/// progress argument carries that wait to the window.
		/// </summary>
		public static ModContent Classify(string contentRoot, IProgress<ImportProgress> progress = null)
		{
			if (String.IsNullOrWhiteSpace(contentRoot)) throw new ArgumentException("The content root is empty.", nameof(contentRoot));

			IReadOnlyList<string> files = FileTree.Files(contentRoot);

			var manifests = new List<string>();
			var asiFiles = new List<string>();
			var notes = new List<string>();
			var reporter = new StageReporter(progress, ImportStage.Inspect);
			long bytes = 0;
			int done = 0;

			foreach (string relative in files)
			{
				string full = FileTree.Combine(contentRoot, relative);

				try
				{
					bytes += new FileInfo(full).Length;
				}
				catch (Exception)
				{
					// A length that we cannot read changes no decision.
				}

				++done;

				reporter.File(done, files.Count, Path.GetFileName(relative));

				if (String.Equals(Path.GetExtension(relative), AsiExtension, StringComparison.OrdinalIgnoreCase))
				{
					asiFiles.Add(relative);
					continue;
				}

				if (ModPackageReader.IsManifest(full)) manifests.Add(relative);
			}

			ModKind kind = manifests.Count > 0 ? ModKind.Binary
				: asiFiles.Count > 0 ? ModKind.Asi
				: ModKind.LooseFiles;

			if (files.Count == 0)
			{
				notes.Add("The mod holds no file.");
			}

			if (manifests.Count > 0 && asiFiles.Count > 0)
			{
				notes.Add($"The mod holds {manifests.Count} manifests and {asiFiles.Count} ASI plugins. " +
					"This application deploys the manifests. It does not deploy the plugins.");
			}

			return new ModContent(kind, files, manifests, asiFiles, bytes, notes);
		}

		/// <summary>
		/// The directory names that mean something inside the game directory.
		///
		/// The first group is the top level of an Underground 2 install, read from a real
		/// listing. The second group holds the two directories that an ASI loader reads.
		/// A wrapper walk stops at any of these names.
		/// </summary>
		public static IReadOnlySet<string> GameRelativeDirectories { get; } =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"CARS", "CREDITS", "FRONTEND", "GLOBAL", "LANGUAGES", "memcard", "MOVIES",
				"NIS", "SDATA", "SOUND", "SUBTITLES", "TRACKS", "Support",
				"scripts", "plugins",
			};

		/// <summary>
		/// Finds the directory that holds the mod itself.
		///
		/// An archive usually wraps its content in one directory that carries the name of
		/// the mod. That wrapper is not part of the game path of the files. Descend through
		/// every level that holds one directory and no file.
		///
		/// Two things stop the walk. A level that holds a file stops it, because a readme
		/// beside the game directories is part of the mod as far as we know. A directory
		/// whose name belongs to the game layout stops it too. The path scripts/plugin.asi
		/// is the whole point of that mod, and a descent into scripts would deploy the
		/// plugin into the game root.
		///
		/// A wrong guess here shifts every deployed file by one directory.
		/// </summary>
		public static string FindContentRoot(string extractedRoot)
		{
			if (String.IsNullOrWhiteSpace(extractedRoot)) throw new ArgumentException("The root is empty.", nameof(extractedRoot));

			string current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractedRoot));

			// A deep chain of wrappers is possible. A loop is not. Stop after a depth that
			// no real archive reaches.
			for (int depth = 0; depth < 16; ++depth)
			{
				string[] files;
				string[] directories;

				try
				{
					files = Directory.GetFiles(current);
					directories = Directory.GetDirectories(current);
				}
				catch (Exception)
				{
					return current;
				}

				if (files.Length != 0 || directories.Length != 1) return current;

				if (GameRelativeDirectories.Contains(Path.GetFileName(directories[0]))) return current;

				current = directories[0];
			}

			return current;
		}
	}
}
