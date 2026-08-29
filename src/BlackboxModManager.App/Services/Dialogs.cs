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

		/// <summary>Lists every directory of this application and lets the user open one.</summary>
		public static void ShowFolders(Window owner,
			System.Collections.Generic.IReadOnlyList<FolderRow> folders)
		{
			FoldersWindow.Show(owner, folders);
		}

		/// <summary>
		/// Asks one question that has two answers. <paramref name="confirmLabel"/> names the
		/// action, and <paramref name="destructive"/> makes the button read as destructive.
		/// </summary>
		public static bool Confirm(Window owner, string question, string confirmLabel = "Yes",
			bool destructive = false)
		{
			return ConfirmWindow.Ask(owner, question, confirmLabel, destructive);
		}

		/// <summary>
		/// Reports a failure and lets the user copy the text.
		///
		/// <b>Every error goes through this and never through MessageBox.</b> A message box
		/// gives the user no way to copy the text, and an error of this application names a
		/// path, a mod, a script line, or a message from one of the three libraries.
		/// </summary>
		/// <remarks>
		/// <b>The title bar names the application and the heading names the failure.</b> Both
		/// took the same string until step 17, Part L, so every error dialog printed the same
		/// sentence twice.
		/// </remarks>
		public static void ShowError(Window owner, string message, string title = "The operation failed.")
		{
			MessageWindow.Show(owner, ApplicationName, title, message, "Copy error");
		}

		public static void ShowMessage(Window owner, string message, string title = ApplicationName)
		{
			MessageWindow.Show(owner, title, null, message, "Copy text");
		}

		/// <summary>
		/// The name of this application, as a user reads it. The two words are separate,
		/// because the games come from EA Black Box.
		///
		/// <b>The namespace and the assembly keep the one-word spelling.</b> That name is an
		/// identifier, and this project renames no identifier for a text fix.
		/// </summary>
		public const string ApplicationName = "BlackBox Mod Manager";
	}
}
