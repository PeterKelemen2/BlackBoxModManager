using System.Windows;

namespace BlackboxModManager.App.Views
{
	/// <summary>
	/// Every path setting of this application, in one window.
	///
	/// The window binds to the <c>MainViewModel</c> and holds no logic. Read
	/// docs/roadmap/12-minimal-ui.md, Part H, before you add any.
	/// </summary>
	public partial class ConfigWindow : Window
	{
		public ConfigWindow(object model)
		{
			this.InitializeComponent();

			this.DataContext = model;
		}

		/// <summary>
		/// Opens the window over its owner and waits for it.
		///
		/// <b>The owner takes every dialog for as long as this window is open.</b> The buttons
		/// here run the same commands that the main window runs, and those commands open a
		/// directory picker through <c>IUserInteraction</c>. A picker owned by the main window
		/// would take a disabled owner, because a modal child disables it. The picker can then
		/// open behind this window and take no input.
		///
		/// The finally block clears the property whatever happens. A main window that keeps a
		/// closed window as its dialog owner opens every later picker against a dead handle.
		/// </summary>
		public static void Show(MainWindow owner, object model)
		{
			var window = new ConfigWindow(model);

			if (owner != null && owner.IsLoaded) window.Owner = owner;

			if (owner is null)
			{
				window.ShowDialog();
				return;
			}

			owner.DialogOwner = window;

			try
			{
				window.ShowDialog();
			}
			finally
			{
				owner.DialogOwner = null;
			}
		}
	}
}
