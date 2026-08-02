using System;
using System.Collections.Generic;
using Endscript.Core;
using Nikki.Core;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// The mechanism that produced an option set.
	/// </summary>
	public enum ModOptionKind
	{
		/// <summary>A combobox. The script names every option. Single select.</summary>
		Combobox = 0,

		/// <summary>A checkbox. The options are always disabled and enabled.</summary>
		Checkbox,
	}

	/// <summary>
	/// One option inside an option set.
	///
	/// Name is the block name in the script. It is not display text. A checkbox always
	/// names its two blocks disabled and enabled, and the script must use those words.
	/// The UI may show anything it likes. The resolver uses Name.
	/// </summary>
	public sealed class ModOption
	{
		public string Name { get; }

		public int Index { get; }

		public ModOption(string name, int index)
		{
			this.Name = name;
			this.Index = index;
		}

		public override string ToString() => $"[{this.Index}] {this.Name}";
	}

	/// <summary>
	/// One question that a script asks. A combobox or a checkbox produces one.
	///
	/// An if statement does not. It carries the same interface and it never pauses,
	/// because ProcessScript evaluates it inline against the loaded containers.
	/// </summary>
	public sealed class ModOptionSet
	{
		/// <summary>
		/// The position of this question among the questions of one script, from zero.
		/// The resolver answers the questions in this order.
		/// </summary>
		public int Ordinal { get; }

		public ModOptionKind Kind { get; }

		public string Description { get; }

		public IReadOnlyList<ModOption> Options { get; }

		/// <summary>The script file that holds the question, relative to the launcher.</summary>
		public string SourceFile { get; }

		/// <summary>The line number in that file, from one.</summary>
		public int SourceLine { get; }

		public ModOptionSet(int ordinal, ModOptionKind kind, string description,
			IReadOnlyList<ModOption> options, string sourceFile, int sourceLine)
		{
			this.Ordinal = ordinal;
			this.Kind = kind;
			this.Description = description ?? String.Empty;
			this.Options = options;
			this.SourceFile = sourceFile ?? String.Empty;
			this.SourceLine = sourceLine;
		}

		public ModOption Find(string name)
		{
			foreach (ModOption option in this.Options)
			{
				if (String.Equals(option.Name, name, StringComparison.Ordinal)) return option;
			}

			return null;
		}
	}

	/// <summary>
	/// Why a variant cannot be installed.
	/// </summary>
	public enum ModVariantState
	{
		/// <summary>The manifest and the script both read. The variant is installable.</summary>
		Ok = 0,

		/// <summary>The manifest named a game that Nikki does not support.</summary>
		UnsupportedGame,

		/// <summary>The manifest did not read.</summary>
		BadManifest,

		/// <summary>The manifest read and the script did not.</summary>
		BadScript,
	}

	/// <summary>
	/// One installable choice inside a mod folder. A sibling VERSN1 manifest makes one.
	///
	/// The user may enable several variants of one package at the same time. The four
	/// 1 Lap manifests are an example. That is a multiple selection, and it is a different
	/// mechanism from the single selection that an option set holds.
	/// </summary>
	public sealed class ModVariant
	{
		/// <summary>The manifest file name without its extension. This names the variant.</summary>
		public string Name { get; }

		public string ManifestPath { get; }

		public ModVariantState State { get; }

		/// <summary>Empty when State is Ok.</summary>
		public string Problem { get; }

		/// <summary>Null when the manifest did not read.</summary>
		public Launch Manifest { get; }

		public GameINT Game { get; }

		/// <summary>
		/// The questions of the script, in the order that the script asks them. Empty when
		/// the script asks nothing.
		///
		/// The roadmap model names one nullable option set. A script can hold more than one
		/// selectable, and ProcessScript then pauses more than once, so this is a list.
		/// </summary>
		public IReadOnlyList<ModOptionSet> OptionSets { get; }

		public bool IsInstallable => this.State == ModVariantState.Ok;

		public ModVariant(string name, string manifestPath, ModVariantState state, string problem,
			Launch manifest, GameINT game, IReadOnlyList<ModOptionSet> optionSets)
		{
			this.Name = name;
			this.ManifestPath = manifestPath;
			this.State = state;
			this.Problem = problem ?? String.Empty;
			this.Manifest = manifest;
			this.Game = game;
			this.OptionSets = optionSets ?? Array.Empty<ModOptionSet>();
		}

		public override string ToString() => $"{this.Name} ({this.State})";
	}

	/// <summary>
	/// One mod folder. It holds one or more variants.
	///
	/// A folder with four manifests is one package with four variants. It is not four
	/// unrelated mods.
	/// </summary>
	public sealed class ModPackage
	{
		public string Root { get; }

		/// <summary>The folder name. This names the package.</summary>
		public string Name { get; }

		public IReadOnlyList<ModVariant> Variants { get; }

		/// <summary>
		/// Problems that belong to the folder and not to one variant. A folder with no
		/// manifest produces one.
		/// </summary>
		public IReadOnlyList<string> Problems { get; }

		public bool IsInstallable
		{
			get
			{
				foreach (ModVariant variant in this.Variants)
				{
					if (variant.IsInstallable) return true;
				}

				return false;
			}
		}

		public ModPackage(string root, string name, IReadOnlyList<ModVariant> variants,
			IReadOnlyList<string> problems)
		{
			this.Root = root;
			this.Name = name;
			this.Variants = variants ?? Array.Empty<ModVariant>();
			this.Problems = problems ?? Array.Empty<string>();
		}

		public ModVariant Find(string name)
		{
			foreach (ModVariant variant in this.Variants)
			{
				if (String.Equals(variant.Name, name, StringComparison.OrdinalIgnoreCase)) return variant;
			}

			return null;
		}

		public override string ToString() => $"{this.Name} ({this.Variants.Count} variants)";
	}
}
