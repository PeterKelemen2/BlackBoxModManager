using System;
using System.IO;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Finds the example mods that the developer put beside the repository. Every step 4 test
	/// runs against these, because they are the only real data that we have.
	///
	/// <b>The repository tracks no example mod.</b> Those mods belong to their authors, so
	/// .gitignore excludes the directory. A fresh clone and every CI runner therefore have no
	/// example_mods directory at all.
	///
	/// A test that needs this data must carry <see cref="ExampleModsFactAttribute"/> or
	/// <see cref="ExampleModsTheoryAttribute"/>. Those two report the test as skipped when the
	/// directory is absent. Never read <see cref="Root"/> from a plain Fact or Theory.
	/// </summary>
	internal static class ExampleMods
	{
		public const string OneLapFolder = "NFSU2 - 1 Lap URL And Other Races v2.0";

		public const string CameraFolder = "3822ca-NFSUG2 - Camera MOD MW to U2 ver.1.0";

		/// <summary>
		/// The message that names the cause. The skip attributes show this text, and
		/// <see cref="Root"/> throws it.
		/// </summary>
		public const string Absent =
			"This test needs the example mods. The repository tracks none of them. " +
			"Put an example_mods directory above the build output.";

		/// <summary>
		/// The directory, or null when it is absent.
		///
		/// This runs one time, and it never throws. A throwing static initializer here broke
		/// the whole test class with a TypeInitializationException, and the message named
		/// reflection and not the missing directory.
		/// </summary>
		private static readonly string Found = Find();

		/// <summary>True when the directory exists. The skip attributes read this.</summary>
		public static bool Exists => Found != null;

		/// <summary>
		/// The directory that holds the example mods.
		///
		/// This throws when the directory is absent. A test that reads it must carry one of
		/// the two skip attributes, so a correct test never reaches the exception.
		/// </summary>
		public static string Root =>
			Found ?? throw new DirectoryNotFoundException(Absent);

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

			return null;
		}
	}
}
