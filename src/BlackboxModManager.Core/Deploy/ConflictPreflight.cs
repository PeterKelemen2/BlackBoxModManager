using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// One field that two enabled variants write with different values, and who wins.
	/// </summary>
	public sealed class ConflictEntry
	{
		public ModConflict Conflict { get; }

		/// <summary>
		/// The variant whose value the game gets. <b>Load order decides this.</b> Every mod
		/// applies to one loaded profile in order, so the last write wins. There is no
		/// second resolution mechanism, and there must not be one.
		/// </summary>
		public string Winner => this.Conflict.RightVariant;

		public string Loser => this.Conflict.LeftVariant;

		public ConflictEntry(ModConflict conflict)
		{
			this.Conflict = conflict;
		}

		public override string ToString() =>
			$"{this.Conflict.Key}: \"{this.Winner}\" wins with {this.Conflict.RightValue}, " +
			$"and \"{this.Loser}\" loses with {this.Conflict.LeftValue}.";
	}

	/// <summary>
	/// What the conflict check found.
	/// </summary>
	public sealed class ConflictReport
	{
		/// <summary>
		/// The real disagreements. Two variants that write one field with the same value are
		/// not here, and they must never be. The ALL variant of the 1 Lap mod is the union of
		/// the other four, so a user can switch on ALL and URL together and every shared
		/// field then carries one value. Reporting those teaches a user to ignore this list.
		/// </summary>
		public IReadOnlyList<ConflictEntry> Conflicts { get; }

		/// <summary>
		/// The variants that the check could not read, with the reason. A variant that uses
		/// an 'if' command lands here, because a static walk cannot resolve one without the
		/// loaded containers. See step 8.
		/// </summary>
		public IReadOnlyList<string> Unchecked { get; }

		public int CheckedVariants { get; }

		public int KeyedEdits { get; }

		public bool IsClean => this.Conflicts.Count == 0;

		public ConflictReport(IReadOnlyList<ConflictEntry> conflicts, IReadOnlyList<string> unchecked_,
			int checkedVariants, int keyedEdits)
		{
			this.Conflicts = conflicts ?? Array.Empty<ConflictEntry>();
			this.Unchecked = unchecked_ ?? Array.Empty<string>();
			this.CheckedVariants = checkedVariants;
			this.KeyedEdits = keyedEdits;
		}

		public string Summary()
		{
			if (this.CheckedVariants == 0) return "The conflict check read no variant.";

			string head = this.Conflicts.Count == 0
				? $"The conflict check read {this.CheckedVariants} variants and {this.KeyedEdits} field edits, " +
					"and it found no conflict."
				: $"The conflict check found {this.Conflicts.Count} conflicts in {this.KeyedEdits} field edits.";

			return this.Unchecked.Count == 0
				? head
				: $"{head} It could not read {this.Unchecked.Count} variants.";
		}
	}

	/// <summary>
	/// Reports the fields that two enabled variants disagree about, before anything writes.
	///
	/// <b>This check never blocks a deploy.</b> Load order already decides every collision,
	/// and the result is what the user asked for. The check exists so that the user can see
	/// the decision and reorder the mods when the winner is wrong.
	///
	/// A variant that the check cannot read is not an error either. The deploy still applies
	/// it, because the container engine resolves what a static walk cannot.
	/// </summary>
	public static class ConflictPreflight
	{
		public static ConflictReport Run(IReadOnlyList<EnabledVariant> variants, Action<string> log = null)
		{
			if (variants is null) throw new ArgumentNullException(nameof(variants));

			Action<string> write = log ?? (line => { });

			var scripts = new List<ResolvedScript>();
			var unchecked_ = new List<string>();
			int keyed = 0;

			foreach (EnabledVariant variant in variants)
			{
				try
				{
					ResolvedScript resolved = ScriptFlattener.Resolve(variant.Variant, variant.Selection);

					// Carry the label of the mod, not the bare variant name. Two mods can
					// hold a variant of one name, and a conflict line has to say which mod.
					scripts.Add(new ResolvedScript(
						variant.Label, resolved.Edits, resolved.Answers, resolved.Notes));

					foreach (ResolvedEdit edit in resolved.KeyedEdits) ++keyed;
				}
				catch (Exception ex)
				{
					unchecked_.Add($"{variant.Label}: {ex.Message}");
				}
			}

			var entries = new List<ConflictEntry>();

			foreach (ModConflict conflict in ConflictDetector.Find(scripts))
			{
				entries.Add(new ConflictEntry(conflict));
			}

			var report = new ConflictReport(entries, unchecked_, scripts.Count, keyed);

			write(report.Summary());

			foreach (ConflictEntry entry in report.Conflicts) write($"  conflict: {entry}");
			foreach (string line in report.Unchecked) write($"  not checked: {line}");

			return report;
		}
	}
}
