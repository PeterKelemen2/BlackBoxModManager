using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// Looks for a game install.
	///
	/// Every result is a suggestion. The user confirms it. Never treat a hit as a silent
	/// answer, because a registry entry can point at a directory that somebody moved or
	/// deleted. Run GameInstallValidator against whatever this class returns.
	///
	/// An empty result is normal. A user who copied the game folder by hand leaves no
	/// registry entry, and the directory can sit anywhere. The UI must offer a browse
	/// button for that user.
	/// </summary>
	public static class GameInstallLocator
	{
		/// <summary>
		/// Returns the candidate directories for one game, best guess first, with no
		/// duplicates. Each candidate holds the game executable. Nothing more is promised.
		/// </summary>
		public static IReadOnlyList<string> FindCandidates(GameDefinition definition)
		{
			if (definition is null) throw new ArgumentNullException(nameof(definition));

			var found = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string path in FromRegistry(definition)) Add(path, found, seen);
			foreach (string path in FromCommonDirectories(definition)) Add(path, found, seen);

			return found;
		}

		/// <summary>
		/// Reads the registry. The game installers write a key under EA GAMES, and an
		/// uninstall entry carries an install location.
		///
		/// This gives nothing under Wine unless the user ran a real installer in the prefix.
		/// That is expected. The directory scan still runs.
		/// </summary>
		public static IReadOnlyList<string> FromRegistry(GameDefinition definition)
		{
			if (definition is null) throw new ArgumentNullException(nameof(definition));
			if (!OperatingSystem.IsWindows()) return Array.Empty<string>();

			return ReadKeys(definition);
		}

		/// <summary>
		/// The registry keys that can hold a game path. We read the publisher keys and the
		/// uninstall keys.
		/// </summary>
		private static readonly string[] SearchKeys =
		{
			@"SOFTWARE\EA GAMES",
			@"SOFTWARE\WOW6432Node\EA GAMES",
			@"SOFTWARE\Electronic Arts",
			@"SOFTWARE\WOW6432Node\Electronic Arts",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
			@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
		};

		[SupportedOSPlatform("windows")]
		private static IReadOnlyList<string> ReadKeys(GameDefinition definition)
		{
			var found = new List<string>();

			foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
			{
				foreach (string path in SearchKeys)
				{
					try
					{
						using RegistryKey parent = hive.OpenSubKey(path);
						if (parent is null) continue;

						ReadEntry(parent, definition, found);

						foreach (string name in parent.GetSubKeyNames())
						{
							using RegistryKey entry = parent.OpenSubKey(name);
							if (entry is null) continue;

							ReadEntry(entry, definition, found);
						}
					}
					catch (Exception)
					{
						// A key that we cannot read is not an error. It is one source that
						// gave nothing. The directory scan still runs.
					}
				}
			}

			return found;
		}

		/// <summary>
		/// Reads every value of one key that can hold a directory, and keeps the ones that
		/// hold the game.
		///
		/// The value names differ between the publisher keys and the uninstall keys, and
		/// they differ between game versions. Test each candidate value instead of a name
		/// that we would have to guess.
		/// </summary>
		[SupportedOSPlatform("windows")]
		private static void ReadEntry(RegistryKey key, GameDefinition definition, List<string> found)
		{
			foreach (string name in key.GetValueNames())
			{
				if (!LooksLikePathValue(name)) continue;

				if (key.GetValue(name) is not string value) continue;
				if (String.IsNullOrWhiteSpace(value)) continue;

				string directory = value.Trim().Trim('"');

				if (GameInstallValidator.LooksLike(definition, directory))
				{
					found.Add(directory);
					continue;
				}

				// An uninstall entry can name the uninstaller and not the directory.
				string parent = ParentOf(directory);

				if (parent != null && GameInstallValidator.LooksLike(definition, parent)) found.Add(parent);
			}
		}

		private static bool LooksLikePathValue(string name)
		{
			if (String.IsNullOrEmpty(name)) return false;

			return name.Contains("Dir", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("Path", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("Location", StringComparison.OrdinalIgnoreCase);
		}

		private static string ParentOf(string path)
		{
			try
			{
				return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Scans the directories where a game usually sits. It tests each parent directory
		/// and each direct child of it.
		/// </summary>
		public static IReadOnlyList<string> FromCommonDirectories(GameDefinition definition)
		{
			if (definition is null) throw new ArgumentNullException(nameof(definition));

			var hinted = new List<string>();
			var others = new List<string>();

			foreach (string parent in CommonParents())
			{
				if (String.IsNullOrWhiteSpace(parent)) continue;

				IEnumerable<string> children;

				try
				{
					if (!Directory.Exists(parent)) continue;

					if (GameInstallValidator.LooksLike(definition, parent)) others.Add(parent);

					children = Directory.EnumerateDirectories(parent);
				}
				catch (Exception)
				{
					continue;
				}

				foreach (string child in children)
				{
					if (!GameInstallValidator.LooksLike(definition, child)) continue;

					// A directory whose name matches the game is the better guess.
					bool matches = !String.IsNullOrEmpty(definition.DirectoryHint)
						&& Path.GetFileName(child).Contains(definition.DirectoryHint, StringComparison.OrdinalIgnoreCase);

					if (matches) hinted.Add(child);
					else others.Add(child);
				}
			}

			hinted.AddRange(others);
			return hinted;
		}

		/// <summary>
		/// The directories that can hold a game directory. The scan looks one level deep
		/// under each of them.
		/// </summary>
		private static IEnumerable<string> CommonParents()
		{
			var roots = new List<string>();

			foreach (Environment.SpecialFolder folder in new[]
			{
				Environment.SpecialFolder.ProgramFiles,
				Environment.SpecialFolder.ProgramFilesX86,
				Environment.SpecialFolder.UserProfile,
				Environment.SpecialFolder.MyDocuments,
			})
			{
				string path = Folder(folder);
				if (!String.IsNullOrEmpty(path)) roots.Add(path);
			}

			foreach (string drive in DriveRoots()) roots.Add(drive);

			// The publisher directory, the launcher directories, and the plain ones that a
			// user makes by hand.
			string[] branches =
			{
				null,
				"EA GAMES",
				"EA Games",
				"Electronic Arts",
				Path.Combine("Steam", "steamapps", "common"),
				"GOG Games",
				"Games",
			};

			foreach (string root in roots)
			{
				foreach (string branch in branches)
				{
					yield return branch is null ? root : Path.Combine(root, branch);
				}
			}
		}

		/// <summary>
		/// The root of every drive that answers. Under Wine, drive C is the prefix and
		/// drive Z is the whole host filesystem, so both are worth a look.
		/// </summary>
		private static IEnumerable<string> DriveRoots()
		{
			DriveInfo[] drives;

			try
			{
				drives = DriveInfo.GetDrives();
			}
			catch (Exception)
			{
				yield break;
			}

			foreach (DriveInfo drive in drives)
			{
				string name = null;

				try
				{
					if (drive.IsReady) name = drive.RootDirectory.FullName;
				}
				catch (Exception)
				{
					// A drive that reports nothing is one source that gave nothing.
				}

				if (name != null) yield return name;
			}
		}

		private static string Folder(Environment.SpecialFolder folder)
		{
			try
			{
				return Environment.GetFolderPath(folder);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void Add(string path, List<string> found, HashSet<string> seen)
		{
			string full;

			try
			{
				full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			}
			catch (Exception)
			{
				return;
			}

			if (seen.Add(full)) found.Add(full);
		}
	}
}
