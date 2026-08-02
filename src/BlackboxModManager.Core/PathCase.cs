using System;
using System.IO;

namespace BlackboxModManager.Core
{
	public sealed class PathCaseResult
	{
		public string Directory { get; }

		/// <summary>
		/// True when a name that differs only in case opens the same file. Wine gives true.
		/// A native Linux run on ext4 gives false.
		/// </summary>
		public bool IsCaseInsensitive { get; }

		/// <summary>
		/// True when a backslash works as a directory separator. Wine gives true.
		/// </summary>
		public bool AcceptsBackslash { get; }

		public string Error { get; }

		internal PathCaseResult(string directory, bool caseInsensitive, bool backslash, string error)
		{
			this.Directory = directory;
			this.IsCaseInsensitive = caseInsensitive;
			this.AcceptsBackslash = backslash;
			this.Error = error ?? String.Empty;
		}
	}

	/// <summary>
	/// Tests how a directory treats letter case and the backslash separator.
	///
	/// This is not a question about the operating system. It is a question about the
	/// filesystem and the runtime. The manifests declare GLOBAL\GLOBALB.LZC and the file on
	/// disk is GLOBAL/GlobalB.lzc. Wine resolves both differences. A native Linux run
	/// resolves neither, and CheckFiles then throws for a file that the listing shows.
	/// </summary>
	public static class PathCase
	{
		public static PathCaseResult Probe(string directory)
		{
			if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("The directory is empty.", nameof(directory));

			string work = Path.Combine(directory, $".blackbox-case-{Guid.NewGuid():N}");

			try
			{
				Directory.CreateDirectory(Path.Combine(work, "Inner"));

				// Write with one case. Read with the other.
				File.WriteAllText(Path.Combine(work, "Inner", "MixedCase.Bin"), "probe");

				bool caseInsensitive = File.Exists(Path.Combine(work, "INNER", "MIXEDCASE.BIN"));
				bool backslash = File.Exists(work + "\\Inner\\MixedCase.Bin");

				return new PathCaseResult(directory, caseInsensitive, backslash, null);
			}
			catch (Exception ex)
			{
				return new PathCaseResult(directory, false, false, $"{ex.GetType().Name}: {ex.Message}");
			}
			finally
			{
				try
				{
					if (Directory.Exists(work)) Directory.Delete(work, true);
				}
				catch (Exception)
				{
					// A probe that cannot clean up must not fail the run.
				}
			}
		}
	}
}
