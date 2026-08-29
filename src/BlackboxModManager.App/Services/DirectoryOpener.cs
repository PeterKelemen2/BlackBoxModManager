using System;
using System.Diagnostics;
using System.IO;

namespace BlackboxModManager.App.Services
{
	/// <summary>
	/// Opens one directory in the file manager of the platform.
	///
	/// <b>Copy path always works and Open does not.</b> A window application under Wine has no
	/// guaranteed file manager, and the shell handler of the platform can be absent. So every
	/// caller offers Copy path beside this, and the failure text below names it. See step 9,
	/// fact 9.
	///
	/// The settings window and the folders window both call this. It lived in
	/// <c>FoldersWindow</c> until step 17, Part A, gave the settings window its own Open button.
	/// </summary>
	public static class DirectoryOpener
	{
		/// <summary>
		/// Opens one directory and reports what happened. It never throws.
		///
		/// It tries the shell handler first, then <c>explorer.exe</c>. Wine ships that program
		/// and it can still refuse a path. The result says which one worked, or that neither
		/// one did.
		/// </summary>
		public static string Open(string path)
		{
			if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
			{
				return $"The directory {path} does not exist, so there is nothing to open.";
			}

			try
			{
				// A directory with UseShellExecute opens the file manager of the platform.
				Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

				return $"Asked the platform to open {path}.";
			}
			catch (Exception first)
			{
				try
				{
					// Wine ships explorer.exe. It is the fallback on a prefix whose shell
					// associations are empty.
					Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
					{
						UseShellExecute = false,
					});

					return $"Asked explorer.exe to open {path}.";
				}
				catch (Exception second)
				{
					return "Neither the shell nor explorer.exe opened the directory. " +
						$"Use Copy path instead. {first.Message} {second.Message}";
				}
			}
		}
	}
}
