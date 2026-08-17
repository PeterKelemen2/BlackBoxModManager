using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Which code applies each enabled Binary mod of one deploy.
	///
	/// <b>The deploy builds this one time and every part reads it.</b> The staging step needs
	/// the answer before it copies one file, the router needs it to split the mods, and the log
	/// needs it to name the route. A second resolution somewhere else could disagree with the
	/// first, and the deploy would then protect the wrong files.
	///
	/// Only a Binary mod appears here. An ASI mod and a loose-file mod have one route.
	/// </summary>
	public sealed class BinaryRoutePlan
	{
		private readonly Dictionary<string, BinaryRoute> _routes;

		/// <summary>The mods that this plan covers, in load order.</summary>
		public IReadOnlyList<string> ModIds { get; }

		/// <summary>The count of mods that take the CLI route.</summary>
		public int CliCount { get; }

		/// <summary>The count of mods that take the native route.</summary>
		public int NativeCount => this.ModIds.Count - this.CliCount;

		/// <summary>
		/// True when at least one enabled Binary mod runs the Binary executable.
		///
		/// <b>The staging step reads this.</b> A true answer means that the staging copy must
		/// hold a private copy of every file. See TreeReplicator and defect 16.
		/// </summary>
		public bool UsesCli => this.CliCount > 0;

		private BinaryRoutePlan(Dictionary<string, BinaryRoute> routes, IReadOnlyList<string> order,
			int cliCount)
		{
			this._routes = routes;
			this.ModIds = order;
			this.CliCount = cliCount;
		}

		/// <summary>
		/// An empty plan. Every mod takes the native route.
		/// </summary>
		public static BinaryRoutePlan Empty { get; } = new BinaryRoutePlan(
			new Dictionary<string, BinaryRoute>(StringComparer.OrdinalIgnoreCase),
			Array.Empty<string>(), 0);

		/// <summary>
		/// Reads the route of every enabled Binary mod of the profile, in load order.
		///
		/// A mod that the store no longer holds stays out. <c>DeployService.ResolveEnabled</c>
		/// reports that case with a message that names the mod, and this call must not throw
		/// first with a worse message.
		/// </summary>
		public static BinaryRoutePlan Build(Profile profile, ModStore store)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));
			if (store is null) throw new ArgumentNullException(nameof(store));

			var routes = new Dictionary<string, BinaryRoute>(StringComparer.OrdinalIgnoreCase);
			var order = new List<string>();
			int cli = 0;

			foreach (ProfileEntry entry in profile.Entries)
			{
				if (!entry.Enabled) continue;

				InstalledMod mod = store.Find(entry.ModId);

				if (mod is null || mod.Kind != ModKind.Binary) continue;

				BinaryRoute route = profile.RouteOf(entry);

				routes[entry.ModId] = route;
				order.Add(entry.ModId);

				if (route == BinaryRoute.BinaryCli) ++cli;
			}

			return new BinaryRoutePlan(routes, order, cli);
		}

		/// <summary>
		/// The route of one mod. A mod that this plan does not know takes the native route,
		/// because that route needs no extra preparation.
		/// </summary>
		public BinaryRoute Of(string modId)
		{
			if (String.IsNullOrEmpty(modId)) return BinaryRoute.Native;

			return this._routes.TryGetValue(modId, out BinaryRoute route) ? route : BinaryRoute.Native;
		}

		/// <summary>
		/// One line for each mod, for the log. The user must be able to read which route ran.
		/// </summary>
		public IReadOnlyList<string> Describe(ModStore store)
		{
			var lines = new List<string>(this.ModIds.Count);

			foreach (string id in this.ModIds)
			{
				InstalledMod mod = store?.Find(id);
				string name = mod?.Name ?? id;

				lines.Add(this.Of(id) == BinaryRoute.BinaryCli
					? $"  The mod \"{name}\" deploys through Binary."
					: $"  The mod \"{name}\" deploys through the container engine.");
			}

			return lines;
		}

		public override string ToString() =>
			$"{this.ModIds.Count} Binary mods. {this.NativeCount} take the container engine and " +
			$"{this.CliCount} take Binary.";
	}
}
