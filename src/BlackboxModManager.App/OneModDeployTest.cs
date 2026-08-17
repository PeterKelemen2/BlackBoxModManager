using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Nikki.Core;

namespace BlackboxModManager.App
{
	/// <summary>
	/// Imports one mod, switches every variant of it on with the default answer of each
	/// question, and deploys it into a scratch copy of a game.
	///
	/// <b>This is the check for a mod that no automated test can hold.</b> DeployTest carries the
	/// two example mods of the repository. A 375 MB vinyl mod cannot live in the repository, and
	/// the window cannot answer its questions without a person. This switch runs the same Core
	/// code that the window runs, with the first option of every question.
	///
	/// <b>Pass a scratch copy of the game, never a live install.</b> The swap replaces the
	/// directory that this check gets.
	///
	/// Start it with:
	/// BlackboxModManager.exe --moddeploy &lt;gameDir&gt; &lt;binaryDir&gt; &lt;modPath&gt; &lt;game&gt; [keep] [answers]
	///
	/// The answers argument overrides the default of one question or of several. Write it as
	/// <c>0=Install,1=enabled,2=enabled</c>. The number is the ordinal of the question and the
	/// text is the option name. A checkbox names its two options "disabled" and "enabled", and
	/// "disabled" is the default of the library.
	/// </summary>
	internal static class OneModDeployTest
	{
		public const string Switch = "--moddeploy";

		/// <summary>
		/// Environment variable that makes this check cancel the first deploy after that many
		/// milliseconds, and then run a second deploy to the end.
		/// </summary>
		public const string CancelSwitch = "BBMM_CANCEL_AFTER_MS";

		private static readonly List<string> Report = new List<string>();
		private static int _failed;

		public static int Run(string gameDirectory, string binaryDirectory, string modPath,
			string gameName, bool revert, string answers = null)
		{
			Line($"The one mod deploy runs against {gameDirectory}.");
			Line($"The Binary install is {binaryDirectory}.");
			Line($"The mod is {modPath}.");
			Line($"The game is {gameName}.");
			Line($"The application data is at {AppPaths.Root}.");
			Line($"Server garbage collection is {System.Runtime.GCSettings.IsServerGC}.");

			var watch = Stopwatch.StartNew();

			try
			{
				Check(gameDirectory, binaryDirectory, modPath, gameName, revert, Parse(answers));
			}
			catch (Exception ex)
			{
				++_failed;
				Line($"The one mod deploy stopped on an exception. {ex}");
			}

			watch.Stop();

			Line($"The whole run took {watch.ElapsedMilliseconds} ms.");
			Line(_failed == 0 ? "PASSED. Every check passed." : $"FAILED. {_failed} checks failed.");

			string log = Path.Combine(AppPaths.LogDirectory, "moddeploy.log");

			try
			{
				Directory.CreateDirectory(AppPaths.LogDirectory);
				File.WriteAllText(log, String.Join(Environment.NewLine, Report));
				Console.WriteLine($"The report is at {log}.");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"The report did not reach {log}. {ex.Message}");
			}

			return _failed == 0 ? 0 : 1;
		}

		/// <summary>
		/// Reads the answers argument into a map of ordinal to option name.
		/// </summary>
		private static Dictionary<int, string> Parse(string answers)
		{
			var result = new Dictionary<int, string>();

			if (String.IsNullOrWhiteSpace(answers)) return result;

			foreach (string pair in answers.Split(',', StringSplitOptions.RemoveEmptyEntries))
			{
				string[] halves = pair.Split('=', 2);

				if (halves.Length != 2 || !Int32.TryParse(halves[0].Trim(), out int ordinal))
				{
					++_failed;
					Line($"The answer \"{pair}\" is not in the form ordinal=option.");
					continue;
				}

				result[ordinal] = halves[1].Trim();
			}

			return result;
		}

