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
		/// dialog does not stop that. It only delays it. Worse, a dialog pumps messages while
		/// it waits, so the failing render runs again and raises the same exception again. The
		/// user then gets a storm of dialogs and a crash. The <c>_showingError</c> guard is what
		/// stops the storm, and it holds for any kind of dialog.
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

			const string Title = "The window hit an error.";

			// Carry the whole exception and not the message alone. This dialog reports a defect
			// in the window, so the copy has to hold the stack that names the line.
			string body =
				$"{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
				$"The application wrote the detail to:{Environment.NewLine}{file}{Environment.NewLine}{Environment.NewLine}" +
				$"The application keeps running. Save your work and start it again." +
				$"{Environment.NewLine}{Environment.NewLine}{e.Exception}";

			try
			{
				try
				{
					Views.MessageWindow.Show(this.MainWindow, Title, Title, body, "Copy error");
				}
				catch (Exception)
				{
					// A render failure reached this handler, so a new WPF window can fail too.
					// A message box is a native dialog and it does not need the render path.
					// The user loses the copy button and still reads the message.
					MessageBox.Show(body, Title, MessageBoxButton.OK, MessageBoxImage.Error);
				}
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
