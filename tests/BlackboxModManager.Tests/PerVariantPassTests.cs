using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Endscript.Commands;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the rule that each variant gets one load, one script, and one save.
	///
	/// The command <c>delete</c> saves a container and then removes it from the profile. One
	/// profile shared by every mod therefore leaves the next mod with nothing to edit, and
	/// that mod fails with "was never loaded". See defect 18.
	///
	/// These tests build the mods by hand and load no container, so they need no game.
	/// </summary>
	public class PerVariantPassTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public PerVariantPassTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose() => this._temp.Dispose();

		// --------------------------------------------------------------- one load per variant

		/// <summary>
		/// The manifest of one pass names the containers of one mod. A pass that loaded the
		/// union would read containers that its own mod never names, which is what made every
		/// forced collection of the library walk a heap of every mod at once.
		/// </summary>
		[Fact]
		public void OnePassLoadsTheContainersOfOneModAndNoMore()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				new Mod("First", new[] { @"GLOBAL\GLOBALB.LZC", @"GLOBAL\INGAMEA.BUN" }, "watermark \"\""),
				new Mod("Second", new[] { @"CARS\TEXTURES.BIN" }, "watermark \"\""));

			string staging = this._temp.Path;

			MergedLoad first = MergedLaunch.Build(new[] { variants[0] }, staging);
			MergedLoad second = MergedLaunch.Build(new[] { variants[1] }, staging);

			Assert.Equal(2, first.Files.Count);
			Assert.Single(second.Files);
			Assert.Contains(@"CARS\TEXTURES.BIN", second.Files);
			Assert.DoesNotContain(@"CARS\TEXTURES.BIN", first.Files);
		}

		[Fact]
		public void TheUnionStillCoversEveryContainerOfEveryMod()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				new Mod("First", new[] { @"GLOBAL\GLOBALB.LZC" }, "watermark \"\""),
				new Mod("Second", new[] { @"CARS\TEXTURES.BIN" }, "watermark \"\""));

			MergedLoad union = MergedLaunch.Build(variants, this._temp.Path, strict: false);

			Assert.Equal(2, union.Files.Count);
		}

		// --------------------------------------------------------------- the spelling rule

		/// <summary>
		/// Two spellings of one container in one load lose the edits of the first mod with no
		/// error, so a strict build stops. See defect 6.
		/// </summary>
		[Fact]
		public void TwoSpellingsInOneLoadStopTheBuild()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				new Mod("First", new[] { @"GLOBAL\GLOBALB.LZC" }, "watermark \"\""),
				new Mod("Second", new[] { "GLOBAL/GlobalB.lzc" }, "watermark \"\""));

			Assert.Throws<DeployServiceException>(
				() => MergedLaunch.Build(variants, this._temp.Path));
		}

		/// <summary>
		/// Nothing loads the union, so two spellings there are harmless. The engine uses the
		/// union to make each container private and to report what it rewrote. One entry for
		/// one file is all that job needs.
		/// </summary>
		[Fact]
		public void TwoSpellingsDoNotStopTheUnion()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				new Mod("First", new[] { @"GLOBAL\GLOBALB.LZC" }, "watermark \"\""),
				new Mod("Second", new[] { "GLOBAL/GlobalB.lzc" }, "watermark \"\""));

			MergedLoad union = MergedLaunch.Build(variants, this._temp.Path, strict: false);

			Assert.Single(union.Files);
			Assert.Equal(2, union.Contributors[union.Files[0]].Count);
		}

		// ------------------------------------------------------- the gate feeds the engine

		/// <summary>
		/// The engine runs the commands that the gate parsed, and it reads entry <c>i</c> for
		/// variant <c>i</c>. A list out of order would run the script of one mod against the
		/// containers of another.
		/// </summary>
		[Fact]
		public void TheGateReturnsOneScriptForEachVariantInLoadOrder()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				new Mod("First", new[] { @"GLOBAL\GLOBALB.LZC" }, "watermark \"first\""),
				new Mod("Second", new[] { @"GLOBAL\GLOBALB.LZC" }, "watermark \"second\""));

			GateResult gate = CommandGate.Check(variants, this.Staging());

			Assert.Equal(variants.Count, gate.Scripts.Count);

			for (int i = 0; i < variants.Count; ++i)
			{
				Assert.Equal(variants[i].Variant.Name, gate.Scripts[i].Variant);
			}
		}

		/// <summary>
		/// The gate carries the parsed script. The engine runs that array, so it never reads
		/// the appended files a second time.
		/// </summary>
		[Fact]
		public void TheGateCarriesTheParsedCommands()
		{
			IReadOnlyList<EnabledVariant> variants = this.Build(
				new Mod("First", new[] { @"GLOBAL\GLOBALB.LZC" },
					"watermark \"\"",
					"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1"));

			GateResult gate = CommandGate.Check(variants, this.Staging());

			BaseCommand[] commands = gate.Scripts[0].Commands;

			// The version header is line one, so the parser yields the two real commands.
			Assert.Equal(2, commands.Length);
			Assert.Equal("watermark", commands[0].Type.ToString());
			Assert.Equal("update_collection", commands[1].Type.ToString());
		}

		// ------------------------------------------------------------------------- fixtures

		private sealed class Mod
		{
			public Mod(string name, string[] files, params string[] lines)
			{
				this.Name = name;
				this.Files = files;
				this.Lines = lines;
			}

			public string Name { get; }

			public string[] Files { get; }

			public string[] Lines { get; }
		}

		/// <summary>
		/// Imports one mod for each entry and switches every one on, in the given order.
		/// </summary>
		private IReadOnlyList<EnabledVariant> Build(params Mod[] mods)
		{
			var profile = new Profile("Test", nameof(GameINT.MostWanted));

			foreach (Mod definition in mods)
			{
				var source = new TempDirectory();

				try
				{
					source.WriteManifest($"{definition.Name}.end", "MostWanted",
						$"{definition.Name}Script.end", definition.Files);

					source.WriteScript($"{definition.Name}Script.end", definition.Lines);

					InstalledMod mod = this._importer.Import(source.Path, GameINT.MostWanted).Mod;

					ProfileEntry entry = profile.Ensure(mod.Id);
					entry.Enabled = true;
					entry.Selections.Ensure(definition.Name).Enabled = true;
				}
				finally
				{
					source.Dispose();
				}
			}

			return VariantReader.Read(profile, this._store, GameINT.MostWanted);
		}

		/// <summary>A staging directory that holds every container that the fixtures name.</summary>
		private string Staging()
		{
			string root = Path.Combine(this._temp.Path, "staging");

			foreach (string relative in new[]
			{
				@"GLOBAL\GLOBALB.LZC",
				@"GLOBAL\INGAMEA.BUN",
				@"CARS\TEXTURES.BIN",
			})
			{
				string path = ModPath.Resolve(root, relative);

				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, "game data");
			}

			return root;
		}
	}
}
