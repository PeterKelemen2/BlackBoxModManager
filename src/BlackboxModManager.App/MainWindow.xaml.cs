using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BlackboxModManager.App.Services;
using BlackboxModManager.App.ViewModels;
using BlackboxModManager.App.Views;

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

			this.Loaded += this.OnLoaded;
		}

		/// <summary>
		/// Runs the update check of the start, if the settings ask for it.
		///
		/// This waits for Loaded and does not run in the constructor. The check writes to the
		/// log, and the log has to exist on screen to carry the line.
		/// </summary>
		private async void OnLoaded(object sender, RoutedEventArgs e)
		{
			// One check for each start. Loaded fires again when the window comes back into the
			// tree, and a second check would ask GitHub twice.
			this.Loaded -= this.OnLoaded;

			try
			{
				await this._model.CheckForUpdatesAtStartAsync();
			}
			catch (Exception)
			{
				// An async void method that throws ends the process. The method above catches
				// every failure of its own, so this only covers a failure before it starts.
			}
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

		// ---------------------------------------------------------------------- The log
		//
		// The log holds one string per line, and the list carries the copy. The selection
		// lives in the control and never in the view model, so these four handlers read
		// LogList and the view model knows nothing about them.
		//
		// SelectionMode Extended in MainWindow.xaml gives the control the Control gesture and
		// the Shift gesture with no code. What the code adds is the right press, the menu, and
		// the copy itself.

		/// <summary>
		/// Selects the line under the pointer before the menu of the list opens.
		///
		/// WPF changes no selection on a right press. A menu that copies the selection would
		/// then copy the lines that the user chose last and not the line under the pointer.
		///
		/// <b>A press inside the selection keeps that selection.</b> A user who picked ten
		/// lines with Control and Shift right clicks one of them to copy all ten, and a reset
		/// here would throw the other nine away.
		///
		/// It never marks the event handled. WPF opens the ContextMenu on the button up that
		/// follows, and a handled press would stop that.
		/// </summary>
		private void LogList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (sender is not ListBox list) return;

			ListBoxItem item = FindListItem(e.OriginalSource as DependencyObject);

			// A press on the empty space below the last line changes nothing.
			if (item is null || item.IsSelected) return;

			int index = list.ItemContainerGenerator.IndexFromContainer(item);

			if (index < 0) return;

			// SelectedIndex drops every other line and moves the anchor of the Shift gesture to
			// this one. A write to IsSelected leaves that anchor where it was.
			list.SelectedIndex = index;
		}

		/// <summary>
		/// Stops the menu while the list holds no selection. A "Copy" that copies nothing is
		/// worse than no menu at all.
		/// </summary>
		private void LogList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
		{
			if (sender is ListBox list && list.SelectedItems.Count > 0) return;

			e.Handled = true;
		}

		/// <summary>Control with C copies the selection, the same as the menu does.</summary>
		private void LogList_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control) return;

			this.CopyLines(sender as ListBox);
			e.Handled = true;
		}

		/// <summary>The "Copy" item of the menu of the log.</summary>
		private void OnCopyLog(object sender, RoutedEventArgs e)
		{
			this.CopyLines(this.LogList);
		}

		/// <summary>
		/// Puts the selected lines on the clipboard, one line per row, in the order that the
		/// log holds them.
		/// </summary>
		private void CopyLines(ListBox list)
		{
			if (list is null || list.SelectedItems.Count == 0) return;

			// SelectedItems holds the lines in the order that the user clicked them. A copy of
			// four scattered lines has to read top to bottom, so the index of each line comes
			// along and sorts them back.
			var lines = new List<(int Index, string Text)>(list.SelectedItems.Count);

			foreach (object item in list.SelectedItems)
			{
				lines.Add((list.Items.IndexOf(item), item?.ToString() ?? String.Empty));
			}

			lines.Sort((a, b) => a.Index.CompareTo(b.Index));

			var text = new StringBuilder();

			foreach ((int _, string line) in lines) text.AppendLine(line);

			try
			{
				Clipboard.SetText(text.ToString());
			}
			catch (Exception ex)
			{
				// Another application can hold the clipboard open. Windows then refuses every
				// write until that application lets go.
				this.ShowError($"The copy did not reach the clipboard. {ex.Message}");
			}
		}

		/// <summary>
		/// The copy button beside the problem of a variant in the Mod tab.
		///
		/// The message names a script file, a line, and the words of the library. A user who
		/// reports a broken mod has to send that text, and no other control on the tab gives
		/// it to them.
		/// </summary>
		private void OnCopyProblem(object sender, RoutedEventArgs e)
		{
			if ((sender as Button)?.Tag is not VariantRowViewModel row) return;

			try
			{
				// The second argument keeps the text on the clipboard after this process ends.
				Clipboard.SetDataObject(row.ProblemReport, true);
			}
			catch (Exception ex)
			{
				// Another application can hold the clipboard open. Windows then refuses every
				// write until that application lets go.
				this.ShowError($"The copy did not reach the clipboard. {ex.Message}");
			}
		}

		/// <summary>The row of a list that contains the source of the event.</summary>
		private static ListBoxItem FindListItem(DependencyObject source)
		{
			while (source != null)
			{
				if (source is ListBoxItem item) return item;

				source = GetParentObject(source);
			}

			return null;
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
		private FrameworkElement _dragElement;
		private Point _dragGrabPoint;
		private int _dragOriginalIndex = -1;
		private DragGhost _dragGhost;

		/// <summary>
		/// The parent of an object of the tree, from the visual tree or from the logical tree.
		///
		/// The source of a mouse event is not always a Visual. A press on the text of a TextBlock
		/// reports a Run, which is a ContentElement, and VisualTreeHelper.GetParent throws for it.
		/// A Run has no visual parent, so the step to its host must go through the content tree.
		/// </summary>
		private static DependencyObject GetParentObject(DependencyObject source)
		{
			if (source is Visual or System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(source);

			if (source is ContentElement content)
			{
				return ContentOperations.GetParent(content) ?? LogicalTreeHelper.GetParent(content);
			}

			return LogicalTreeHelper.GetParent(source);
		}

		/// <summary>The row whose template root, named "RowBorder", contains the source of the event.</summary>
		private static FrameworkElement FindRowElement(DependencyObject source)
		{
			while (source != null)
			{
				if (source is FrameworkElement element && element.Name == "RowBorder") return element;

				source = GetParentObject(source);
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

				source = GetParentObject(source);
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
			this._dragElement = element;

			// Where inside the row the user took hold of it. The floating copy keeps that point
			// under the pointer, so the row does not jump when the drag starts.
			this._dragGrabPoint = e.GetPosition(element);

			this._model.SelectedMod = row;
		}

		/// <summary>
		/// Selects the row under the pointer before its menu opens.
		///
		/// The menu of the row runs <c>Set game</c> and <c>Remove</c>, and both act on the
		/// selected mod. Without this handler the menu acts on the row that the user selected
		/// last, which is the wrong mod. See docs/roadmap/12-minimal-ui.md, Part E.
		///
		/// It never marks the event handled. WPF opens the ContextMenu of the row on the button
		/// up that follows, and a handled press would stop that.
		/// </summary>
		private void ModList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			FrameworkElement element = FindRowElement(e.OriginalSource as DependencyObject);

			if (element?.DataContext is not ModRowViewModel row) return;

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

			FrameworkElement element = this._dragElement;

			this._dragStartPoint = null;
			this._dragCandidate = null;
			this._dragElement = null;
			this._dragOriginalIndex = this._model.Mods.IndexOf(row);

			if (this._dragOriginalIndex < 0) return;

			// Capture first, then dim. The floating copy has to hold the resting look of the row.
			//
			// The host is DragLayer and never the mod list. AdornerLayer.GetAdornerLayer returns
			// the nearest layer above the element, and a ScrollContentPresenter carries one of its
			// own. The mod list sits inside a ScrollViewer, so a copy hosted there clips to the
			// viewport of the list. DragLayer is the root content of the window, whose layer comes
			// from the AdornerDecorator of the window template and clips to nothing inside it.
			this._dragGhost = DragGhost.Attach(this.DragLayer, element,
				(Brush)this.FindResource("BorderStrong"));

			row.IsDragSource = true;

			this.MoveGhost(e.GetPosition(this.DragLayer));

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

				this._dragGhost?.Detach();
				this._dragGhost = null;
			}
		}

		/// <summary>
		/// Puts the floating copy under the pointer. The point comes in the coordinates of
		/// DragLayer, which is the element that the adorner adorns.
		/// </summary>
		private void MoveGhost(Point inList)
		{
			this._dragGhost?.MoveTo(new Point(
				inList.X - this._dragGrabPoint.X,
				inList.Y - this._dragGrabPoint.Y));
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
			// A file that comes from outside the window is an import and never a reorder. It
			// carries no dragged row, so the preview below has nothing to move.
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;

				return;
			}

			e.Effects = DragDropEffects.Move;
			e.Handled = true;

			// The floating copy follows the pointer whatever it sits over. Only the landing slot
			// waits for a hit on a row.
			this.MoveGhost(e.GetPosition(this.DragLayer));

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

			// An import from outside the window. ModImporter reads an archive or a directory,
			// so one path covers both. See docs/roadmap/12-minimal-ui.md, Part I.
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) this.ImportDropped(paths);

				return;
			}

			if (!e.Data.GetDataPresent(typeof(string))) return;

			string modId = (string)e.Data.GetData(typeof(string));
			int index = this.DraggedIndex();

			if (index < 0) return;

			// The collection already shows this order, so the commit moves nothing on screen.
			this._model.MoveModTo(modId, index);
		}

		/// <summary>
		/// Starts the import of a drop and does not wait for it.
		///
		/// <c>Drop</c> cannot await. The view model reports every failure of a long operation
		/// through <c>RunAsync</c>, which catches, logs, and shows a dialog, so nothing escapes
		/// this call.
		/// </summary>
		private async void ImportDropped(string[] paths)
		{
			try
			{
				await this._model.ImportDropAsync(paths);
			}
			catch (Exception ex)
			{
				// An async void method that throws ends the process. RunAsync already catches,
				// so this only covers a failure before the operation starts.
				this.ShowError(ex.Message);
			}
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

		/// <summary>
		/// The window that owns the next dialog. It is this window unless another window of
		/// this application is open in front of it.
		///
		/// <b>A dialog must never take a disabled owner.</b> ConfigWindow of step 12 runs
		/// modal, which disables this window, and its buttons run the same commands that this
		/// window runs. A file dialog owned by a disabled window can open behind that window
		/// and take no input. ConfigWindow sets this property for as long as it is open. See
		/// docs/roadmap/12-minimal-ui.md, Part H.
		/// </summary>
		public Window DialogOwner { get; set; }

		private Window Owner_() => this.DialogOwner ?? this;

		public string PickDirectory(string title, string start = null) =>
			Dialogs.PickDirectory(this.Owner_(), title, start);

		public string PickFile(string title, string filter) => Dialogs.PickFile(this.Owner_(), title, filter);

		public string AskText(string question, string value = null) =>
			Dialogs.AskText(this.Owner_(), question, value);

		public string PickChoice(string question,
			System.Collections.Generic.IReadOnlyList<Views.UserChoice> choices, string current = null) =>
			Dialogs.PickChoice(this.Owner_(), question, choices, current);

		public void ShowFolders(System.Collections.Generic.IReadOnlyList<Views.FolderRow> folders) =>
			Dialogs.ShowFolders(this.Owner_(), folders);

		public bool Confirm(string question, string confirmLabel = "Yes", bool destructive = false) =>
			Dialogs.Confirm(this.Owner_(), question, confirmLabel, destructive);

		public void ShowError(string message) => Dialogs.ShowError(this.Owner_(), message);

		public void ShowMessage(string message) => Dialogs.ShowMessage(this.Owner_(), message);

		// ---------------------------------------------------------------- The bar and the menus

		/// <summary>
		/// Opens the menu that the pressed button carries.
		///
		/// A button does not open its own ContextMenu on a left press, so the window does it
		/// here. The menu reads its DataContext from the placement target, so this method sets
		/// that target on every open.
		/// </summary>
		private void OnOpenOwnMenu(object sender, RoutedEventArgs e)
		{
			if (sender is not Button button) return;

			ShowMenu(button, button.ContextMenu);
		}

		/// <summary>
		/// Opens the import menu under the tile of the empty state. The tile carries no menu of
		/// its own, so both ways into an import offer one pair of answers.
		/// </summary>
		private void OnOpenImportMenu(object sender, RoutedEventArgs e)
		{
			if (sender is not Button button) return;

			ShowMenu(button, this.ImportButton.ContextMenu);
		}

		private static void ShowMenu(Button anchor, ContextMenu menu)
		{
			if (anchor is null || menu is null) return;

			menu.PlacementTarget = anchor;
			menu.Placement = PlacementMode.Bottom;
			menu.VerticalOffset = 2;
			menu.IsOpen = true;
		}

		/// <summary>
		/// Opens the settings. Every path of this application lives there, and nothing else
		/// does.
		/// </summary>
		private void OnOpenConfig(object sender, RoutedEventArgs e) => ConfigWindow.Show(this, this._model);
	}
}
