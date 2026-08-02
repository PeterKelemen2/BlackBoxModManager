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

		public static void CreateRoot()
		{
			Directory.CreateDirectory(Root);
		}
	}
}
