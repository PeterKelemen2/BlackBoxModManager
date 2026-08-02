using System;
using System.IO;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// The directories that belong to this application. Every path that we write to
	/// resolves through here. We never write into the install of another application.
	/// </summary>
	public static class AppPaths
	{
		public const string FolderName = "BlackBoxModManager";

		/// <summary>
		/// The root of our own data. This is %APPDATA%\BlackBoxModManager on Windows and
		/// under Wine. A native Linux run puts it under the XDG configuration directory.
		/// </summary>
		public static string Root { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

		public static string SettingsFile => Path.Combine(Root, "settings.json");

		/// <summary>
		/// The parent of every CustomHashList path. BaseProfile.Save writes into this
		/// directory. See defect 7.
		/// </summary>
		public static string CustomKeysDirectory => Path.Combine(Root, "customkeys");

		/// <summary>
		/// The working directory for any operation that calls Nikki. The loaders and the
		/// savers write MainLog.txt into the current directory. See defect 9.
		/// </summary>
		public static string LogDirectory => Path.Combine(Root, "logs");

		/// <summary>
		/// The managed mod store. Each mod gets one directory here. This sits outside every
		/// game directory, so a game reinstall does not delete the library of the user.
		/// </summary>
		public static string ModsDirectory => Path.Combine(Root, "mods");

		/// <summary>
		/// The profiles of every game. One subdirectory per game.
		/// </summary>
		public static string ProfilesDirectory => Path.Combine(Root, "profiles");

		/// <summary>
		/// The scratch space of an import. An import extracts here first, and it moves the
		/// result into the mod store only after the read succeeds.
		/// </summary>
		public static string ImportDirectory => Path.Combine(Root, "import");

		/// <summary>
		/// The vanilla snapshot of every game install. One file per install.
		/// </summary>
		public static string SnapshotDirectory => Path.Combine(Root, "snapshots");

		public static void CreateRoot()
		{
			Directory.CreateDirectory(Root);
		}
	}
}
