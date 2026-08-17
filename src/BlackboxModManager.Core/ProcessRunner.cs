using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// What one program did when we ran it.
	/// </summary>
	public sealed class ProcessResult
	{
		public int ExitCode { get; }

		/// <summary>Whatever reached our pipe. This can be empty for a program that owns a console.</summary>
		public string StandardOutput { get; }

		public string StandardError { get; }

		/// <summary>True when the program ran past its time and we ended it.</summary>
		public bool TimedOut { get; }

		public TimeSpan Duration { get; }

		public ProcessResult(int exitCode, string standardOutput, string standardError, bool timedOut,
			TimeSpan duration)
		{
			this.ExitCode = exitCode;
			this.StandardOutput = standardOutput ?? String.Empty;
			this.StandardError = standardError ?? String.Empty;
			this.TimedOut = timedOut;
			this.Duration = duration;
		}
	}

	/// <summary>
	/// One program to run.
	/// </summary>
	public sealed class ProcessRequest
	{
		public string ExecutablePath { get; }

		/// <summary>
		/// The arguments, one per entry. <b>Never join these into one string.</b> A mod path
		/// holds a space, and a joined command line then splits in the wrong place.
		/// </summary>
		public IReadOnlyList<string> Arguments { get; }

		/// <summary>
		/// The directory that the program starts in. A program that writes a log file with a
		/// bare name writes it here.
		/// </summary>
		public string WorkingDirectory { get; }

		public TimeSpan Timeout { get; }

		public ProcessRequest(string executablePath, IReadOnlyList<string> arguments,
			string workingDirectory, TimeSpan timeout)
		{
			this.ExecutablePath = executablePath;
			this.Arguments = arguments ?? Array.Empty<string>();
			this.WorkingDirectory = workingDirectory;
			this.Timeout = timeout;
		}
	}

	/// <summary>
	/// Runs one program and reports what it did.
	///
	/// <b>The interface exists so that a test can replace the program.</b> The CLI route runs
	/// Binary.exe, which we cannot redistribute and which needs a runtime that a build agent
	/// does not have. A test supplies its own runner and writes the log files that the real
	/// program would write.
	/// </summary>
	public interface IProcessRunner
	{
		ProcessResult Run(ProcessRequest request, CancellationToken cancellation = default);
	}

	/// <summary>
	/// Runs a real program.
	///
	/// This follows the shape that SevenZipTool already uses. Two rules matter for every
	/// program that we start.
	///
	/// 1. Close the input at once. A program that waits for an answer then fails instead of
	///    holding the deploy open until the timeout.
	/// 2. Read the output on another thread. A program that fills the pipe buffer stops until
	///    somebody reads it, and a wait for exit would then never return.
	/// </summary>
	public sealed class ProcessRunner : IProcessRunner
	{
		public ProcessResult Run(ProcessRequest request, CancellationToken cancellation = default)
		{
			if (request is null) throw new ArgumentNullException(nameof(request));

			var start = new ProcessStartInfo
			{
				FileName = request.ExecutablePath,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			foreach (string argument in request.Arguments) start.ArgumentList.Add(argument);

			if (!String.IsNullOrWhiteSpace(request.WorkingDirectory))
			{
				start.WorkingDirectory = request.WorkingDirectory;
			}

			var output = new StringBuilder();
			var errors = new StringBuilder();
			var clock = Stopwatch.StartNew();

			using (var process = new Process())
			{
				process.StartInfo = start;

				process.OutputDataReceived += (sender, args) =>
				{
					if (args.Data != null) output.AppendLine(args.Data);
				};

				process.ErrorDataReceived += (sender, args) =>
				{
					if (args.Data != null) errors.AppendLine(args.Data);
				};

				try
				{
					process.Start();
				}
				catch (Win32Exception exception)
				{
					throw new ProcessStartException(
						$"{Path.GetFileName(request.ExecutablePath)} did not start. {exception.Message}",
						exception);
				}

				process.BeginOutputReadLine();
				process.BeginErrorReadLine();

				// Close the input now. A program that asks a question reads the end of the
				// stream and fails, and it does not hold the deploy open.
				try
				{
					process.StandardInput.Close();
				}
				catch (Exception)
				{
					// A program that already ended needs no closed input.
				}

				bool exited = Wait(process, request.Timeout, cancellation);

				if (!exited)
				{
					Kill(process);
					clock.Stop();

					return new ProcessResult(-1, output.ToString(), errors.ToString(), true, clock.Elapsed);
				}

				// Let the two readers drain what the program wrote last.
				process.WaitForExit();
				clock.Stop();

				return new ProcessResult(process.ExitCode, output.ToString(), errors.ToString(),
					false, clock.Elapsed);
			}
		}

		/// <summary>
		/// Waits for the program, and ends the wait when the user cancels.
		///
		/// A canceled deploy must not leave the program running against the staging copy. So a
		/// cancel ends the program, and the caller then reports the cancel.
		/// </summary>
		private static bool Wait(Process process, TimeSpan timeout, CancellationToken cancellation)
		{
			var clock = Stopwatch.StartNew();

			while (clock.Elapsed < timeout)
			{
				if (process.WaitForExit(200)) return true;

				if (!cancellation.IsCancellationRequested) continue;

				Kill(process);
				cancellation.ThrowIfCancellationRequested();
			}

			return process.WaitForExit(0);
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
	}

	/// <summary>
	/// A program that we could not start. The message names the program.
	/// </summary>
	public sealed class ProcessStartException : Exception
	{
		public ProcessStartException(string message, Exception inner = null) : base(message, inner) { }
	}
}
