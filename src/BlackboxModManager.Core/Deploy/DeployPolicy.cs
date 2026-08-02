using System;
using System.Collections.Generic;
using System.IO;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Decides how one file reaches the staging copy.
	///
	/// <b>A hard link shares the content.</b> The deployed file and the file in the mod
	/// store are one file with two names. A write through either name changes both. The
	/// game writes to its configuration files, so a hard link there would edit the mod
	/// store of the user.
	///
	/// A symbolic link has the same problem, and it adds one of its own. The link resolves
	/// to a path outside the game directory, so a game that follows it reads from the mod
	/// store at run time.
	///
	/// The rule is one line long. Copy any file that something can write. Link the rest.
	/// </summary>
	public static class DeployPolicy
	{
		/// <summary>
		/// The extensions of a file that the game, a plugin, or the user edits after a
		/// deploy. Every one of these gets a private copy.
		///
		/// Add an extension here when a mod reports that its settings do not persist, or
		/// that a file in the mod store changed by itself.
		/// </summary>
		public static IReadOnlySet<string> WritableExtensions { get; } =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				".ini", ".cfg", ".conf", ".config", ".log", ".txt", ".xml", ".json",
				".yml", ".yaml", ".sav", ".save", ".dat", ".db",
			};

		/// <summary>
		/// True when this file needs a private copy in the staging directory.
		/// </summary>
		public static bool NeedsCopy(string relativePath)
		{
			if (String.IsNullOrWhiteSpace(relativePath)) return true;

			return WritableExtensions.Contains(Path.GetExtension(relativePath));
		}
	}
}
