using System;
using System.Collections.Generic;
using System.IO;

namespace BlackboxModManager.Core.Files
{
	/// <summary>
	/// Reads and removes directory trees.
	///
	/// Every relative path that this class returns uses the forward slash and keeps the
	/// letter case of the disk. Compare two of them with PathKey, never with a plain string
	/// comparison.
	/// </summary>
	public static class FileTree
	{
		/// <summary>
		/// Returns every file under the root, as a path relative to the root.
		///
		/// Pass a predicate in skipDirectory to leave a subtree out. The predicate gets the
		/// relative path of the directory.
		/// </summary>
		public static IReadOnlyList<string> Files(string root, Func<string, bool> skipDirectory = null)
		{
			if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("The root is empty.", nameof(root));

			var found = new List<string>();
			string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

			if (!Directory.Exists(full)) return found;

			Walk(full, String.Empty, found, skipDirectory);
			found.Sort(StringComparer.OrdinalIgnoreCase);

			return found;
		}

		private static void Walk(string directory, string prefix, List<string> found,
			Func<string, bool> skipDirectory)
		{
			foreach (string file in Directory.EnumerateFiles(directory))
			{
				found.Add(prefix + Path.GetFileName(file));
			}

			foreach (string child in Directory.EnumerateDirectories(directory))
			{
				string relative = prefix + Path.GetFileName(child);

				if (skipDirectory != null && skipDirectory(relative)) continue;

				Walk(child, relative + "/", found, skipDirectory);
			}
		}

		/// <summary>
		/// Joins a root and a relative path that this class returned.
		/// </summary>
		public static string Combine(string root, string relative)
		{
			return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
		}

		/// <summary>
		/// Creates the parent directory of a file path.
		/// </summary>
		public static void CreateParent(string path)
		{
			string parent = Path.GetDirectoryName(Path.GetFullPath(path));

			if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
		}

		/// <summary>
		/// Clears the read-only flag on every file in a tree.
		///
		/// The game install holds read-only files. server.dll is one. A copy carries the
		/// flag across, and a later delete of that copy then fails. Clear the flag before
		/// every delete and after every copy.
		/// </summary>
		public static void ClearReadOnly(string root)
		{
			if (!Directory.Exists(root)) return;

			foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
			{
				try
				{
					var file = new FileInfo(path);
					if (file.IsReadOnly) file.IsReadOnly = false;
				}
				catch (Exception)
				{
					// One file that keeps its flag does not stop the walk. The delete that
					// follows names the file that blocked it.
				}
			}
		}

		/// <summary>
		/// Removes a tree, read-only files included. It does nothing when the directory is
		/// absent.
		/// </summary>
		public static void Delete(string root)
		{
			if (!Directory.Exists(root)) return;

			ClearReadOnly(root);
			Directory.Delete(root, true);
		}

		/// <summary>
		/// Tests whether two paths sit on one volume.
		///
		/// <b>Say this only when it is true.</b> A hard link cannot cross a volume and a rename
		/// cannot either, so a volume boundary explains several failures. It explains none of
		/// the others. A message that named a volume boundary for an access denial sent a user
		/// hunting for a second drive on a machine with one. See
		/// docs/roadmap/05-mvp-shell.md.
		///
		/// An unreadable path reports false, which is the answer that makes a caller take the
		/// slow and safe route.
		/// </summary>
		public static bool SameVolume(string first, string second)
		{
			try
			{
				string a = Path.GetPathRoot(Path.GetFullPath(first));
				string b = Path.GetPathRoot(Path.GetFullPath(second));

				if (String.IsNullOrEmpty(a) || String.IsNullOrEmpty(b)) return false;

				return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Tests whether one path is the other path, or sits inside it.
		///
		/// Call this before every destructive operation. A staging directory inside the
		/// game install would make a swap delete the game.
		/// </summary>
		public static bool IsSameOrInside(string candidate, string root)
		{
			string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
			string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

			if (String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

			return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}
	}
}
