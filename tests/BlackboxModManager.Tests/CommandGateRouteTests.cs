using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the gate when another program runs the commands.
	///
	/// The gate raises two kinds of stop, and the CLI route treats them differently.
	///
	/// A <b>refused command</b> is a limit of this application. Binary 2.8.3 runs it, and a mod
	/// that needs one is the reason the CLI route exists. So the gate must let it through and
	/// say so.
	///
	/// An <b>escaped path</b> is a safety rule. A write outside the staging copy reaches the
	/// real system and no revert undoes it. That rule never relaxes for either route.
	/// </summary>
	public class CommandGateRouteTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public CommandGateRouteTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose() => this._temp.Dispose();

		private IReadOnlyList<EnabledVariant> Build(params string[] lines)
		{
			var profile = new Profile("Test", nameof(GameINT.Underground2));
			var source = new TempDirectory();

			try
			{
				source.WriteManifest("Alpha.end", "Underground2", "AlphaScript.end");
				source.WriteScript("AlphaScript.end", lines);

				InstalledMod mod = this._importer.Import(source.Path, GameINT.Underground2).Mod;

				ProfileEntry entry = profile.Ensure(mod.Id);
				entry.Enabled = true;
				entry.Selections.Ensure("Alpha").Enabled = true;
			}
			finally
			{
				source.Dispose();
			}

			return VariantReader.Read(profile, this._store, GameINT.Underground2);
		}

		private string Staging()
		{
			string staging = Path.Combine(this._temp.Path, "staging");
			Directory.CreateDirectory(Path.Combine(staging, "GLOBAL"));
			File.WriteAllText(Path.Combine(staging, "GLOBAL", "GLOBALB.LZC"), "container b");

			return staging;
		}

		// ---------------------------------------------------------------- refused commands

		[Fact]
		public void ARefusedCommandStopsTheNativeRoute()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				"stop_errors true");

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => CommandGate.Check(variants, this.Staging()));

			Assert.Contains("does not have", error.Message);
		}

		/// <summary>
		/// The test that carries the feature. The same mod must pass the gate for the CLI route,
		/// because Binary runs the command.
		/// </summary>
		[Fact]
		public void ARefusedCommandPassesTheGateForTheCliRoute()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				"stop_errors true");

			var log = new List<string>();

			GateResult gate = CommandGate.Check(variants, this.Staging(), log.Add, null,
				refuseUnsupported: false);

			Assert.NotEmpty(gate.Scripts);
			Assert.Contains(log, line => line.Contains("Binary runs"));
		}

		/// <summary>
		/// The gate still collects the containers of the mod. The report needs them, and the
		/// verify trusts the report.
		/// </summary>
		[Fact]
		public void TheGateStillNamesTheContainersOnTheCliRoute()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				"stop_errors true");

			GateResult gate = CommandGate.Check(variants, this.Staging(), null, null,
				refuseUnsupported: false);

			Assert.Contains(@"GLOBAL\GLOBALB.LZC", gate.Containers);
		}

		// ---------------------------------------------------------------- escaped paths

		/// <summary>
		/// A path that leaves the staging copy stops both routes. Binary would write into the
		/// real filesystem, and the revert would never see it.
		/// </summary>
		[Fact]
		public void AnEscapedPathStopsTheCliRouteToo()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				@"unpack_stream a b ..\..\outside");

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => CommandGate.Check(variants, this.Staging(), null, null, refuseUnsupported: false));

			Assert.Contains("outside the staging copy", error.Message);
		}

		[Fact]
		public void AnEscapedPathStopsTheNativeRoute()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				@"unpack_stream a b ..\..\outside");

			DeployServiceException error = Assert.Throws<DeployServiceException>(
				() => CommandGate.Check(variants, this.Staging()));

			Assert.Contains("outside the staging copy", error.Message);
		}

		// ---------------------------------------------------------------- the default

		/// <summary>
		/// The strict gate is the default. A caller that passes nothing keeps the rule that the
		/// container engine needs.
		/// </summary>
		[Fact]
		public void TheGateRefusesByDefault()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				"stop_errors true");

			Assert.Throws<DeployServiceException>(() => CommandGate.Check(variants, this.Staging(), null));
		}

		[Fact]
		public void ACleanModPassesEitherWay()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4");

			Assert.NotEmpty(CommandGate.Check(variants, this.Staging()).Scripts);
			Assert.NotEmpty(CommandGate.Check(variants, this.Staging(), null, null, false).Scripts);
		}
	}
}
