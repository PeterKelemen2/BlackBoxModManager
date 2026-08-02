using System;
using System.IO;
using BlackboxModManager.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the path probe of step 3.
	///
	/// These tests do not assert an answer. The answer belongs to the filesystem and the
	/// runtime, and it differs between a Wine run and a native Linux run. That is the whole
	/// reason the probe exists. The recorded answers live in
	/// docs/roadmap/03-wine-verification.md.
	/// </summary>
	public class PathCaseTests : IDisposable
	{
		private readonly string _root;

		public PathCaseTests()
		{
			this._root = Path.Combine(Path.GetTempPath(), $"case-test-{Guid.NewGuid():N}");
			Directory.CreateDirectory(this._root);
		}

		public void Dispose()
		{
			if (Directory.Exists(this._root)) Directory.Delete(this._root, true);
		}

		[Fact]
		public void TheProbeAnswersWithoutAnError()
		{
			PathCaseResult result = PathCase.Probe(this._root);

			Assert.Empty(result.Error);
			Assert.Equal(this._root, result.Directory);
		}

		[Fact]
		public void TheProbeRemovesEverythingThatItMade()
		{
			PathCase.Probe(this._root);

			Assert.Empty(Directory.GetFileSystemEntries(this._root));
		}

		[Fact]
		public void ADirectoryThatCannotBeUsedReportsAnErrorAndDoesNotThrow()
		{
			PathCaseResult result = PathCase.Probe(Path.Combine(this._root, "\0bad"));

			Assert.NotEmpty(result.Error);
			Assert.False(result.IsCaseInsensitive);
			Assert.False(result.AcceptsBackslash);
		}

		[Fact]
		public void AnEmptyDirectoryArgumentIsARejectedArgument()
		{
			Assert.Throws<ArgumentException>(() => PathCase.Probe(" "));
		}

		[Fact]
		public void ANativeLinuxRunOnAnExtFilesystemIsCaseSensitive()
		{
			// This is the failure that step 1 recorded. The manifests declare
			// GLOBAL\GLOBALB.LZC and the file on disk is GLOBAL/GlobalB.lzc. Wine resolves
			// the difference. A native run does not, and CheckFiles then throws for a file
			// that the directory listing shows.
			if (OperatingSystem.IsWindows()) return;

			PathCaseResult result = PathCase.Probe(this._root);

			Assert.False(result.IsCaseInsensitive);
			Assert.False(result.AcceptsBackslash);
		}
	}
}
