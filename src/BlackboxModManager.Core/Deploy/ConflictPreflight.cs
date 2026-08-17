using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// One thing that two enabled variants disagree about, and who wins.
	/// </summary>
	public sealed class ConflictEntry
	{
		public ModConflict Conflict { get; }

		/// <summary>
		/// The variant whose value the game gets. <b>Load order decides this.</b> Every mod
		/// applies to one loaded profile in order, so the last write wins. There is no
		/// second resolution mechanism, and there must not be one.
		///
		/// This holds for a field write. It does not hold for every category. An existence
		/// conflict makes the second command fail, so neither mod wins. Read
		/// <c>Conflict.Kind</c> before you show a winner.
		/// </summary>
		public string Winner => this.Conflict.RightVariant;

		public string Loser => this.Conflict.LeftVariant;

		/// <summary>
		/// True when load order alone settles the conflict and the deploy still runs. False
		/// when the conflict makes a command fail.
		/// </summary>
		public bool LoadOrderDecides => this.Conflict.Kind == ConflictKind.FieldValue;

		public ConflictEntry(ModConflict conflict)
		{
			this.Conflict = conflict;
		}

		public override string ToString()
		{
			if (this.LoadOrderDecides)
			{
				return $"{this.Conflict.Key}: \"{this.Winner}\" wins with {this.Conflict.RightValue}, " +
					$"and \"{this.Loser}\" loses with {this.Conflict.LeftValue}.";
			}

			return $"{this.Conflict.Kind} {this.Conflict.Key}: \"{this.Loser}\" and \"{this.Winner}\". " +
				this.Conflict.Reason;
		}
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
		/// The variants that the check could not read, with the reason. A script with an
		/// unknown verb lands here, and so does a script that the parser rejects.
		/// </summary>
		public IReadOnlyList<string> Unchecked { get; }

		/// <summary>
		/// The commands that the check cannot compare against another mod, with the reason.
		/// A warning does not stop the deploy. It tells the user that the conflict list does
		/// not cover that command.
		/// </summary>
		public IReadOnlyList<string> Warnings { get; }

		/// <summary>
		/// The commands that this application refuses to run. <b>Each one stops the deploy.</b>
		/// The container engine tests the same rule again before it writes.
		/// </summary>
		public IReadOnlyList<string> Rejections { get; }

		/// <summary>
		/// The paths that leave the staging copy and the mod directory. <b>Each one stops the
		/// deploy.</b> A write outside staging reaches the real system, and the revert never
		/// sees it.
		/// </summary>
		public IReadOnlyList<string> Escapes { get; }

		/// <summary>
		/// The variants that hold an <c>if</c> command. The check walked both branches of
		/// each one, so a conflict against a conditional edit is possible and not certain.
		/// </summary>
		public IReadOnlyList<string> Approximate { get; }

		public int CheckedVariants { get; }

		public int KeyedEdits { get; }

		/// <summary>
		/// True when the check found no disagreement. A warning does not change this, and a
		/// rejection does not either. Read <see cref="CanDeploy"/> for that.
		/// </summary>
		public bool IsClean => this.Conflicts.Count == 0;

		/// <summary>False when a rejected command or an escaped path stops the deploy.</summary>
		public bool CanDeploy => this.Rejections.Count == 0 && this.Escapes.Count == 0;

		public ConflictReport(IReadOnlyList<ConflictEntry> conflicts, IReadOnlyList<string> unchecked_,
			int checkedVariants, int keyedEdits, IReadOnlyList<string> warnings = null,
			IReadOnlyList<string> rejections = null, IReadOnlyList<string> escapes = null,
			IReadOnlyList<string> approximate = null)
		{
			this.Conflicts = conflicts ?? Array.Empty<ConflictEntry>();
			this.Unchecked = unchecked_ ?? Array.Empty<string>();
			this.Warnings = warnings ?? Array.Empty<string>();
			this.Rejections = rejections ?? Array.Empty<string>();
			this.Escapes = escapes ?? Array.Empty<string>();
			this.Approximate = approximate ?? Array.Empty<string>();
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

			var tail = new List<string> { head };

			if (this.Unchecked.Count > 0) tail.Add($"It could not read {this.Unchecked.Count} variants.");
			if (this.Warnings.Count > 0) tail.Add($"It cannot compare {this.Warnings.Count} commands.");
			if (this.Rejections.Count > 0) tail.Add($"It refuses {this.Rejections.Count} commands.");
			if (this.Escapes.Count > 0) tail.Add($"It found {this.Escapes.Count} paths outside staging.");
			if (this.Approximate.Count > 0) tail.Add($"{this.Approximate.Count} variants use an 'if' command.");

			return String.Join(" ", tail);
		}
	}

	/// <summary>
	/// Reports what the enabled variants disagree about, before anything writes.
	///
	/// <b>A field conflict never blocks a deploy.</b> Load order already decides every
	/// collision, and the result is what the user asked for. The check exists so that the user
	/// can see the decision and reorder the mods when the winner is wrong.
	///
	/// <b>A rejected command and an escaped path do block a deploy.</b> Those are not
	/// disagreements between mods. Read <c>CanDeploy</c>.
	///
	/// A variant that the check cannot read is not an error either. The deploy still applies
	/// it, because the container engine resolves what a static walk cannot.
	/// </summary>
	public static class ConflictPreflight
	{
		/// <summary>
		/// Runs the check. Pass the staging directory so that the check can resolve every
		/// path of a filesystem command. With no staging directory the check reports no
		/// escaped path, and that is not a pass.
		///
		/// Pass the cache of a deploy so that the command gate and this check share one resolve
		/// of every script. With no cache this method builds its own.
		/// </summary>
		public static ConflictReport Run(IReadOnlyList<EnabledVariant> variants,
			string stagingDirectory = null, Action<string> log = null,
			ScriptResolutionCache cache = null)
		{
			if (variants is null) throw new ArgumentNullException(nameof(variants));

			Action<string> write = log ?? (line => { });
			ScriptResolutionCache resolver = cache ?? new ScriptResolutionCache(stagingDirectory);

			var scripts = new List<ResolvedScript>();
			var unchecked_ = new List<string>();
			var warnings = new List<string>();
			var rejections = new List<string>();
			var escapes = new List<string>();
			var approximate = new List<string>();
			int keyed = 0;

			foreach (EnabledVariant variant in variants)
			{
				try
				{
					ResolvedScript resolved = resolver.Resolve(variant);

					// Carry the label of the mod, not the bare variant name. Two mods can
					// hold a variant of one name, and a conflict line has to say which mod.
					scripts.Add(new ResolvedScript(
						variant.Label, resolved.Edits, resolved.Answers, resolved.Notes,
						resolved.Warnings, resolved.IsApproximate));

					foreach (ResolvedEdit edit in resolved.KeyedEdits) ++keyed;

					foreach (ScriptWarning warning in resolved.Warnings)
					{
						warnings.Add($"{variant.Label}: {warning}");
					}

					foreach (ResolvedEdit edit in resolved.Rejected)
					{
						rejections.Add($"{variant.Label}: {edit.Where}: this application does not run " +
							$"the command \"{edit.Verb}\". {edit.Facts.Note} ({edit.Text})");
					}

					foreach ((ResolvedEdit Edit, PathEffect Path) escape in resolved.Escapes())
					{
						escapes.Add($"{variant.Label}: {escape.Edit.Where}: the command " +
							$"\"{escape.Edit.Verb}\" leaves the staging copy. {escape.Path.Violation}");
					}

					if (resolved.IsApproximate) approximate.Add(variant.Label);
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

			var report = new ConflictReport(entries, unchecked_, scripts.Count, keyed,
				warnings, rejections, escapes, approximate);

			write(report.Summary());

			foreach (ConflictEntry entry in report.Conflicts) write($"  conflict: {entry}");
			foreach (string line in report.Rejections) write($"  refused: {line}");
			foreach (string line in report.Escapes) write($"  outside staging: {line}");
			foreach (string line in report.Warnings) write($"  warning: {line}");
			foreach (string line in report.Unchecked) write($"  not checked: {line}");
			foreach (string line in report.Approximate) write($"  both branches walked: {line}");

			return report;
		}
	}
}
