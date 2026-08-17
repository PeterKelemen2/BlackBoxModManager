using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the rule that a container write never reaches the vanilla copy or the game.
	///
	/// TreeReplicator builds the staging copy with hard links, and Nikki writes a container
	/// with FileMode.Create. That write keeps the share, so every name of the file gets the
	/// new content. See defect 16.
	///
	/// These tests build the mod by hand and load no container, so they need no game.
	/// </summary>
	public class ContainerPrivacyTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public ContainerPrivacyTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose() => this._temp.Dispose();

		// ------------------------------------------------------- the mechanism that we guard

		[Fact]
		public void AWriteThroughAHardLinkReachesEveryName()
		{
			// This is the defect that MakePrivate exists for. Prove that the platform behaves
			// this way, so that a change of platform cannot make the guard look unnecessary.
			string source = Path.Combine(this._temp.Path, "vanilla.bin");
			string link = Path.Combine(this._temp.Path, "staging.bin");

			File.WriteAllText(source, "vanilla");
			LinkSupport.Create(LinkKind.HardLink, source, link);

			using (var writer = new BinaryWriter(File.Open(link, FileMode.Create)))
			{
				writer.Write(new byte[] { 1, 2, 3 });
			}

			Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
		}

		[Fact]
		public void MakePrivateBreaksTheShareAndKeepsTheContent()
		{
			string source = Path.Combine(this._temp.Path, "vanilla.bin");
			string link = Path.Combine(this._temp.Path, "staging.bin");

			File.WriteAllText(source, "vanilla");
			LinkSupport.Create(LinkKind.HardLink, source, link);

			Assert.True(StagingFiles.MakePrivate(link));

			using (var writer = new BinaryWriter(File.Open(link, FileMode.Create)))
			{
				writer.Write(new byte[] { 1, 2, 3 });
			}

			Assert.Equal("vanilla", File.ReadAllText(source));
		}

		// ------------------------------------------------------------ what the gate reports

		/// <summary>
		/// The manifest of the Recompiled Vinyls mod names one container. Its script creates
		/// one container for each car with "new" and writes it back with "delete". The gate
		/// has to report those, or the engine never makes them private.
		/// </summary>
		[Fact]
		public void TheGateReportsAContainerThatOnlyTheScriptNames()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				"new negate \"CARS\\FORDGT\\VINYLS.BIN\"",
				"update_collection CARS\\FORDGT\\VINYLS.BIN VectorVinyls A B 1",
				"delete \"CARS\\FORDGT\\VINYLS.BIN\"");

			GateResult gate = CommandGate.Check(variants, this.Staging());

			Assert.Contains(gate.Containers,
				file => PathKey.Normalize(file) == PathKey.Normalize(@"CARS\FORDGT\VINYLS.BIN"));
		}

		[Fact]
		public void TheGateReportsEachContainerOneTime()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
				"update_collection GLOBAL/GlobalB.lzc CarTypeInfos A C 2");

			GateResult gate = CommandGate.Check(variants, this.Staging());

			Assert.Single(gate.Containers);
		}

		/// <summary>
		/// A container is not the only thing that a script writes. The command
		/// <c>unlock_memory all</c> writes a header over five memory files of the game. Those
		/// carry no edit key, so the container list holds none of them.
		///
		/// One deploy rewrote four of these files in the vanilla copy of a real install. See
		/// defect 16.
		/// </summary>
		[Fact]
		public void TheGateReportsTheFilesThatUnlockMemoryWrites()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build("unlock_memory all");

			string staging = this.Staging();
			GateResult gate = CommandGate.Check(variants, staging);

			Assert.Equal(5, gate.WritePaths.Count);

			Assert.Contains(gate.WritePaths,
				path => path.EndsWith("GLOBALMEMORYFILE.BIN", StringComparison.OrdinalIgnoreCase));

			// Every one of them stays inside the staging copy.
			foreach (string path in gate.WritePaths) Assert.StartsWith(staging, path, StringComparison.OrdinalIgnoreCase);

			// None of them carries an edit key, so the container list misses all five.
			Assert.Empty(gate.Containers);
		}

		[Fact]
		public void TheGateReportsTheTargetOfAMoveAndNotTheSource()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				"move_file negate absolute absolute \"CARS\\TEXTURES.BIN\" \"CARS\\BACKUP\\TEXTURES.BIN\"");

			GateResult gate = CommandGate.Check(variants, this.Staging());

			Assert.Contains(gate.WritePaths,
				path => path.EndsWith(Path.Combine("CARS", "BACKUP", "TEXTURES.BIN"), StringComparison.OrdinalIgnoreCase));

			Assert.DoesNotContain(gate.WritePaths,
				path => path.EndsWith(Path.Combine("CARS", "TEXTURES.BIN"), StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// A mod that edits one container for each car repeats one warning for each file. The
		/// gate reports one line for each verb and keeps the count.
		/// </summary>
		[Fact]
		public void TheGateCollapsesARepeatedWarningIntoOneLine()
		{
			var lines = new List<string>();

			IReadOnlyList<EnabledVariant> variants = this.Build(
				"new negate \"CARS\\FORDGT\\VINYLS.BIN\"",
				"delete \"CARS\\FORDGT\\VINYLS.BIN\"",
				"new negate \"CARS\\SUPRA\\VINYLS.BIN\"",
				"delete \"CARS\\SUPRA\\VINYLS.BIN\"",
				"new negate \"CARS\\GTO\\VINYLS.BIN\"",
				"delete \"CARS\\GTO\\VINYLS.BIN\"");

			CommandGate.Check(variants, this.Staging(), lines.Add);

			var warnings = new List<string>();

			foreach (string line in lines)
			{
				if (line.Contains("warning:", StringComparison.Ordinal)) warnings.Add(line);
			}

			// Six commands, two verbs, two lines. Each line carries the count.
			Assert.Equal(2, warnings.Count);
			Assert.Contains(warnings, line => line.Contains("It runs 3 times", StringComparison.Ordinal));
		}

		// ------------------------------------------------------------- the merged container report

		/// <summary>
		/// The manifest of the Recompiled Vinyls mod names one container, GLOBAL\GLOBALB.LZC.
		/// Its script writes a second one that the manifest never names. Both have to reach the
		/// report, or the verify fails the second one with "no mod supplied it." See defect 16.
		/// </summary>
		[Fact]
		public void TheReportedContainersHoldBothTheManifestOneAndTheScriptOnlyOne()
		{
			string staging = this.Staging();

			IReadOnlyList<EnabledVariant> variants = this.Build(
				"new negate \"CARS\\FORDGT\\VINYLS.BIN\"",
				"update_collection CARS\\FORDGT\\VINYLS.BIN VectorVinyls A B 1",
				"delete \"CARS\\FORDGT\\VINYLS.BIN\"");

			GateResult gate = CommandGate.Check(variants, staging);
			MergedLoad merged = MergedLaunch.Build(variants, staging);

			IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);

			Assert.Contains(containers,
				write => PathKey.Normalize(write.RelativePath) == PathKey.Normalize(@"GLOBAL\GLOBALB.LZC"));

			Assert.Contains(containers,
				write => PathKey.Normalize(write.RelativePath) == PathKey.Normalize(@"CARS\FORDGT\VINYLS.BIN"));
		}

		[Fact]
		public void TheManifestContainerKeepsItsManifestContributors()
		{
			string staging = this.Staging();

			IReadOnlyList<EnabledVariant> variants = this.Build(
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			GateResult gate = CommandGate.Check(variants, staging);
			MergedLoad merged = MergedLaunch.Build(variants, staging);

			IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);

			ContainerWrite globalb = Assert.Single(containers,
				write => PathKey.Normalize(write.RelativePath) == PathKey.Normalize(@"GLOBAL\GLOBALB.LZC"));

			Assert.Equal(merged.Contributors[@"GLOBAL\GLOBALB.LZC"], globalb.Contributors);
		}

		[Fact]
		public void TheScriptOnlyContainerCarriesTheVariantThatWroteIt()
		{
			string staging = this.Staging();

			IReadOnlyList<EnabledVariant> variants = this.Build(
				"new negate \"CARS\\FORDGT\\VINYLS.BIN\"",
				"delete \"CARS\\FORDGT\\VINYLS.BIN\"");

			GateResult gate = CommandGate.Check(variants, staging);
			MergedLoad merged = MergedLaunch.Build(variants, staging);

			IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);

			ContainerWrite fordgt = Assert.Single(containers,
				write => PathKey.Normalize(write.RelativePath) == PathKey.Normalize(@"CARS\FORDGT\VINYLS.BIN"));

			Assert.Contains(variants[0].Label, fordgt.Contributors);
		}

		/// <summary>A container that both the manifest and the script name reaches the report once.</summary>
		[Fact]
		public void ADuplicateBetweenTheManifestAndTheScriptAppearsOnce()
		{
			string staging = this.Staging();

			IReadOnlyList<EnabledVariant> variants = this.Build(
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			GateResult gate = CommandGate.Check(variants, staging);
			MergedLoad merged = MergedLaunch.Build(variants, staging);

			IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);

			Assert.Single(containers,
				write => PathKey.Normalize(write.RelativePath) == PathKey.Normalize(@"GLOBAL\GLOBALB.LZC"));
		}

		/// <summary>
		/// Proves the fix end to end. The script rewrites a container that its manifest never
		/// names, and the staging copy now differs from the vanilla snapshot for that path. The
		/// verify has to accept it, the way it already accepts a manifest-declared container.
		/// </summary>
		[Fact]
		public void TheVerifyAcceptsAContainerThatOnlyTheScriptNames()
		{
			string staging = this.Staging();
			string carPath = Path.Combine(staging, "CARS", "FORDGT", "VINYLS.BIN");

			Directory.CreateDirectory(Path.GetDirectoryName(carPath));
			File.WriteAllText(carPath, "vanilla vinyls");

			VanillaSnapshot snapshot = SnapshotReader.Create(staging);

			IReadOnlyList<EnabledVariant> variants = this.Build(
				"new negate \"CARS\\FORDGT\\VINYLS.BIN\"",
				"update_collection CARS\\FORDGT\\VINYLS.BIN VectorVinyls A B 1",
				"delete \"CARS\\FORDGT\\VINYLS.BIN\"");

			GateResult gate = CommandGate.Check(variants, staging);
			MergedLoad merged = MergedLaunch.Build(variants, staging);
			IReadOnlyList<ContainerWrite> containers = ContainerReportBuilder.Build(merged, gate);

			// The container engine would have rewritten this file. Stand in for that write.
			File.WriteAllText(carPath, "rewritten vinyls");

			var report = new DeployReport(null, null, null, null, containers);
			VerificationResult verification = StagingVerifier.Verify(staging, snapshot, report, this._store);

			Assert.True(verification.IsClean);
		}

		// ------------------------------------------------------------------------- fixtures

		/// <summary>
		/// Imports a one-variant mod that runs the given script lines, and switches it on.
		/// </summary>
		private IReadOnlyList<EnabledVariant> Build(params string[] lines)
		{
			var source = new TempDirectory();

			try
			{
				source.WriteManifest("Mod.end", "MostWanted", "Script.end");
				source.WriteScript("Script.end", lines);

				InstalledMod mod = this._importer.Import(source.Path, GameINT.MostWanted).Mod;

				var profile = new Profile("Test", nameof(GameINT.MostWanted));
				ProfileEntry entry = profile.Ensure(mod.Id);
				entry.Enabled = true;
				entry.Selections.Ensure("Mod").Enabled = true;

				return VariantReader.Read(profile, this._store, GameINT.MostWanted);
			}
			finally
			{
				source.Dispose();
			}
		}

		/// <summary>A staging directory that holds the container of the manifest.</summary>
		private string Staging()
		{
			string root = Path.Combine(this._temp.Path, "staging");
			string path = Path.Combine(root, "GLOBAL", "GLOBALB.LZC");

			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, "game data");

			return root;
		}
	}
}
