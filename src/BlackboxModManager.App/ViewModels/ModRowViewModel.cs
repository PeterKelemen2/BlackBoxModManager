using System;
using CommunityToolkit.Mvvm.ComponentModel;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.App.ViewModels
{
	/// <summary>
	/// One row of the mod list.
	///
	/// The row holds the mod and the profile entry that goes with it. A change to Enabled
	/// writes into the profile and calls back, so that the window can save the profile.
	/// </summary>
	public sealed class ModRowViewModel : ObservableObject
	{
		private readonly ProfileEntry _entry;
		private readonly Action _changed;

		public InstalledMod Mod { get; }

		public string Id => this.Mod.Id;

		public string Name => this.Mod.Name;

		public string Kind => this.Mod.Kind.ToString();

		public int FileCount => this.Mod.Manifest.FileCount;

		/// <summary>
		/// The game of the mod. "Any game" means that the metadata names none, and the mod
		/// then shows under every game. The Set game button gives it one.
		/// </summary>
		public string GameName => this.Mod.Game?.ToString() ?? "Any game";

		/// <summary>The size of the mod, in a unit that a person reads.</summary>
		public string Size
		{
			get
			{
				long bytes = this.Mod.Manifest.TotalBytes;

				if (bytes < 1024) return $"{bytes} B";
				if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";

				return $"{bytes / (1024 * 1024)} MB";
			}
		}

		/// <summary>The position in the load order, from one. The window sets this.</summary>
		public int Order
		{
			get => this._order;
			set => this.SetProperty(ref this._order, value);
		}

		private int _order;

		public bool Enabled
		{
			get => this._entry.Enabled;
			set
			{
				if (this._entry.Enabled == value) return;

				this._entry.Enabled = value;
				this.OnPropertyChanged();
				this._changed();
			}
		}

		public ModRowViewModel(InstalledMod mod, ProfileEntry entry, int order, Action changed)
		{
			this.Mod = mod ?? throw new ArgumentNullException(nameof(mod));
			this._entry = entry ?? throw new ArgumentNullException(nameof(entry));
			this._changed = changed ?? throw new ArgumentNullException(nameof(changed));
			this._order = order;
		}
	}
}
