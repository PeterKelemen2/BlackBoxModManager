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

			// This must run before the first window opens. A window keeps the render mode
			// that it started with. See Rendering.
			Rendering.Apply();

			var application = new App();
			application.InitializeComponent();

			// Shows the error dialog and exits.
			//
			// A XAML error in a dialog surfaces when the dialog opens, and the error dialog
			// opens when something already went wrong. That is the worst moment to find out.
			// This switch opens it on demand, so the run platform answers the question first.
			if (args.Length == 1 && args[0] == DialogSwitch) return ShowDialogTest();

			return application.Run();
		}

		/// <summary>The argument that opens the error dialog and exits.</summary>
		private const string DialogSwitch = "--dialogtest";

		private static int ShowDialogTest()
		{
			Views.MessageWindow.Show(null, "The operation failed.", "The operation failed.",
				"This is a sample error. The Copy error button puts this whole text on the " +
				"clipboard, including the heading above." + Environment.NewLine + Environment.NewLine +
				"A real message names a path, a mod, a script line, or a message from one of the " +
				"three libraries. Those are long, so the box scrolls and the text selects." +
				Environment.NewLine + Environment.NewLine +
				"Press Copy error, then paste somewhere to confirm that the clipboard of the " +
				"platform accepts it.",
				"Copy error");

			return 0;
		}
	}
}
