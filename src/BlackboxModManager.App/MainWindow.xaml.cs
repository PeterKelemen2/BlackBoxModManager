using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

		// ---------------------------------------------------------------------- The mod list
		//
		// The DataGrid of step 9 became a row list in step 10. A drag reorders the load
		// order. See docs/roadmap/10-dark-theme.md, Part D. The row template lives in
		// Theme/Parts.xaml as a shared resource with no code-behind, so every handler below
		// attaches to the ItemsControl itself. PreviewMouseLeftButtonDown and
		// PreviewMouseMove tunnel from the window down to the row, and DragOver, Drop, and
		// DragLeave bubble from the row back up, so the ItemsControl sees every one of them.

		private Point? _dragStartPoint;
		private ModRowViewModel _dragCandidate;

		/// <summary>The row whose template root, named "RowBorder", contains the source of the event.</summary>
		private static FrameworkElement FindRowElement(DependencyObject source)
		{
			while (source != null)
			{
				if (source is FrameworkElement element && element.Name == "RowBorder") return element;

				source = VisualTreeHelper.GetParent(source);
			}

			return null;
		}

		private void ModList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			FrameworkElement element = FindRowElement(e.OriginalSource as DependencyObject);
			if (element?.DataContext is not ModRowViewModel row) return;

			this._dragStartPoint = e.GetPosition(null);
			this._dragCandidate = row;

			this._model.SelectedMod = row;
		}

		private void ModList_PreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Pressed) return;
			if (this._dragStartPoint is not Point start || this._dragCandidate is not ModRowViewModel row) return;

			Point current = e.GetPosition(null);

			// A drag that starts on the first pixel breaks every click.
			if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
				Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

			this._dragStartPoint = null;
			this._dragCandidate = null;

			row.IsDragSource = true;

			try
			{
				DragDrop.DoDragDrop((DependencyObject)sender, row.Id, DragDropEffects.Move);
			}
			finally
			{
				row.IsDragSource = false;
				this.ClearDropMarkers();
			}
		}

		private void ModList_DragOver(object sender, DragEventArgs e)
		{
			FrameworkElement element = FindRowElement(e.OriginalSource as DependencyObject);

			this.ClearDropMarkers();

			if (element?.DataContext is ModRowViewModel row)
			{
				double middle = element.ActualHeight / 2;
				double y = e.GetPosition(element).Y;

				if (y < middle) row.DropBefore = true;
				else row.DropAfter = true;
			}

			e.Effects = DragDropEffects.Move;
			e.Handled = true;
		}

		private void ModList_DragLeave(object sender, DragEventArgs e) => this.ClearDropMarkers();

		private void ModList_Drop(object sender, DragEventArgs e)
		{
			FrameworkElement element = FindRowElement(e.OriginalSource as DependencyObject);
			ModRowViewModel target = element?.DataContext as ModRowViewModel;
			bool before = target?.DropBefore ?? false;

			this.ClearDropMarkers();

			if (target is null || !e.Data.GetDataPresent(typeof(string))) return;

			string modId = (string)e.Data.GetData(typeof(string));
			int index = target.Order - 1 + (before ? 0 : 1);

			this._model.MoveModTo(modId, index);
			e.Handled = true;
		}

		private void ClearDropMarkers()
		{
			foreach (ModRowViewModel row in this._model.Mods)
			{
				row.DropBefore = false;
				row.DropAfter = false;
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
