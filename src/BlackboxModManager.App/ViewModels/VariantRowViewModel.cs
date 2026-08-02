using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BlackboxModManager.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlackboxModManager.App.ViewModels
{
	/// <summary>
	/// One question of one variant.
	///
	/// A combobox shows a single-select control with the description of the script as the
	/// caption. A checkbox shows a toggle. The two names of a checkbox are fixed by the
	/// library, so the toggle maps to them and the user never sees the words.
	/// </summary>
	public sealed class OptionSetViewModel : ObservableObject
	{
		/// <summary>The name that the library gives the off state of a checkbox.</summary>
		public const string Disabled = "disabled";

		/// <summary>The name that the library gives the on state of a checkbox.</summary>
		public const string Enabled = "enabled";

		private readonly VariantSelection _selection;
		private readonly ModOptionSet _set;
		private readonly Action _changed;

		public string Description => this._set.Description;

		public int Ordinal => this._set.Ordinal;

		public bool IsCombobox => this._set.Kind == ModOptionKind.Combobox;

		public bool IsCheckbox => this._set.Kind == ModOptionKind.Checkbox;

		/// <summary>The option names, in the order that the script lists them.</summary>
		public IReadOnlyList<string> Options { get; }

		/// <summary>Where the question sits, for a message that a user can act on.</summary>
		public string Source => $"{this._set.SourceFile} line {this._set.SourceLine}";

		public OptionSetViewModel(ModOptionSet set, VariantSelection selection, Action changed)
		{
			this._set = set ?? throw new ArgumentNullException(nameof(set));
			this._selection = selection ?? throw new ArgumentNullException(nameof(selection));
			this._changed = changed ?? throw new ArgumentNullException(nameof(changed));

			var names = new List<string>(set.Options.Count);
			foreach (ModOption option in set.Options) names.Add(option.Name);
			this.Options = names;
		}

		/// <summary>
		/// The chosen option name. A profile stores the name and never the index, because a
		/// mod update that reorders the options would move a stored index to another branch
		/// with no error.
		/// </summary>
		public string SelectedOption
		{
			get
			{
				string stored = this._selection.Answer(this._set.Ordinal);

				if (stored != null && this._set.Find(stored) != null) return stored;

				// No answer yet. Show what a deploy would apply, which is the first option.
				return this.Options.Count > 0 ? this.Options[0] : null;
			}
			set
			{
				if (String.IsNullOrEmpty(value)) return;
				if (this.SelectedOption == value) return;

				this._selection.Choose(this._set.Ordinal, value);
				this.OnPropertyChanged();
				this.OnPropertyChanged(nameof(this.IsChecked));
				this._changed();
			}
		}

		/// <summary>
		/// The state of a checkbox question. The library names the two blocks disabled and
		/// enabled, and the script must use those words.
		/// </summary>
		public bool IsChecked
		{
			get => String.Equals(this.SelectedOption, Enabled, StringComparison.OrdinalIgnoreCase);
			set => this.SelectedOption = value ? Enabled : Disabled;
		}
	}

	/// <summary>
	/// One variant of a Binary mod.
	///
	/// The user switches any number of variants of one mod on. That is a multiple selection,
	/// and it is a different mechanism from the single selection inside a question.
	/// </summary>
	public sealed class VariantRowViewModel : ObservableObject
	{
		private readonly VariantSelection _selection;
		private readonly ModVariant _variant;
		private readonly Action _changed;

		public string Name => this._variant.Name;

		public bool IsInstallable => this._variant.IsInstallable;

		/// <summary>Why the variant cannot install. This is empty when it can.</summary>
		public string Problem => this._variant.Problem;

		public bool HasProblem => !this.IsInstallable;

		public ObservableCollection<OptionSetViewModel> Questions { get; } =
			new ObservableCollection<OptionSetViewModel>();

		public bool HasQuestions => this.Questions.Count > 0;

		public VariantRowViewModel(ModVariant variant, VariantSelection selection, Action changed)
		{
			this._variant = variant ?? throw new ArgumentNullException(nameof(variant));
			this._selection = selection ?? throw new ArgumentNullException(nameof(selection));
			this._changed = changed ?? throw new ArgumentNullException(nameof(changed));

			foreach (ModOptionSet set in variant.OptionSets)
			{
				this.Questions.Add(new OptionSetViewModel(set, selection, changed));
			}
		}

		public bool Enabled
		{
			get => this._selection.Enabled;
			set
			{
				if (this._selection.Enabled == value) return;

				this._selection.Enabled = value;
				this.OnPropertyChanged();
				this._changed();
			}
		}
	}
}
