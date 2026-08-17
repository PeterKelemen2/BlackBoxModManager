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
	/// Records which mods it received, and reports nothing.
	/// </summary>
	internal sealed class RecordingEngine : IDeployEngine
	{
		private readonly List<string> _order;

		public RecordingEngine(string name, List<string> order)
		{
			this.Name = name;
			this._order = order;
		}

		public string Name { get; }

		public IReadOnlySet<ModKind> Kinds { get; } = new HashSet<ModKind> { ModKind.Binary };

		/// <summary>Each call, as the names of the mods that it received.</summary>
		public List<string> Calls { get; } = new List<string>();

		public DeployReport Deploy(DeployContext context, IReadOnlyList<InstalledMod> mods)
		{
			var names = new List<string>(mods.Count);

			foreach (InstalledMod mod in mods)
			{
				names.Add(mod.Id);
				this._order.Add($"{this.Name}:{mod.Id}");
			}

			this.Calls.Add(String.Join(",", names));

			return new DeployReport(null, null, null, null);
		}
	}

	/// <summary>
	/// Covers the router that splits the Binary kind over two engines.
	///
	/// <b>The load order is the whole point.</b> The edits composite through the disk, so a mod
	/// must never run before a mod that sits above it in the profile. A router that grouped every
	/// native mod and then every CLI mod would reorder the deploy and give a different result.
	/// </summary>
	public class BinaryRouteEngineTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly FakeGame _game = new FakeGame();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public BinaryRouteEngineTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose()
		{
			this._temp.Dispose();
			this._game.Dispose();
		}

		private Profile Profile { get; set; }

		/// <summary>
		/// Imports one mod for each name and switches it on, in order. The route of each one
		/// comes from the matching entry of the routes array.
		/// </summary>
		private IReadOnlyList<InstalledMod> Build(string[] names, BinaryRouteChoice[] routes)
		{
			var profile = new Profile("Test", nameof(GameINT.Underground2));
			var mods = new List<InstalledMod>();

			for (int i = 0; i < names.Length; ++i)
			{
				var source = new TempDirectory();

				try
				{
					source.WriteManifest($"{names[i]}.end", "Underground2", $"{names[i]}Script.end");
					source.WriteScript($"{names[i]}Script.end",
						@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4");

					InstalledMod mod = this._importer.Import(source.Path, GameINT.Underground2).Mod;
					mods.Add(mod);

					ProfileEntry entry = profile.Ensure(mod.Id);
					entry.Enabled = true;
					entry.Route = routes[i];
				}
				finally
				{
					source.Dispose();
				}
			}

			this.Profile = profile;

			return mods;
		}

		private DeployContext Context()
		{
			string staging = Path.Combine(this._temp.Path, "staging");
			Directory.CreateDirectory(staging);

			return new DeployContext(this._game.Install(), staging, this.Profile, this._store,
				null, null, null, null, null, Array.Empty<EnabledVariant>(),
				new ScriptResolutionCache(staging), null, default,
				BinaryRoutePlan.Build(this.Profile, this._store));
		}

		[Fact]
		public void OneRouteForEveryModGivesOneCall()
		{
			IReadOnlyList<InstalledMod> mods = this.Build(
				new[] { "Alpha", "Beta" },
				new[] { BinaryRouteChoice.Native, BinaryRouteChoice.Native });

			var order = new List<string>();
			var native = new RecordingEngine("native", order);
			var cli = new RecordingEngine("cli", order);

			new BinaryRouteEngine(native, cli).Deploy(this.Context(), mods);

			Assert.Single(native.Calls);
			Assert.Equal($"{mods[0].Id},{mods[1].Id}", native.Calls[0]);
			Assert.Empty(cli.Calls);
		}

		[Fact]
		public void EveryModOnTheCliRouteGoesToTheCliEngine()
		{
			IReadOnlyList<InstalledMod> mods = this.Build(
				new[] { "Alpha", "Beta" },
				new[] { BinaryRouteChoice.BinaryCli, BinaryRouteChoice.BinaryCli });

			var order = new List<string>();
			var native = new RecordingEngine("native", order);
			var cli = new RecordingEngine("cli", order);

			new BinaryRouteEngine(native, cli).Deploy(this.Context(), mods);

			Assert.Empty(native.Calls);
			Assert.Single(cli.Calls);
			Assert.Equal($"{mods[0].Id},{mods[1].Id}", cli.Calls[0]);
		}

		/// <summary>
		/// The test that carries the feature. A profile that alternates the route must still
		/// apply the mods in profile order.
		/// </summary>
		[Fact]
		public void AMixedProfileKeepsTheLoadOrderAcrossBothRoutes()
		{
			IReadOnlyList<InstalledMod> mods = this.Build(
				new[] { "Alpha", "Beta", "Gamma", "Delta" },
				new[]
				{
					BinaryRouteChoice.Native,
					BinaryRouteChoice.BinaryCli,
					BinaryRouteChoice.BinaryCli,
					BinaryRouteChoice.Native,
				});

			var order = new List<string>();
			var native = new RecordingEngine("native", order);
			var cli = new RecordingEngine("cli", order);

			new BinaryRouteEngine(native, cli).Deploy(this.Context(), mods);

			Assert.Equal(
				new[]
				{
					$"native:{mods[0].Id}",
					$"cli:{mods[1].Id}",
					$"cli:{mods[2].Id}",
					$"native:{mods[3].Id}",
				},
				order.ToArray());
		}

		/// <summary>
		/// A run of mods that share one route reaches its engine in one call. That keeps the cost
		/// of the split at one call per change of route and not one call per mod.
		/// </summary>
		[Fact]
		public void ARunOfOneRouteReachesItsEngineInOneCall()
		{
			IReadOnlyList<InstalledMod> mods = this.Build(
				new[] { "Alpha", "Beta", "Gamma" },
				new[]
				{
					BinaryRouteChoice.Native,
					BinaryRouteChoice.BinaryCli,
					BinaryRouteChoice.BinaryCli,
				});

			var order = new List<string>();
			var native = new RecordingEngine("native", order);
			var cli = new RecordingEngine("cli", order);

			new BinaryRouteEngine(native, cli).Deploy(this.Context(), mods);

			Assert.Single(native.Calls);
			Assert.Single(cli.Calls);
			Assert.Equal($"{mods[1].Id},{mods[2].Id}", cli.Calls[0]);
		}

		[Fact]
		public void TheProfileDefaultAppliesToAModThatChoosesNothing()
		{
			IReadOnlyList<InstalledMod> mods = this.Build(
				new[] { "Alpha", "Beta" },
				new[] { BinaryRouteChoice.Inherit, BinaryRouteChoice.Native });

			this.Profile.BinaryRoute = BinaryRoute.BinaryCli;

			var order = new List<string>();
			var native = new RecordingEngine("native", order);
			var cli = new RecordingEngine("cli", order);

			new BinaryRouteEngine(native, cli).Deploy(this.Context(), mods);

			Assert.Equal(new[] { $"cli:{mods[0].Id}", $"native:{mods[1].Id}" }, order.ToArray());
		}

		[Fact]
		public void NoModGivesNoCall()
		{
			this.Build(Array.Empty<string>(), Array.Empty<BinaryRouteChoice>());

			var order = new List<string>();
			var native = new RecordingEngine("native", order);
			var cli = new RecordingEngine("cli", order);

			new BinaryRouteEngine(native, cli).Deploy(this.Context(), Array.Empty<InstalledMod>());

			Assert.Empty(order);
		}

		// ---------------------------------------------------------------- the plan

		[Fact]
		public void ThePlanCountsBothRoutes()
		{
			this.Build(
				new[] { "Alpha", "Beta", "Gamma" },
				new[]
				{
					BinaryRouteChoice.Native,
					BinaryRouteChoice.BinaryCli,
					BinaryRouteChoice.BinaryCli,
				});

			BinaryRoutePlan plan = BinaryRoutePlan.Build(this.Profile, this._store);

			Assert.True(plan.UsesCli);
			Assert.Equal(2, plan.CliCount);
			Assert.Equal(1, plan.NativeCount);
			Assert.Equal(3, plan.ModIds.Count);
		}

		/// <summary>
		/// The staging step reads UsesCli. A profile with no CLI mod must keep the cheap copy.
		/// </summary>
		[Fact]
		public void ThePlanReportsNoCliRouteWhenEveryModIsNative()
		{
			this.Build(
				new[] { "Alpha", "Beta" },
				new[] { BinaryRouteChoice.Native, BinaryRouteChoice.Inherit });

			BinaryRoutePlan plan = BinaryRoutePlan.Build(this.Profile, this._store);

			Assert.False(plan.UsesCli);
			Assert.Equal(0, plan.CliCount);
		}

		/// <summary>
		/// A disabled mod supplies nothing, so it must not force the expensive staging copy.
		/// </summary>
		[Fact]
		public void ADisabledCliModDoesNotForceTheFullCopy()
		{
			this.Build(new[] { "Alpha" }, new[] { BinaryRouteChoice.BinaryCli });

			this.Profile.Entries[0].Enabled = false;

			BinaryRoutePlan plan = BinaryRoutePlan.Build(this.Profile, this._store);

			Assert.False(plan.UsesCli);
			Assert.Empty(plan.ModIds);
		}

		[Fact]
		public void AnEmptyPlanSendsEveryModToTheNativeRoute()
		{
			Assert.False(BinaryRoutePlan.Empty.UsesCli);
			Assert.Equal(BinaryRoute.Native, BinaryRoutePlan.Empty.Of("anything"));
		}
	}
}
