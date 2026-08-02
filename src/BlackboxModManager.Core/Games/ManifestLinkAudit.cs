using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;
using Endscript.Core;
using Endscript.Helpers;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// One difference between the Links list of a manifest and the expected set of the game.
	/// </summary>
	public sealed class LinkDeviation
	{
		/// <summary>The variant that carries the manifest.</summary>
		public string Variant { get; }

		/// <summary>The link that the manifest holds and the expected set does not.</summary>
		public ManifestLink Extra { get; }

		/// <summary>The link that the expected set holds and the manifest does not.</summary>
		public ManifestLink Missing { get; }

		internal LinkDeviation(string variant, ManifestLink extra, ManifestLink missing)
		{
			this.Variant = variant;
			this.Extra = extra;
			this.Missing = missing;
		}

		public override string ToString()
		{
			return this.Extra != null
				? $"The variant \"{this.Variant}\" holds the link {this.Extra}, and the expected set of the game does not."
				: $"The variant \"{this.Variant}\" holds no link {this.Missing}, and the expected set of the game does.";
		}
	}

	/// <summary>
	/// Compares the Links list of every manifest of a package against the expected set of
	/// the game.
	///
	/// <b>The boilerplate assumption holds for Underground 2 only.</b> All four inspected
	/// manifests are Underground 2 manifests, and all four carry one identical Links list.
	/// Whether each of the other games has its own fixed set is an assumption.
	///
	/// This class reports deviations. It never blocks anything. A deviation is information
	/// for the person who gathers the samples of a new game. Nikki loads a link that no
	/// expected set names, and it returns for a link file that does not exist.
	///
	/// <b>A game with an empty ExpectedLinks list produces no report.</b> The audit has
	/// nothing to compare against, so silence there means "not checked" and never "clean".
	/// Read HasExpectation before you show a result.
	/// </summary>
	public static class ManifestLinkAudit
	{
		/// <summary>
		/// True when the game has a recorded expected set. False means that the audit of
		/// that game reports nothing.
		/// </summary>
		public static bool HasExpectation(GameDefinition definition)
		{
			if (definition is null) throw new ArgumentNullException(nameof(definition));

			return definition.ExpectedLinks.Count > 0;
		}

		/// <summary>
		/// Returns every deviation of every readable variant of one package. The list is
		/// empty when the game has no expected set.
		/// </summary>
		public static IReadOnlyList<LinkDeviation> Run(ModPackage package, GameDefinition definition)
		{
			if (package is null) throw new ArgumentNullException(nameof(package));
			if (definition is null) throw new ArgumentNullException(nameof(definition));

			var found = new List<LinkDeviation>();

			if (!HasExpectation(definition)) return found;

			foreach (ModVariant variant in package.Variants)
			{
				// A manifest that did not read has no Links list. The reader already
				// reported that, and this audit adds nothing.
				if (variant.Manifest is null) continue;
				if (variant.Game != definition.Game) continue;

				found.AddRange(Compare(variant.Name, variant.Manifest, definition));
			}

			return found;
		}

		/// <summary>
		/// Compares one manifest. The comparison ignores the order of the links, because no
		/// loader in Nikki depends on it.
		/// </summary>
		public static IReadOnlyList<LinkDeviation> Compare(string variantName, Launch manifest,
			GameDefinition definition)
		{
			if (manifest is null) throw new ArgumentNullException(nameof(manifest));
			if (definition is null) throw new ArgumentNullException(nameof(definition));

			var found = new List<LinkDeviation>();

			if (!HasExpectation(definition)) return found;

			var expected = new Dictionary<string, ManifestLink>(StringComparer.Ordinal);

			foreach (ManifestLink link in definition.ExpectedLinks) expected[link.Key] = link;

			var held = new Dictionary<string, ManifestLink>(StringComparer.Ordinal);

			foreach (SubLoader loader in manifest.Links ?? new List<SubLoader>())
			{
				var link = new ManifestLink(loader.LoadType, loader.PathType, loader.File);
				held[link.Key] = link;
			}

			foreach (KeyValuePair<string, ManifestLink> entry in held)
			{
				if (expected.ContainsKey(entry.Key)) continue;

				found.Add(new LinkDeviation(variantName, entry.Value, null));
			}

			foreach (KeyValuePair<string, ManifestLink> entry in expected)
			{
				if (held.ContainsKey(entry.Key)) continue;

				found.Add(new LinkDeviation(variantName, null, entry.Value));
			}

			return found;
		}
	}
}
