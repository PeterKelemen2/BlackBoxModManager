using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Sends each Binary mod to the engine that its route names.
	///
	/// <b>DeployService groups the mods by kind, so one kind needs one engine.</b> The container
	/// engine and the CLI engine both apply a Binary mod. This router claims the kind, and it
	/// hands each mod to the right engine underneath.
	///
	/// <b>The load order holds across both routes.</b> The edits composite through the disk, so
	/// a mod must never run before a mod that sits above it in the profile. The router therefore
	/// cuts the list into runs of one route and calls the engines in that order. A profile that
	/// alternates the route produces one call for each run, and each call still reads what the
	/// call before it wrote.
	/// </summary>
	public sealed class BinaryRouteEngine : IDeployEngine
	{
		private readonly IDeployEngine _native;
		private readonly IDeployEngine _cli;

		public BinaryRouteEngine() : this(new ContainerDeployEngine(), new BinaryCliDeployEngine()) { }

		public BinaryRouteEngine(IDeployEngine native, IDeployEngine cli)
		{
			this._native = native ?? throw new ArgumentNullException(nameof(native));
			this._cli = cli ?? throw new ArgumentNullException(nameof(cli));
		}

		public string Name => "Binary router";

		public IReadOnlySet<ModKind> Kinds { get; } = new HashSet<ModKind> { ModKind.Binary };

		public DeployReport Deploy(DeployContext context, IReadOnlyList<InstalledMod> mods)
		{
			if (context is null) throw new ArgumentNullException(nameof(context));
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			if (mods.Count == 0) return new DeployReport(null, null, null, null);

			IReadOnlyList<Run> runs = Split(context, mods);

			if (runs.Count == 1)
			{
				// One route for every mod. Do not say anything extra, because the engine that
				// runs names itself.
				return this.Engine(runs[0].Route).Deploy(context, runs[0].Mods);
			}

			// DeployService already listed the route of every mod. Say only what this split adds.
			context.Log($"The profile splits {mods.Count} Binary mods over two routes, in " +
				$"{runs.Count} runs. Each run reads what the run before it wrote.");

			var joined = new List<DeployReport>(runs.Count);

			foreach (Run run in runs)
			{
				context.Cancellation.ThrowIfCancellationRequested();

				joined.Add(this.Engine(run.Route).Deploy(context, run.Mods));
			}

			return Join(joined);
		}

		private IDeployEngine Engine(BinaryRoute route) =>
			route == BinaryRoute.BinaryCli ? this._cli : this._native;

		/// <summary>
		/// Cuts the mods into runs of one route, and keeps the given order.
		/// </summary>
		private static IReadOnlyList<Run> Split(DeployContext context, IReadOnlyList<InstalledMod> mods)
		{
			var runs = new List<Run>();
			List<InstalledMod> current = null;
			BinaryRoute route = BinaryRoute.Native;

			foreach (InstalledMod mod in mods)
			{
				BinaryRoute mine = context.Routes.Of(mod.Id);

				if (current is null || mine != route)
				{
					current = new List<InstalledMod>();
					route = mine;
					runs.Add(new Run(route, current));
				}

				current.Add(mod);
			}

			return runs;
		}

		/// <summary>
		/// Joins the reports of every run, in the order that the runs happened.
		///
		/// This mirrors DeployService.RunEngines. A later run wins the method note, so the first
		/// note that carries text stays.
		/// </summary>
		private static DeployReport Join(IReadOnlyList<DeployReport> reports)
		{
			var files = new List<DeployedFile>();
			var overrides = new List<DeployOverride>();
			var containers = new List<ContainerWrite>();
			var settings = new List<SettingsWrite>();
			var writes = new List<ScriptWrite>();
			var methods = new Dictionary<LinkKind, int>();
			string note = String.Empty;

			foreach (DeployReport report in reports)
			{
				files.AddRange(report.Files);
				overrides.AddRange(report.Overrides);
				containers.AddRange(report.Containers);
				settings.AddRange(report.Settings);
				writes.AddRange(report.ScriptWrites);

				foreach (KeyValuePair<LinkKind, int> entry in report.Methods)
				{
					methods[entry.Key] = methods.TryGetValue(entry.Key, out int count)
						? count + entry.Value
						: entry.Value;
				}

				if (note.Length == 0) note = report.MethodNote;
			}

			return new DeployReport(files, overrides, methods, note, containers, settings, null, writes);
		}

		/// <summary>One block of mods that share one route.</summary>
		private readonly struct Run
		{
			public Run(BinaryRoute route, IReadOnlyList<InstalledMod> mods)
			{
				this.Route = route;
				this.Mods = mods;
			}

			public BinaryRoute Route { get; }

			public IReadOnlyList<InstalledMod> Mods { get; }
		}
	}
}
