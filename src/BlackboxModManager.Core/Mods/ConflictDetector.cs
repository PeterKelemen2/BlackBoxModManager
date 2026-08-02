using System;
using System.Collections.Generic;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Two enabled variants write different values to one field.
	/// </summary>
	public sealed class ModConflict
	{
		public EditKey Key { get; }

		public string LeftVariant { get; }

		public string LeftValue { get; }

		public string RightVariant { get; }

		public string RightValue { get; }

		public ModConflict(EditKey key, string leftVariant, string leftValue, string rightVariant, string rightValue)
		{
			this.Key = key;
			this.LeftVariant = leftVariant;
			this.LeftValue = leftValue;
			this.RightVariant = rightVariant;
			this.RightValue = rightValue;
		}

		public override string ToString() =>
			$"{this.Key}: \"{this.LeftVariant}\" writes {this.LeftValue} and \"{this.RightVariant}\" writes {this.RightValue}";
	}

	/// <summary>
	/// Finds the fields that two or more enabled variants disagree about.
	///
	/// The rule is one line long. Same key and same value is fine. Same key and a different
	/// value is a conflict.
	///
	/// The same-value case is not a corner case. The ALL variant of the 1 Lap mod is the
	/// union of the other four, so a user can legitimately enable ALL and URL together.
	/// Every shared key then carries the same value, and reporting those would make the
	/// tool useless.
	///
	/// Step 6 owns what the application does with the result. This class only reports.
	/// </summary>
	public static class ConflictDetector
	{
		/// <summary>
		/// Compares the keyed edits of every resolved variant. A later variant that repeats
		/// a key with a different value produces one conflict against the first writer.
		/// </summary>
		public static IReadOnlyList<ModConflict> Find(IEnumerable<ResolvedScript> scripts)
		{
			if (scripts is null) throw new ArgumentNullException(nameof(scripts));

			var conflicts = new List<ModConflict>();
			var writers = new Dictionary<EditKey, (string Variant, string Value)>();

			foreach (ResolvedScript script in scripts)
			{
				// One variant that writes the same key twice is not a conflict with itself.
				// The last write wins, which is what the script author asked for.
				var seen = new Dictionary<EditKey, string>();

				foreach (ResolvedEdit edit in script.KeyedEdits) seen[edit.Key] = edit.Value;

				foreach (KeyValuePair<EditKey, string> entry in seen)
				{
					if (!writers.TryGetValue(entry.Key, out (string Variant, string Value) first))
					{
						writers[entry.Key] = (script.Variant, entry.Value);
						continue;
					}

					if (SameValue(first.Value, entry.Value)) continue;

					conflicts.Add(new ModConflict(entry.Key, first.Variant, first.Value, script.Variant, entry.Value));
				}
			}

			return conflicts;
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
