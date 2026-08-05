using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlackboxModManager.App.ViewModels
{
	/// <summary>
	/// One option of one settings file.
	///
	/// The editor comes from the value alone. <b>The row never reads the comment to build an
	/// editor.</b> A comment such as <c>(1 = Cropped | 2 = Stretched)</c> reads like a list of
	/// choices, and a mod author writes that text any way they want.
	///
	/// <b>Every editor is a guess, and a wrong guess must not trap the user.</b>
	/// <c>FPSLimit = -1</c> looks like a number and means "the refresh rate of the monitor".
	/// <c>ImproveGamepadSupport = 0</c> looks like a check box and holds five states. So
	/// <see cref="FreeText"/> switches any row to a plain text box.
	/// </summary>
	public sealed partial class SettingsRowViewModel : ObservableObject
	{
		private readonly ProfileEntry _entry;
		private readonly string _file;
		private readonly IniEntry _option;
		private readonly Action _changed;

		private bool _freeText;

		public string Key => this._option.Key.Key;

		/// <summary>The text form of the key, <c>SECTION/Key</c>. The profile stores this.</summary>
		public string StoredKey => this._option.Key.ToString();

		/// <summary>The comment of the line. The question mark marker shows this.</summary>
		public string Comment => this._option.Comment;

		/// <summary>True when the key carries a trailing comment. A key with none shows no marker.</summary>
		public bool HasComment => this._option.Comment.Length > 0;

		/// <summary>The value that the mod shipped.</summary>
		public string Original => this._option.Value;

		/// <summary>True when a second line of the file repeats this key.</summary>
		public bool IsDuplicate => this._option.IsDuplicate;

		public string Where => $"line {this._option.LineNumber}";

		public SettingsRowViewModel(ProfileEntry entry, string file, IniEntry option, Action changed)
		{
			this._entry = entry ?? throw new ArgumentNullException(nameof(entry));
			this._file = file ?? throw new ArgumentNullException(nameof(file));
			this._option = option ?? throw new ArgumentNullException(nameof(option));
			this._changed = changed ?? throw new ArgumentNullException(nameof(changed));
		}

		/// <summary>
		/// The value that a deploy writes. This is the answer of the profile when one exists,
		/// and the value of the file otherwise.
		/// </summary>
		public string Value
		{
			get
			{
				IReadOnlyDictionary<string, string> answers = this._entry.IniFor(this._file);

				return answers.TryGetValue(this.StoredKey, out string stored) ? stored : this.Original;
			}
			set
			{
				string clean = value ?? String.Empty;

				if (this.Value == clean) return;

				// An answer that matches the file leaves the profile. The deployed file then
				// matches the mod store byte for byte again.
				this._entry.SetIni(this._file, this.StoredKey, clean, this.Original);

				this.OnPropertyChanged();
				this.OnPropertyChanged(nameof(this.IsChecked));
				this.OnPropertyChanged(nameof(this.IsChanged));
				this._changed();
			}
		}

		/// <summary>True when the profile answers this option.</summary>
		public bool IsChanged => this.Value != this.Original;

		/// <summary>The state of a check box row.</summary>
		public bool IsChecked
		{
			get => IniValue.IsOn(this.Value);
			set => this.Value = IniValue.FromFlag(value);
		}

		/// <summary>
		/// The way back to free text entry. The window shows a text box for a row that has
		/// this set, whatever the value looks like.
		/// </summary>
		public bool FreeText
		{
			get => this._freeText;
			set
			{
				if (this._freeText == value) return;

				this._freeText = value;

				this.OnPropertyChanged();
				this.OnPropertyChanged(nameof(this.IsFlag));
				this.OnPropertyChanged(nameof(this.IsNumber));
				this.OnPropertyChanged(nameof(this.IsText));
			}
		}

		private IniValueKind Kind => this._freeText ? IniValueKind.Text : IniValue.Classify(this.Value);

		public bool IsFlag => this.Kind == IniValueKind.Flag;

		public bool IsNumber => this.Kind == IniValueKind.Integer || this.Kind == IniValueKind.Decimal;

		public bool IsText => this.Kind == IniValueKind.Text;

		/// <summary>Puts the value of the mod back.</summary>
		[RelayCommand]
		private void Reset() => this.Value = this.Original;

		public override string ToString() => $"{this.StoredKey} = {this.Value}";
	}

	/// <summary>
	/// One section of one settings file. The window shows one group per section.
	/// </summary>
	public sealed class SettingsSectionViewModel
	{
		/// <summary>The name in the brackets, or a caption for the keys above the first one.</summary>
		public string Name { get; }

		public ObservableCollection<SettingsRowViewModel> Rows { get; } =
			new ObservableCollection<SettingsRowViewModel>();

		public SettingsSectionViewModel(string name)
		{
			this.Name = name;
		}
	}

	/// <summary>
	/// One settings file of one mod.
	/// </summary>
	public sealed class SettingsFileViewModel
	{
		/// <summary>The file name. The window shows this as the heading.</summary>
		public string Name { get; }

		/// <summary>
		/// One line that says which plugin this file configures, or that it configures none.
		///
		/// <b>An unmatched file gets its own heading and no owner.</b> The Widescreen Fix ships
		/// a <c>.dat</c> file beside the plugin, and a mod can ship an <c>.ini</c> that belongs
		/// to nothing that this application knows.
		/// </summary>
		public string Owner { get; }

		public ObservableCollection<SettingsSectionViewModel> Sections { get; } =
			new ObservableCollection<SettingsSectionViewModel>();

		/// <summary>What the reader could not make sense of. This is empty for a clean file.</summary>
		public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();

		public bool HasWarnings => this.Warnings.Count > 0;

		public SettingsFileViewModel(string name, string owner)
		{
			this.Name = name;
			this.Owner = owner;
		}

		/// <summary>
		/// Builds the panel of one settings file. A section with no key produces no group.
		/// </summary>
		public static SettingsFileViewModel Build(AsiSettingsFile file, ProfileEntry entry, Action changed)
		{
			if (file is null) throw new ArgumentNullException(nameof(file));
			if (entry is null) throw new ArgumentNullException(nameof(entry));

			string owner = file.HasPlugin
				? $"The settings of {System.IO.Path.GetFileName(file.PluginPath)}."
				: "This application found no plugin with a matching name. The file may belong to " +
					"something else.";

			var model = new SettingsFileViewModel(file.Name, owner);

			if (!file.IsReadable)
			{
				model.Warnings.Add(file.Problem);
				return model;
			}

			foreach (IniSection section in file.Document.Sections)
			{
				if (section.Entries.Count == 0) continue;

				var group = new SettingsSectionViewModel(section.IsUnnamed
					? "Keys above the first section"
					: section.Name);

				foreach (IniEntry option in section.Entries)
				{
					// A duplicated key is one option with two lines. The deploy edits the first
					// line, so show that one and leave the later lines out of the panel.
					if (option.IsDuplicate) continue;

					group.Rows.Add(new SettingsRowViewModel(entry, file.RelativePath, option, changed));
				}

				if (group.Rows.Count > 0) model.Sections.Add(group);
			}

			foreach (string warning in file.Document.Warnings) model.Warnings.Add(warning);

			return model;
		}
	}
}
