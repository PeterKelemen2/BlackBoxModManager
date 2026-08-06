using System.Collections.Generic;
using BlackboxModManager.App.Views;

namespace BlackboxModManager.App.Services
{
	/// <summary>
	/// Every question that the view model asks the user.
	///
	/// The window implements this with real dialogs. The view model holds no reference to a
	/// window, so a test can answer these questions without one.
	/// </summary>
	public interface IUserInteraction
	{
		/// <summary>Asks for a directory. It returns null when the user cancels.</summary>
		string PickDirectory(string title, string start = null);

		/// <summary>Asks for a file. It returns null when the user cancels.</summary>
		string PickFile(string title, string filter);

		/// <summary>Asks for one line of text. It returns null when the user cancels.</summary>
		string AskText(string question, string value = null);

		/// <summary>
		/// Asks the user to pick one of several things. It returns the key of the chosen row.
		/// It returns null when the user cancels, and an empty string when the user chooses
		/// "ask me again".
		/// </summary>
		string PickChoice(string question, IReadOnlyList<UserChoice> choices, string current = null);

		/// <summary>Lists every directory of this application and lets the user open one.</summary>
		void ShowFolders(IReadOnlyList<FolderRow> folders);

		/// <summary>
		/// Asks one question that has two answers. <paramref name="confirmLabel"/> names the
		/// action that the first button takes, because a question such as "move the mods or
		/// read them where they are" has two legitimate answers and no cancel. Set
		/// <paramref name="destructive"/> when the answer deletes something.
		/// </summary>
		bool Confirm(string question, string confirmLabel = "Yes", bool destructive = false);

		void ShowError(string message);

		void ShowMessage(string message);
	}
}
