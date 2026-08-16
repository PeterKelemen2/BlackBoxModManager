using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nikki.Core;

namespace BlackboxModManager.Core.Profiles
{
	/// <summary>
	/// Reads and writes the profiles of one game.
	///
	/// Each game gets one directory. Each profile is one JSON file inside it. A damaged
	/// file hides one profile and never blocks the others.
	/// </summary>
	public sealed class ProfileStore
	{
		public const string DefaultProfileName = "Default";

		private const string Extension = ".json";

		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		public string Root { get; }

		public ProfileStore() : this(AppPaths.ProfilesDirectory) { }

		public ProfileStore(string root)
		{
			if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("The root is empty.", nameof(root));

			this.Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
		}

		/// <summary>
		/// Copies a profile, with no shared object between the copy and the original.
		///
		/// <b>A background check reads a copy and never the live profile.</b> The window
		/// changes the live profile when the user clicks, and a read of a list that another
		/// thread changes gives a wrong answer or an exception.
		/// </summary>
		public static Profile Clone(Profile profile)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));

			return JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, Options), Options);
		}

		public string DirectoryOf(GameINT game) => Path.Combine(this.Root, game.ToString());

		/// <summary>
		/// Returns every profile of one game, by name. The list is empty when the game has
		/// no profile yet.
		/// </summary>
		public IReadOnlyList<Profile> List(GameINT game)
		{
			var found = new List<Profile>();
			string directory = this.DirectoryOf(game);

			if (!Directory.Exists(directory)) return found;

			foreach (string path in Directory.EnumerateFiles(directory, "*" + Extension))
			{
				Profile profile = Read(path, game);
				if (profile != null) found.Add(profile);
			}

			found.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			return found;
		}

		public Profile Find(GameINT game, string name)
		{
			if (String.IsNullOrWhiteSpace(name)) return null;

			return Read(this.PathOf(game, name), game);
		}

		/// <summary>
		/// Returns the profile of the given name and makes an empty one when it is absent.
		/// </summary>
		public Profile Ensure(GameINT game, string name)
		{
			Profile profile = this.Find(game, name);

			if (profile != null) return profile;

			profile = new Profile(name, game.ToString());
			this.Save(game, profile);

			return profile;
		}

		public void Save(GameINT game, Profile profile)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));

			if (String.IsNullOrWhiteSpace(profile.Name))
			{
				throw new ArgumentException("The profile has no name.", nameof(profile));
			}

			profile.Game = game.ToString();

			string path = this.PathOf(game, profile.Name);
			Directory.CreateDirectory(Path.GetDirectoryName(path));

			// Write to a temporary file first. A crash during the write then leaves the
			// last good file in place.
			string temporary = path + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(profile, Options));
			File.Move(temporary, path, true);
		}

		public void Delete(GameINT game, string name)
		{
			string path = this.PathOf(game, name);

			if (File.Exists(path)) File.Delete(path);
		}

		/// <summary>
		/// Renames a profile. It fails when the new name is in use.
		/// </summary>
		public void Rename(GameINT game, string oldName, string newName)
		{
			Profile profile = this.Find(game, oldName);

			if (profile is null)
			{
				throw new ArgumentException($"The game {game} has no profile named \"{oldName}\".", nameof(oldName));
			}

			if (this.Find(game, newName) != null)
			{
				throw new ArgumentException($"The game {game} already has a profile named \"{newName}\".", nameof(newName));
			}

			profile.Name = newName;
			string before = this.PathOf(game, oldName);
			this.Save(game, profile);

			// Two names can clean to one file name. A delete would then remove the file
			// that the save just wrote.
			if (!String.Equals(before, this.PathOf(game, newName), StringComparison.OrdinalIgnoreCase))
			{
				this.Delete(game, oldName);
			}
		}

		private Profile Read(string path, GameINT game)
		{
			try
			{
				if (!File.Exists(path)) return null;

				Profile profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Options);

				if (profile is null) return null;

				// The file name is the name. A copied file then reads under its new name.
				profile.Name = Path.GetFileNameWithoutExtension(path);
				profile.Game = game.ToString();

				foreach (ProfileEntry entry in profile.Entries)
				{
					entry.Selections ??= new Mods.ModSelections();
				}

				return profile;
			}
			catch (Exception)
			{
				// A damaged file hides one profile. It must not stop the list.
				return null;
			}
		}

		private string PathOf(GameINT game, string name)
		{
			return Path.Combine(this.DirectoryOf(game), FileName(name) + Extension);
		}

		/// <summary>
		/// Turns a profile name into a safe file name.
		///
		/// A user types the name, so it can hold any character. The name goes into a path,
		/// so it keeps letters, digits, spaces, and three separators.
		/// </summary>
		public static string FileName(string name)
		{
			var text = new StringBuilder();

			foreach (char c in name ?? String.Empty)
			{
				if (Char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') text.Append(c);
				else if (text.Length > 0 && text[^1] != ' ') text.Append(' ');
			}

			string clean = text.ToString().Trim(' ', '.');

			if (clean.Length > 64) clean = clean.Substring(0, 64).TrimEnd(' ', '.');

			return clean.Length == 0 ? DefaultProfileName : clean;
		}
	}
}
