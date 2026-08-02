using System;
using System.Collections.Generic;
using System.IO;
using Nikki.Core;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// Maps a game to its two hash list paths.
	///
	/// MainHashList is input. It comes from the Binary install of the user and we only read it.
	/// CustomHashList is output. BaseProfile.Save creates its directory and overwrites the
	/// file, so it must point at a path that we own. See defect 7.
	/// </summary>
	public static class HashListPaths
	{
		public const string MainKeysFolder = "mainkeys";

		/// <summary>
		/// The six games that Nikki supports, which are also the six games that this
		/// application targets. GameINT.None is not one of them.
		///
		/// This list says which hash lists a Binary install has to hold. It does not say
		/// which games the application manages today. GameCatalog answers that.
		/// </summary>
		public static IReadOnlyList<GameINT> SupportedGames { get; } = new[]
		{
			GameINT.Underground1,
			GameINT.Underground2,
			GameINT.MostWanted,
			GameINT.Carbon,
			GameINT.Prostreet,
			GameINT.Undercover,
		};

		/// <summary>
		/// The hash list file name of a game. Binary names the six files after the GameINT
		/// members in lower case. The name is "underground2.txt" for GameINT.Underground2.
		/// </summary>
		public static string FileName(GameINT game)
		{
			if (game == GameINT.None) throw new ArgumentOutOfRangeException(nameof(game), "GameINT.None has no hash list.");

			return game.ToString().ToLowerInvariant() + ".txt";
		}

		public static string MainKeysDirectory(string binaryRoot)
		{
			if (String.IsNullOrWhiteSpace(binaryRoot)) throw new ArgumentException("The Binary root is empty.", nameof(binaryRoot));

			return Path.Combine(binaryRoot, MainKeysFolder);
		}

		/// <summary>
		/// The input hash list, inside the Binary install. We read this file. We never write it.
		/// </summary>
		public static string MainHashList(string binaryRoot, GameINT game)
		{
			return Path.Combine(MainKeysDirectory(binaryRoot), FileName(game));
		}

		/// <summary>
		/// The output hash list, under our own application data. Save overwrites this file.
		/// </summary>
		public static string CustomHashList(GameINT game)
		{
			return Path.Combine(AppPaths.CustomKeysDirectory, FileName(game));
		}
	}
}
