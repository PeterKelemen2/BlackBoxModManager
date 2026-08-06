using System.Windows;
using BlackboxModManager.App.Theme;

namespace BlackboxModManager.App.Views
{
	/// <summary>
	/// Asks the user one question that has two answers.
	///
	/// This replaced the message box of step 10. A message box draws the theme of the host
	/// and not the theme of this application, so the delete question was the last light
	/// surface of the window. See docs/roadmap/11-ui-polish.md, Part A.
	/// </summary>
	public partial class ConfirmWindow : Window
	{
		public ConfirmWindow(string question, string confirmLabel, bool destructive)
		{
			this.InitializeComponent();

			this.Question.Text = question;
			this.ConfirmButton.Content = confirmLabel;

			// A destructive answer reads as destructive. Every other question keeps the accent.
			Kind.SetValue(this.ConfirmButton, destructive ? ButtonKind.Danger : ButtonKind.Primary);

			this.CancelButton.Focus();
		}

		/// <summary>
		/// Shows the dialog and returns the answer. Escape, the close box, and the cancel
		/// button all answer false.
		/// </summary>
		public static bool Ask(Window owner, string question, string confirmLabel = "Yes",
			bool destructive = false)
		{
			var window = new ConfirmWindow(question, confirmLabel, destructive);

			// The same rule as MessageWindow. An owner that has not loaded yet throws.
			if (owner != null && owner.IsLoaded) window.Owner = owner;

			return window.ShowDialog() == true;
		}

		private void OnConfirm(object sender, RoutedEventArgs e)
		{
			this.DialogResult = true;
		}
	}
}
