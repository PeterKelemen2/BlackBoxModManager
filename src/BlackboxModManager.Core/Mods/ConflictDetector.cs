using System;
using System.Collections.Generic;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// The category of one conflict. The category tells the user what breaks.
	/// </summary>
	public enum ConflictKind
	{
		/// <summary>
		/// Two mods write one field with two values. Load order decides the winner and the
		/// deploy still runs.
		/// </summary>
		FieldValue = 0,

		/// <summary>
		/// Two mods disagree about whether a thing exists. One mod adds it and the other one
		/// removes it, or both add it, or both remove it. <b>The second command fails.</b>
		/// Manager.Add rejects a duplicate name and Manager.Remove rejects a missing name.
		/// </summary>
		Existence,

		/// <summary>
		/// One mod removes a thing and another mod edits something inside that thing. The two
		/// keys never match, and the removal key is a prefix of the edit key.
		/// </summary>
		Coverage,

		/// <summary>Two mods touch one path, and at least one of them writes.</summary>
		Filesystem,

		/// <summary>
		/// Two mods change one container with a command that names no single field. The tool
		/// cannot compare the two. <b>Never read this as conflict free.</b>
		/// </summary>
		Opaque,
	}

	/// <summary>
	/// How sure the check is.
	/// </summary>
	public enum ConflictCertainty
	{
		/// <summary>Both commands run on every deploy of this selection.</summary>
		Certain = 0,

		/// <summary>
		/// An <c>if</c> command encloses one of the commands, or one command is opaque. The
		/// deploy decides, and this check cannot.
		/// </summary>
		Possible,
	}

	/// <summary>
	/// Two enabled variants disagree about one thing.
	/// </summary>
	public sealed class ModConflict
	{
		public EditKey Key { get; }

		public ConflictKind Kind { get; }

		public ConflictCertainty Certainty { get; }

		public string LeftVariant { get; }

		public string LeftValue { get; }

		public string RightVariant { get; }

		public string RightValue { get; }

		/// <summary>The command of the first variant. Null for a path conflict with no key.</summary>
		public ResolvedEdit LeftEdit { get; }

		/// <summary>The command of the second variant.</summary>
		public ResolvedEdit RightEdit { get; }

		/// <summary>One sentence that names what breaks.</summary>
		public string Reason { get; }

		public ModConflict(EditKey key, string leftVariant, string leftValue, string rightVariant,
			string rightValue, ConflictKind kind = ConflictKind.FieldValue,
			ConflictCertainty certainty = ConflictCertainty.Certain,
			ResolvedEdit leftEdit = null, ResolvedEdit rightEdit = null, string reason = null)
		{
			this.Key = key;
			this.Kind = kind;
			this.Certainty = certainty;
			this.LeftVariant = leftVariant;
			this.LeftValue = leftValue;
			this.RightVariant = rightVariant;
			this.RightValue = rightValue;
			this.LeftEdit = leftEdit;
			this.RightEdit = rightEdit;
			this.Reason = reason ?? String.Empty;
		}

		public override string ToString()
		{
			string head = this.Kind == ConflictKind.FieldValue
				? $"{this.Key}: \"{this.LeftVariant}\" writes {this.LeftValue} and " +
					$"\"{this.RightVariant}\" writes {this.RightValue}"
				: $"{this.Key}: \"{this.LeftVariant}\" and \"{this.RightVariant}\". {this.Reason}";

			return this.Certainty == ConflictCertainty.Possible ? $"{head} This is possible and not certain." : head;
		}
	}

	/// <summary>
	/// Finds the things that two or more enabled variants disagree about.
	///
	/// The field rule is one line long. Same key and same value is fine. Same key and a
	/// different value is a conflict.
	///
	/// The same-value case is not a corner case. The ALL variant of the 1 Lap mod is the
	/// union of the other four, so a user can legitimately enable ALL and URL together.
	/// Every shared key then carries the same value, and reporting those would make the
	/// tool useless.
	///
	/// Step 8 added four more rules. An existence change against an existence change. A
	/// removal against an edit inside the removed thing. A path against a path. An opaque
	/// command against anything on the same container.
	///
	/// Step 6 owns what the application does with the result. This class only reports.
	/// </summary>
	public static class ConflictDetector
	{
		/// <summary>
		/// Compares every resolved variant against every earlier one. The first writer of a
		/// key is the left side of a conflict, and the later writer is the right side.
		/// </summary>
		public static IReadOnlyList<ModConflict> Find(IEnumerable<ResolvedScript> scripts)
		{
			if (scripts is null) throw new ArgumentNullException(nameof(scripts));

			var conflicts = new List<ModConflict>();
			var all = new List<Summary>();

			foreach (ResolvedScript script in scripts) all.Add(new Summary(script));

			// One key that three variants write produces two conflicts and not three. Both
			// name the first writer as the left side, because that is the mod that loses.
			var owners = new Dictionary<EditKey, (Summary Owner, ResolvedEdit Edit)>();

			foreach (Summary summary in all)
			{
				foreach (KeyValuePair<EditKey, ResolvedEdit> entry in summary.Keys)
				{
					if (owners.TryGetValue(entry.Key, out (Summary Owner, ResolvedEdit Edit) first))
					{
						SameKey(first.Owner, first.Edit, summary, entry.Value, conflicts);
						continue;
					}

					owners[entry.Key] = (summary, entry.Value);
				}
			}

			// The rest compares every pair, because each pair names two different mods.
			for (int i = 0; i < all.Count; ++i)
			{
				for (int j = i + 1; j < all.Count; ++j)
				{
					Coverage(all[i], all[j], conflicts);
					Coverage(all[j], all[i], conflicts);
					Opaque(all[i], all[j], conflicts);
					Opaque(all[j], all[i], conflicts);
					Paths(all[i], all[j], conflicts);
				}
			}

			return conflicts;
		}

		/// <summary>
		/// What one variant does, indexed for comparison.
		///
		/// One variant that writes the same key twice is not a conflict with itself. The last
		/// write wins, which is what the script author asked for. The index therefore keeps
		/// the last edit of each key.
		/// </summary>
		private sealed class Summary
		{
			public Summary(ResolvedScript script)
			{
				this.Variant = script.Variant;

				foreach (ResolvedEdit edit in script.ContainerEdits)
				{
					this.Keys[edit.Key] = edit;

					if (edit.Removes) this.Removals.Add(edit);
					if (edit.Opaque) this.Opaques.Add(edit);
				}

				foreach (ResolvedEdit edit in script.FilesystemEdits)
				{
					foreach (PathEffect path in edit.Paths) this.Paths.Add((edit, path));
				}
			}

			public string Variant { get; }

			public Dictionary<EditKey, ResolvedEdit> Keys { get; } = new Dictionary<EditKey, ResolvedEdit>();

			public List<ResolvedEdit> Removals { get; } = new List<ResolvedEdit>();

			public List<ResolvedEdit> Opaques { get; } = new List<ResolvedEdit>();

			public List<(ResolvedEdit Edit, PathEffect Path)> Paths { get; } =
				new List<(ResolvedEdit, PathEffect)>();
		}

		/// <summary>
		/// Both variants name one thing. A different value conflicts. An existence change
		/// against an existence change conflicts whatever the values are.
		/// </summary>
		private static void SameKey(Summary left, ResolvedEdit first, Summary right,
			ResolvedEdit second, List<ModConflict> conflicts)
		{
			EditKey key = first.Key;

			// An opaque command shares the key of another command and says nothing about what
			// it changes. Opaque covers that case, so skip it here.
			if (first.Opaque || second.Opaque) return;

			if (first.Removes || second.Removes)
			{
				conflicts.Add(new ModConflict(key, left.Variant, first.Value,
					right.Variant, second.Value, ConflictKind.Existence, Certainty(first, second),
					first, second, Existence(first, second)));

				return;
			}

			if (Creates(first) && Creates(second))
			{
				conflicts.Add(new ModConflict(key, left.Variant, first.Value,
					right.Variant, second.Value, ConflictKind.Existence, Certainty(first, second),
					first, second,
					"Both mods create it. Manager.Add rejects a duplicate name, so the second " +
					"command fails and the deploy stops."));

				return;
			}

			if (SameValue(first.Value, second.Value)) return;

			conflicts.Add(new ModConflict(key, left.Variant, first.Value,
				right.Variant, second.Value, Kind(first, second), Certainty(first, second),
				first, second,
				$"The command at {second.Where} wins, because it applies later."));
		}

		/// <summary>
		/// A removal in the first variant against an edit inside the removed thing in the
		/// second variant.
		/// </summary>
		private static void Coverage(Summary remover, Summary editor, List<ModConflict> conflicts)
		{
			foreach (ResolvedEdit removal in remover.Removals)
			{
				foreach (KeyValuePair<EditKey, ResolvedEdit> entry in editor.Keys)
				{
					// The equal case belongs to SameKey. Report only the case where the
					// removal key names something that holds the edit key.
					if (entry.Key.Equals(removal.Key)) continue;
					if (!removal.Key.Covers(entry.Key)) continue;

					conflicts.Add(new ModConflict(entry.Key, remover.Variant, removal.Text,
						editor.Variant, entry.Value.Value, ConflictKind.Coverage,
						Certainty(removal, entry.Value), removal, entry.Value,
						$"The command \"{removal.Verb}\" at {removal.Where} removes {removal.Key}. " +
						$"The command at {entry.Value.Where} edits something inside it. " +
						"The later command of the two fails, or the earlier edit disappears."));
				}
			}
		}

		/// <summary>
		/// An opaque command against anything on the same container. The tool cannot name
		/// what the opaque command changes, so it reports the pair and says so.
		/// </summary>
		private static void Opaque(Summary left, Summary right, List<ModConflict> conflicts)
		{
			foreach (ResolvedEdit opaque in left.Opaques)
			{
				foreach (KeyValuePair<EditKey, ResolvedEdit> entry in right.Keys)
				{
					if (!opaque.Key.Covers(entry.Key)) continue;

					conflicts.Add(new ModConflict(entry.Key, left.Variant, opaque.Text,
						right.Variant, entry.Value.Value, ConflictKind.Opaque,
						ConflictCertainty.Possible, opaque, entry.Value,
						$"The command \"{opaque.Verb}\" at {opaque.Where} reads its names at deploy " +
						"time. This check cannot say whether the two commands touch one thing."));
				}
			}
		}

		/// <summary>
		/// Two variants that touch one path. A read against a read is fine. Anything else is
		/// a conflict.
		/// </summary>
		private static void Paths(Summary left, Summary right, List<ModConflict> conflicts)
		{
			foreach ((ResolvedEdit Edit, PathEffect Path) first in left.Paths)
			{
				foreach ((ResolvedEdit Edit, PathEffect Path) second in right.Paths)
				{
					if (!first.Path.Writes && !second.Path.Writes) continue;
					if (!SamePath(first.Path, second.Path)) continue;

					string what = first.Path.Writes && second.Path.Writes
						? "Both mods write it, and the later command of the two wins."
						: "One mod writes it and the other mod reads it. Load order decides what the " +
							"read sees.";

					conflicts.Add(new ModConflict(EditKey.Container(first.Path.Written),
						left.Variant, first.Edit.Text, right.Variant, second.Edit.Text,
						ConflictKind.Filesystem, Certainty(first.Edit, second.Edit),
						first.Edit, second.Edit,
						$"The command \"{first.Edit.Verb}\" at {first.Edit.Where} and the command " +
						$"\"{second.Edit.Verb}\" at {second.Edit.Where} name one path. {what}"));
				}
			}
		}

		/// <summary>True when the command brings the thing into existence.</summary>
		private static bool Creates(ResolvedEdit edit)
		{
			return edit.Category == CommandCategory.ExistenceChange && !edit.Removes && !edit.Opaque;
		}

		private static ConflictKind Kind(ResolvedEdit first, ResolvedEdit second)
		{
			if (first.Category == CommandCategory.ExistenceChange
				|| second.Category == CommandCategory.ExistenceChange)
			{
				return ConflictKind.Existence;
			}

			return ConflictKind.FieldValue;
		}

		private static ConflictCertainty Certainty(ResolvedEdit first, ResolvedEdit second)
		{
			return first.Conditional || second.Conditional
				? ConflictCertainty.Possible
				: ConflictCertainty.Certain;
		}

		private static string Existence(ResolvedEdit first, ResolvedEdit second)
		{
			if (first.Removes && second.Removes)
			{
				return "Both mods remove it. Manager.Remove rejects a missing name, so the second " +
					"command fails and the deploy stops.";
			}

			ResolvedEdit removal = first.Removes ? first : second;
			ResolvedEdit other = first.Removes ? second : first;

			return $"The command at {removal.Where} removes it and the command at {other.Where} " +
				"needs it. The later command of the two fails.";
		}

		/// <summary>
		/// Compares two paths. A resolved path is the real target and the tool compares those.
		/// With no staging directory the tool compares the text that the script wrote.
		/// </summary>
		private static bool SamePath(PathEffect left, PathEffect right)
		{
			if (left.Anchor != right.Anchor) return false;

			if (left.Resolved != null && right.Resolved != null)
			{
				return String.Equals(left.Resolved, right.Resolved, StringComparison.OrdinalIgnoreCase);
			}

			return PathKey.Same(left.Written, right.Written);
		}

		/// <summary>
		/// Compares two values as text, without case.
		///
		/// Never parse and compare as numbers. The scripts carry values such as
		/// -0.19500002, and a parse and format round trip changes the text that we would
		/// then write back.
		/// </summary>
		private static bool SameValue(string left, string right)
		{
			return String.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
		}
	}
}
