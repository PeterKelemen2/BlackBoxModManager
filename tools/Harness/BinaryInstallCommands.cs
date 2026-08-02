using System;
using System.Collections.Generic;
using BlackboxModManager.Core;
using Nikki.Core;

namespace Harness
{
	/// <summary>
	/// The console face of the Binary install discovery. Step 5 replaces this with the UI.
	/// The logic sits in BlackboxModManager.Core. This file only prints and prompts.
	/// </summary>
	internal static class BinaryInstallCommands
	{
		/// <summary>
		/// Runs --show-binary, --set-binary, or --forget-binary. Each one stops the harness.
		/// </summary>
		public static int Run(Options options)
		{
			if (options.ProbeDir != null) return Probe(options.ProbeDir);

			var service = new BinaryInstallService();

			if (options.ForgetBinary)
			{
				service.Forget();
				Console.WriteLine($"The stored Binary install is removed from {AppPaths.SettingsFile}.");
				return 0;
			}

			if (options.SetBinaryDir != null)
			{
				BinaryInstallStatus status = service.Store(options.SetBinaryDir);

				if (!status.IsUsable)
				{
					Report(status);
					Console.Error.WriteLine("ERROR: The directory is not stored, because it failed a check.");
					return 2;
				}

				Report(status);
				Console.WriteLine($"Stored in {AppPaths.SettingsFile}.");
				return 0;
			}

			BinaryInstallResolution resolution = service.Resolve();
			ReportResolution(resolution);
			return resolution.IsUsable ? 0 : 2;
		}

		/// <summary>
		/// Resolves the install for a deploy run. Prints the outcome either way.
		///
		/// There is no question here. Console.ReadLine never returns on a Wine console, and
		/// Wine is the only environment that this harness runs in. The first-run question
		/// belongs to the UI of step 5, as a dialog. This method reports what the locator
		/// found and names the command that answers it.
		/// </summary>
		public static bool TryResolve(string overridePath, out BinaryInstall install)
		{
			install = null;

			var service = new BinaryInstallService();
			BinaryInstallResolution resolution = service.Resolve(overridePath);

			ReportResolution(resolution);

			if (resolution.IsUsable)
			{
				install = resolution.Install;
				return true;
			}

			Console.Error.WriteLine();
			Console.Error.WriteLine("ERROR: No usable Binary install. The container editor needs its hash lists.");

			if (resolution.Candidates.Count > 0)
			{
				Console.Error.WriteLine($"  Fix         --set-binary \"{resolution.Candidates[0]}\"");
			}
			else
			{
				Console.Error.WriteLine("  Fix         Run the harness once with --set-binary <dir>.");
			}

			Console.Error.WriteLine("  Or          Pass --binary <dir> for one run only.");
			return false;
		}

		/// <summary>
		/// Reports which link methods the target directory supports. Step 5 needs this
		/// answer per Wine build and per filesystem. Copy always works, so this never
		/// blocks a deploy. It only decides the cost.
		/// </summary>
		private static int Probe(string directory)
		{
			Console.WriteLine($"Directory     {directory}");
			Console.WriteLine($"OS            {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
			Console.WriteLine();
			Console.WriteLine("Link methods");

			LinkProbeResult links = LinkSupport.Probe(directory);

			foreach (LinkProbe probe in links.Probes)
			{
				Console.WriteLine($"  {probe.Kind,-14} {(probe.Works ? "works" : "FAILED")}");

				if (!probe.Works) Console.WriteLine($"                 {probe.Error}");
			}

			Console.WriteLine($"  Best           {links.Best}");
			Console.WriteLine();
			Console.WriteLine("Path handling");

			PathCaseResult paths = PathCase.Probe(directory);

			if (paths.Error.Length > 0)
			{
				Console.WriteLine($"  FAILED         {paths.Error}");
			}
			else
			{
				Console.WriteLine($"  Letter case    {(paths.IsCaseInsensitive ? "insensitive" : "SENSITIVE")}");
				Console.WriteLine($"  Backslash      {(paths.AcceptsBackslash ? "works as a separator" : "DOES NOT WORK")}");
			}

			// Copy is the floor. A directory where even a copy fails is not usable at all.
			return links.Works(LinkKind.Copy) ? 0 : 1;
		}

		private static void ReportResolution(BinaryInstallResolution resolution)
		{
			Console.WriteLine($"Source        {Describe(resolution.Source)}");
			Report(resolution.Status);

			if (resolution.Candidates.Count == 0)
			{
				if (resolution.Source == BinaryInstallSource.None)
				{
					Console.WriteLine("Candidates    none. This machine holds no install that the locator can see.");
				}

				return;
			}

			Console.WriteLine($"Candidates    {resolution.Candidates.Count}. Each one needs the confirmation of the user.");

			foreach (string candidate in resolution.Candidates) Console.WriteLine($"  {candidate}");
		}

		private static void Report(BinaryInstallStatus status)
		{
			if (status.Root != null) Console.WriteLine($"Root          {status.Root}");

			Console.WriteLine($"Check         {status.Check}");

			if (status.Version != null) Console.WriteLine($"Version       {status.Version}");

			if (status.Message.Length > 0) Console.WriteLine($"Problem       {status.Message}");

			if (status.VersionWarning.Length > 0) Console.WriteLine($"WARNING       {status.VersionWarning}");

			if (!status.IsUsable) return;

			Console.WriteLine("Hash lists    all six games present");

			foreach (GameINT game in HashListPaths.SupportedGames)
			{
				Console.WriteLine($"  {game,-13} {HashListPaths.FileName(game)}");
			}
		}

		private static string Describe(BinaryInstallSource source)
		{
			return source switch
			{
				BinaryInstallSource.Override => "the --binary argument of this run",
				BinaryInstallSource.Settings => "the settings file",
				_ => "nothing. No path is stored.",
			};
		}
	}
}
