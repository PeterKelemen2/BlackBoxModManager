using System;
using System.Collections.Generic;
using Nikki.Core;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// One entry of the Links list of a VERSN1 manifest.
	///
	/// Endscript names this type SubLoader. We keep our own copy, because a descriptor is
	/// plain data and it must not depend on the library.
	///
	/// The comparison ignores letter case and the separator. Two manifests spell one path
	/// differently, and the audit must not report that as a deviation.
	/// </summary>
	public sealed class ManifestLink
	{
		/// <summary>The LoadType value. Attributes, FeAttrib, and Labels are the known ones.</summary>
		public string LoadType { get; }

		/// <summary>The PathType value. Every link that we have seen says Absolute.</summary>
		public string PathType { get; }

		/// <summary>The file, relative to the game install root.</summary>
		public string File { get; }

		public ManifestLink(string loadType, string pathType, string file)
		{
			this.LoadType = loadType ?? String.Empty;
			this.PathType = pathType ?? String.Empty;
			this.File = file ?? String.Empty;
		}

		/// <summary>
		/// The text that the audit compares. It lowers the letter case and turns every
		/// backslash into a forward slash.
		/// </summary>
		public string Key =>
			$"{this.LoadType}|{this.PathType}|{this.File.Replace('\\', '/')}".ToLowerInvariant();

		public override string ToString() => $"{this.LoadType} {this.PathType} {this.File}";
	}

	/// <summary>
	/// What one game install looks like on the disk.
	///
	/// Every value here comes from a real install that we listed. Never add a game from
	/// memory. A wrong executable name makes the validator reject a good directory, and the
	/// message then blames the user.
	/// </summary>
	public sealed class GameDefinition
	{
		public required GameINT Game { get; init; }

		/// <summary>The name that the UI shows.</summary>
		public required string DisplayName { get; init; }

		/// <summary>
		/// The game executable, relative to the install root. The validator needs this file.
		/// </summary>
		public required string Executable { get; init; }

		/// <summary>
		/// More files that a real install holds. They separate an install from a directory
		/// that only carries a copy of the executable.
		/// </summary>
		public IReadOnlyList<string> MarkerFiles { get; init; } = Array.Empty<string>();

		/// <summary>Directories that a real install holds.</summary>
		public IReadOnlyList<string> MarkerDirectories { get; init; } = Array.Empty<string>();

		/// <summary>
		/// The names that a directory of this game usually carries. The locator uses them to
		/// rank the candidates. They are not a check.
		/// </summary>
		public IReadOnlyList<string> DirectoryHints { get; init; } = Array.Empty<string>();

		/// <summary>
		/// The containers that a Binary mod of this game edits, relative to the install root.
		///
		/// A manifest names its own containers, and this list is not a filter for them. The
		/// list says which containers a real install of this game holds. A directory that
		/// passes the validator and holds none of them takes no Binary mod, and the UI can
		/// say so before the user enables one.
		/// </summary>
		public IReadOnlyList<string> ContainerFiles { get; init; } = Array.Empty<string>();

		/// <summary>
		/// The Links set that every manifest of this game is expected to carry.
		///
		/// <b>An empty list means that we recorded no expectation.</b> The audit then reports
		/// nothing, because it has nothing to compare against. Fill this list only from real
		/// manifest samples of that game. See ManifestLinkAudit.
		/// </summary>
		public IReadOnlyList<ManifestLink> ExpectedLinks { get; init; } = Array.Empty<ManifestLink>();

		public override string ToString() => $"{this.DisplayName} ({this.Game})";
	}

	/// <summary>
	/// The games that this application manages.
	///
	/// This application targets all six games that Nikki supports: Underground 1,
	/// Underground 2, Most Wanted, Carbon, ProStreet, and Undercover.
	///
	/// <b>A target is not the same thing as a supported game.</b> This list holds the games
	/// that the application manages today, and it is the only answer to that question. Never
	/// read the membership of GameINT instead. Absent names the targets that wait for a
	/// listing of a real install.
	/// </summary>
	public static class GameCatalog
	{
		public static IReadOnlyList<GameDefinition> All { get; } = new[]
		{
			new GameDefinition
			{
				Game = GameINT.Underground2,
				DisplayName = "Need for Speed Underground 2",
				Executable = "SPEED2.EXE",

				// GLOBALB.LZC reads as GlobalB.lzc on the disk. The lookup ignores letter
				// case, so either spelling matches. See 00-test-environment.md.
				MarkerFiles = new[] { "GLOBAL/GLOBALA.BUN", "GLOBAL/GlobalB.lzc" },
				MarkerDirectories = new[] { "CARS", "TRACKS", "FRONTEND" },
				DirectoryHints = new[] { "Need for Speed Underground 2", "NFSU2" },
				ContainerFiles = new[] { "GLOBAL/GlobalB.lzc" },

				// Both example mods carry these four links, in this order. Step 6 confirmed
				// that a vanilla install holds only LANGUAGES\Labels.bin of them.
				ExpectedLinks = new[]
				{
					new ManifestLink("Attributes", "Absolute", @"GLOBAL\attributes.bin"),
					new ManifestLink("FeAttrib", "Absolute", @"GLOBAL\fe_attrib.bin"),
					new ManifestLink("Labels", "Absolute", @"LANGUAGES\Labels_Global.bin"),
					new ManifestLink("Labels", "Absolute", @"LANGUAGES\Labels.bin"),
				},
			},

			new GameDefinition
			{
				Game = GameINT.MostWanted,
				DisplayName = "Need for Speed Most Wanted",
				Executable = "speed.exe",
				MarkerFiles = new[] { "GLOBAL/GLOBALA.BUN", "GLOBAL/GLOBALB.BUN" },
				MarkerDirectories = new[] { "CARS", "TRACKS", "FRONTEND" },
				DirectoryHints = new[] { "Need for Speed Most Wanted", "NFSMW" },
				ContainerFiles = new[] { "GLOBAL/GlobalB.lzc" },

				// We hold no manifest sample for this game. See ManifestLinkAudit.
				ExpectedLinks = Array.Empty<ManifestLink>(),
			},

			new GameDefinition
			{
				Game = GameINT.Prostreet,
				DisplayName = "Need for Speed ProStreet",
				Executable = "nfs.exe",
				MarkerFiles = new[] { "GLOBAL/GLOBALA.BUN", "GLOBAL/GLOBALB.BUN" },
				MarkerDirectories = new[] { "CARS", "TRACKS", "FRONTEND" },
				DirectoryHints = new[] { "Need for Speed ProStreet", "NFSPS" },
				ContainerFiles = new[] { "GLOBAL/GlobalB.lzc" },

				// We hold no manifest sample for this game. See ManifestLinkAudit.
				ExpectedLinks = Array.Empty<ManifestLink>(),
			},
		};

		/// <summary>
		/// The target games that have no descriptor yet. Each one waits for a listing of a
		/// real install. The UI reads this to name them.
		/// </summary>
		public static IReadOnlyList<GameINT> Absent { get; } = BuildAbsent();

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
				$"This application does not manage {game} yet. A descriptor for it needs a " +
				"listing of a real install.");
		}

		private static IReadOnlyList<GameINT> BuildAbsent()
		{
			var found = new List<GameINT>();

			foreach (GameINT game in HashListPaths.SupportedGames)
			{
				if (Find(game) is null) found.Add(game);
			}

			return found;
		}
	}
}
