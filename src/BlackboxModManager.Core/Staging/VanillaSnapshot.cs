using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// One file in a snapshot.
	/// </summary>
	public sealed class SnapshotEntry
	{
		/// <summary>The XxHash128 of the content, as lowercase hexadecimal.</summary>
		public string Hash { get; set; }

		public long Length { get; set; }
	}

	/// <summary>
	/// The state of a game install at one moment, by content.
	///
	/// <b>A snapshot never holds a modification time.</b> An extraction resets the
	/// timestamp of a file, and a copy resets it again, so a time proves nothing. The hash
	/// of the content is the only answer.
	/// </summary>
	public sealed class VanillaSnapshot
	{
		/// <summary>The shape of the file. Raise this when a change needs a migration.</summary>
		public int Version { get; set; } = 1;

		/// <summary>The directory that the snapshot read.</summary>
		public string Root { get; set; }

		public DateTimeOffset Created { get; set; }

		/// <summary>
		/// One entry per file, keyed by the path relative to the root, with a forward slash
		/// separator.
		/// </summary>
		public Dictionary<string, SnapshotEntry> Files { get; set; } =
			new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);

		[JsonIgnore]
		public int Count => this.Files.Count;

		[JsonIgnore]
		public long TotalBytes
		{
			get
			{
				long total = 0;

				foreach (SnapshotEntry entry in this.Files.Values) total += entry.Length;

				return total;
			}
		}
	}

	/// <summary>
	/// How one file differs from the snapshot.
	/// </summary>
	public enum SnapshotDifferenceKind
	{
		/// <summary>The snapshot holds the file and the directory does not.</summary>
		Missing = 0,

		/// <summary>Both hold the file and the content differs.</summary>
		Changed,

		/// <summary>The directory holds the file and the snapshot does not.</summary>
		Added,
	}

	public sealed class SnapshotDifference
	{
		public SnapshotDifferenceKind Kind { get; }

		public string RelativePath { get; }

		public SnapshotDifference(SnapshotDifferenceKind kind, string relativePath)
		{
			this.Kind = kind;
			this.RelativePath = relativePath;
		}

		public override string ToString() => $"{this.Kind}: {this.RelativePath}";
	}

	/// <summary>
	/// Reads a game install into a snapshot, and compares a directory against one.
	/// </summary>
	public static class SnapshotReader
	{
		/// <summary>
		/// The extension that Binary gives to its own backup of a container.
		///
		/// <b>Ignore these files.</b> GLOBAL/GLOBALA.BUN.bacc and GLOBAL/GLOBALB.LZC.bacc
		/// sit beside the real files in an install that Binary has edited. They are the
		/// bookkeeping of another tool, not game content. A snapshot that holds them treats
		/// the state after a Binary run as the vanilla state.
		/// </summary>
		public const string BackupExtension = ".bacc";

		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			WriteIndented = false,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		/// <summary>
		/// True when the snapshot leaves this file out.
		/// </summary>
		public static bool Ignores(string relativePath)
		{
			return String.Equals(Path.GetExtension(relativePath), BackupExtension, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Reads every file of a directory and hashes it. This reads the whole install, so
		/// it takes as long as a read of the disk allows.
		/// </summary>
		public static VanillaSnapshot Create(string root, Action<string> log = null)
		{
			if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("The root is empty.", nameof(root));

			string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

			if (!Directory.Exists(full))
			{
				throw new DirectoryNotFoundException($"The directory {full} does not exist.");
			}

			var snapshot = new VanillaSnapshot
			{
				Root = full,
				Created = DateTimeOffset.UtcNow,
			};

			IReadOnlyList<string> files = FileTree.Files(full, IsWorkspace);
			int read = 0;

			foreach (string relative in files)
			{
				if (Ignores(relative)) continue;

				string path = FileTree.Combine(full, relative);

				snapshot.Files[relative] = new SnapshotEntry
				{
					Hash = FileHash.Compute(path),
					Length = new FileInfo(path).Length,
				};

				if (++read % 500 == 0) log?.Invoke($"The snapshot read {read} of {files.Count} files.");
			}

			log?.Invoke($"The snapshot holds {snapshot.Count} files and {snapshot.TotalBytes / (1024 * 1024)} MB.");

			return snapshot;
		}

		/// <summary>
		/// Compares a directory against a snapshot.
		///
		/// Pass false in hashContent to compare the length only. A quick compare catches a
		/// missing file and a file of a different size. It does not catch a file that
		/// changed and kept its size. Use the full compare when the answer has to be exact.
		/// </summary>
		public static IReadOnlyList<SnapshotDifference> Compare(VanillaSnapshot snapshot, string root,
			bool hashContent = true, Func<string, bool> skip = null)
		{
			if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

			var differences = new List<SnapshotDifference>();
			string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string relative in FileTree.Files(full, IsWorkspace))
			{
				if (Ignores(relative)) continue;
				if (skip != null && skip(relative)) continue;

				seen.Add(relative);

				if (!snapshot.Files.TryGetValue(relative, out SnapshotEntry entry))
				{
					differences.Add(new SnapshotDifference(SnapshotDifferenceKind.Added, relative));
					continue;
				}

				string path = FileTree.Combine(full, relative);
				var info = new FileInfo(path);

				if (info.Length != entry.Length)
				{
					differences.Add(new SnapshotDifference(SnapshotDifferenceKind.Changed, relative));
					continue;
				}

				if (hashContent && FileHash.Compute(path) != entry.Hash)
				{
					differences.Add(new SnapshotDifference(SnapshotDifferenceKind.Changed, relative));
				}
			}

			foreach (string relative in snapshot.Files.Keys)
			{
				if (seen.Contains(relative)) continue;
				if (skip != null && skip(relative)) continue;

				differences.Add(new SnapshotDifference(SnapshotDifferenceKind.Missing, relative));
			}

			differences.Sort((a, b) => String.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

			return differences;
		}

		public static void Save(string path, VanillaSnapshot snapshot)
		{
			if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

			FileTree.CreateParent(path);

			string temporary = path + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, Options));
			File.Move(temporary, path, true);
		}

		/// <summary>
		/// Reads a snapshot file. It returns null when the file is absent or damaged. A
		/// caller that gets null must take a new snapshot, never continue without one.
		/// </summary>
		public static VanillaSnapshot Load(string path)
		{
			try
			{
				if (!File.Exists(path)) return null;

				VanillaSnapshot snapshot = JsonSerializer.Deserialize<VanillaSnapshot>(File.ReadAllText(path), Options);

				if (snapshot?.Files is null) return null;

				// A deserialized dictionary carries the default comparer, which compares
				// letter case. Rebuild it so that a lookup behaves the same way everywhere.
				var files = new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);

				foreach (KeyValuePair<string, SnapshotEntry> entry in snapshot.Files) files[entry.Key] = entry.Value;

				snapshot.Files = files;
				return snapshot;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// True when a directory belongs to this application. A workspace beside the game is
		/// the normal layout, and a workspace inside the game must never enter a snapshot.
		/// </summary>
		private static bool IsWorkspace(string relativeDirectory)
		{
			return relativeDirectory.EndsWith(GameWorkspace.WorkspaceSuffix, StringComparison.OrdinalIgnoreCase);
		}
	}
}