		private static void Check(string gameDirectory, string binaryDirectory, string modPath,
			string gameName, bool revert, Dictionary<int, string> answers)
		{
			if (!Enum.TryParse(gameName, true, out GameINT game))
			{
				++_failed;
				Line($"The game name {gameName} is not a GameINT member.");
				return;
			}

			GameInstallStatus status = GameInstallValidator.Validate(game, gameDirectory);
			Expect(status.IsUsable, $"The game directory validates. {status.Message}");

			if (!status.IsUsable) return;

			BinaryInstallStatus binary = BinaryInstallValidator.Validate(binaryDirectory);
			Expect(binary.IsUsable, $"The Binary install validates. {binary.Message}");

			if (!binary.IsUsable) return;

			GameInstall install = status.Install;

			// A fresh store for every run. A leftover mod would change the load order.
			var store = new ModStore(Path.Combine(AppPaths.Root, "moddeploy-mods"));
			FileTree.Delete(store.Root);

			var importer = new ModImporter(store, Path.Combine(AppPaths.Root, "moddeploy-import"));

			var importWatch = Stopwatch.StartNew();
			InstalledMod mod = importer.Import(modPath, game).Mod;
			importWatch.Stop();

			Line($"The import took {importWatch.ElapsedMilliseconds} ms.");
			Expect(mod.Kind == ModKind.Binary, $"The mod is a Binary mod. It is {mod.Kind}.");

			ModPackage package = ModPackageReader.Read(mod.ContentRoot);

			Line($"The package holds {package.Variants.Count} variants.");

			var profile = new Profile($"One mod deploy {mod.Id}", game.ToString());
			ProfileEntry entry = profile.Ensure(mod.Id);
			entry.Enabled = true;

			foreach (ModVariant variant in package.Variants)
			{
				VariantSelection selection = entry.Selections.Ensure(variant.Name);
				selection.Enabled = true;

				// Take the override first, then let ApplyDefaults fill the rest. ApplyDefaults
				// leaves an answer that already exists alone.
				foreach (KeyValuePair<int, string> answer in answers) selection.Choose(answer.Key, answer.Value);

				entry.Selections.ApplyDefaults(variant);

				var chosen = new List<string>();

				foreach (ModOptionSet set in variant.OptionSets)
				{
					chosen.Add($"{set.Ordinal}=\"{selection.Answer(set.Ordinal)}\"");
				}

				Line($"  variant \"{variant.Name}\" with {variant.OptionSets.Count} questions. " +
					String.Join(", ", chosen));
			}

			var service = new DeployService(store, binary.Install);

			// The cancel check. It proves that a long deploy stops, that the game directory does
			// not change, and that the next deploy still passes the baseline check. An ended
			// process is what damaged a vanilla baseline before. See defect 16.
			string after = Environment.GetEnvironmentVariable(CancelSwitch);

			if (Int32.TryParse(after, out int milliseconds) && milliseconds > 0)
			{
				using var source = new System.Threading.CancellationTokenSource(milliseconds);

				string live = FileHash.Compute(Path.Combine(gameDirectory, "GLOBAL", "GlobalB.lzc"));

				try
				{
					service.Deploy(install, profile, false, Line, source.Token);
					++_failed;
					Line($"FAIL The deploy finished although the cancel fired after {milliseconds} ms.");
				}
				catch (OperationCanceledException)
				{
					Line($"ok   The deploy stopped on the cancel after {milliseconds} ms.");
				}

				Expect(live == FileHash.Compute(Path.Combine(gameDirectory, "GLOBAL", "GlobalB.lzc")),
					"The canceled deploy left GLOBAL\\GlobalB.lzc of the game directory alone.");

				Line("The deploy runs again, to prove that the baseline check still passes.");
			}

			DeployResult result = service.Deploy(install, profile, false, Line);

			Expect(result.Verification.IsClean,
				$"The verify is clean. It found {result.Verification.Problems.Count} problems.");

			Line(result.Report.Summary());

			foreach (ContainerWrite container in result.Report.Containers) Line($"  container {container}");

			if (!revert)
			{
				Line("The result stays in place. Start the game from the scratch copy.");
				return;
			}

			service.Revert(install, Line);
			Line("The revert put the vanilla state back.");
		}

		private static void Expect(bool condition, string what)
		{
			if (!condition) ++_failed;

			Line((condition ? "ok   " : "FAIL ") + what);
		}

		private static void Line(string text)
		{
			Report.Add(text);
			Console.WriteLine(text);
		}
	}
}
