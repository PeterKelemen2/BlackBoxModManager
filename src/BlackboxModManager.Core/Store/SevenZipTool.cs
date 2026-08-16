using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// Runs the 7-Zip program that ships beside the application.
	///
	/// <b>SharpCompress decodes a solid 7z once for each entry.</b> The cost of an import
	/// then grows with the square of the entry count, and a 98 MB archive of 1205 files takes
	/// more than 30 minutes. 7-Zip writes the same files in 4 seconds. See
	/// docs/roadmap/98-known-upstream-defects.md, defect 14.
	///
	/// The program is a child process and not a library call. A child process needs no
	/// interop layer, it cannot corrupt the memory of this process, and a crash of the
	/// decoder ends as an exit code.
	///
	/// This class never decides what is safe to write. <c>ArchiveExtractor</c> reads the
	/// listing of the archive first and refuses a name that leaves the target directory.
	/// </summary>
	public static class SevenZipTool
	{
		/// <summary>The directory of the program, under the directory of the application.</summary>
		public const string DirectoryName = "7-Zip";

		public const string ExecutableName = "7z.exe";

		/// <summary>
		/// The path of the program, or null when the application ships without it.
		///
		/// A build that misses the program still imports. <c>ArchiveExtractor</c> falls back
		/// to SharpCompress, which is correct and slow.
		/// </summary>
		public static string Path
		{
			get
			{
				string full = System.IO.Path.Combine(AppContext.BaseDirectory, DirectoryName, ExecutableName);

				return File.Exists(full) ? full : null;
			}
		}

		public static bool Exists => Path != null;

		/// <summary>
		/// Extracts every entry into the target directory, and reports each file.
		///
		/// It returns the number of files that 7-Zip named. The count comes from the output
		/// of the program, so it counts what reached the disk.
		///
		/// <b>A failure throws and does not fall back.</b> A fallback would repeat a broken
		/// read for half an hour and then report the same failure.
		/// </summary>
		public static int Extract(string archivePath, string target, int total,
			IProgress<ImportProgress> progress)
		{
			string tool = Path
				?? throw new ArchiveReadException($"The file {DirectoryName}\\{ExecutableName} is not beside the application.", archivePath);

			var start = new ProcessStartInfo
			{
				FileName = tool,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			// x keeps the directory of each entry. -y answers every question with yes. -bb1
			// names each item on the standard output, and this method counts those names.
			// -bsp0 drops the percent line, which would mix into the same stream.
			start.ArgumentList.Add("x");
			start.ArgumentList.Add(archivePath);
			start.ArgumentList.Add($"-o{target}");
			start.ArgumentList.Add("-y");
			start.ArgumentList.Add("-bb1");
			start.ArgumentList.Add("-bsp0");

			var errors = new StringBuilder();
			var reporter = new StageReporter(progress, ImportStage.Unpack);
			int written = 0;

			try
			{
				using var process = new Process { StartInfo = start };

				process.ErrorDataReceived += (sender, line) =>
				{
					if (line.Data != null) errors.AppendLine(line.Data);
				};

				process.Start();

				// An archive that wants a password stops for an answer. The closed input gives
				// the program an end of file, so it fails instead of waiting forever.
				process.StandardInput.Close();
				process.BeginErrorReadLine();

				string line;

				while ((line = process.StandardOutput.ReadLine()) != null)
				{
					string name = FileNameOf(line);

					if (name is null) continue;

					++written;

					reporter.File(written, total, name);
				}

				process.WaitForExit();

				// 0 is success. 1 is success with a warning, such as one file that the program
				// could not open. Every other code is a failure.
				if (process.ExitCode > 1)
				{
					throw new ArchiveReadException(
						$"7-Zip did not read the archive {archivePath}. It stopped with code {process.ExitCode}. " +
						Tail(errors.ToString()),
						archivePath);
				}
			}
			catch (ArchiveReadException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new ArchiveReadException(
					$"7-Zip did not run. {ex.Message}", archivePath, ex);
			}

			return written;
		}

		/// <summary>
		/// The file name inside one output line of 7-Zip, or null when the line names no file.
		///
		/// The -bb1 switch writes one line for each item, in the form "- path\to\file". A line
		/// that ends with a separator names a directory, and a directory is not a file.
		/// </summary>
		private static string FileNameOf(string line)
		{
			if (line is null || !line.StartsWith("- ", StringComparison.Ordinal)) return null;

			string path = line.Substring(2).TrimEnd();

			if (path.Length == 0) return null;
			if (path.EndsWith("\\", StringComparison.Ordinal)) return null;
			if (path.EndsWith("/", StringComparison.Ordinal)) return null;

			return System.IO.Path.GetFileName(path);
		}

		/// <summary>The last part of the message of the program, for an error of ours.</summary>
		private static string Tail(string text)
		{
			if (String.IsNullOrWhiteSpace(text)) return String.Empty;

			string trimmed = text.Trim();

			return trimmed.Length <= 300 ? trimmed : trimmed.Substring(trimmed.Length - 300);
		}
	}
}
