using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Files;
using Endscript.Core;
using Endscript.Helpers;
using Endscript.Profiles;
using Nikki.Core;
using Xunit;
using Xunit.Abstractions;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Proves that a change inside the vendored libraries writes the same container bytes.
	///
	/// <b>This is the gate for every change under third_party.</b> The libraries serialize the
	/// game data of the user. A change that is one byte wrong gives a game that does not start,
	/// and no unit test of our own code catches it.
	///
	/// The test loads real containers, saves them with no edit, and compares the result against
	/// hashes recorded from the library before the change. A save is not a copy. It reads the
	/// container, decompresses it, and assembles it again, so the output differs from the input
	/// and stays the same across runs.
	///
	/// <b>The test needs a real game install.</b> It reads the vanilla copy of the workspace and
	/// the hash lists of a Binary 2.8.3 install, both named in the settings of the application.
	/// A machine without them skips the check and says so.
	/// </summary>
	public class ContainerByteStabilityTests
	{
		private readonly ITestOutputHelper _out;

		public ContainerByteStabilityTests(ITestOutputHelper output) => this._out = output;

		/// <summary>
		/// One container of the check. Input names the file that the vanilla copy has to hold,
		/// and Output names what one load and one save has to write.
		///
		/// <b>The check reads the input hash first.</b> An answer about the library only holds
		/// for the file that produced it. A vanilla copy of another install, or of a repaired
		/// install, holds other bytes and gives other output. Without the input hash that reads
		/// as a library regression, and it is not one.
		/// </summary>
		private readonly struct Container
		{
			public Container(string file, string inputHash, long length, string outputHash)
			{
				this.File = file;
				this.InputHash = inputHash;
				this.Length = length;
				this.OutputHash = outputHash;
			}

			public string File { get; }

			public string InputHash { get; }

			public long Length { get; }

			public string OutputHash { get; }
		}

		/// <summary>
		/// The containers of a vanilla Most Wanted install, and what the library writes for
		/// each one.
		///
		/// Recorded with the libraries as they were at commit 1e4661b, before the changes of
		/// defects 17 to 20. Do not update an output value here to make a test pass. A changed
		/// output means that the library writes different bytes, and that needs a reason and a
		/// run of the game.
		/// </summary>
		private static readonly Container[] Expected =
		{
			new Container(@"GLOBAL\GLOBALB.LZC", "a14ea7383b3abd2c9f4e2584240d57dd", 2832856, "d3583648665eeef7761f83aba53c7809"),
			new Container(@"GLOBAL\GLOBALA.BUN", "0b6c903446e3a38ed5fb11cc9ccc0c0a", 430348, "209047dbc1df12e1d77c7ac3f765f53a"),
			new Container(@"CARS\TEXTURES.BIN", "cac36751e31b8249393924f4dd3967d4", 1120256, "ecee2f8e3fcbd1202787e5d0c411c1be"),
			new Container(@"CARS\911GT2\VINYLS.BIN", "f874f76e3a166beb424007346caf43a5", 1750208, "a5e738b7592c6585663a23959834cc2b"),
			new Container(@"FRONTEND\FRONTB.LZC", "170a8beee27d09dcf095bfa2e06e585b", 12903608, "06ed9eb8a1e52ae6b8a1655803bbff90"),
			new Container(@"GLOBAL\INGAMEA.BUN", "35678c952275efa7a63c56f5a85ff617", 5919232, "19f0a6a169862ef05e880b0d91486fb0"),
		};

		[Fact]
		public void ASaveWritesTheSameBytesAsBefore()
		{
			if (!Fixture.Exists(out string vanilla, out string binary, out string why))
			{
				this._out.WriteLine($"The check did not run. {why}");
				return;
			}

			string scratch = Path.Combine(Path.GetTempPath(), "bbmm-byte-stability");

			if (Directory.Exists(scratch)) Directory.Delete(scratch, true);

			Directory.CreateDirectory(scratch);

			try
			{
				var files = new List<string>();

				foreach (Container container in Expected)
				{
					string relative = container.File.Replace('\\', Path.DirectorySeparatorChar);
					string from = Path.Combine(vanilla, relative);

					// A vanilla copy that lost a file cannot answer for that file. Say which
					// one, and check the rest.
					if (!File.Exists(from))
					{
						this._out.WriteLine($"The vanilla copy holds no {container.File}, so the check skips it.");
						continue;
					}

					// The recorded output belongs to one input. Another install, or a repaired
					// install, holds other bytes and writes other bytes. That is not a library
					// regression, so skip the file and say why.
					string inputHash = FileHash.Compute(from);

					if (inputHash != container.InputHash)
					{
						this._out.WriteLine($"The vanilla copy of {container.File} hashes to {inputHash} " +
							$"and this check knows the file that hashes to {container.InputHash}. " +
							"The check skips it.");

						continue;
					}

					string to = Path.Combine(scratch, relative);

					Directory.CreateDirectory(Path.GetDirectoryName(to));
					File.Copy(from, to, true);
					files.Add(container.File);
				}

				// Every container skipped means the check proved nothing. Fail, so that a
				// green run always carries an answer.
				Assert.NotEmpty(files);

				long elapsed = Roundtrip(scratch, binary, files);

				this._out.WriteLine($"The load and the save of {files.Count} containers took {elapsed} ms.");

				var wrong = new List<string>();

				foreach (Container container in Expected)
				{
					if (!files.Contains(container.File)) continue;

					string path = Path.Combine(scratch, container.File.Replace('\\', Path.DirectorySeparatorChar));
					long actualLength = new FileInfo(path).Length;
					string actualHash = FileHash.Compute(path);

					this._out.WriteLine($"{container.File} {actualLength} {actualHash}");

					if (actualLength != container.Length || actualHash != container.OutputHash)
					{
						wrong.Add($"{container.File} is {actualLength} bytes and hashes to {actualHash}. " +
							$"It has to be {container.Length} bytes and hash to {container.OutputHash}.");
					}
				}

				Assert.True(wrong.Count == 0,
					"A change under third_party made the library write different container bytes. " +
					String.Join(" ", wrong));
			}
			finally
			{
				try
				{
					Directory.Delete(scratch, true);
				}
				catch (Exception)
				{
					// A leftover scratch directory changes no result.
				}
			}
		}

		/// <summary>
		/// Loads every named container and saves it again. It returns the time in milliseconds.
		/// </summary>
		private static long Roundtrip(string scratch, string binary, IReadOnlyList<string> files)
		{
			using (LibraryGate.Enter())
			{
				ProfileHashLists.Apply(HashListPaths.MainHashList(binary, GameINT.MostWanted),
					HashListPaths.CustomHashList(GameINT.MostWanted), GameINT.MostWanted);

				// Nikki writes MainLog.txt into the current directory. See defect 9.
				string before = Directory.GetCurrentDirectory();
				Directory.CreateDirectory(AppPaths.LogDirectory);
				Directory.SetCurrentDirectory(AppPaths.LogDirectory);

				try
				{
					BaseProfile profile = BaseProfile.NewProfile(GameINT.MostWanted, scratch);

					var launch = new Launch
					{
						Game = nameof(GameINT.MostWanted),
						Directory = scratch,
						ThisDir = scratch,
						Usage = "Modder",
						Endscript = String.Empty,
						Files = new List<string>(files),
						Links = new List<SubLoader>(),
					};

					var watch = Stopwatch.StartNew();

					Assert.Empty(profile.Load(launch));
					Assert.Equal(files.Count, profile.Count);
					Assert.Empty(profile.Save());

					watch.Stop();

					return watch.ElapsedMilliseconds;
				}
				finally
				{
					Directory.SetCurrentDirectory(before);
				}
			}
		}

		/// <summary>
		/// Finds the vanilla copy and the Binary install that this check needs.
		/// </summary>
		private static class Fixture
		{
			public static bool Exists(out string vanilla, out string binary, out string why)
			{
				vanilla = null;
				binary = null;

				BlackboxModManager.Core.Settings settings = SettingsStore.Load();

				if (!settings.GameDirectories.TryGetValue(nameof(GameINT.MostWanted), out string game)
					|| String.IsNullOrWhiteSpace(game))
				{
					why = "The settings name no Most Wanted install.";
					return false;
				}

				binary = settings.BinaryInstallDirectory;

				if (String.IsNullOrWhiteSpace(binary) || !Directory.Exists(binary))
				{
					why = "The settings name no Binary 2.8.3 install, and the hash lists come from it.";
					return false;
				}

				vanilla = Path.Combine(
					Path.TrimEndingDirectorySeparator(game)
						+ BlackboxModManager.Core.Staging.GameWorkspace.WorkspaceSuffix, "vanilla");

				if (!Directory.Exists(vanilla))
				{
					why = $"The workspace holds no vanilla copy at {vanilla}. Deploy once to build it.";
					return false;
				}

				why = null;
				return true;
			}
		}
	}
}
