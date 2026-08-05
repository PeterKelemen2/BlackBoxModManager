using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

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
	/// Lists every directory of this application and opens one.
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

			this.Report.Text = Open(row.Path);
		}

		/// <summary>
		/// Opens one directory in the file manager of the platform.
		///
		/// It tries the shell handler first, then <c>explorer.exe</c>. Wine ships that program
		/// and it may still refuse a path. The result says which one worked, or that neither
		/// did, and it never throws.
		/// </summary>
		private static string Open(string path)
		{
			if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
			{
				return $"The directory {path} does not exist, so there is nothing to open.";
			}

			try
			{
				// A directory with UseShellExecute opens the file manager of the platform.
				Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

				return $"Asked the platform to open {path}.";
			}
			catch (Exception first)
			{
				try
				{
					// Wine ships explorer.exe. It is the fallback on a prefix whose shell
					// associations are empty.
					Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
					{
						UseShellExecute = false,
					});

					return $"Asked explorer.exe to open {path}.";
				}
				catch (Exception second)
				{
					return "Neither the shell nor explorer.exe opened the directory. " +
						$"Use Copy path instead. {first.Message} {second.Message}";
				}
			}
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
