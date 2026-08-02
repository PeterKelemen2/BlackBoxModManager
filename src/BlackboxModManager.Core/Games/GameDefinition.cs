using System;
using System.Collections.Generic;
using Nikki.Core;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// What one game install looks like on the disk.
	///
	/// Every value here comes from a real install that we listed. Never add a game from
	/// memory. A wrong executable name makes the validator reject a good directory, and the
	/// message then blames the user.
	/// </summary>
	public sealed class GameDefinition
	{
		public GameINT Game { get; }

		/// <summary>The name that the UI shows.</summary>
		public string DisplayName { get; }

		/// <summary>
		/// The game executable, relative to the install root. The validator needs this file.
		/// </summary>
		public string Executable { get; }

		/// <summary>
		/// More files that a real install holds. They separate an install from a directory
		/// that only carries a copy of the executable.
		/// </summary>
		public IReadOnlyList<string> MarkerFiles { get; }

		/// <summary>Directories that a real install holds.</summary>
		public IReadOnlyList<string> MarkerDirectories { get; }

		/// <summary>
		/// A name that a directory of this game usually carries. The locator uses it to
		/// rank the candidates. It is not a check.
		/// </summary>
		public string DirectoryHint { get; }

		public GameDefinition(GameINT game, string displayName, string executable,
			IReadOnlyList<string> markerFiles, IReadOnlyList<string> markerDirectories, string directoryHint)
		{
			this.Game = game;
			this.DisplayName = displayName;
			this.Executable = executable;
			this.MarkerFiles = markerFiles ?? Array.Empty<string>();
			this.MarkerDirectories = markerDirectories ?? Array.Empty<string>();
			this.DirectoryHint = directoryHint ?? String.Empty;
		}

		public override string ToString() => $"{this.DisplayName} ({this.Game})";
	}

	/// <summary>
	/// The games that this application manages.
	///
	/// Underground 2 is the only entry. We listed that install file by file, so every
	/// marker below is a fact.
	///
	/// This application targets all six games that Nikki supports: Underground 1,
	/// Underground 2, Most Wanted, Carbon, ProStreet, and Undercover. <b>Step 7 adds the five
	/// that are absent.</b> Add an entry only after a listing of a real install of that game
	/// confirms the executable name and the markers.
	///
	/// <b>A target is not the same thing as a supported game.</b> This list holds the games
	/// that the application manages today, and it is the only answer to that question. Never
	/// read the membership of GameINT instead, because every game there is a target and five
	/// of them have no confirmed descriptor yet.
	/// </summary>
	public static class GameCatalog
	{
		public static IReadOnlyList<GameDefinition> All { get; } = new[]
		{
			new GameDefinition(
				GameINT.Underground2,
				"Need for Speed Underground 2",
				"SPEED2.EXE",
				// GLOBALB.LZC reads as GlobalB.lzc on the disk. The lookup ignores letter
				// case, so either spelling matches. See 00-test-environment.md.
				new[] { "GLOBAL/GLOBALA.BUN", "GLOBAL/GlobalB.lzc" },
				new[] { "CARS", "TRACKS", "FRONTEND" },
				"Need for Speed Underground 2"),
		};

		/// <summary>
		/// Returns the definition of one game, or null when this application does not
		/// manage that game yet.
		/// </summary>
		public static GameDefinition Find(GameINT game)
		{
			foreach (GameDefinition definition in All)
			{
				if (definition.Game == game) return definition;
			}

			return null;
		}

		/// <summary>
		/// Returns the definition of one game and throws when the game is absent. Call this
		/// where a missing definition is a programming error and not a user choice.
		/// </summary>
		public static GameDefinition Demand(GameINT game)
		{
			GameDefinition definition = Find(game);

			if (definition != null) return definition;

			throw new ArgumentOutOfRangeException(nameof(game),
				$"This application does not manage {game} yet. Step 7 adds the other games.");
		}
	}
}
