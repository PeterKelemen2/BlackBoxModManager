using System;
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
		public int Version { get; set; } = 1;

		/// <summary>
		/// The Binary install directory that the user confirmed. This is null until the
		/// first run answers the question. A stored path can go stale, so validate it on
		/// every launch.
		/// </summary>
		public string BinaryInstallDirectory { get; set; }
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
				return settings ?? new Settings();
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
