using System;
using System.Collections.Generic;
using System.Globalization;
using BlackboxModManager.Core;

namespace Harness
{
	/// <summary>
	/// The command line of the harness. The harness must run without a human, so every
	/// answer comes from an argument. See docs/roadmap/01-console-harness.md, work item 8.
	/// </summary>
	internal sealed class Options
	{
		/// <summary>
		/// The vanilla install. The harness reads this directory. The harness never writes to it.
		/// </summary>
		public string VanillaDir { get; private set; } = Defaults.VanillaDir;

		/// <summary>
		/// The scratch copy. The harness deletes and rebuilds this directory on every run.
		/// </summary>
		public string ScratchDir { get; private set; } = Defaults.ScratchDir;

		/// <summary>
		/// Overrides the hash list that the Binary install would give. Null means derive it.
		/// </summary>
		public string MainHashList { get; private set; }

		/// <summary>
		/// Overrides the hash list output path. Null means put it under our application data.
		/// </summary>
		public string CustomHashList { get; private set; }

		/// <summary>
		/// The Binary install for this run only. Null means read the stored answer.
		/// </summary>
		public string BinaryDir { get; private set; }

		/// <summary>
		/// Validates a Binary install directory, stores it, and stops.
		/// </summary>
		public string SetBinaryDir { get; private set; }

		/// <summary>
		/// Removes the stored Binary install directory and stops.
		/// </summary>
		public bool ForgetBinary { get; private set; }

		/// <summary>
		/// Reports the Binary install that this machine gives, and stops.
		/// </summary>
		public bool ShowBinary { get; private set; }

		public string ManifestPath { get; private set; }

		/// <summary>
		/// One answer for each option pause, in the order that the pauses occur.
		/// </summary>
		public List<int> Choices { get; } = new List<int>();

		/// <summary>
		/// Keeps the scratch copy from the last run. Use this to iterate. A 1.7 GB copy is slow.
		/// </summary>
		public bool SkipCopy { get; private set; }

		/// <summary>
		/// Parses the script and reports the counts. Loads no container and writes no file.
		/// </summary>
		public bool CountOnly { get; private set; }

		/// <summary>
		/// True when the run manages the Binary install and applies no manifest.
		/// </summary>
		public bool IsInstallCommand =>
			this.SetBinaryDir != null || this.ForgetBinary || this.ShowBinary;

		public static bool TryParse(string[] args, out Options options, out string error)
		{
			options = new Options();
			error = null;

			for (int i = 0; i < args.Length; ++i)
			{
				string arg = args[i];

				switch (arg)
				{
					case "--manifest":
						if (!Next(args, ref i, arg, out string manifest, out error)) return false;
						options.ManifestPath = manifest;
						break;

					case "--game":
						if (!Next(args, ref i, arg, out string game, out error)) return false;
						options.VanillaDir = game;
						break;

					case "--scratch":
						if (!Next(args, ref i, arg, out string scratch, out error)) return false;
						options.ScratchDir = scratch;
						break;

					case "--main-keys":
						if (!Next(args, ref i, arg, out string mainKeys, out error)) return false;
						options.MainHashList = mainKeys;
						break;

					case "--custom-keys":
						if (!Next(args, ref i, arg, out string customKeys, out error)) return false;
						options.CustomHashList = customKeys;
						break;

					case "--binary":
						if (!Next(args, ref i, arg, out string binary, out error)) return false;
						options.BinaryDir = binary;
						break;

					case "--set-binary":
						if (!Next(args, ref i, arg, out string setBinary, out error)) return false;
						options.SetBinaryDir = setBinary;
						break;

					case "--forget-binary":
						options.ForgetBinary = true;
						break;

					case "--show-binary":
						options.ShowBinary = true;
						break;

					case "--choice":
						if (!Next(args, ref i, arg, out string choices, out error)) return false;
						if (!ParseChoices(choices, options.Choices, out error)) return false;
						break;

					case "--skip-copy":
						options.SkipCopy = true;
						break;

					case "--count-only":
						options.CountOnly = true;
						break;

					case "--help":
					case "-h":
						error = null;
						options = null;
						return false;

					default:
						error = $"Unknown argument {arg}.";
						return false;
				}
			}

			// The install management commands do their work and stop. They need no manifest.
			if (options.IsInstallCommand) return true;

			if (String.IsNullOrEmpty(options.ManifestPath))
			{
				error = "No manifest. Use --manifest <path>.";
				return false;
			}

			return true;
		}

		private static bool ParseChoices(string value, List<int> target, out string error)
		{
			error = null;

			foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
			{
				if (!Int32.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int choice))
				{
					error = $"The choice {part.Trim()} is not a whole number.";
					return false;
				}

				if (choice < 0)
				{
					error = $"The choice {choice} is negative.";
					return false;
				}

				target.Add(choice);
			}

			return true;
		}

		private static bool Next(string[] args, ref int index, string name, out string value, out string error)
		{
			if (index + 1 >= args.Length)
			{
				value = null;
				error = $"The argument {name} needs a value.";
				return false;
			}

			value = args[++index];
			error = null;
			return true;
		}

		public static void PrintUsage()
		{
			Console.WriteLine("Harness --manifest <path> [options]");
			Console.WriteLine();
			Console.WriteLine("  --manifest <path>      The VERSN1 manifest to apply. Required.");
			Console.WriteLine("  --choice <n[,n...]>    One answer for each option pause, in order.");
			Console.WriteLine("  --game <dir>           The vanilla install to copy. The harness reads it only.");
			Console.WriteLine("  --scratch <dir>        The scratch copy. The harness deletes this on every run.");
			Console.WriteLine("  --binary <dir>         The Binary install for this run only.");
			Console.WriteLine("  --main-keys <file>     Overrides the mainkeys list that the install gives.");
			Console.WriteLine("  --custom-keys <file>   Overrides the hash list output path.");
			Console.WriteLine("  --skip-copy            Keeps the scratch copy from the last run.");
			Console.WriteLine("  --count-only           Parses the script and stops. Writes no file.");
			Console.WriteLine("  --help                 Shows this text.");
			Console.WriteLine();
			Console.WriteLine("Binary install commands. Each one does its work and stops.");
			Console.WriteLine();
			Console.WriteLine("  --show-binary          Reports the install, the candidates, and the paths.");
			Console.WriteLine("  --set-binary <dir>     Validates a directory and stores it.");
			Console.WriteLine("  --forget-binary        Removes the stored directory.");
			Console.WriteLine();
			Console.WriteLine("Defaults:");
			Console.WriteLine($"  --game         {Defaults.VanillaDir}");
			Console.WriteLine($"  --scratch      {Defaults.ScratchDir}");
			Console.WriteLine($"  settings       {AppPaths.SettingsFile}");
			Console.WriteLine($"  custom keys    {AppPaths.CustomKeysDirectory}");
		}
	}

	/// <summary>
	/// The game paths of one developer machine. They come from
	/// docs/roadmap/00-test-environment.md. The values are Wine drive Z paths, because the
	/// harness runs under Wine.
	///
	/// The Binary install is no longer here. Step 2 replaced it with discovery. The hash
	/// list paths come from BinaryInstallService and AppPaths.
	/// </summary>
	internal static class Defaults
	{
		public const string VanillaDir =
			@"Z:\mnt\Data\Games\WinePrefixes\NFSU2ModTest\drive_c\Program Files (x86)\EA GAMES\Need for Speed Underground 2";

		public const string ScratchDir =
			@"Z:\mnt\Data\Games\HarnessScratch\Need for Speed Underground 2";
	}
}
