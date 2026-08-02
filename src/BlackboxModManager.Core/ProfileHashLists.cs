using System;
using System.IO;
using Endscript.Profiles;
using Nikki.Core;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// Sets MainHashList and CustomHashList on the profile class of one game.
	///
	/// One pair of properties exists per game class, and both are static, so they are
	/// process-global. Set the pair for the target game immediately before every Load.
	/// Setting them for Underground 2 and then loading a Most Wanted profile leaves the
	/// wrong paths on the wrong class.
	/// </summary>
	public static class ProfileHashLists
	{
		/// <summary>
		/// Points the profile class of one game at its two hash lists and creates the output
		/// directory. Hold LibraryGate for the whole deploy that follows this call.
		/// </summary>
		public static void Apply(BinaryInstall install, GameINT game)
		{
			if (install is null) throw new ArgumentNullException(nameof(install));

			Apply(install.MainHashList(game), HashListPaths.CustomHashList(game), game);
		}

		/// <summary>
		/// The same operation with explicit paths. The harness uses this to override a path.
		/// </summary>
		public static void Apply(string mainHashList, string customHashList, GameINT game)
		{
			LibraryGate.DemandHeld(nameof(ProfileHashLists) + "." + nameof(Apply));

			// A null CustomHashList throws inside Save, after the containers already wrote.
			// Fail here instead, where the message can name the cause.
			if (String.IsNullOrWhiteSpace(mainHashList))
			{
				throw new ArgumentException($"The main hash list path for {game} is empty.", nameof(mainHashList));
			}

			if (String.IsNullOrWhiteSpace(customHashList))
			{
				throw new ArgumentException($"The custom hash list path for {game} is empty.", nameof(customHashList));
			}

			if (!File.Exists(mainHashList))
			{
				throw new FileNotFoundException($"The main hash list for {game} is not at {mainHashList}.", mainHashList);
			}

			// Save creates this directory itself. Create it here so that a permission problem
			// surfaces before the containers write, not after.
			string directory = Path.GetDirectoryName(Path.GetFullPath(customHashList));
			if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

			switch (game)
			{
				case GameINT.Underground1:
					Underground1Profile.MainHashList = mainHashList;
					Underground1Profile.CustomHashList = customHashList;
					break;

				case GameINT.Underground2:
					Underground2Profile.MainHashList = mainHashList;
					Underground2Profile.CustomHashList = customHashList;
					break;

				case GameINT.MostWanted:
					MostWantedProfile.MainHashList = mainHashList;
					MostWantedProfile.CustomHashList = customHashList;
					break;

				case GameINT.Carbon:
					CarbonProfile.MainHashList = mainHashList;
					CarbonProfile.CustomHashList = customHashList;
					break;

				case GameINT.Prostreet:
					ProstreetProfile.MainHashList = mainHashList;
					ProstreetProfile.CustomHashList = customHashList;
					break;

				case GameINT.Undercover:
					UndercoverProfile.MainHashList = mainHashList;
					UndercoverProfile.CustomHashList = customHashList;
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(game), $"The game {game} has no profile class.");
			}
		}

		/// <summary>
		/// Reads back the pair that is live on the profile class of one game. The deploy code
		/// checks this before it starts.
		/// </summary>
		public static (string Main, string Custom) Current(GameINT game)
		{
			return game switch
			{
				GameINT.Underground1 => (Underground1Profile.MainHashList, Underground1Profile.CustomHashList),
				GameINT.Underground2 => (Underground2Profile.MainHashList, Underground2Profile.CustomHashList),
				GameINT.MostWanted => (MostWantedProfile.MainHashList, MostWantedProfile.CustomHashList),
				GameINT.Carbon => (CarbonProfile.MainHashList, CarbonProfile.CustomHashList),
				GameINT.Prostreet => (ProstreetProfile.MainHashList, ProstreetProfile.CustomHashList),
				GameINT.Undercover => (UndercoverProfile.MainHashList, UndercoverProfile.CustomHashList),
				_ => throw new ArgumentOutOfRangeException(nameof(game), $"The game {game} has no profile class."),
			};
		}
	}
}
