using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Nikki.Core;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// One variant that a profile switched on, with everything that a deploy needs for it.
	/// </summary>
	public sealed class EnabledVariant
	{
		public InstalledMod Mod { get; }

		public ModVariant Variant { get; }

		/// <summary>The answers of the user for this variant. This is never null.</summary>
		public VariantSelection Selection { get; }

		/// <summary>The position in the load order, from one.</summary>
		public int Order { get; }

		/// <summary>The name that a message shows. It names the mod and the variant.</summary>
		public string Label => $"{this.Mod.Name} / {this.Variant.Name}";

		public EnabledVariant(InstalledMod mod, ModVariant variant, VariantSelection selection, int order)
		{
			this.Mod = mod;
			this.Variant = variant;
			this.Selection = selection ?? new VariantSelection(variant.Name);
			this.Order = order;
		}

		public override string ToString() => $"[{this.Order}] {this.Label}";
	}

	/// <summary>
	/// Reads the variants that a profile switched on, in load order.
	///
	/// The load order comes from the profile entry list. Inside one mod, the order comes
	/// from the variant list of the package, which sorts by name. One list, one order.
	/// </summary>
	public static class VariantReader
	{
		/// <summary>
		/// Returns every enabled variant of every Binary mod in the profile, in load order.
		///
		/// It stops on a variant that cannot install. A variant with a broken manifest or a
		/// broken script would fail deep inside the library, and the message there names
		/// neither the mod nor the file.
		/// </summary>
		public static IReadOnlyList<EnabledVariant> Read(Profile profile, ModStore store, GameINT game,
			Action<string> log = null)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));
			if (store is null) throw new ArgumentNullException(nameof(store));

			Action<string> write = log ?? (line => { });
			var found = new List<EnabledVariant>();
			var problems = new List<string>();

			foreach (ProfileEntry entry in profile.Entries)
			{
				if (!entry.Enabled) continue;

				InstalledMod mod = store.Find(entry.ModId);

				if (mod is null || mod.Kind != ModKind.Binary) continue;

				ModPackage package = ModPackageReader.Read(mod.ContentRoot);
				IReadOnlyList<string> wanted = entry.Selections?.EnabledVariants() ?? Array.Empty<string>();

				if (wanted.Count == 0)
				{
					problems.Add($"The mod \"{mod.Name}\" is on and it has no variant switched on. " +
						"Choose at least one variant, or switch the mod off.");
					continue;
				}

				// Walk the package, not the selection. The package order is stable, and a
				// name in the profile that the package no longer holds must be reported.
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				foreach (ModVariant variant in package.Variants)
				{
					if (!entry.Selections.IsEnabled(variant.Name)) continue;

					seen.Add(variant.Name);

					if (!variant.IsInstallable)
					{
						problems.Add($"The variant \"{variant.Name}\" of the mod \"{mod.Name}\" cannot install. " +
							$"{variant.Problem}");
						continue;
					}

					if (variant.Game != game)
					{
						problems.Add($"The variant \"{variant.Name}\" of the mod \"{mod.Name}\" belongs to " +
							$"{variant.Game} and this deploy is for {game}.");
						continue;
					}

					found.Add(new EnabledVariant(mod, variant, entry.Selections.For(variant.Name), found.Count + 1));
				}

				foreach (string name in wanted)
				{
					if (seen.Contains(name)) continue;

					problems.Add($"The profile switches on the variant \"{name}\" of the mod \"{mod.Name}\", " +
						"and the mod no longer holds it. Choose the variants of that mod again.");
				}
			}

			if (problems.Count > 0)
			{
				throw new DeployServiceException(
					$"The profile \"{profile.Name}\" cannot deploy. {String.Join(" ", problems)}");
			}

			foreach (EnabledVariant variant in found) write($"Load order {variant.Order}: {variant.Label}.");

			return found;
		}
	}
}
