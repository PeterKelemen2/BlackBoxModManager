using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.App.ViewModels
{
	/// <summary>
	/// One import that still runs. The mod list shows this row under the mods.
	///
	/// The row appears when the import starts, and it goes away when the import ends. The
	/// window drops it in both cases, so a failed import leaves no row behind.
	///
	/// A mod that the store holds is a <see cref="ModRowViewModel"/> and never this one. The
	/// two rows share no base class on purpose. This row carries no mod, no profile entry,
	/// and no load order, so the drag and the toggle of a mod row cannot reach it.
	/// </summary>
	public sealed class ImportRowViewModel : ObservableObject
	{
		/// <summary>
		/// The name that the mod takes. <c>ModImporter</c> names a mod after its archive with
		/// no extension, so this row shows the same name before the mod exists.
		/// </summary>
		public string Name { get; }

		public ImportRowViewModel(string source)
		{
			if (String.IsNullOrWhiteSpace(source)) throw new ArgumentException("The source is empty.", nameof(source));

			string trimmed = Path.TrimEndingDirectorySeparator(source);

			this.Name = Directory.Exists(trimmed)
				? Path.GetFileName(trimmed)
				: Path.GetFileNameWithoutExtension(trimmed);
		}

		private ImportStage _stage = ImportStage.Unpack;
		private int _done;
		private int _total;
		private string _detail = String.Empty;

		/// <summary>What the import does now, with the count and the file that it reads.</summary>
		public string Line
		{
			get
			{
				string stage = StageText(this._stage);

				if (this._total > 0) stage += $" · {this._done} of {this._total} files";
				if (this._detail.Length > 0) stage += $" · {this._detail}";

				return stage;
			}
		}

		/// <summary>How much of the step is done, from 0 to 100.</summary>
		public double Percent => this._total > 0 ? this._done * 100.0 / this._total : 0;

		/// <summary>
		/// True while the step counts its files. A step that cannot count draws no bar,
		/// because a bar at zero reads as a stopped import.
		/// </summary>
		public bool HasBar => this._total > 0;

		/// <summary>Takes one report of the importer. Call this on the window thread.</summary>
		public void Apply(ImportProgress progress)
		{
			if (progress is null) return;

			this._stage = progress.Stage;
			this._done = progress.Done;
			this._total = progress.Total;
			this._detail = progress.Detail;

			this.OnPropertyChanged(nameof(this.Line));
			this.OnPropertyChanged(nameof(this.Percent));
			this.OnPropertyChanged(nameof(this.HasBar));
		}

		/// <summary>The name of a step, for a person to read.</summary>
		public static string StageText(ImportStage stage)
		{
			switch (stage)
			{
				case ImportStage.Unpack: return "Unpack";
				case ImportStage.Inspect: return "Read the files";
				case ImportStage.Store: return "Move into the store";
				default: return stage.ToString();
			}
		}
	}
}
