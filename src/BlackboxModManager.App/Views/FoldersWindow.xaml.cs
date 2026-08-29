using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BlackboxModManager.App.Services;

namespace BlackboxModManager.App.Views
{
	/// <summary>
	/// One directory that this application reads or writes.
	/// </summary>
	public sealed class FolderRow
	{
		public string Name { get; }

		/// <summary>One line that says what the directory holds.</summary>
		public string Purpose { get; }

		public string Path { get; }

		public bool Exists => !String.IsNullOrEmpty(this.Path) && Directory.Exists(this.Path);

		/// <summary>
		/// What the directory holds right now, or why it is absent. A staging directory exists
		/// only after a deploy built one.
		/// </summary>
		public string State
		{
			get
			{
				if (String.IsNullOrEmpty(this.Path)) return "No path. Set the game install first.";
				if (!this.Exists) return "This directory does not exist yet.";

				try
				{
					int files = Directory.GetFiles(this.Path).Length;
					int directories = Directory.GetDirectories(this.Path).Length;

					return $"{files} files and {directories} directories at the top level.";
				}
				catch (Exception ex)
				{
					return $"This application could not read the directory. {ex.Message}";
				}
			}
		}

		public FolderRow(string name, string purpose, string path)
		{
			this.Name = name;
			this.Purpose = purpose;
			this.Path = path ?? String.Empty;
		}
	}

	/// <summary>
	/// Lists the directories that a user can look at but cannot change, and opens one.
	///
	/// <b>The settings window owns every directory that a user can change.</b> Step 17, Part A,
	/// split the two lists. One directory gets one row in one window.
	///
	/// <b>Copy path always works and Open does not.</b> A window application under Wine has no
	/// guaranteed file manager, and the shell handler of the platform can be absent. So every
	/// row carries both, and the path itself sits in a selectable box.
	/// </summary>
	public partial class FoldersWindow : Window
	{
		public FoldersWindow(IReadOnlyList<FolderRow> rows)
		{
			this.InitializeComponent();

			this.Rows.ItemsSource = rows;
		}

		public static void Show(Window owner, IReadOnlyList<FolderRow> rows)
		{
			var window = new FoldersWindow(rows);

			if (owner != null && owner.IsLoaded) window.Owner = owner;

			window.ShowDialog();
		}

		private void OnOpen(object sender, RoutedEventArgs e)
		{
			if ((sender as Button)?.Tag is not FolderRow row) return;

			this.Report.Text = DirectoryOpener.Open(row.Path);
		}

		private void OnCopy(object sender, RoutedEventArgs e)
		{
			if ((sender as Button)?.Tag is not FolderRow row) return;

			try
			{
				// The second argument keeps the text on the clipboard after this process ends.
				Clipboard.SetDataObject(row.Path, true);

				this.Report.Text = $"Copied {row.Path}";
			}
			catch (Exception ex)
			{
				this.Report.Text = "The clipboard refused the text. Another program holds it. " +
					$"Select the path above and press Control C. {ex.Message}";
			}
		}
	}
}
