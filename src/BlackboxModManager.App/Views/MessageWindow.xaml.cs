using System;
using System.Windows;

namespace BlackboxModManager.App.Views
{
	/// <summary>
	/// Shows one message and lets the user copy it.
	///
	/// <b>A message box gives the user no way to copy the text.</b> An error of this
	/// application names a path, a mod, a script line, or a message from one of the three
	/// libraries. A user who reports a problem has to be able to send that text, and a user who
	/// reads it under Wine cannot select it in a message box.
	/// </summary>
	public partial class MessageWindow : Window
	{
		private readonly string _text;

		public MessageWindow(string title, string heading, string message, string copyLabel)
		{
			this.InitializeComponent();

			this.Title = title ?? "Blackbox Mod Manager";
			this.Heading.Text = heading ?? String.Empty;
			this.Heading.Visibility = String.IsNullOrEmpty(heading) ? Visibility.Collapsed : Visibility.Visible;

			this._text = message ?? String.Empty;
			this.Body.Text = this._text;

			if (!String.IsNullOrEmpty(copyLabel)) this.CopyButton.Content = copyLabel;

			this.Body.Focus();
		}

		/// <summary>
		/// Shows the window and waits. Pass the heading that names what failed and the message
		/// that the user copies.
		/// </summary>
		public static void Show(Window owner, string title, string heading, string message,
			string copyLabel = null)
		{
			var window = new MessageWindow(title, heading, message, copyLabel);

			// A window with no owner still has to appear. The self test and an early failure
			// both run before the main window exists.
			if (owner != null && owner.IsLoaded) window.Owner = owner;

			window.ShowDialog();
		}

		/// <summary>
		/// Puts the whole message on the clipboard.
		///
		/// The clipboard belongs to the whole desktop, and another process can hold it. WPF
		/// then throws a COM exception. The copy is a convenience, so a failure says so on the
		/// button line and never closes the window.
		/// </summary>
		private void OnCopy(object sender, RoutedEventArgs e)
		{
			// The heading names what failed, so a report that carries it needs less back and
			// forth. Copy both.
			string all = this.Heading.Visibility == Visibility.Visible && this.Heading.Text.Length > 0
				? $"{this.Heading.Text}{Environment.NewLine}{Environment.NewLine}{this._text}"
				: this._text;

			try
			{
				// The second argument keeps the text on the clipboard after this process ends.
				Clipboard.SetDataObject(all, true);

				this.CopyState.Text = $"Copied {all.Length} characters.";
			}
			catch (Exception ex)
			{
				this.CopyState.Text = "The clipboard refused the text. Another program holds it. " +
					$"Select the message and press Control C. {ex.Message}";

				this.Body.SelectAll();
				this.Body.Focus();
			}
		}

		private void OnClose(object sender, RoutedEventArgs e)
		{
			this.Close();
		}
	}
}
