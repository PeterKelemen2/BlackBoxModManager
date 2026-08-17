using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Endscript.Core;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Stands in for Binary.exe.
	///
	/// The real program cannot run here. It is not ours to redistribute, and it needs the .NET
	/// Core 3.1 Desktop runtime. This class records what the route asked for, and it writes the
	/// files that the real program writes.
	/// </summary>
	internal sealed class FakeBinary : IProcessRunner
	{
		private readonly string _error;
		private readonly bool _timeOut;

		/// <summary>Every request that the route made, in order.</summary>
		public List<ProcessRequest> Requests { get; } = new List<ProcessRequest>();

		/// <summary>What the fake writes into each staged container that the manifest names.</summary>
		public string Writes { get; set; }

		/// <summary>
		/// Paths inside the staging copy that the fake also writes, relative to it.
		///
		/// A real script does this through unlock_memory, move_file, and every verb that this
		/// application does not classify. The route cannot predict these, so it has to observe
		/// them.
		/// </summary>
		public List<string> AlsoWrites { get; } = new List<string>();

		public FakeBinary(string error = null, bool timeOut = false)
		{
			this._error = error;
			this._timeOut = timeOut;
		}

		public ProcessResult Run(ProcessRequest request, CancellationToken cancellation = default)
		{
			this.Requests.Add(request);

			// Binary writes both logs into the directory that it starts in.
			File.WriteAllText(Path.Combine(request.WorkingDirectory,
				BinaryCliDeployEngine.MainLogName), "the trace of the run");

			if (this._error != null)
			{
				File.WriteAllText(Path.Combine(request.WorkingDirectory,
					BinaryCliDeployEngine.ErrorLogName), this._error);
			}

			if (this.Writes != null || this.AlsoWrites.Count > 0) this.Apply(request);

			// Binary always returns zero, even for a run that applied nothing.
			return new ProcessResult(0, "Loading profile...", String.Empty, this._timeOut,
				TimeSpan.FromSeconds(1));
		}

		/// <summary>
		/// Rewrites every container that the manifest names, the way a real apply does.
		/// </summary>
		private void Apply(ProcessRequest request)
		{
			Launch.Deserialize(request.Arguments[1], out Launch launch);

			if (this.Writes != null)
			{
				foreach (string file in launch.Files)
				{
					string path = ModPath.Resolve(launch.Directory, file);

					if (File.Exists(path)) File.WriteAllText(path, this.Writes);
				}
			}

			foreach (string file in this.AlsoWrites)
			{
				File.WriteAllText(ModPath.Resolve(launch.Directory, file), "what Binary wrote");
			}
		}
	}

	/// <summary>
	/// Covers the route that runs the Binary executable.
	///
	/// Three properties of that program shape every test here. It always exits with code zero,
	/// so EndError.log is the verdict. It reads an answer from its own console, so a mod that
	/// asks a question needs a generated script. It writes its logs into the directory that it
	/// starts in.
	/// </summary>
	public class BinaryCliEngineTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly FakeGame _game = new FakeGame();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		private const string Container = @"GLOBAL\GlobalB.lzc";

		public BinaryCliEngineTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose()
		{
			this._temp.Dispose();
			this._game.Dispose();
		}

		// ---------------------------------------------------------------- fixtures

		/// <summary>
		/// A Binary install that passes the validator. The route only needs the executable and
		/// the hash lists to exist.
		/// </summary>
		private BinaryInstall Binary()
		{
			string root = Path.Combine(this._temp.Path, "Binary_v2.8.3");
			Directory.CreateDirectory(Path.Combine(root, HashListPaths.MainKeysFolder));

			File.WriteAllText(Path.Combine(root, BinaryInstallValidator.ExecutableName), "not a real program");

			foreach (GameINT game in HashListPaths.SupportedGames)
			{
				File.WriteAllText(HashListPaths.MainHashList(root, game), "A_LABEL");
			}

			BinaryInstallStatus status = BinaryInstallValidator.Validate(root);

			Assert.True(status.IsUsable, status.Message);

			return status.Install;
		}

		/// <summary>A staging copy of the game that the route may write into.</summary>
		private string Staging()
		{
			string staging = Path.Combine(this._temp.Path, "staging");

			Core.Staging.TreeReplicator.Build(this._game.Root, staging, null, linkFiles: false);

			return staging;
		}

		private IReadOnlyList<EnabledVariant> Import(string name, params string[] lines)
		{
			var profile = new Profile("Test", nameof(GameINT.Underground2));
			var source = new TempDirectory();

			try
			{
				source.WriteManifest($"{name}.end", "Underground2", $"{name}Script.end", Container);
				source.WriteScript($"{name}Script.end", lines);

				InstalledMod mod = this._importer.Import(source.Path, GameINT.Underground2).Mod;

				ProfileEntry entry = profile.Ensure(mod.Id);
				entry.Enabled = true;
				entry.Route = BinaryRouteChoice.BinaryCli;
				entry.Selections.Ensure(name).Enabled = true;
			}
			finally
			{
				source.Dispose();
			}

			this.Profile = profile;

			return VariantReader.Read(profile, this._store, GameINT.Underground2);
		}

		private Profile Profile { get; set; }

		private DeployContext Context(string staging, IReadOnlyList<EnabledVariant> variants,
			List<string> log = null)
		{
			return new DeployContext(this._game.Install(), staging, this.Profile, this._store,
				this.Binary(), line => log?.Add(line), null, null, null, variants,
				new ScriptResolutionCache(staging), null, default,
				BinaryRoutePlan.Build(this.Profile, this._store));
		}

		private IReadOnlyList<InstalledMod> Mods() => this._store.List(GameINT.Underground2);

		// ---------------------------------------------------------------- the command line

		[Fact]
		public void TheRouteRunsTheExecutableOneTimeForEachVariant()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary();
			var engine = new BinaryCliDeployEngine(fake);

			engine.Deploy(this.Context(this.Staging(), variants), this.Mods());

			Assert.Single(fake.Requests);
			Assert.EndsWith(BinaryInstallValidator.ExecutableName, fake.Requests[0].ExecutablePath);
		}

		/// <summary>
		/// The command line is positional and holds three arguments. The first is the usage mode,
		/// which Binary parses and never reads.
		/// </summary>
		[Fact]
		public void TheCommandLineHoldsTheModeTheManifestAndTheScript()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(this.Staging(), variants), this.Mods());

			IReadOnlyList<string> arguments = fake.Requests[0].Arguments;

			Assert.Equal(3, arguments.Count);
			Assert.Equal("modder", arguments[0]);
			Assert.EndsWith(BinaryCliDeployEngine.ManifestFileName, arguments[1]);
			Assert.EndsWith("AlphaScript.end", arguments[2]);
		}

		/// <summary>
		/// LoadProfile throws unless the manifest says Modder, and Directory must name the
		/// staging copy and never the live install.
		/// </summary>
		[Fact]
		public void TheManifestSaysModderAndPointsAtTheStagingCopy()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			string staging = this.Staging();
			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(staging, variants), this.Mods());

			Launch.Deserialize(fake.Requests[0].Arguments[1], out Launch launch);

			Assert.Equal("Modder", launch.Usage);
			Assert.Equal(Path.GetFullPath(staging), Path.GetFullPath(launch.Directory));
			Assert.NotEqual(Path.GetFullPath(this._game.Root), Path.GetFullPath(launch.Directory));
			Assert.Contains(Container, launch.Files);
		}

		/// <summary>
		/// The script path must be rooted. Path.Combine returns a rooted second argument
		/// unchanged, so the manifest resolves the script whatever base it uses. That is what
		/// lets the manifest sit in the scratch directory.
		/// </summary>
		[Fact]
		public void TheManifestNamesTheScriptByAFullPath()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(this.Staging(), variants), this.Mods());

			Launch.Deserialize(fake.Requests[0].Arguments[1], out Launch launch);

			Assert.True(Path.IsPathRooted(launch.Endscript), launch.Endscript);
			Assert.True(File.Exists(launch.Endscript), launch.Endscript);
		}

		/// <summary>
		/// Binary writes EndError.log and MainLog.txt with a bare name, so the route must start
		/// it in a directory that we own and that starts empty.
		/// </summary>
		[Fact]
		public void TheRouteStartsTheProgramInItsOwnEmptyDirectory()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(this.Staging(), variants), this.Mods());

			string work = fake.Requests[0].WorkingDirectory;

			Assert.True(Directory.Exists(work));
			Assert.StartsWith(Path.GetFullPath(AppPaths.BinaryCliDirectory), Path.GetFullPath(work));
			Assert.NotEqual(Path.GetFullPath(this._game.Root), Path.GetFullPath(work));
		}

		// ---------------------------------------------------------------- the verdict

		/// <summary>
		/// The test that matters most. Binary returns zero for a failed run, so the route must
		/// read EndError.log and must report the text of Binary and not a text of its own.
		/// </summary>
		[Fact]
		public void AnErrorLogStopsTheDeployAndCarriesTheTextOfBinary()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary("Collection named SUPRA does not exist");
			var engine = new BinaryCliDeployEngine(fake);

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => engine.Deploy(this.Context(this.Staging(), variants), this.Mods()));

			Assert.Contains("Collection named SUPRA does not exist", error.Message);
			Assert.Contains("Alpha", error.Message);
		}

		[Fact]
		public void AnEmptyErrorLogIsASuccess()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary(String.Empty);

			DeployReport report = new BinaryCliDeployEngine(fake)
				.Deploy(this.Context(this.Staging(), variants), this.Mods());

			Assert.NotEmpty(report.Containers);
		}

		[Fact]
		public void ATimeoutStopsTheDeployAndNamesTheLikelyCause()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary(null, timeOut: true);
			var engine = new BinaryCliDeployEngine(fake);

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => engine.Deploy(this.Context(this.Staging(), variants), this.Mods()));

			Assert.Contains("question", error.Message);
		}

		/// <summary>
		/// A program that we cannot start must produce a message that names the runtime. The
		/// message is the only guidance that the user gets.
		/// </summary>
		[Fact]
		public void AProgramThatWillNotStartNamesTheRuntime()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var engine = new BinaryCliDeployEngine(new BrokenRunner());

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => engine.Deploy(this.Context(this.Staging(), variants), this.Mods()));

			Assert.Contains(".NET Core 3.1", error.Message);
		}

		private sealed class BrokenRunner : IProcessRunner
		{
			public ProcessResult Run(ProcessRequest request, CancellationToken cancellation = default)
			{
				throw new ProcessStartException("Binary.exe did not start. The file is not an application.");
			}
		}

		// ---------------------------------------------------------------- the report

		/// <summary>
		/// The verify reports every staged file that differs from the vanilla state and that no
		/// mod claimed. Binary writes files that the command parse cannot name, so the route has
		/// to report what actually changed. See defect 16.
		/// </summary>
		[Fact]
		public void TheRouteReportsAFileThatOnlyBinaryChanged()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			string staging = this.Staging();
			var snapshot = Core.Staging.SnapshotReader.Create(staging);

			var fake = new FakeBinary();
			var log = new List<string>();

			var context = new DeployContext(this._game.Install(), staging, this.Profile, this._store,
				this.Binary(), log.Add, null, null, snapshot, variants,
				new ScriptResolutionCache(staging), null, default,
				BinaryRoutePlan.Build(this.Profile, this._store));

			// A real run of Binary writes a file that no command of the script names. The
			// unlock_memory command does exactly this. The write has to happen during the run,
			// because the route measures the difference across it.
			fake.Writes = "the container that the script names";
			fake.AlsoWrites.Add(@"GLOBAL\GLOBALA.BUN");

			DeployReport report = new BinaryCliDeployEngine(fake).Deploy(context, this.Mods());

			var reported = new List<string>();

			foreach (ScriptWrite write in report.ScriptWrites) reported.Add(write.RelativePath);
			foreach (ContainerWrite write in report.Containers) reported.Add(write.RelativePath);

			Assert.Contains(reported, path => path.Contains("GLOBALA.BUN", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public void TheLogNamesTheRouteForEveryMod()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var log = new List<string>();

			new BinaryCliDeployEngine(new FakeBinary())
				.Deploy(this.Context(this.Staging(), variants, log), this.Mods());

			Assert.Contains(log, line => line.Contains("deploys through Binary"));
		}

		// ---------------------------------------------------------------- questions

		/// <summary>
		/// Binary reads an answer from its own console, so the route writes a script that holds
		/// the chosen commands and no question. The file goes beside the launcher, because
		/// seventeen commands read a file relative to that directory.
		/// </summary>
		[Fact]
		public void AModThatAsksAQuestionGetsAGeneratedScriptBesideItsLauncher()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				"combobox Fast Slow \"Pick a speed\"",
				"Fast",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 9",
				"Slow",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 2",
				"end");

			variants[0].Selection.Choose(0, "Slow");

			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(this.Staging(), variants), this.Mods());

			string handed = fake.Requests[0].Arguments[2];

			Assert.Equal(ScriptEmitter.GeneratedFileName, Path.GetFileName(handed));
			Assert.Equal(
				Path.GetFullPath(Path.GetDirectoryName(variants[0].Variant.Manifest.Endscript) ?? String.Empty,
					variants[0].Variant.Manifest.ThisDir),
				Path.GetFullPath(Path.GetDirectoryName(handed)));
		}

		/// <summary>
		/// The generated file must not stay behind. The mod store holds what the user imported,
		/// and nothing else.
		/// </summary>
		[Fact]
		public void TheGeneratedScriptDoesNotStayInTheModStore()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				"combobox Fast Slow \"Pick a speed\"",
				"Fast",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 9",
				"Slow",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 2",
				"end");

			variants[0].Selection.Choose(0, "Fast");

			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(this.Staging(), variants), this.Mods());

			Assert.False(File.Exists(fake.Requests[0].Arguments[2]));
		}

		/// <summary>
		/// A mod that asks nothing keeps its own launcher. The route generates no file, which is
		/// what "let Binary take over" means.
		/// </summary>
		[Fact]
		public void AModThatAsksNothingKeepsItsOwnLauncher()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var fake = new FakeBinary();

			new BinaryCliDeployEngine(fake).Deploy(this.Context(this.Staging(), variants), this.Mods());

			string handed = fake.Requests[0].Arguments[2];

			Assert.NotEqual(ScriptEmitter.GeneratedFileName, Path.GetFileName(handed));
			Assert.True(File.Exists(handed));
		}

		// ---------------------------------------------------------------- guards

		[Fact]
		public void TheRouteRefusesWithNoBinaryInstall()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			string staging = this.Staging();

			var context = new DeployContext(this._game.Install(), staging, this.Profile, this._store,
				null, null, null, null, null, variants, new ScriptResolutionCache(staging), null,
				default, BinaryRoutePlan.Build(this.Profile, this._store));

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => new BinaryCliDeployEngine(new FakeBinary()).Deploy(context, this.Mods()));

			Assert.Contains("Binary install", error.Message);
		}

		/// <summary>
		/// The live install must never receive a write. This route hands a directory to another
		/// program, so the check matters more here, not less.
		/// </summary>
		[Fact]
		public void TheRouteRefusesToPointBinaryAtTheLiveInstall()
		{
			IReadOnlyList<EnabledVariant> variants = this.Import("Alpha",
				$@"update_collection {Container} CarTypeInfos SUPRA Manufacturer 4");

			var context = new DeployContext(this._game.Install(), this._game.Root, this.Profile,
				this._store, this.Binary(), null, null, null, null, variants,
				new ScriptResolutionCache(this._game.Root), null, default,
				BinaryRoutePlan.Build(this.Profile, this._store));

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => new BinaryCliDeployEngine(new FakeBinary()).Deploy(context, this.Mods()));

			Assert.Contains("staging copy", error.Message);
		}
	}
}
