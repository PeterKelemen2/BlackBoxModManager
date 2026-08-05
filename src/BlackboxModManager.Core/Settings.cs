using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// The settings of this application. A JSON file under our own application data holds
	/// them. We do not use the registry for our own settings.
	/// </summary>
	public sealed class Settings
	{
		/// <summary>
		/// The shape of the file. Raise this when a change needs a migration.
		/// </summary>
		public int Version { get; set; } = 2;

		/// <summary>
		/// The Binary install directory that the user confirmed. This is null until the
		/// first run answers the question. A stored path can go stale, so validate it on
		/// every launch.
		/// </summary>
		public string BinaryInstallDirectory { get; set; }

		/// <summary>
		/// The confirmed install directory of each game, keyed by the GameINT name. A stored
		/// path can go stale, so validate it on every launch.
		/// </summary>
		public Dictionary<string, string> GameDirectories { get; set; } =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// The GameINT name of the game that the window showed last. This is null until the
		/// user picks a game. A name that the catalog does not hold falls back to the first
		/// game of the catalog.
		/// </summary>
		public string LastGame { get; set; }

		/// <summary>
		/// The active profile of each game, keyed by the GameINT name. A name that no longer
		/// exists falls back to the first profile.
		/// </summary>
		public Dictionary<string, string> ActiveProfiles { get; set; } =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Where the workspace of every game goes. A workspace holds the vanilla copy and
		/// the staging copy.
		///
		/// Null puts the workspace beside the game install. That default matters. A hard
		/// link cannot cross a volume, and a directory move across a volume is a full copy.
		/// Both the staging build and the swap are cheap only on the volume of the game.
		/// Set this value only when the volume of the game has no free space.
		/// </summary>
		public string WorkRootOverride { get; set; }

		/// <summary>
		/// Where the mod store goes. Null puts it under our own application data.
		///
		/// The volume of this directory decides the cost of every deploy. <b>A hard link
		/// cannot cross a volume.</b> A store on the volume of the game gets hard links, and
		/// a deploy then costs almost no disk space and almost no time. A store on another
		/// volume falls through to Copy, and every deploy writes every byte of every mod.
		///
		/// Keep the store outside every game directory whatever you set. A game reinstall
		/// deletes its own directory, and that would take the library of the user with it.
		/// </summary>
		public string ModStoreOverride { get; set; }

		/// <summary>
		/// The mod store directory that this application uses. It falls back to the default
		/// when the setting holds nothing.
		/// </summary>
		public string ResolveModStore()
		{
			return String.IsNullOrWhiteSpace(this.ModStoreOverride)
				? AppPaths.ModsDirectory
				: Path.TrimEndingDirectorySeparator(Path.GetFullPath(this.ModStoreOverride));
		}

		/// <summary>True when the mod store sits at the default place.</summary>
		public bool ModStoreIsDefault => String.IsNullOrWhiteSpace(this.ModStoreOverride);

		/// <summary>
		/// Rebuilds both dictionaries so that they ignore letter case.
		///
		/// A deserialized dictionary carries the default comparer, which compares letter
		/// case. SettingsStore.Load calls this, so every reader gets the same lookup that a
		/// fresh instance gives.
		/// </summary>
		public void Normalize()
		{
			this.GameDirectories = CaseInsensitive(this.GameDirectories);
			this.ActiveProfiles = CaseInsensitive(this.ActiveProfiles);
		}

		private static Dictionary<string, string> CaseInsensitive(Dictionary<string, string> source)
		{
			var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			if (source is null) return target;

			foreach (KeyValuePair<string, string> entry in source) target[entry.Key] = entry.Value;

			return target;
		}
	}

	/// <summary>
	/// Reads and writes the settings file. Every method tolerates a missing file. A damaged
	/// file gives fresh settings and never throws, because a user must be able to start the
	/// application and set the path again.
	/// </summary>
	public static class SettingsStore
	{
		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		public static Settings Load() => Load(AppPaths.SettingsFile);

		public static Settings Load(string path)
		{
			try
			{
				if (!File.Exists(path)) return new Settings();

				Settings settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options);

				if (settings is null) return new Settings();

				settings.Normalize();
				return settings;
			}
			catch (Exception)
			{
				// A damaged file must not block a start. The user sets the path again.
				return new Settings();
			}
		}

		public static void Save(Settings settings) => Save(AppPaths.SettingsFile, settings);

		public static void Save(string path, Settings settings)
		{
			if (settings is null) throw new ArgumentNullException(nameof(settings));

			string directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

			// Write to a temporary file first. A crash during the write then leaves the
			// last good file in place.
			string temporary = path + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
			File.Move(temporary, path, true);
		}
	}
}
