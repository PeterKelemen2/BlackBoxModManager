using System;
using System.Collections.Generic;
using System.Globalization;

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

		public string MainHashList { get; private set; } = Defaults.MainHashList;

		public string CustomHashList { get; private set; } = Defaults.CustomHashList;

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
			Console.WriteLine("  --main-keys <file>     The mainkeys list of Binary for Underground 2.");
			Console.WriteLine("  --custom-keys <file>   The hash list output path. Never point this into Binary.");
			Console.WriteLine("  --skip-copy            Keeps the scratch copy from the last run.");
			Console.WriteLine("  --count-only           Parses the script and stops. Writes no file.");
			Console.WriteLine("  --help                 Shows this text.");
			Console.WriteLine();
			Console.WriteLine("Defaults:");
			Console.WriteLine($"  --game         {Defaults.VanillaDir}");
			Console.WriteLine($"  --scratch      {Defaults.ScratchDir}");
			Console.WriteLine($"  --main-keys    {Defaults.MainHashList}");
			Console.WriteLine($"  --custom-keys  {Defaults.CustomHashList}");
		}
	}

	/// <summary>
	/// Machine paths for step 1. Step 2 replaces the Binary path with discovery.
	/// The values are Wine drive Z paths, because the harness runs under Wine.
	/// The paths come from docs/roadmap/00-test-environment.md.
	/// </summary>
	internal static class Defaults
	{
		public const string VanillaDir =
			@"Z:\mnt\Data\Games\WinePrefixes\NFSU2ModTest\drive_c\Program Files (x86)\EA GAMES\Need for Speed Underground 2";

		public const string ScratchDir =
			@"Z:\mnt\Data\Games\HarnessScratch\Need for Speed Underground 2";

		public const string MainHashList =
			@"Z:\mnt\Data\Games\Binary_v2.8.3\mainkeys\underground2.txt";

		// Defect 7: Save writes this file and creates its directory. Keep it out of the
		// Binary install, and keep it out of the scratch copy that the next run deletes.
		public const string CustomHashList =
			@"Z:\mnt\Data\Games\HarnessData\userkeys\underground2.txt";
	}
}
