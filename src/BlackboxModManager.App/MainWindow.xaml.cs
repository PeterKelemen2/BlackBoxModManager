using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using BlackboxModManager.App.Services;
using BlackboxModManager.App.ViewModels;

namespace BlackboxModManager.App
{
	/// <summary>
	/// The window. It owns the dialogs and it holds no logic of its own.
	/// </summary>
	public partial class MainWindow : Window, IUserInteraction
	{
		private readonly MainViewModel _model;

		public MainWindow()
		{
			this.InitializeComponent();

			this._model = new MainViewModel(this);
			this.DataContext = this._model;

			// Keep the last log line in view. A deploy writes while the user watches.
			((INotifyCollectionChanged)this._model.Log).CollectionChanged += this.OnLogChanged;
		}

		private bool _scrollQueued;

		/// <summary>
		/// Asks for a scroll to the end of the log, once, after the list settles.
		///
		/// <b>Never scroll from inside this handler.</b> ScrollIntoView lays the list out at
		/// once, and that makes the item container generator run while the collection change
		/// is still in flight. The generator then counts one event fewer than the list holds,
		/// and it throws "An ItemsControl is inconsistent with its items source".
		///
		/// The failure is a race, so it appears on one machine and not on another. Post the
		/// scroll at Background priority instead. The list has finished with the change by
		/// the time the scroll runs.
		/// </summary>
		private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			// One scroll covers a burst of lines. A deploy writes many.
			if (this._scrollQueued) return;

			this._scrollQueued = true;
			this.Dispatcher.BeginInvoke(new Action(this.ScrollLogToEnd), DispatcherPriority.Background);
		}

		private void ScrollLogToEnd()
		{
			this._scrollQueued = false;

			try
			{
				int count = this.LogList.Items.Count;

				if (count > 0) this.LogList.ScrollIntoView(this.LogList.Items[count - 1]);
			}
			catch (Exception)
			{
				// A scroll is cosmetic. It never fails an operation, and it never closes the
				// window.
			}
		}

		// ---------------------------------------------------------------- IUserInteraction

		public string PickDirectory(string title, string start = null) =>
			Dialogs.PickDirectory(this, title, start);

		public string PickFile(string title, string filter) => Dialogs.PickFile(this, title, filter);

		public string AskText(string question, string value = null) => Dialogs.AskText(this, question, value);

		public string PickChoice(string question,
			System.Collections.Generic.IReadOnlyList<Views.UserChoice> choices, string current = null) =>
			Dialogs.PickChoice(this, question, choices, current);

		public void ShowFolders(System.Collections.Generic.IReadOnlyList<Views.FolderRow> folders) =>
			Dialogs.ShowFolders(this, folders);

		public bool Confirm(string question) => Dialogs.Confirm(this, question);

		public void ShowError(string message) => Dialogs.ShowError(this, message);

		public void ShowMessage(string message) => Dialogs.ShowMessage(this, message);
	}
}
