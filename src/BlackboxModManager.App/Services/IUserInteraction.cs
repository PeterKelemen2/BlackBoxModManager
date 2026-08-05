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

		bool Confirm(string question);

		void ShowError(string message);

		void ShowMessage(string message);
	}
}
