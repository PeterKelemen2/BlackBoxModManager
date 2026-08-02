using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// Looks for a Binary install.
	///
	/// Every result is a suggestion. The user confirms it. Never treat a hit as a silent
	/// answer, because a registry entry can point at a directory that somebody moved or
	/// deleted. Run the full validator against whatever this class returns.
	/// </summary>
	public static class BinaryInstallLocator
	{
		/// <summary>
		/// Returns the candidate directories, best guess first, with no duplicates.
		/// An empty result means that the user has to give the path.
		/// </summary>
		public static IReadOnlyList<string> FindCandidates()
		{
			var found = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string path in FromRegistry()) Add(path, found, seen);
			foreach (string path in FromCommonDirectories()) Add(path, found, seen);

			return found;
		}

		/// <summary>
		/// Reads the uninstall keys. Binary ships as a directory that a user unpacks, so a
		/// registry entry is unlikely. The scan costs little and it covers the case where a
		/// packager made one.
		/// </summary>
		public static IReadOnlyList<string> FromRegistry()
		{
			if (!OperatingSystem.IsWindows()) return Array.Empty<string>();

			return ReadUninstallKeys();
		}

		[SupportedOSPlatform("windows")]
		private static IReadOnlyList<string> ReadUninstallKeys()
		{
			const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
			const string uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

			var found = new List<string>();

			foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
			{
				foreach (string path in new[] { uninstall, uninstall32 })
				{
					try
					{
						using RegistryKey parent = hive.OpenSubKey(path);
						if (parent is null) continue;

						foreach (string name in parent.GetSubKeyNames())
						{
							using RegistryKey entry = parent.OpenSubKey(name);
							if (entry is null) continue;

							string location = entry.GetValue("InstallLocation") as string;

							if (!String.IsNullOrWhiteSpace(location) && LooksLikeBinary(location))
							{
								found.Add(location);
							}
						}
					}
					catch (Exception)
					{
						// A hive that we cannot read is not an error. It is one source that
						// gave nothing. The common directory scan still runs.
					}
				}
			}

			return found;
		}

		/// <summary>
		/// Scans the directories where a user usually unpacks a tool. It tests each root and
		/// each direct child whose name starts with "Binary".
		/// </summary>
		public static IReadOnlyList<string> FromCommonDirectories()
		{
			var found = new List<string>();

			foreach (string root in CommonRoots())
			{
				if (String.IsNullOrWhiteSpace(root)) continue;

				if (LooksLikeBinary(root)) found.Add(root);

				IEnumerable<string> children;

				try
				{
					if (!Directory.Exists(root)) continue;

					children = Directory.EnumerateDirectories(root, "Binary*");
				}
				catch (Exception)
				{
					continue;
				}

				foreach (string child in children)
				{
					if (LooksLikeBinary(child)) found.Add(child);
				}
			}

			return found;
		}

		/// <summary>
		/// Tests one directory cheaply. A full check needs BinaryInstallValidator.
		/// </summary>
		public static bool LooksLikeBinary(string directory)
		{
			try
			{
				return !String.IsNullOrWhiteSpace(directory)
					&& File.Exists(Path.Combine(directory, BinaryInstallValidator.ExecutableName));
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static IEnumerable<string> CommonRoots()
		{
			yield return Folder(Environment.SpecialFolder.ProgramFiles);
			yield return Folder(Environment.SpecialFolder.ProgramFilesX86);
			yield return Folder(Environment.SpecialFolder.UserProfile);
			yield return Folder(Environment.SpecialFolder.DesktopDirectory);
			yield return Folder(Environment.SpecialFolder.MyDocuments);

			string home = Folder(Environment.SpecialFolder.UserProfile);

			if (!String.IsNullOrEmpty(home))
			{
				yield return Path.Combine(home, "Downloads");
			}
		}

		private static string Folder(Environment.SpecialFolder folder)
		{
			try
			{
				return Environment.GetFolderPath(folder);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void Add(string path, List<string> found, HashSet<string> seen)
		{
			string full;

			try
			{
				full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			}
			catch (Exception)
			{
				return;
			}

			if (seen.Add(full)) found.Add(full);
		}
	}
}
