using System;
using System.Windows;
using System.Windows.Input;

namespace BlackboxModManager.App.Views
{
	/// <summary>
	/// Asks the user for one line of text.
	///
	/// The application asks every question in a dialog. Console.ReadLine never returns on a
	/// Wine console, so a console prompt would hang the application. See
	/// docs/roadmap/00-test-environment.md.
	/// </summary>
	public partial class TextPromptWindow : Window
	{
		public string Value => this.Answer.Text.Trim();

		public TextPromptWindow(string question, string value)
		{
			this.InitializeComponent();

			this.Question.Text = question;
			this.Answer.Text = value ?? String.Empty;
			this.Answer.SelectAll();
			this.Answer.Focus();
		}

		/// <summary>
		/// Shows the dialog and returns the text. It returns null when the user cancels, or
		/// when the user gives an empty answer.
		/// </summary>
		public static string Ask(Window owner, string question, string value = null)
		{
			var window = new TextPromptWindow(question, value) { Owner = owner };

			if (window.ShowDialog() != true) return null;

			return window.Value.Length == 0 ? null : window.Value;
		}

		private void OnAccept(object sender, RoutedEventArgs e)
		{
			this.DialogResult = true;
		}

		private void OnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter) this.DialogResult = true;
		}
	}
}
