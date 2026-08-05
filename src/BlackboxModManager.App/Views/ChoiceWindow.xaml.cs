using System;
using System.Collections.Generic;
using System.Windows;

namespace BlackboxModManager.App.Views
{
	/// <summary>
	/// One row of a choice dialog.
	///
	/// The key is what the caller gets back. The title and the detail are what the user reads.
	/// An empty key means "ask me again", and the caller decides whether to offer it.
	/// </summary>
	public sealed class UserChoice
	{
		public string Key { get; }

		public string Title { get; }

		public string Detail { get; }

		public UserChoice(string key, string title, string detail = null)
		{
			this.Key = key ?? String.Empty;
			this.Title = title ?? String.Empty;
			this.Detail = detail ?? String.Empty;
		}

		public override string ToString() => this.Title;
	}

	/// <summary>
	/// Asks the user to pick one of several things.
	///
	/// The dialog ranks nothing and it preselects nothing but the current answer. The loader
	/// choice needs that: version numbers on a proxy DLL are often absent or wrong, so a dialog
	/// that preselected the highest number would give the user a reason to trust a number that
	/// means nothing.
	/// </summary>
	public partial class ChoiceWindow : Window
	{
		public ChoiceWindow(string question, IReadOnlyList<UserChoice> choices, string current)
		{
			this.InitializeComponent();

			this.Question.Text = question ?? String.Empty;
			this.Choices.ItemsSource = choices;

			foreach (UserChoice choice in choices)
			{
				if (!String.Equals(choice.Key, current, StringComparison.OrdinalIgnoreCase)) continue;

				this.Choices.SelectedItem = choice;
				break;
			}

			this.Choices.Focus();
		}

		/// <summary>
		/// Shows the dialog and returns the key of the chosen row. It returns null when the
		/// user cancels, so a caller can tell "cancelled" from "chose ask me again", which
		/// returns an empty string.
		/// </summary>
		public static string Ask(Window owner, string question, IReadOnlyList<UserChoice> choices,
			string current = null)
		{
			if (choices is null || choices.Count == 0) return null;

			var window = new ChoiceWindow(question, choices, current) { Owner = owner };

			if (window.ShowDialog() != true) return null;

			return (window.Choices.SelectedItem as UserChoice)?.Key;
		}

		private void OnAccept(object sender, RoutedEventArgs e)
		{
			// A dialog that closes with nothing selected would read as a cancel. Keep it open.
			if (this.Choices.SelectedItem is null) return;

			this.DialogResult = true;
		}

		private void OnDoubleClick(object sender, RoutedEventArgs e)
		{
			if (this.Choices.SelectedItem != null) this.DialogResult = true;
		}
	}
}
