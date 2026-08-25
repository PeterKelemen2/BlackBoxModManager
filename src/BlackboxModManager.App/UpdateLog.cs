using System;
using System.IO;
using BlackboxModManager.Core;
using Velopack.Logging;

namespace BlackboxModManager.App
{
	/// <summary>
	/// Writes what Velopack reports into %APPDATA%\BlackBoxModManager\logs\update.log.
	///
	/// <b>An install step and an uninstall step run with no window and no console.</b> Velopack
	/// starts this program again with a hook argument, and that process shows nothing. Without
	/// this file a failure in one of those steps leaves no trace at all.
	///
	/// This class swallows every error, in the same way that App.WriteErrorLog does. A logger
	/// that throws would break the operation that it only had to describe.
	/// </summary>
	internal sealed class UpdateLog : IVelopackLogger
	{
		/// <summary>Below this level the log says nothing. Trace and Debug are noise.</summary>
		private const VelopackLogLevel Least = VelopackLogLevel.Information;

		public void Log(VelopackLogLevel level, string message, Exception exception)
		{
			if (level < Least && exception == null) return;

			try
			{
				Directory.CreateDirectory(AppPaths.LogDirectory);

				string text = $"{DateTimeOffset.UtcNow:u} [{level}] {message}";

				if (exception != null) text += Environment.NewLine + exception;

				File.AppendAllText(
					Path.Combine(AppPaths.LogDirectory, "update.log"),
					text + Environment.NewLine);
			}
			catch (Exception)
			{
				// The log is a convenience. Never let it stop an install or an update.
			}
		}
	}
}
