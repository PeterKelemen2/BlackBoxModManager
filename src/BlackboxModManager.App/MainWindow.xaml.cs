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
		// order. See docs/roadmap/11-ui-polish.md, Part B. The row template lives in
		// Theme/Parts.xaml as a shared resource with no code-behind, so every handler below
		// attaches to the ItemsControl itself. PreviewMouseLeftButtonDown and
		// PreviewMouseMove tunnel from the window down to the row, and DragOver, Drop, and
		// DragLeave bubble from the row back up, so the ItemsControl sees every one of them.
		//
		// Step 10 drew an insertion line and step 11 replaced it. The dragged row now moves
		// inside the collection to the place where it would land, and it draws as a ghost. The
		// line blinked because DragOver cleared every marker on each mouse move, and the gap
		// between two rows belongs to the panel and not to a row.
		//
		// The rule that replaced that: act on a hit, and never clear on a miss.

		private Point? _dragStartPoint;
		private ModRowViewModel _dragCandidate;
		private int _dragOriginalIndex = -1;

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

		/// <summary>True when the point landed on a button, a check box, or the toggle of a row.</summary>
		private static bool IsControlSurface(DependencyObject source)
		{
			while (source != null)
			{
				if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
				if (source is FrameworkElement element && element.Name == "RowBorder") return false;

				source = VisualTreeHelper.GetParent(source);
			}

			return false;
		}

		private void ModList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			var source = e.OriginalSource as DependencyObject;

			FrameworkElement element = FindRowElement(source);
			if (element?.DataContext is not ModRowViewModel row) return;

			// A press on the toggle of the row must never arm a drag. The handler sits on the
			// ItemsControl, so it sees every press inside a row, and a small movement of the
			// pointer would then steal the click.
			if (IsControlSurface(source)) return;

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
			this._dragOriginalIndex = this._model.Mods.IndexOf(row);

			if (this._dragOriginalIndex < 0) return;

			row.IsDragSource = true;

			try
			{
				DragDropEffects result =
					DragDrop.DoDragDrop((DependencyObject)sender, row.Id, DragDropEffects.Move);

				// None covers Escape and a drop outside the list. The preview then goes back and
				// the profile keeps the order that it holds on disk.
				if (result == DragDropEffects.None) this._model.CancelPreview(row, this._dragOriginalIndex);
			}
			finally
			{
				row.IsDragSource = false;
				this._dragOriginalIndex = -1;
			}
		}

		/// <summary>
		/// Moves the dragged row to the place under the pointer.
		///
		/// <b>A miss changes nothing.</b> The pointer crosses the gap between two rows on the
		/// way to the next one, and the gap belongs to the panel. A handler that clears the
		/// preview there makes the ghost blink once per row.
		/// </summary>
		private void ModList_DragOver(object sender, DragEventArgs e)
		{
			e.Effects = DragDropEffects.Move;
			e.Handled = true;

			FrameworkElement element = FindRowElement(e.OriginalSource as DependencyObject);

			if (element?.DataContext is not ModRowViewModel target) return;
			if (this._dragOriginalIndex < 0) return;

			int from = this.DraggedIndex();
			int to = this._model.Mods.IndexOf(target);

			if (from < 0 || to < 0 || from == to) return;

			// The pointer past the middle of a row means the far side of that row. A row that
			// travels down passes over the target, so the index of the target is already the
			// index that the dragged row takes.
			bool second = e.GetPosition(element).Y >= element.ActualHeight / 2;

			if (from < to && !second) to--;
			else if (from > to && second) to++;

			if (from == to) return;

			this._model.PreviewMove(from, to);
		}

		private void ModList_DragLeave(object sender, DragEventArgs e)
		{
			// Nothing. DragLeave fires when the pointer crosses from one row into the next, so a
			// handler that clears the preview here undoes the move that DragOver just made.
		}

		private void ModList_Drop(object sender, DragEventArgs e)
		{
			e.Handled = true;

			if (!e.Data.GetDataPresent(typeof(string))) return;

			string modId = (string)e.Data.GetData(typeof(string));
			int index = this.DraggedIndex();

			if (index < 0) return;

			// The collection already shows this order, so the commit moves nothing on screen.
			this._model.MoveModTo(modId, index);
		}

		/// <summary>One cursor for the whole drag.</summary>
		/// <remarks>
		/// The default cursors of the drag change between move and no-drop as the pointer
		/// crosses a surface that takes no drop. Under Wine that change is itself a flicker.
		/// </remarks>
		private void ModList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
		{
			e.UseDefaultCursors = false;
			Mouse.SetCursor(Cursors.SizeAll);
			e.Handled = true;
		}

		private void ModList_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
		{
			if (!e.EscapePressed) return;

			e.Action = DragAction.Cancel;
			e.Handled = true;
		}

		/// <summary>The index that the row of this drag holds in the collection right now.</summary>
		private int DraggedIndex()
		{
			foreach (ModRowViewModel row in this._model.Mods)
			{
				if (row.IsDragSource) return this._model.Mods.IndexOf(row);
			}

			return -1;
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

		public bool Confirm(string question, string confirmLabel = "Yes", bool destructive = false) =>
			Dialogs.Confirm(this, question, confirmLabel, destructive);

		public void ShowError(string message) => Dialogs.ShowError(this, message);

		public void ShowMessage(string message) => Dialogs.ShowMessage(this, message);
	}
}
