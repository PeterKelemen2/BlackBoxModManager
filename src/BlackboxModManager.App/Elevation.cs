using System;
using System.Diagnostics;
using System.Security.Principal;

namespace BlackboxModManager.App
{
	/// <summary>
	/// Starts this application again with administrator rights.
	///
	/// <b>Never ask for administrator rights in the application manifest.</b> Two things break
	/// when every run is elevated.
	///
	/// 1. <b>The drag and drop import stops working.</b> Windows refuses a drop from a normal
	///    Explorer window into an elevated process, and it reports nothing. The import of step
	///    13 would go quiet with no message and no log line.
	/// 2. <b>An elevated run leaves files that a normal run cannot write.</b> The mod store and
	///    the workspace would then refuse the next ordinary start.
	///
	/// A game outside Program Files needs no elevation at all. So the application asks only when
	/// AccessPreflight says that a deploy cannot finish.
	/// </summary>
	internal static class Elevation
	{
		/// <summary>
		/// True when this process already runs with administrator rights.
		///
		/// A failure to read the token reports false. The caller then offers the restart, and
		/// the restart is what answers the question for certain.
		/// </summary>
		public static bool IsAdministrator()
		{
			try
			{
				using var identity = WindowsIdentity.GetCurrent();

				return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Starts this application again as administrator and reports whether that started.
		///
		/// The caller closes this window when this returns true. A user who refuses the prompt
		/// of Windows makes this return false, and the application then keeps running.
		/// </summary>
		public static bool Restart(out string error)
		{
			error = null;

			try
			{
				string path = Environment.ProcessPath;

				if (String.IsNullOrEmpty(path))
				{
					error = "The path of this program is not readable.";

					return false;
				}

				// UseShellExecute has to be true. The runas verb goes to the shell, and the
				// shell is what shows the prompt of Windows.
				var start = new ProcessStartInfo
				{
					FileName = path,
					UseShellExecute = true,
					Verb = "runas",
				};

				Process.Start(start);

				return true;
			}
			catch (Exception ex)
			{
				// A user who answers No to the prompt of Windows arrives here. That is a choice
				// and not a failure, so the caller reports it as one line and nothing more.
				error = ex.Message;

				return false;
			}
		}
	}
}
