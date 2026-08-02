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

			var application = new App();
			application.InitializeComponent();
			return application.Run();
		}
	}
}
