using System;
using System.Collections.Generic;
using Endscript.Commands;
using Endscript.Interfaces;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Thrown when a stored selection cannot become a valid Choice.
	///
	/// Defect 5: an out-of-range Choice inside ProcessScript surfaces as "Unable to find
	/// end to a selectable statement". That message names neither the file nor the real
	/// problem. This exception names the mod, the script, the line, and the option set.
	/// </summary>
	public sealed class ModSelectionException : Exception
	{
		public string Variant { get; }

		public ModSelectionException(string message, string variant) : base(message)
		{
			this.Variant = variant;
		}
	}

	/// <summary>
	/// One assumption that the resolver made. A missing answer is not an error, and the
	/// user still has to be able to see what the tool decided.
	/// </summary>
	public sealed class ResolverNote
	{
		public int Ordinal { get; }

		public string Description { get; }

		public string ChosenOption { get; }

		public string Reason { get; }

		public ResolverNote(int ordinal, string description, string chosenOption, string reason)
		{
			this.Ordinal = ordinal;
			this.Description = description;
			this.ChosenOption = chosenOption;
			this.Reason = reason;
		}

		public override string ToString() => $"Question {this.Ordinal} \"{this.Description}\": {this.Reason}";
	}

	/// <summary>
	/// Answers the option pauses of one variant from stored selections, with no user
	/// present.
	///
	/// A deploy must never block on a prompt. When no selection is stored, this takes the
	/// first option and records the assumption.
	/// </summary>
	public sealed class SelectionResolver
	{
		private readonly ModVariant _variant;
		private readonly VariantSelection _selection;
		private readonly List<ResolverNote> _notes = new List<ResolverNote>();

		public IReadOnlyList<ResolverNote> Notes => this._notes;

		public SelectionResolver(ModVariant variant, VariantSelection selection)
		{
			this._variant = variant ?? throw new ArgumentNullException(nameof(variant));
			this._selection = selection;
		}

		public SelectionResolver(ModVariant variant, ModSelections selections)
			: this(variant, selections?.For(variant?.Name)) { }

		/// <summary>
		/// Returns the Choice for one pause. Pass the pauses in the order that
		/// ProcessScript reports them, starting at zero.
		///
		/// Validate the result against Options.Length before assigning it. This method
		/// already does that and throws when it fails, so the caller can assign the answer
		/// with no further check.
		/// </summary>
		public int Resolve(ISelectable selectable, int ordinal)
		{
			if (selectable is null) throw new ArgumentNullException(nameof(selectable));

			string where = Where(selectable);
			int count = selectable.Options.Length;

			if (count == 0)
			{
				throw new ModSelectionException(
					$"The mod \"{this._variant.Name}\" asks a question at {where} that offers no option.",
					this._variant.Name);
			}

			string stored = this._selection?.Answer(ordinal);

			if (String.IsNullOrEmpty(stored))
			{
				// No answer. Take the first option and say so. Never block.
				this._notes.Add(new ResolverNote(ordinal, selectable.Description, selectable.Options[0].Name,
					"No selection is stored. The first option applies."));

				return 0;
			}

			int index = selectable.ParseOption(stored);

			if (index < 0)
			{
				throw new ModSelectionException(
					$"The mod \"{this._variant.Name}\" has no option named \"{stored}\" at {where}. " +
					$"The question is \"{selectable.Description}\". The options are {Names(selectable)}. " +
					"An update of the mod can rename or remove an option. Choose again.",
					this._variant.Name);
			}

			if (index >= count)
			{
				// ParseOption should never return this. Check anyway, because the value goes
				// straight into an array index inside ProcessScript.
				throw new ModSelectionException(
					$"The mod \"{this._variant.Name}\" resolved \"{stored}\" to index {index} at {where}, " +
					$"and the question offers {count} options.",
					this._variant.Name);
			}

			return index;
		}

		private string Where(ISelectable selectable)
		{
			if (selectable is BaseCommand command && !String.IsNullOrEmpty(command.Filename))
			{
				return $"{command.Filename} line {command.Index}";
			}

			return "an unknown place in the script";
		}

		private static string Names(ISelectable selectable)
		{
			var names = new List<string>(selectable.Options.Length);

			foreach (Endscript.Helpers.OptionState state in selectable.Options) names.Add($"\"{state.Name}\"");

			return String.Join(", ", names);
		}
	}
}
