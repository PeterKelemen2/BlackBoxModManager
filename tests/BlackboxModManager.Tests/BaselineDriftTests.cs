using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Staging;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the check that stops a deploy against a changed baseline.
	///
	/// A past deploy wrote through a hard link and rewrote 8 containers in the vanilla copy of
	/// a real install. Every later deploy then read modded input and reported errors that named
	/// no cause. See defect 16.
	///
	/// The check answers one question. Does the vanilla copy still hold what the snapshot
	/// recorded. It never asks whether the content is vanilla, because a user may have modded
	/// the install by hand before the snapshot.
	/// </summary>
	public class BaselineDriftTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();

		public void Dispose() => this._temp.Dispose();

		[Fact]
		public void AnUnchangedBaselinePassesTheCheck()
		{
			(VanillaSnapshot snapshot, string root) = this.Baseline();

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				snapshot, root, new[] { @"GLOBAL\GLOBALB.LZC", @"CARS\TEXTURES.BIN" });

			Assert.Empty(drift);
		}

		/// <summary>
		/// The command <c>unlock_memory</c> writes a short header over a memory file. The
		/// length does not change, so only the content answers.
		/// </summary>
		[Fact]
		public void AChangeThatKeepsTheLengthIsFound()
		{
			(VanillaSnapshot snapshot, string root) = this.Baseline();

			string path = Path.Combine(root, "GLOBAL", "GLOBALB.LZC");
			byte[] bytes = File.ReadAllBytes(path);
			bytes[0] ^= 0xFF;
			File.WriteAllBytes(path, bytes);

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				snapshot, root, new[] { @"GLOBAL\GLOBALB.LZC" });

			SnapshotDifference found = Assert.Single(drift);
			Assert.Equal(SnapshotDifferenceKind.Changed, found.Kind);
			Assert.Equal("GLOBAL/GLOBALB.LZC", found.RelativePath);
		}

		[Fact]
		public void ATruncatedFileIsFound()
		{
			(VanillaSnapshot snapshot, string root) = this.Baseline();

			File.WriteAllText(Path.Combine(root, "CARS", "TEXTURES.BIN"), "cut");

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				snapshot, root, new[] { @"CARS\TEXTURES.BIN" });

			Assert.Single(drift);
		}

		/// <summary>
		/// The check reads only the files that the deploy writes. A file that changed and that
		/// no mod touches is not this check's business.
		/// </summary>
		[Fact]
		public void AFileOutsideTheListIsNotRead()
		{
			(VanillaSnapshot snapshot, string root) = this.Baseline();

			File.WriteAllText(Path.Combine(root, "CARS", "TEXTURES.BIN"), "cut");

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				snapshot, root, new[] { @"GLOBAL\GLOBALB.LZC" });

			Assert.Empty(drift);
		}

		/// <summary>
		/// A script creates a container with <c>new</c>, and no baseline holds it. That is
		/// normal and it is not drift.
		/// </summary>
		[Fact]
		public void AFileThatTheSnapshotNeverHeldIsNotDrift()
		{
			(VanillaSnapshot snapshot, string root) = this.Baseline();

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				snapshot, root, new[] { @"CARS\FORDGT\VINYLS.BIN" });

			Assert.Empty(drift);
		}

		[Fact]
		public void ADeletedFileIsFound()
		{
			(VanillaSnapshot snapshot, string root) = this.Baseline();

			File.Delete(Path.Combine(root, "CARS", "TEXTURES.BIN"));

			IReadOnlyList<SnapshotDifference> drift = BaselineVerifier.CheckFiles(
				snapshot, root, new[] { @"CARS\TEXTURES.BIN" });

			Assert.Equal(SnapshotDifferenceKind.Missing, Assert.Single(drift).Kind);
		}

		/// <summary>
		/// The gate reports a full path in the staging copy. The snapshot keys on the path
		/// relative to the root, with a forward slash.
		/// </summary>
		[Fact]
		public void AFullStagingPathBecomesASnapshotKey()
		{
			string staging = Path.Combine(this._temp.Path, "staging");
			string full = Path.Combine(staging, "GLOBAL", "GlobalMemoryFile.bin");

			Assert.Equal("GLOBAL/GlobalMemoryFile.bin", BaselineVerifier.RelativeTo(staging, full));
		}

		[Fact]
		public void APathOutsideTheStagingCopyHasNoKey()
		{
			string staging = Path.Combine(this._temp.Path, "staging");

			Assert.Null(BaselineVerifier.RelativeTo(staging, Path.Combine(this._temp.Path, "other", "file.bin")));
		}

		[Fact]
		public void TheMessageNamesEveryChangedFile()
		{
			var drift = new List<SnapshotDifference>
			{
				new SnapshotDifference(SnapshotDifferenceKind.Changed, "CARS/TEXTURES.BIN"),
				new SnapshotDifference(SnapshotDifferenceKind.Changed, "GLOBAL/GLOBALB.LZC"),
			};

			string message = BaselineVerifier.Describe(drift);

			Assert.Contains("CARS/TEXTURES.BIN", message, StringComparison.Ordinal);
			Assert.Contains("GLOBAL/GLOBALB.LZC", message, StringComparison.Ordinal);
			Assert.Contains("2 files changed", message, StringComparison.Ordinal);
		}

		[Fact]
		public void AnEmptyDifferenceListGivesNoMessage()
		{
			Assert.Equal(String.Empty, BaselineVerifier.Describe(Array.Empty<SnapshotDifference>()));
		}

		// ------------------------------------------------------------------------- fixtures

		/// <summary>
		/// Builds a small vanilla copy and the snapshot that records it.
		/// </summary>
		private (VanillaSnapshot Snapshot, string Root) Baseline()
		{
			string root = Path.Combine(this._temp.Path, "vanilla");

			Write(root, Path.Combine("GLOBAL", "GLOBALB.LZC"), "container one");
			Write(root, Path.Combine("CARS", "TEXTURES.BIN"), "container two");
			Write(root, Path.Combine("GLOBAL", "GlobalMemoryFile.bin"), "memory");

			VanillaSnapshot snapshot = SnapshotReader.Create(root);

			return (snapshot, root);
		}

		private static void Write(string root, string relative, string text)
		{
			string path = Path.Combine(root, relative);

			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
		}
	}
}
