using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

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
	/// This class refuses one thing only. The switches below make 7-Zip write a link entry
	/// as a plain file, so no entry of an archive can create a link on the disk.
	/// <c>ArchiveExtractor</c> owns every other check. It reads the listing of the archive
	/// first, it refuses a name that leaves the target directory, and it walks the target
	/// afterwards.
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
		/// How often the wait tests the child process and the cancel. See
		/// <c>ProcessRunner.Wait</c>, which polls at the same rate.
		/// </summary>
		private const int PollMilliseconds = 200;

		/// <summary>
		/// Extracts every entry into the target directory, and reports each file.
		///
		/// It returns the number of files that 7-Zip named. The count comes from the output
		/// of the program, so it counts what reached the disk.
		///
		/// A cancel ends the child process and throws <c>OperationCanceledException</c>. The
		/// caller then removes the scratch directory.
		///
		/// <b>A failure throws and does not fall back.</b> A fallback would repeat a broken
		/// read for half an hour and then report the same failure.
		/// </summary>
		public static int Extract(string archivePath, string target, int total,
			IProgress<ImportProgress> progress, CancellationToken cancellation = default)
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

			// -snl- and -snh- make 7-Zip write a link entry as a plain file. A real link in
			// the target lets a later entry write through it to a place outside the target,
			// and the guard of ArchiveExtractor never sees that write. See
			// docs/roadmap/99-api-notes.md for the switch names of 7-Zip 26.01.
			start.ArgumentList.Add("-snl-");
			start.ArgumentList.Add("-snh-");

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

				// Read the output on another thread. A wait cannot poll for a cancel while it
				// blocks on ReadLine, and a full pipe buffer stops the program until somebody
				// reads it. The runtime raises this event one line at a time, so the counter
				// needs no lock.
				process.OutputDataReceived += (sender, line) =>
				{
					string name = FileNameOf(line.Data);

					if (name is null) return;

					++written;

					reporter.File(written, total, name);
				};

				process.Start();

				// An archive that wants a password stops for an answer. The closed input gives
				// the program an end of file, so it fails instead of waiting forever.
				process.StandardInput.Close();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				Wait(process, cancellation);

				// Let the two readers drain what the program wrote last.
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
			catch (OperationCanceledException)
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
		/// Waits for the program, and ends the wait when the user cancels.
		///
		/// A canceled import must not leave the program writing into the scratch directory,
		/// because the caller removes that directory next. So a cancel ends the program
		/// first. This follows <c>ProcessRunner.Wait</c>.
		///
		/// <b>This method sets no timeout.</b> A 4 GB archive is legitimate, and a timeout
		/// needs a limit that the size of the archive gives. Step 19, Part 8 owns that.
		/// </summary>
		private static void Wait(Process process, CancellationToken cancellation)
		{
			while (!process.WaitForExit(PollMilliseconds))
			{
				if (!cancellation.IsCancellationRequested) continue;

				Kill(process);
				cancellation.ThrowIfCancellationRequested();
			}
		}

		private static void Kill(Process process)
		{
			try
			{
				if (!process.HasExited) process.Kill(true);
			}
			catch (Exception)
			{
				// A program that ended by itself needs no kill.
			}
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
