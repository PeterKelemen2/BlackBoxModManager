using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// Checks that the vanilla copy still holds what the snapshot recorded.
	///
	/// <b>This never asks whether the baseline is vanilla.</b> A snapshot records the install
	/// as the user had it. A mod that the user added by hand before the snapshot is part of
	/// that baseline, and it must keep working. The only question here is whether the
	/// baseline changed after we recorded it.
	///
	/// <b>A timestamp proves nothing.</b> An extract resets it and a copy resets it again.
	/// The length and the hash of the content are the only answers.
	///
	/// A change means that something wrote through a hard link. TreeReplicator builds the
	/// staging copy with hard links, so a staging file and a vanilla file are one file with
	/// two names. A write that does not break the share first reaches both. See defect 16.
	/// </summary>
	public static class BaselineVerifier
	{
		/// <summary>
		/// Compares the given files against the snapshot and returns each one that differs.
		///
		/// Pass the paths relative to the root of the vanilla copy. A path that the snapshot
		/// does not hold produces no result, because a container that a script creates is in
		/// no baseline.
		///
		/// This reads the content of every named file, so keep the list to the files that the
		/// deploy writes.
		/// </summary>
		public static IReadOnlyList<SnapshotDifference> CheckFiles(VanillaSnapshot snapshot,
			string vanillaDirectory, IEnumerable<string> relativePaths)
		{
			if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
			if (relativePaths is null) throw new ArgumentNullException(nameof(relativePaths));

			if (String.IsNullOrWhiteSpace(vanillaDirectory))
			{
				throw new ArgumentException("The vanilla directory is empty.", nameof(vanillaDirectory));
			}

			string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vanillaDirectory));
			var differences = new List<SnapshotDifference>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string written in relativePaths)
			{
				if (String.IsNullOrWhiteSpace(written)) continue;

				string relative = Key(written);

				if (!seen.Add(relative)) continue;
				if (!snapshot.Files.TryGetValue(relative, out SnapshotEntry entry)) continue;

				string path = FileTree.Combine(root, relative);

				if (!File.Exists(path))
				{
					differences.Add(new SnapshotDifference(SnapshotDifferenceKind.Missing, relative));
					continue;
				}

				if (new FileInfo(path).Length != entry.Length || FileHash.Compute(path) != entry.Hash)
				{
					differences.Add(new SnapshotDifference(SnapshotDifferenceKind.Changed, relative));
				}
			}

			differences.Sort((a, b) => String.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

			return differences;
		}

		/// <summary>
		/// Turns one message out of a difference list. It returns an empty string when the
		/// list is empty.
		/// </summary>
		public static string Describe(IReadOnlyList<SnapshotDifference> differences)
		{
			if (differences is null || differences.Count == 0) return String.Empty;

			var names = new List<string>(differences.Count);

			foreach (SnapshotDifference difference in differences) names.Add(difference.RelativePath);

			return $"The vanilla copy no longer matches the record of it. {differences.Count} files changed " +
				$"after this application recorded them: {String.Join(", ", names)}. A deploy against a " +
				"changed baseline gives a wrong result, because each mod then edits a file that another " +
				"mod already edited. Repair the game install, delete the workspace directory, and deploy " +
				"again.";
		}

		/// <summary>
		/// Turns a path that a script wrote, or a full path in the staging copy, into the key
		/// that a snapshot uses.
		///
		/// A snapshot key holds a forward slash. A script writes a backslash, and a full path
		/// holds the separator of this machine.
		/// </summary>
		private static string Key(string written)
		{
			return written.Replace('\\', '/').TrimStart('/');
		}

		/// <summary>
		/// Turns a full path inside the staging copy into the key that a snapshot uses.
		/// Returns null when the path is outside the staging copy.
		/// </summary>
		public static string RelativeTo(string stagingDirectory, string fullPath)
		{
			if (String.IsNullOrWhiteSpace(stagingDirectory)) return null;
			if (String.IsNullOrWhiteSpace(fullPath)) return null;

			string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDirectory));
			string full = Path.GetFullPath(fullPath);

			if (!FileTree.IsSameOrInside(full, root)) return null;

			return Key(full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/', '\\'));
		}
	}
}
