using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Nikki.Core;

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
	///
	/// <b>The scan collects the candidate directories once and then tests every descriptor
	/// against them.</b> A separate walk per game would read the same directories six times,
	/// and the drive scan is the slow part of the operation.
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

			return FindAll(new[] { definition })[definition.Game];
		}

		/// <summary>
		/// Returns the candidate directories of every game that this application manages.
		/// Every game gets an entry, and a game with no candidate gets an empty list.
		/// </summary>
		public static IReadOnlyDictionary<GameINT, IReadOnlyList<string>> FindAll()
		{
			return FindAll(GameCatalog.All);
		}

		/// <summary>
		/// Returns the candidate directories of the given games. One scan serves them all.
		/// </summary>
		public static IReadOnlyDictionary<GameINT, IReadOnlyList<string>> FindAll(
			IReadOnlyList<GameDefinition> definitions)
		{
			if (definitions is null) throw new ArgumentNullException(nameof(definitions));

			var hinted = new Dictionary<GameINT, List<string>>();
			var others = new Dictionary<GameINT, List<string>>();
			var seen = new Dictionary<GameINT, HashSet<string>>();

			foreach (GameDefinition definition in definitions)
			{
				hinted[definition.Game] = new List<string>();
				others[definition.Game] = new List<string>();
				seen[definition.Game] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			// The registry answers first, because an installer wrote those paths.
			foreach (string directory in RegistryDirectories())
			{
				Offer(directory, definitions, hinted, others, seen);
			}

			foreach (string directory in ScanDirectories())
			{
				Offer(directory, definitions, hinted, others, seen);
			}

			var result = new Dictionary<GameINT, IReadOnlyList<string>>();

			foreach (GameDefinition definition in definitions)
			{
				List<string> found = hinted[definition.Game];
				found.AddRange(others[definition.Game]);
				result[definition.Game] = found;
			}

			return result;
		}

		/// <summary>
		/// Returns every game that one directory can be. The list holds more than one entry
		/// only when two games share an executable name. No pair of our descriptors does.
		///
		/// The browse dialog calls this. A user who picks a Most Wanted directory while the
		/// window manages Underground 2 then gets a message that names the real game.
		/// </summary>
		public static IReadOnlyList<GameDefinition> Identify(string directory)
		{
			var found = new List<GameDefinition>();

			foreach (GameDefinition definition in GameCatalog.All)
			{
				if (GameInstallValidator.LooksLike(definition, directory)) found.Add(definition);
			}

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

			var found = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string directory in RegistryDirectories())
			{
				if (!GameInstallValidator.LooksLike(definition, directory)) continue;

				string full = Normalize(directory);

				if (full != null && seen.Add(full)) found.Add(full);
			}

			return found;
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
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string directory in ScanDirectories())
			{
				if (!GameInstallValidator.LooksLike(definition, directory)) continue;

				string full = Normalize(directory);

				if (full is null || !seen.Add(full)) continue;

				if (MatchesHint(definition, full)) hinted.Add(full);
				else others.Add(full);
			}

			hinted.AddRange(others);
			return hinted;
		}

		// ---------------------------------------------------------------- the shared scan

		/// <summary>
		/// Tests one directory against every descriptor and files it under each game that it
		/// can be.
		/// </summary>
		private static void Offer(string directory, IReadOnlyList<GameDefinition> definitions,
			Dictionary<GameINT, List<string>> hinted, Dictionary<GameINT, List<string>> others,
			Dictionary<GameINT, HashSet<string>> seen)
		{
			string full = Normalize(directory);

			if (full is null) return;

			foreach (GameDefinition definition in definitions)
			{
				if (!GameInstallValidator.LooksLike(definition, full)) continue;
				if (!seen[definition.Game].Add(full)) continue;

				if (MatchesHint(definition, full)) hinted[definition.Game].Add(full);
				else others[definition.Game].Add(full);
			}
		}

		/// <summary>
		/// True when the directory name carries a name that this game usually carries. A
		/// directory of that name is the better guess. This is not a check.
		/// </summary>
		private static bool MatchesHint(GameDefinition definition, string directory)
		{
			string name = Path.GetFileName(directory);

			if (String.IsNullOrEmpty(name)) return false;

			foreach (string hint in definition.DirectoryHints)
			{
				if (String.IsNullOrEmpty(hint)) continue;

				if (name.Contains(hint, StringComparison.OrdinalIgnoreCase)) return true;
			}

			return false;
		}

		/// <summary>
		/// Every directory that the scan offers. It yields each common parent and each direct
		/// child of it, once.
		/// </summary>
		private static IEnumerable<string> ScanDirectories()
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string parent in CommonParents())
			{
				if (String.IsNullOrWhiteSpace(parent)) continue;
				if (!seen.Add(parent)) continue;

				IEnumerable<string> children;

				try
				{
					if (!Directory.Exists(parent)) continue;

					children = Directory.EnumerateDirectories(parent);
				}
				catch (Exception)
				{
					continue;
				}

				yield return parent;

				// EnumerateDirectories reads lazily, so the read can still throw below.
				using IEnumerator<string> walk = children.GetEnumerator();

				while (true)
				{
					string child;

					try
					{
						if (!walk.MoveNext()) break;

						child = walk.Current;
					}
					catch (Exception)
					{
						break;
					}

					yield return child;
				}
			}
		}

		/// <summary>
		/// The registry keys that can hold a game path. We read the publisher keys and the
		/// uninstall keys.
		///
		/// <b>The descriptors name no registry key.</b> This scan reads every value of these
		/// keys that can hold a directory, and it then tests the directory itself. A per-game
		/// key name would need a real Windows install of that game to confirm it. See the
		/// Results section of 07-game-profiles.md.
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

		/// <summary>
		/// Every directory that the registry offers, plus the parent of each one. An
		/// uninstall entry can name the uninstaller and not the directory.
		/// </summary>
		private static IReadOnlyList<string> RegistryDirectories()
		{
			if (!OperatingSystem.IsWindows()) return Array.Empty<string>();

			return ReadKeys();
		}

		[SupportedOSPlatform("windows")]
		private static IReadOnlyList<string> ReadKeys()
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

						ReadEntry(parent, found);

						foreach (string name in parent.GetSubKeyNames())
						{
							using RegistryKey entry = parent.OpenSubKey(name);
							if (entry is null) continue;

							ReadEntry(entry, found);
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
		/// Reads every value of one key that can hold a directory.
		///
		/// The value names differ between the publisher keys and the uninstall keys, and they
		/// differ between game versions. Collect every candidate value instead of a name that
		/// we would have to guess.
		/// </summary>
		[SupportedOSPlatform("windows")]
		private static void ReadEntry(RegistryKey key, List<string> found)
		{
			foreach (string name in key.GetValueNames())
			{
				if (!LooksLikePathValue(name)) continue;

				if (key.GetValue(name) is not string value) continue;
				if (String.IsNullOrWhiteSpace(value)) continue;

				string directory = value.Trim().Trim('"');
				found.Add(directory);

				string parent = ParentOf(directory);

				if (parent != null) found.Add(parent);
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

		private static string Normalize(string path)
		{
			try
			{
				return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
