using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Nikki.Core;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// Validates a directory as a Binary install.
	///
	/// Run this on every launch, not only on the first run. The user can move or delete the
	/// install between sessions. A stale stored path must give a clear message here, not a
	/// crash inside the libraries.
	/// </summary>
	public static class BinaryInstallValidator
	{
		public const string ExecutableName = "Binary.exe";

		/// <summary>
		/// The managed assembly of the application. Binary.exe is only the host, and it
		/// carries no version resource that a Unix run can read. See the note in Validate.
		/// </summary>
		public const string AssemblyName = "Binary.dll";

		/// <summary>
		/// Runs the checks in order and stops at the first failure.
		///
		/// 1. The directory exists.
		/// 2. Binary.exe exists inside it.
		/// 3. The version is 2.8.3. A different version is a warning, not a stop.
		/// 4. A mainkeys file exists for each supported game.
		/// </summary>
		public static BinaryInstallStatus Validate(string root)
		{
			if (String.IsNullOrWhiteSpace(root))
			{
				return Fail(BinaryInstallCheck.NoPath, null,
					"No Binary install is set. Binary 2.8.3 holds the hash lists that the container editor needs.");
			}

			string full;

			try
			{
				full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
			}
			catch (Exception ex)
			{
				return Fail(BinaryInstallCheck.DirectoryMissing, root,
					$"The path {root} is not a valid path. {ex.Message}");
			}

			if (!Directory.Exists(full))
			{
				return Fail(BinaryInstallCheck.DirectoryMissing, full,
					$"The directory {full} does not exist.");
			}

			if (!File.Exists(Path.Combine(full, ExecutableName)))
			{
				return Fail(BinaryInstallCheck.ExecutableMissing, full,
					$"The directory {full} holds no {ExecutableName}. It is not a Binary install.");
			}

			Version version = ReadVersion(full);
			string versionWarning = VersionWarning(full, version);

			string mainKeys = HashListPaths.MainKeysDirectory(full);

			if (!Directory.Exists(mainKeys))
			{
				return Fail(BinaryInstallCheck.MainKeysDirectoryMissing, full,
					$"The install holds no {HashListPaths.MainKeysFolder} directory at {mainKeys}.", version, versionWarning);
			}

			var missing = new List<GameINT>();

			foreach (GameINT game in HashListPaths.SupportedGames)
			{
				if (!File.Exists(HashListPaths.MainHashList(full, game))) missing.Add(game);
			}

			if (missing.Count > 0)
			{
				var names = new List<string>(missing.Count);
				foreach (GameINT game in missing) names.Add(HashListPaths.FileName(game));

				return new BinaryInstallStatus(BinaryInstallCheck.HashListMissing, full, version, missing,
					$"The {HashListPaths.MainKeysFolder} directory holds no hash list for {String.Join(", ", names)}.",
					versionWarning);
			}

			return new BinaryInstallStatus(BinaryInstallCheck.Ok, full, version, Array.Empty<GameINT>(),
				String.Empty, versionWarning);
		}

		/// <summary>
		/// Reads the version from the install. Never read it from the directory name, because
		/// users rename directories.
		///
		/// The version lives in Binary.dll. Binary.exe is the .NET host executable. Its
		/// version resource reads as empty under a Unix run of .NET, so a read of the host
		/// gives a false negative. Binary.dll is the assembly that carries 2.8.3.0.
		/// </summary>
		public static Version ReadVersion(string root)
		{
			string assembly = Path.Combine(root, AssemblyName);

			if (File.Exists(assembly))
			{
				Version fromResource = ParseVersion(FileVersionInfo.GetVersionInfo(assembly).FileVersion);
				if (fromResource != null) return fromResource;

				try
				{
					return System.Reflection.AssemblyName.GetAssemblyName(assembly).Version;
				}
				catch (Exception)
				{
					// Not a managed assembly, or unreadable. Fall through to the host.
				}
			}

			string host = Path.Combine(root, ExecutableName);

			return File.Exists(host) ? ParseVersion(FileVersionInfo.GetVersionInfo(host).FileVersion) : null;
		}

		private static string VersionWarning(string root, Version version)
		{
			if (version == null)
			{
				return $"The version of the Binary install at {root} is unreadable. " +
					$"This tool expects {BinaryInstallStatus.ExpectedVersion}. Continuing.";
			}

			if (version == BinaryInstallStatus.ExpectedVersion) return String.Empty;

			return $"The Binary install at {root} reports version {version}. " +
				$"This tool expects {BinaryInstallStatus.ExpectedVersion}. Continuing.";
		}

		private static Version ParseVersion(string text)
		{
			return Version.TryParse(text, out Version version) ? version : null;
		}

		private static BinaryInstallStatus Fail(BinaryInstallCheck check, string root, string message,
			Version version = null, string versionWarning = null)
		{
			return new BinaryInstallStatus(check, root, version, Array.Empty<GameINT>(), message, versionWarning);
		}
	}
}
