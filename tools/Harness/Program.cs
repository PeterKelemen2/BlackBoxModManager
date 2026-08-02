using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BlackboxModManager.Core;
using Endscript.Commands;
using Endscript.Core;
using Endscript.Enums;
using Endscript.Helpers;
using Endscript.Interfaces;
using Endscript.Profiles;
using Nikki.Core;

namespace Harness
{
	/// <summary>
	/// The step 1 console harness. It applies one manifest to a scratch copy of the game.
	/// It exists to answer one question: do the libraries work. Throw it away after step 3.
	/// Do not grow it into the application.
	/// </summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			// This must stay the first statement. Script floats such as -0.19500002 parse
			// wrong under a comma-decimal locale.
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

			if (!Options.TryParse(args, out Options options, out string parseError))
			{
				if (parseError != null) Console.Error.WriteLine($"ERROR: {parseError}");
				Console.WriteLine();
				Options.PrintUsage();
				return parseError is null ? 0 : 2;
			}

			try
			{
				return options.IsInstallCommand ? BinaryInstallCommands.Run(options) : Run(options);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine();
				Console.Error.WriteLine("FAILED. The harness stopped on an exception.");
				Console.Error.WriteLine(ex.ToString());
				return 1;
			}
		}

		private static int Run(Options options)
		{
			Section("Environment");
			Console.WriteLine($"Process architecture   {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
			Console.WriteLine($"Runtime                {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
			Console.WriteLine($"OS                     {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
			Console.WriteLine($"Base directory         {AppContext.BaseDirectory}");
			Console.WriteLine($"Manifest               {options.ManifestPath}");
			Console.WriteLine($"Vanilla install        {options.VanillaDir}");
			Console.WriteLine($"Scratch copy           {options.ScratchDir}");
			Console.WriteLine($"Application data       {AppPaths.Root}");
			Console.WriteLine($"Choices                {(options.Choices.Count == 0 ? "(none given)" : String.Join(", ", options.Choices))}");

			// A missing native library gives a silent P/Invoke failure deep in the container code.
			// Report it here instead.
			string lzPath = Path.Combine(AppContext.BaseDirectory, "LZCompressLib.dll");
			Console.WriteLine($"LZCompressLib.dll      {(File.Exists(lzPath) ? "present" : "MISSING")}");

			if (!File.Exists(lzPath))
			{
				Console.Error.WriteLine("ERROR: LZCompressLib.dll must sit beside the harness executable.");
				return 1;
			}

			if (!File.Exists(options.ManifestPath))
			{
				Console.Error.WriteLine($"ERROR: The manifest {options.ManifestPath} does not exist.");
				return 1;
			}

			// ---------------------------------------------------------------- Binary install

			Section("Binary install");

			if (!BinaryInstallCommands.TryResolve(options.BinaryDir, out BinaryInstall install))
			{
				return 2;
			}

			// ---------------------------------------------------------------- manifest

			Section("Manifest");
			Launch launch = ReadManifest(options.ManifestPath);

			Console.WriteLine($"ThisDir       {launch.ThisDir}");
			Console.WriteLine($"Game          {launch.Game} ({launch.GameID})");
			Console.WriteLine($"Endscript     {launch.Endscript}");
			Console.WriteLine($"Files         {launch.Files.Count}");

			foreach (string file in launch.Files) Console.WriteLine($"  {file}");

			Console.WriteLine($"Links         {launch.Links.Count}");

			foreach (SubLoader link in launch.Links) Console.WriteLine($"  {link.LoadType,-12} {link.PathType,-10} {link.File}");

			if (launch.GameID != Nikki.Core.GameINT.Underground2)
			{
				Console.Error.WriteLine($"ERROR: The harness supports Underground 2 only. The manifest names {launch.Game}.");
				return 1;
			}

			// ---------------------------------------------------------------- scratch copy

			Section("Scratch copy");

			if (options.SkipCopy)
			{
				Console.WriteLine("Skipped. The scratch copy holds the state of the last run.");

				if (!Directory.Exists(options.ScratchDir))
				{
					Console.Error.WriteLine($"ERROR: The scratch directory {options.ScratchDir} does not exist.");
					return 1;
				}
			}
			else if (!options.CountOnly)
			{
				if (!Directory.Exists(options.VanillaDir))
				{
					Console.Error.WriteLine($"ERROR: The vanilla install {options.VanillaDir} does not exist.");
					return 1;
				}

				if (IsSameOrInside(options.ScratchDir, options.VanillaDir))
				{
					Console.Error.WriteLine("ERROR: The scratch directory is the vanilla install, or sits inside it.");
					return 1;
				}

				CopyGame(options.VanillaDir, options.ScratchDir);
			}
			else
			{
				Console.WriteLine("Skipped. --count-only parses the script and writes nothing.");
			}

			// Point the manifest at the scratch copy. Never at a real install.
			// Resolve it now. Deploy moves the current directory, and a relative path would
			// then resolve against the log directory.
			launch.Directory = Path.GetFullPath(options.ScratchDir);
			launch.Usage = nameof(eUsage.Modder);

			// ---------------------------------------------------------------- script

			Section("Script");
			string scriptPath = Path.Combine(launch.ThisDir, launch.Endscript);
			Console.WriteLine($"Path          {scriptPath}");

			BaseCommand[] commands = ParseScript(scriptPath);
			ReportCommands(commands);

			if (options.CountOnly)
			{
				Console.WriteLine();
				Console.WriteLine("--count-only. The harness stops here.");
				return 0;
			}

			// ------------------------------------------------ load, run, and save

			// One gate covers the static assignment, Load, the script run, and Save. Every
			// one of those touches global state in Nikki. See defect 8.
			using (LibraryGate.Enter())
			{
				return Deploy(options, install, launch, commands);
			}
		}

		private static int Deploy(Options options, BinaryInstall install, Launch launch, BaseCommand[] commands)
		{
			Section("Load");

			// Nikki writes MainLog.txt into the current directory. Point that at our own
			// data before any container work. See defect 9.
			Directory.CreateDirectory(AppPaths.LogDirectory);
			Directory.SetCurrentDirectory(AppPaths.LogDirectory);

			GameINT game = launch.GameID;
			string mainKeys = options.MainHashList ?? install.MainHashList(game);
			string customKeys = options.CustomHashList ?? HashListPaths.CustomHashList(game);

			// Both statics must hold a value before Load. Load calls LoadHashList first.
			ProfileHashLists.Apply(mainKeys, customKeys, game);

			Console.WriteLine($"Main hash list    {mainKeys}");
			Console.WriteLine($"Custom hash list  {customKeys}");
			Console.WriteLine($"Log directory     {AppPaths.LogDirectory}");

			launch.CheckEndscript();
			launch.CheckFiles();

			BaseProfile profile = BaseProfile.NewProfile(launch.GameID, launch.Directory);
			string[] loadErrors = profile.Load(launch);

			Console.WriteLine($"Containers    {profile.Count}");
			foreach (SynchronizedDatabase sdb in profile) Console.WriteLine($"  {sdb.Filename}");

			bool failed = ReportStrings("Load errors", loadErrors);

			if (launch.Files.Count > 0 && profile.Count == 0)
			{
				Console.Error.WriteLine("ERROR: The manifest names files, and no container loaded.");
				return 1;
			}

			// ---------------------------------------------------------------- run

			Section("Run");
			var manager = new EndScriptManager(profile, commands, launch.Endscript);

			// Without CommandChase the jump targets stay unresolved and every selectable fails.
			manager.CommandChase();

			int pause = 0;

			while (!manager.ProcessScript())
			{
				var selectable = (ISelectable)manager.CurrentCommand;

				if (!TryResolveChoice(options, selectable, pause, out int choice))
				{
					return 1;
				}

				Console.WriteLine($"Pause {pause}       \"{selectable.Description}\"");

				for (int i = 0; i < selectable.Options.Length; ++i)
				{
					string mark = i == choice ? "->" : "  ";
					Console.WriteLine($"  {mark} [{i}] {selectable.Options[i].Name}");
				}

				selectable.Choice = choice;
				++pause;
			}

			Console.WriteLine($"Option pauses {pause}");
			Console.WriteLine("The script reached its end.");

			// A script can apply and still produce errors. Treat any entry as a failed deploy.
			failed |= ReportEndErrors(manager);

			// ---------------------------------------------------------------- save

			Section("Save");
			string[] saveErrors = profile.Save();
			failed |= ReportStrings("Save errors", saveErrors);

			Section("Result");

			if (failed)
			{
				Console.WriteLine("FAILED. The output above holds at least one error.");
				return 1;
			}

			Console.WriteLine("PASSED. No error.");
			Console.WriteLine($"The changed containers sit in {options.ScratchDir}.");
			return 0;
		}

		// -------------------------------------------------------------------- manifest

		private static Launch ReadManifest(string path)
		{
			try
			{
				Launch.Deserialize(path, out Launch launch);

				// Defect 2: ThisDir carries [JsonIgnore], so Deserialize leaves it null.
				// Every relative path resolves through it.
				launch.ThisDir = Path.GetDirectoryName(Path.GetFullPath(path));
				return launch;
			}
			catch (Endscript.Exceptions.InvalidVersionException)
			{
				// Defect 3: the message reads as "this is not a VERSN1 file" and hides the
				// real cause. Show the first bytes.
				byte[] head = File.ReadAllBytes(path).Take(16).ToArray();
				string hex = String.Join(" ", head.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
				throw new InvalidOperationException(
					$"The file {path} does not start with [VERSN1]. The first {head.Length} bytes are: {hex}");
			}
		}

		// -------------------------------------------------------------------- script

		private static BaseCommand[] ParseScript(string path)
		{
			var parser = new EndScriptParser(path);

			try
			{
				return parser.Read();
			}
			catch (Exception ex)
			{
				// CurrentFile, CurrentLine, and CurrentIndex identify the exact failure point.
				throw new InvalidOperationException(
					$"The parser stopped in {parser.CurrentFile} at line {parser.CurrentIndex}: " +
					$"\"{parser.CurrentLine}\" -> {ex.Message}", ex);
			}
		}

		private static void ReportCommands(BaseCommand[] commands)
		{
			if (commands is null)
			{
				Console.WriteLine("Commands      none. The parser read a VERSN3 description file.");
				return;
			}

			Console.WriteLine($"Commands      {commands.Length}");
			Console.WriteLine();
			Console.WriteLine("By source file:");

			foreach (var group in commands.GroupBy(c => c.Filename).OrderBy(g => g.Key, StringComparer.Ordinal))
			{
				Console.WriteLine($"  {group.Count(),6}  {group.Key}");
			}

			Console.WriteLine();
			Console.WriteLine("By command type:");

			foreach (var group in commands.GroupBy(c => c.Type).OrderByDescending(g => g.Count()))
			{
				Console.WriteLine($"  {group.Count(),6}  {group.Key}");
			}
		}

		// -------------------------------------------------------------------- choices

		/// <summary>
		/// Defect 5: an out-of-range Choice becomes the message "Unable to find end to a
		/// selectable statement", which names neither the file nor the real problem.
		/// Validate the range here and name the script, the option set, and the value.
		/// </summary>
		private static bool TryResolveChoice(Options options, ISelectable selectable, int pause, out int choice)
		{
			choice = -1;
			var command = (BaseCommand)selectable;
			string where = $"{command.Filename} line {command.Index}";

			if (pause >= options.Choices.Count)
			{
				Console.Error.WriteLine($"ERROR: The script paused for option {pause} and no answer exists.");
				Console.Error.WriteLine($"  Script      {where}");
				Console.Error.WriteLine($"  Question    \"{selectable.Description}\"");

				for (int i = 0; i < selectable.Options.Length; ++i)
				{
					Console.Error.WriteLine($"  [{i}]         {selectable.Options[i].Name}");
				}

				Console.Error.WriteLine($"  Fix         Give --choice one value for each pause. This run needs {pause + 1}.");
				return false;
			}

			int value = options.Choices[pause];

			if (value < 0 || value >= selectable.Options.Length)
			{
				Console.Error.WriteLine($"ERROR: The choice {value} is out of range for option {pause}.");
				Console.Error.WriteLine($"  Script      {where}");
				Console.Error.WriteLine($"  Question    \"{selectable.Description}\"");
				Console.Error.WriteLine($"  Range       0 to {selectable.Options.Length - 1}");

				for (int i = 0; i < selectable.Options.Length; ++i)
				{
					Console.Error.WriteLine($"  [{i}]         {selectable.Options[i].Name}");
				}

				return false;
			}

			choice = value;
			return true;
		}

		// -------------------------------------------------------------------- reports

		private static bool ReportStrings(string title, string[] errors)
		{
			if (errors is null || errors.Length == 0)
			{
				Console.WriteLine($"{title}: none.");
				return false;
			}

			Console.WriteLine($"{title}: {errors.Length}.");
			foreach (string error in errors) Console.WriteLine($"  {error}");
			return true;
		}

		private static bool ReportEndErrors(EndScriptManager manager)
		{
			var errors = manager.Errors.ToList();

			if (errors.Count == 0)
			{
				Console.WriteLine("Script errors: none.");
				return false;
			}

			Console.WriteLine($"Script errors: {errors.Count}.");

			foreach (var error in errors)
			{
				Console.WriteLine($"  {error.Filename} line {error.Index}: {error.Error}");
				Console.WriteLine($"    {error.Line}");
			}

			return true;
		}

		// -------------------------------------------------------------------- copy

		private static void CopyGame(string source, string target)
		{
			if (Directory.Exists(target))
			{
				Console.WriteLine("Delete        the scratch copy of the last run");

				// The game install holds read-only files, such as server.dll. A recursive
				// delete stops on the first one. Clear the flag first.
				ClearReadOnly(new DirectoryInfo(target));
				Directory.Delete(target, true);
			}

			Console.WriteLine($"Copy          {source}");
			Console.WriteLine($"  to          {target}");

			long start = Environment.TickCount64;
			var counters = new CopyCounters();
			CopyTree(new DirectoryInfo(source), target, counters);
			long spent = Environment.TickCount64 - start;

			Console.WriteLine($"Copied        {counters.Files} files, {counters.Bytes / (1024 * 1024)} MB, in {spent / 1000.0:F1} s");
		}

		private sealed class CopyCounters
		{
			public int Files;
			public long Bytes;
		}

		private static void CopyTree(DirectoryInfo source, string target, CopyCounters counters)
		{
			Directory.CreateDirectory(target);

			foreach (FileInfo file in source.EnumerateFiles())
			{
				FileInfo copy = file.CopyTo(Path.Combine(target, file.Name), true);

				// CopyTo carries the read-only flag across. The scratch copy must stay writable.
				if (copy.IsReadOnly) copy.IsReadOnly = false;

				++counters.Files;
				counters.Bytes += file.Length;
			}

			foreach (DirectoryInfo child in source.EnumerateDirectories())
			{
				CopyTree(child, Path.Combine(target, child.Name), counters);
			}
		}

		private static void ClearReadOnly(DirectoryInfo directory)
		{
			foreach (FileInfo file in directory.EnumerateFiles())
			{
				if (file.IsReadOnly) file.IsReadOnly = false;
			}

			foreach (DirectoryInfo child in directory.EnumerateDirectories())
			{
				ClearReadOnly(child);
			}
		}

		private static bool IsSameOrInside(string candidate, string root)
		{
			string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
			string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

			if (String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

			return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}

		private static void Section(string title)
		{
			Console.WriteLine();
			Console.WriteLine($"== {title} {new string('=', Math.Max(0, 60 - title.Length))}");
		}
	}
}
