using System;
using System.IO;
using System.Text;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Builds an ASI mod on disk for a test.
	///
	/// <b>This is synthetic data.</b> The repository holds no real ASI mod. The settings text
	/// below reproduces the shape that step 9 documents for the Widescreen Fix of Underground 2.
	/// Replace it with a real sample when one arrives, and keep the awkward cases that this text
	/// carries.
	/// </summary>
	internal static class AsiFixture
	{
		public const string PluginName = "NFSUnderground2.WidescreenFix";

		public const string PluginPath = "scripts/" + PluginName + ".asi";

		public const string SettingsPath = "scripts/" + PluginName + ".ini";

		/// <summary>The HUD offsets that the fix ships beside the plugin. This is not settings.</summary>
		public const string DataPath = "scripts/" + PluginName + ".dat";

		public const string LoaderPath = "dinput8.dll";

		/// <summary>
		/// The settings text. Every line of it exercises one rule of the reader.
		///
		/// - The file opens with a comment above the first section.
		/// - <c>ResX</c> is an integer and it carries a comment.
		/// - <c>FixHUD</c> is a flag.
		/// - <c>FMVWidescreenMode</c> carries a comment that reads like a list of choices.
		/// - <c>LeftStickDeadzone</c> is a decimal.
		/// - <c>CustomUserFilesDirectoryInGameDir</c> is text.
		/// - <c>FPSLimit</c> is a negative integer that means "the refresh rate of the monitor".
		/// - <c>NoComment</c> carries no comment, so the row shows no marker.
		/// - The file uses a Windows line terminator, which the writer must keep.
		/// </summary>
		public const string SettingsText =
			"; Settings of the widescreen fix.\r\n" +
			"\r\n" +
			"[MAIN]\r\n" +
			"ResX = 0                    ; Use this option to control the horizontal resolution.\r\n" +
			"ResY = 0                    ; Use this option to control the vertical resolution.\r\n" +
			"FixHUD = 1                  ; Corrects HUD aspect ratio.\r\n" +
			"FMVWidescreenMode = 1       ; FMVs will appear in fullscreen for 16:9. (1 = Cropped | 2 = Stretched)\r\n" +
			"NoComment = 4\r\n" +
			"\r\n" +
			"[MISC]\r\n" +
			"LeftStickDeadzone = 10.0    ; Controls the deadzone of the left analog stick.\r\n" +
			"CustomUserFilesDirectoryInGameDir = SAVEGAMES    ; Use '0' to disable.\r\n" +
			"FPSLimit = -1               ; Use '-1' for the refresh rate of the monitor.\r\n";

		/// <summary>
		/// Writes one ASI mod into a directory. Pass a loader body to make the mod supply
		/// <c>dinput8.dll</c>, and pass null to leave it out.
		/// </summary>
		public static string Write(string root, string loaderBody = null, string settingsText = null)
		{
			if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("The root is empty.", nameof(root));

			Directory.CreateDirectory(Path.Combine(root, "scripts"));

			File.WriteAllBytes(Path.Combine(root, "scripts", PluginName + ".asi"),
				Encoding.ASCII.GetBytes("MZ plugin bytes"));

			File.WriteAllBytes(Path.Combine(root, "scripts", PluginName + ".dat"),
				Encoding.ASCII.GetBytes("hud offsets"));

			File.WriteAllText(Path.Combine(root, "scripts", PluginName + ".ini"),
				settingsText ?? SettingsText);

			if (loaderBody != null)
			{
				File.WriteAllBytes(Path.Combine(root, LoaderPath), Encoding.ASCII.GetBytes(loaderBody));
			}

			return root;
		}
	}
}
