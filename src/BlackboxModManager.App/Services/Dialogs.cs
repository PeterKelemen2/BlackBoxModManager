using System;
using System.Windows;
using BlackboxModManager.App.Views;
using Microsoft.Win32;

namespace BlackboxModManager.App.Services
{
	/// <summary>
	/// Every question that the application asks the user.
	///
	/// <b>The application asks in a dialog and never on a console.</b> Console.ReadLine
	/// never returns on a Wine console, and the user of a window application sees no
	/// console anyway. See docs/roadmap/00-test-environment.md.
	/// </summary>
	public static class Dialogs
	{
		/// <summary>
		/// Asks for a directory. It returns null when the user cancels.
		/// </summary>
		public static string PickDirectory(Window owner, string title, string start = null)
		{
			var dialog = new OpenFolderDialog
			{
				Title = title,
				Multiselect = false,
			};

			if (!String.IsNullOrWhiteSpace(start)) dialog.InitialDirectory = start;

			return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
		}

		/// <summary>
		/// Asks for a mod archive or any file. It returns null when the user cancels.
		/// </summary>
		public static string PickFile(Window owner, string title, string filter)
		{
			var dialog = new OpenFileDialog
			{
				Title = title,
				Filter = filter,
				Multiselect = false,
			};

			return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
		}

		public static string AskText(Window owner, string question, string value = null)
		{
			return TextPromptWindow.Ask(owner, question, value);
		}

		/// <summary>
		/// Asks the user to pick one of several things. It returns null when the user cancels.
		/// </summary>
		public static string PickChoice(Window owner, string question,
			System.Collections.Generic.IReadOnlyList<UserChoice> choices, string current = null)
		{
			return ChoiceWindow.Ask(owner, question, choices, current);
		}

		public static bool Confirm(Window owner, string question, string title = "Blackbox Mod Manager")
		{
			return MessageBox.Show(owner, question, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
				== MessageBoxResult.Yes;
		}

		public static void ShowError(Window owner, string message, string title = "The operation failed.")
		{
			MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
		}

		public static void ShowMessage(Window owner, string message, string title = "Blackbox Mod Manager")
		{
			MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
		}
	}
}
