using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Mods;
using Nikki.Core;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// Validates a directory as a game install.
	///
	/// Run this on every launch, not only on the first run. The user can move, delete, or
	/// reinstall the game between sessions. A stale stored path must give a clear message
	/// here, not a crash deep inside a deploy.
	///
	/// Every lookup goes through ModPath.Resolve. The markers carry Windows separators and
	/// the letter case of the manifests. A native run on a case-sensitive filesystem must
	/// still find GLOBAL/GlobalB.lzc for the marker GLOBAL\GLOBALB.LZC.
	/// </summary>
	public static class GameInstallValidator
	{
		/// <summary>
		/// Runs the checks in order and stops at the first failure.
		///
		/// 1. This application manages the game.
		/// 2. The directory exists.
		/// 3. The game executable exists inside it.
		/// 4. Every marker file and marker directory exists.
		/// </summary>
		public static GameInstallStatus Validate(GameINT game, string root)
		{
			GameDefinition definition = GameCatalog.Find(game);

			if (definition is null)
			{
				return Fail(GameInstallCheck.UnknownGame, game, root, null,
					$"This application does not manage {game} yet.");
			}

			if (String.IsNullOrWhiteSpace(root))
			{
				return Fail(GameInstallCheck.NoPath, game, null, definition,
					$"No install directory is set for {definition.DisplayName}.");
			}

			string full;

			try
			{
				full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
			}
			catch (Exception ex)
			{
				return Fail(GameInstallCheck.DirectoryMissing, game, root, definition,
					$"The path {root} is not a valid path. {ex.Message}");
			}

			if (!Directory.Exists(full))
			{
				return Fail(GameInstallCheck.DirectoryMissing, game, full, definition,
					$"The directory {full} does not exist.");
			}

			if (!File.Exists(ModPath.Resolve(full, definition.Executable)))
			{
				return Fail(GameInstallCheck.ExecutableMissing, game, full, definition,
					$"The directory {full} holds no {definition.Executable}. " +
					$"It is not an install of {definition.DisplayName}.{OtherGame(full)}");
			}

			var missing = new List<string>();

			foreach (string marker in definition.MarkerFiles)
			{
				if (!File.Exists(ModPath.Resolve(full, marker))) missing.Add(marker);
			}

			foreach (string marker in definition.MarkerDirectories)
			{
				if (!Directory.Exists(ModPath.Resolve(full, marker))) missing.Add(marker + "/");
			}

			if (missing.Count > 0)
			{
				return new GameInstallStatus(GameInstallCheck.ContentMissing, game, full, definition, missing,
					$"The directory {full} holds {definition.Executable} and it does not hold " +
					$"{String.Join(", ", missing)}. The install is incomplete.");
			}

			return new GameInstallStatus(GameInstallCheck.Ok, game, full, definition,
				Array.Empty<string>(), String.Empty);
		}

		/// <summary>
		/// Tests one directory cheaply. The locator uses this to filter its candidates. A
		/// full check needs Validate or MatchesFully.
		/// </summary>
		public static bool LooksLike(GameDefinition definition, string directory)
		{
			if (definition is null) throw new ArgumentNullException(nameof(definition));

			try
			{
				return !String.IsNullOrWhiteSpace(directory)
					&& Directory.Exists(directory)
					&& File.Exists(ModPath.Resolve(directory, definition.Executable));
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Tests the executable and every marker, with no message and no call into Identify.
		///
		/// Validate calls Identify on failure to name the game that a directory really holds,
		/// and Identify calls this method instead of Validate for that reason. Two of our
		/// descriptors share an executable name once a Windows lookup ignores its letter case,
		/// so Identify needs the marker check and not just LooksLike, but it must not call back
		/// into Validate or the two methods recurse into each other forever.
		/// </summary>
		public static bool MatchesFully(GameDefinition definition, string directory)
		{
			if (!LooksLike(definition, directory)) return false;

			try
			{
				foreach (string marker in definition.MarkerFiles)
				{
					if (!File.Exists(ModPath.Resolve(directory, marker))) return false;
				}

				foreach (string marker in definition.MarkerDirectories)
				{
					if (!Directory.Exists(ModPath.Resolve(directory, marker))) return false;
				}

				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Names the game that the directory really holds, or returns an empty string.
		///
		/// Six games look alike from the outside. A user who picks the directory of another
		/// game gets the name of that game, and not a message that says only "no executable".
		/// </summary>
		private static string OtherGame(string directory)
		{
			var names = new List<string>();

			foreach (GameDefinition other in GameInstallLocator.Identify(directory))
			{
				names.Add(other.DisplayName);
			}

			return names.Count == 0 ? String.Empty : $" It holds {String.Join(" or ", names)}.";
		}

		private static GameInstallStatus Fail(GameInstallCheck check, GameINT game, string root,
			GameDefinition definition, string message)
		{
			return new GameInstallStatus(check, game, root, definition, Array.Empty<string>(), message);
		}
	}
}
