using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// What the user chose for one variant.
	///
	/// The answers are option names, never indexes. A mod update that reorders or inserts
	/// an option changes what an index means. The stored choice would then apply to the
	/// wrong branch, and nothing would report an error. A name that no longer exists fails
	/// loudly, which is the outcome we want.
	///
	/// One entry per question, keyed by the ordinal of the question in the script.
	/// </summary>
	public sealed class VariantSelection
	{
		/// <summary>The variant name. This is the manifest file name without its extension.</summary>
		public string Variant { get; set; }

		/// <summary>The chosen option name for each question, keyed by the ordinal.</summary>
		public Dictionary<int, string> Answers { get; set; } = new Dictionary<int, string>();

		public VariantSelection() { }

		public VariantSelection(string variant)
		{
			this.Variant = variant;
		}

		public string Answer(int ordinal)
		{
			return this.Answers.TryGetValue(ordinal, out string name) ? name : null;
		}

		public void Choose(int ordinal, string optionName)
		{
			if (String.IsNullOrWhiteSpace(optionName))
			{
				throw new ArgumentException("The option name is empty.", nameof(optionName));
			}

			this.Answers[ordinal] = optionName;
		}
	}

	/// <summary>
	/// The selections of one profile. A profile plus its selections fully determines the
	/// resolved edit list, so a deploy needs nothing else from the user.
	///
	/// This type serializes to JSON. Keep it a plain data holder.
	/// </summary>
	public sealed class ModSelections
	{
		/// <summary>One entry per enabled variant, keyed by the variant name.</summary>
		public Dictionary<string, VariantSelection> Variants { get; set; } =
			new Dictionary<string, VariantSelection>(StringComparer.OrdinalIgnoreCase);

		[JsonIgnore]
		public int Count => this.Variants.Count;

		public VariantSelection For(string variant)
		{
			return this.Variants.TryGetValue(variant, out VariantSelection selection) ? selection : null;
		}

		/// <summary>
		/// Returns the entry for a variant and makes one when it is absent.
		/// </summary>
		public VariantSelection Ensure(string variant)
		{
			if (String.IsNullOrWhiteSpace(variant))
			{
				throw new ArgumentException("The variant name is empty.", nameof(variant));
			}

			if (!this.Variants.TryGetValue(variant, out VariantSelection selection))
			{
				selection = new VariantSelection(variant);
				this.Variants[variant] = selection;
			}

			return selection;
		}

		/// <summary>
		/// Fills in the first option of every question that holds no answer yet. Use this to
		/// build a starting point that a user can then change.
		/// </summary>
		public void ApplyDefaults(ModVariant variant)
		{
			if (variant is null) throw new ArgumentNullException(nameof(variant));

			VariantSelection selection = this.Ensure(variant.Name);

			foreach (ModOptionSet set in variant.OptionSets)
			{
				if (selection.Answer(set.Ordinal) != null) continue;
				if (set.Options.Count == 0) continue;

				selection.Choose(set.Ordinal, set.Options[0].Name);
			}
		}
	}
}
