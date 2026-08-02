using System;
using System.Globalization;
using System.Threading;

namespace BlackboxModManager.App
{
	/// <summary>
	/// The entry point of the application.
	///
	/// WPF generates an entry point of its own from App.xaml. The project sets
	/// StartupObject to this class, so this method runs instead. The culture assignment
	/// must run before any other code, and a generated entry point cannot hold it.
	/// </summary>
	internal static class Program
	{
		[STAThread]
		private static int Main(string[] args)
		{
			// This must stay the first statement. A comma-decimal locale reads the script
			// float -0.19500002 as a different number. Binary forces en-US for the same
			// reason. Do not depend on the locale of the machine.
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
			Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

			// The self test drives the deploy path with no window. It answers the questions
			// that only the run platform can answer. See SelfTest.
			if (args.Length == 2 && args[0] == SelfTest.Switch) return SelfTest.Run(args[1]);

			// The deploy test installs both example mods into a scratch copy of the game.
			// It delivers the success criterion of the project brief with no window.
			if (args.Length >= 4 && args[0] == DeployTest.Switch)
			{
				// A fifth argument of "keep" leaves the deploy in place, so that somebody can
				// start the game and look at the result.
				bool revert = args.Length < 5 || args[4] != "keep";

				return DeployTest.Run(args[1], args[2], args[3], revert);
			}

			var application = new App();
			application.InitializeComponent();
			return application.Run();
		}
	}
}
