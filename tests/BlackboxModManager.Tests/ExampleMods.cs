using System;
using System.IO;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Finds the example mods that ship with the repository. Every step 4 test runs against
	/// these, because they are the only real data that we have.
	/// </summary>
	internal static class ExampleMods
	{
		public const string OneLapFolder = "NFSU2 - 1 Lap URL And Other Races v2.0";

		public const string CameraFolder = "3822ca-NFSUG2 - Camera MOD MW to U2 ver.1.0";

		public static string Root { get; } = Find();

		public static string OneLap => Path.Combine(Root, OneLapFolder);

		public static string Camera => Path.Combine(Root, CameraFolder);

		private static string Find()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);

			while (directory != null)
			{
				string candidate = Path.Combine(directory.FullName, "example_mods");

				if (Directory.Exists(candidate)) return candidate;

				directory = directory.Parent;
			}

			throw new DirectoryNotFoundException($"No example_mods directory above {AppContext.BaseDirectory}.");
		}
	}
}
