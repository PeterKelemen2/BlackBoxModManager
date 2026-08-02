using System;
using System.Collections.Generic;
using System.IO;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Resolves a path that a manifest or a script wrote.
	///
	/// Those files come from Windows. They use a backslash separator, and their letter case
	/// does not always match the disk. A manifest writes "MOD\URL.end" and the file is at
	/// "MOD/URL.end". Wine resolves both differences. A native run on a case-sensitive
	/// filesystem resolves neither.
	///
	/// This layer reads mod text with no game present, so it must work on both. It resolves
	/// the separator always, and it falls back to a case-insensitive walk when the exact
	/// name is absent.
	/// </summary>
	public static class ModPath
	{
		/// <summary>
		/// Joins a base directory and a relative path that a mod file wrote, and returns a
		/// path that opens on this machine.
		///
		/// Returns the plain join when nothing matches, so that an error message names the
		/// path that the mod asked for.
		/// </summary>
		public static string Resolve(string baseDirectory, string relative)
		{
			if (String.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("The base directory is empty.", nameof(baseDirectory));
			if (relative is null) throw new ArgumentNullException(nameof(relative));

			string[] segments = relative.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
			string plain = Path.GetFullPath(Path.Combine(baseDirectory, Path.Combine(segments)));

			if (File.Exists(plain) || Directory.Exists(plain)) return plain;

			// Walk down one segment at a time and match without case at each step.
			string current = Path.GetFullPath(baseDirectory);

			foreach (string segment in segments)
			{
				string match = FindChild(current, segment);

				if (match is null) return plain;

				current = match;
			}

			return current;
		}

		private static string FindChild(string parent, string name)
		{
			string exact = Path.Combine(parent, name);

			if (File.Exists(exact) || Directory.Exists(exact)) return exact;

			IEnumerable<string> entries;

			try
			{
				entries = Directory.EnumerateFileSystemEntries(parent);
			}
			catch (Exception)
			{
				return null;
			}

			foreach (string entry in entries)
			{
				if (String.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)) return entry;
			}

			return null;
		}
	}
}
