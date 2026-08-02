using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BlackboxModManager.Core;

namespace BlackboxModManager.App
{
	/// <summary>
	/// The application object. Program.Main creates it after it sets the culture.
	/// </summary>
	public partial class App : Application
	{
		/// <summary>True while a message box is open. It stops a storm of dialogs.</summary>
		private bool _showingError;

		protected override void OnStartup(StartupEventArgs e)
		{
			// An unhandled exception on the UI thread closes the window with no message.
			// Show the text instead, because a user under Wine sees no console.
			this.DispatcherUnhandledException += this.OnUnhandledException;

			base.OnStartup(e);

			var window = new MainWindow();
			window.Show();
		}

		/// <summary>
		/// Reports an exception that reached the dispatcher, and keeps the application alive.
		///
		/// <b>Set Handled first.</b> An unhandled exception here ends the process, and the
		/// message box does not stop that. It only delays it. Worse, the box pumps messages
		/// while it waits, so the failing render runs again and raises the same exception
		/// again. The user then gets a storm of dialogs and a crash.
		///
		/// Every disk operation of the application already reports its own failure through
		/// MainViewModel.RunAsync. An exception that arrives here is a defect in the window,
		/// so the file that this method writes is the record of it.
		/// </summary>
		private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			e.Handled = true;

			string file = WriteErrorLog(e.Exception);

			// A second dialog while the first one waits would repeat without end.
			if (this._showingError) return;

			this._showingError = true;

			try
			{
				MessageBox.Show(
					$"{e.Exception.Message}\n\nThe application wrote the detail to:\n{file}\n\n" +
					"The application keeps running. Save your work and start it again.",
					"The window hit an error.",
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
			finally
			{
				this._showingError = false;
			}
		}

		/// <summary>
		/// Appends one exception to the error log and returns the path. It returns a message
		/// instead of the path when the write fails, because a failed write must not raise a
		/// second exception here.
		/// </summary>
		private static string WriteErrorLog(Exception error)
		{
			string path = Path.Combine(AppPaths.LogDirectory, "error.log");

			try
			{
				Directory.CreateDirectory(AppPaths.LogDirectory);

				string text =
					$"{Environment.NewLine}==== {DateTimeOffset.UtcNow:u} ===={Environment.NewLine}" +
					error + Environment.NewLine;

				File.AppendAllText(path, text);

				return path;
			}
			catch (Exception)
			{
				return "(the application could not write the log file)";
			}
		}
	}
}
