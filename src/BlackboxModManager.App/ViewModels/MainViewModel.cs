using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackboxModManager.App.Services;
using BlackboxModManager.App.Theme;
using BlackboxModManager.App.Views;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Staging;
using BlackboxModManager.Core.Store;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikki.Core;
using Velopack;

namespace BlackboxModManager.App.ViewModels
{
	/// <summary>
	/// The operation that runs right now.
	///
	/// <c>IsBusy</c> answers whether one runs. This answers which one, and the deploy button
	/// and the revert button each draw a spinner from that answer. Only one operation runs at
	/// a time, so the two buttons never spin together.
	/// </summary>
	public enum RunningWork
	{
		/// <summary>Nothing runs.</summary>
		None = 0,

		Deploy,

		Revert,

		/// <summary>An import, a scan, or another operation that draws no spinner of its own.</summary>
		Other,
	}

	/// <summary>
	/// The window.
	///
	/// The view model owns the state and the commands. Every long operation runs on a
	/// background thread, and only one runs at a time. The library statics of Nikki are
	/// process-global, so a second deploy on a second thread would corrupt the first. See
	/// defect 8.
	/// </summary>
	public sealed partial class MainViewModel : ObservableObject
	{
		private readonly IUserInteraction _ask;
		private readonly GameInstallService _games = new GameInstallService();
		private readonly BinaryInstallService _binary = new BinaryInstallService();
		private readonly ProfileStore _profiles = new ProfileStore();

		// The store and the importer follow the setting, so a move of the store takes effect
		// with no restart. Every command reads these fields and never a captured copy.
		private ModStore _store;
		private ModImporter _importer;

		private Settings _settings;
		private Profile _profile;
		private GameInstall _install;
		private BinaryInstall _binaryInstall;
		private GameDefinition _game;

		/// <summary>
		/// The games that this application manages. The picker shows this list.
		/// </summary>
		public ObservableCollection<GameDefinition> Games { get; } =
			new ObservableCollection<GameDefinition>(GameCatalog.All);

		/// <summary>The game that the window manages.</summary>
		public GameINT Game => this._game.Game;

		public ObservableCollection<ModRowViewModel> Mods { get; } = new ObservableCollection<ModRowViewModel>();

		/// <summary>
		/// The imports that still run. The mod list draws one row for each of them, under the
		/// mods, which is where the finished mod lands.
		///
		/// One import runs at a time, so this holds one row or none. It is a collection and
		/// not one property, because the list binds to it and an empty collection draws
		/// nothing with no converter.
		/// </summary>
		public ObservableCollection<ImportRowViewModel> Imports { get; } =
			new ObservableCollection<ImportRowViewModel>();

		public ObservableCollection<string> ProfileNames { get; } = new ObservableCollection<string>();

		public ObservableCollection<string> Log { get; } = new ObservableCollection<string>();

		/// <summary>
		/// The variants of the mod that the user selected. This is empty unless that mod is a
		/// Binary mod.
		/// </summary>
		public ObservableCollection<VariantRowViewModel> Variants { get; } =
			new ObservableCollection<VariantRowViewModel>();

		/// <summary>
		/// One line per conflict, plus one per variant that the check could not read.
		/// </summary>
		public ObservableCollection<string> Conflicts { get; } = new ObservableCollection<string>();

		/// <summary>
		/// The settings files of the mod that the user selected. This is empty unless that mod
		/// ships an <c>.ini</c> file.
		/// </summary>
		public ObservableCollection<SettingsFileViewModel> SettingsFiles { get; } =
			new ObservableCollection<SettingsFileViewModel>();

		/// <summary>One row per ASI loader file that the enabled mods supply.</summary>
		public ObservableCollection<LoaderRowViewModel> Loaders { get; } =
			new ObservableCollection<LoaderRowViewModel>();

		public MainViewModel(IUserInteraction ask)
		{
			this._ask = ask ?? throw new ArgumentNullException(nameof(ask));

			AppPaths.CreateRoot();
			this._settings = SettingsStore.Load();
			this._game = StoredGame(this._settings);
			this._selectedGame = this._game;

			// The field and not the property. The setter saves the settings file, and the
			// settings file is where this value just came from. The property setter's other
			// work still needs to run once, by hand.
			//
			// The look comes first. RefreshHeroLook reads it, and a call before this line
			// would draw the corner accent whatever the file says.
			this._heroBackground = StoredHeroBackground(this._settings);
			this.RefreshHeroLook(this._game);

			this._fullVerify = this._settings.FullVerify;
			this._checkForUpdatesAtStart = this._settings.CheckForUpdatesAtStart;

			this.OpenStore();

			// The version comes first. A user who reports a defect pastes this log, and the
			// first line then names the build that produced everything below it.
			this.Write($"This build is version {AppVersion.Display}.");
			this.Write(Rendering.Report);
			this.Write($"The application data directory is {AppPaths.Root}.");
			this.Write($"The mod store is {this._store.Root}." +
				(this._settings.ModStoreIsDefault ? String.Empty : " The settings name that place."));
			this.Write($"This build manages {this.Games.Count} games: {String.Join(", ", this.Games)}.");

			if (GameCatalog.Absent.Count > 0)
			{
				this.Write($"It does not manage {String.Join(", ", GameCatalog.Absent)} yet. " +
					"A descriptor for each one needs a listing of a real install.");
			}

			this.RefreshGame();
			this.RefreshBinary();
			this.RefreshProfiles();
		}

		/// <summary>
		/// The game of the settings file, or the first game of the catalog. A stored name that
		/// the catalog no longer holds falls back in the same way.
		/// </summary>
		private static GameDefinition StoredGame(Settings settings)
		{
			if (Enum.TryParse(settings.LastGame, ignoreCase: true, out GameINT game))
			{
				GameDefinition definition = GameCatalog.Find(game);

				if (definition != null) return definition;
			}

			return GameCatalog.All[0];
		}

		/// <summary>
		/// The window look of the settings file, or the corner accent. A missing key and a
		/// name that this build does not hold both fall back to the corner accent, which is
		/// what every build before this one drew.
		/// </summary>
		private static HeroBackground StoredHeroBackground(Settings settings)
		{
			return Enum.TryParse(settings.HeroBackground, ignoreCase: true, out HeroBackground look)
				&& Enum.IsDefined(typeof(HeroBackground), look)
					? look
					: HeroBackground.Corner;
		}

		/// <summary>
		/// Writes one change into the settings file, on top of what the file holds now.
		///
		/// <b>Three objects write this file.</b> This view model keeps a copy from its last
		/// read. <c>GameInstallService</c> and <c>BinaryInstallService</c> write their own keys
		/// straight to disk. A save of the old copy drops every key that those two services
		/// wrote after that read.
		///
		/// That is why a game install path went away. <c>BrowseGame</c> wrote the path to disk.
		/// The next game switch saved the old copy, which held no such path.
		///
		/// <c>SettingsStore.Update</c> reads the file again and merges. This method keeps what
		/// it wrote, so the field holds the same values as the file.
		/// </summary>
		private void SaveSettings(Action<Settings> change)
		{
			this._settings = SettingsStore.Update(change);
		}

		/// <summary>
		/// Reads the settings file again into the field.
		///
		/// Call this after a service writes the file. The field is the source of every later
		/// read and of every later merge. See <see cref="SaveSettings"/>.
		/// </summary>
		private void ReloadSettings()
		{
			this._settings = SettingsStore.Load();
		}

		// ---------------------------------------------------------------- state

		[ObservableProperty]
		private string _gameStatus = String.Empty;

		// ------------------------------------------------------------ the four paths
		//
		// Every path setting of the settings window draws the same three lines. The status
		// line says what the state is. The path box holds the path and nothing else. The hint
		// line says what the choice costs. Step 17, Part C, made all four match.
		//
		// A group whose path is empty hides its box and grays its two buttons. An empty box
		// with a border of no width reads as a layout defect.

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasGamePath))]
		private string _gamePath = String.Empty;

		public bool HasGamePath => this.GamePath.Length > 0;

		[ObservableProperty]
		private string _binaryStatus = String.Empty;

		/// <summary>The Binary 2.8.3 install directory, with no sentence around it.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasBinaryPath))]
		private string _binaryPath = String.Empty;

		public bool HasBinaryPath => this.BinaryPath.Length > 0;

		/// <summary>The mod store directory, with no sentence around it.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasModStorePath))]
		private string _modStorePath = String.Empty;

		public bool HasModStorePath => this.ModStorePath.Length > 0;

		[ObservableProperty]
		private string _deployedState = String.Empty;

		[ObservableProperty]
		private string _status = "Ready.";

		[ObservableProperty]
		private bool _fullVerify;

		/// <summary>
		/// Keeps the answer of the config window. The check box moved off the action bar in
		/// step 12, and a setting that only a separate window shows has to survive a restart.
		/// </summary>
		partial void OnFullVerifyChanged(bool value)
		{
			this.SaveSettings(settings => settings.FullVerify = value);
		}

		[ObservableProperty]
		private bool _checkForUpdatesAtStart;

		/// <summary>Keeps the answer of the config window, in the same way as FullVerify.</summary>
		partial void OnCheckForUpdatesAtStartChanged(bool value)
		{
			this.SaveSettings(settings => settings.CheckForUpdatesAtStart = value);
		}

		/// <summary>
		/// Which look the window draws behind itself. The settings window sets it, and the
		/// three attributes below keep the three radio buttons of that window in step.
		/// </summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HeroBackgroundIsOff))]
		[NotifyPropertyChangedFor(nameof(HeroBackgroundIsCorner))]
		[NotifyPropertyChangedFor(nameof(HeroBackgroundIsFull))]
		private HeroBackground _heroBackground;

		/// <summary>
		/// Keeps the answer of the config window, in the same way as FullVerify, and draws
		/// the new look at once. The config window shares this view model, so the main
		/// window changes while that window is still open.
		/// </summary>
		partial void OnHeroBackgroundChanged(HeroBackground value)
		{
			this.SaveSettings(settings => settings.HeroBackground = value.ToString());
			this.RefreshHeroLook(this._game);
		}

		/// <summary>
		/// The look as three booleans, so that three radio buttons can bind to it. This
		/// matches the shape that BinaryRouteIsCli already uses for a toggle.
		///
		/// <b>Each setter acts on true alone.</b> WPF pushes false into the two buttons that
		/// lose the group, and a setter that answered false would fight the one that won.
		/// </summary>
		public bool HeroBackgroundIsOff
		{
			get => this.HeroBackground == HeroBackground.Off;
			set { if (value) this.HeroBackground = HeroBackground.Off; }
		}

		/// <summary>The corner accent, in the same way as HeroBackgroundIsOff.</summary>
		public bool HeroBackgroundIsCorner
		{
			get => this.HeroBackground == HeroBackground.Corner;
			set { if (value) this.HeroBackground = HeroBackground.Corner; }
		}

		/// <summary>The full wash, in the same way as HeroBackgroundIsOff.</summary>
		public bool HeroBackgroundIsFull
		{
			get => this.HeroBackground == HeroBackground.Full;
			set { if (value) this.HeroBackground = HeroBackground.Full; }
		}

		/// <summary>
		/// The version for the status bar. This never changes while the application runs, so it
		/// needs no ObservableProperty.
		/// </summary>
		public string VersionLabel => $"Version {AppVersion.Display}";

		/// <summary>
		/// Which code applies every Binary mod of the profile. A mod can override this on its
		/// own row.
		///
		/// <b>This lives on the profile and not in the settings.</b> A profile fully determines
		/// the deployed result, and the route changes the bytes that a deploy writes. So it
		/// saves through SaveProfile.
		/// </summary>
		private BinaryRoute _binaryRoute;

		public BinaryRoute BinaryRoute
		{
			get => this._binaryRoute;
			set
			{
				if (this._binaryRoute == value) return;

				this._binaryRoute = value;
				this.OnPropertyChanged();
				this.OnPropertyChanged(nameof(this.BinaryRouteIsCli));

				if (this._profile is null) return;

				this._profile.BinaryRoute = value;
				this.SaveProfile();

				this.Write(value == BinaryRoute.BinaryCli
					? "The profile now deploys every Binary mod through the Binary 2.8.3 install. " +
						"Binary writes in place, so every deploy copies the whole game directory."
					: "The profile now deploys every Binary mod through the container engine of this " +
						"application.");
			}
		}

		/// <summary>
		/// The route as one boolean, so that a toggle can bind to it. The toggle matches the
		/// shape that the config window already uses for FullVerify.
		/// </summary>
		public bool BinaryRouteIsCli
		{
			get => this.BinaryRoute == BinaryRoute.BinaryCli;
			set => this.BinaryRoute = value ? BinaryRoute.BinaryCli : BinaryRoute.Native;
		}

		/// <summary>
		/// The one line of state that the status bar shows. It joins the game and the deployed
		/// state, because step 12 took the five status lines off the main window.
		///
		/// The full detail lives in the config window and in the tooltip of that line.
		/// </summary>
		[ObservableProperty]
		private string _stateSummary = String.Empty;

		private void RefreshStateSummary()
		{
			string game = this.GameStatus ?? String.Empty;
			string deployed = this.DeployedState ?? String.Empty;

			this.StateSummary = String.Join(" ", new[] { game, deployed })
				.Trim();
		}

		/// <summary>
		/// True when the profile holds a change that the game directory does not. The action
		/// row shows a warning while this is true.
		/// </summary>
		[ObservableProperty]
		private bool _hasPendingChanges;

		/// <summary>The one line of that warning.</summary>
		[ObservableProperty]
		private string _pendingMessage = String.Empty;

		/// <summary>
		/// True when the last deploy, revert, or other run failed. The action row shows the
		/// message while this is true.
		///
		/// <b>The modal dialog reports the failure once, and this banner keeps reporting it.</b>
		/// A user who closes the dialog must still see which run failed and why. RunAsync sets
		/// this beside the dialog, not instead of it. DismissDeployError and the next run both
		/// clear it.
		/// </summary>
		[ObservableProperty]
		private bool _hasDeployError;

		/// <summary>The name of the run and the message of the exception that it threw.</summary>
		[ObservableProperty]
		private string _deployError = String.Empty;

		/// <summary>
		/// The fingerprint of the profile that the game directory holds. A null value means
		/// that no workspace answered the question yet.
		/// </summary>
		private string _deployedFingerprint;

		/// <summary>
		/// Compares the profile against the game directory and sets the warning.
		///
		/// <b>Call this after every change to the profile and after every deploy.</b>
		/// <c>SaveProfile</c>, <c>RefreshMods</c>, and <c>RefreshDeployedState</c> all end
		/// here, and every path that changes the profile passes through one of those three.
		///
		/// The comparison reads the fingerprint of the profile in memory. See
		/// <c>ProfileFingerprint</c> for what reaches that string and what does not.
		/// </summary>
		private void RefreshPending()
		{
			if (this._install is null || this._profile is null)
			{
				this.HasPendingChanges = false;
				this.PendingMessage = String.Empty;

				return;
			}

			// A workspace of an older build holds no fingerprint. We cannot prove that the
			// game directory matches the profile, so we say that and ask for a deploy.
			if (this._deployedFingerprint is null)
			{
				this.HasPendingChanges = true;
				this.PendingMessage = "The game directory does not report what it holds. Deploy to be sure.";

				return;
			}

			bool same = String.Equals(
				this._deployedFingerprint, ProfileFingerprint.Of(this._profile), StringComparison.Ordinal);

			this.HasPendingChanges = !same;
			this.PendingMessage = same
				? String.Empty
				: "Changes not deployed yet.";
		}

		[ObservableProperty]
		private string _detailsHeader = "Select a mod.";

		[ObservableProperty]
		private string _settingsHeader = "Select a mod.";

		[ObservableProperty]
		private string _loaderHeader = String.Empty;

		/// <summary>
		/// True while one loader file has more than one supplier and the profile names none of
		/// them. The Loader tab draws a mark while this is true. See step 17, Part I.
		/// </summary>
		[ObservableProperty]
		private bool _loaderNeedsAnswer;

		[ObservableProperty]
		private string _modStoreStatus = String.Empty;

		/// <summary>
		/// The one line that a path button of the settings window writes. Open and Copy path
		/// both report here, because that window shows no status bar.
		/// </summary>
		[ObservableProperty]
		private string _settingsReport = String.Empty;

		/// <summary>
		/// Shows one directory in the file manager of the platform.
		///
		/// <b>Copy path must keep working when this fails.</b> A Wine prefix has no guaranteed
		/// file manager. See step 9, fact 9, and <see cref="DirectoryOpener"/>.
		/// </summary>
		[RelayCommand]
		private void OpenDirectory(string path)
		{
			this.SettingsReport = DirectoryOpener.Open(path);
		}

		/// <summary>Puts one path on the clipboard. This works when Open does not.</summary>
		[RelayCommand]
		private void CopyPath(string path)
		{
			if (String.IsNullOrEmpty(path)) return;

			try
			{
				// The second argument keeps the text on the clipboard after this process ends.
				Clipboard.SetDataObject(path, true);

				this.SettingsReport = $"Copied {path}";
			}
			catch (Exception ex)
			{
				this.SettingsReport = "The clipboard refused the text. Select the path above " +
					$"and press Control C. {ex.Message}";
			}
		}

		private GameDefinition _selectedGame;

		/// <summary>
		/// The game that the picker shows.
		///
		/// A switch reloads the install, the profiles, and the mods. It touches no file of any
		/// game, so it is safe while nothing else runs. The setter refuses a switch during a
		/// long operation, because a deploy holds the install of the game that it started with.
		/// </summary>
		public GameDefinition SelectedGame
		{
			get => this._selectedGame;
			set
			{
				if (value is null) return;

				if (this.IsBusy)
				{
					// The picker already shows the new value. Put the old one back.
					this.OnPropertyChanged();
					this._ask.ShowMessage("An operation runs. Wait for it, then switch the game.");
					return;
				}

				if (!this.SetProperty(ref this._selectedGame, value)) return;

				this.SwitchGame(value);
			}
		}

		private void SwitchGame(GameDefinition definition)
		{
			this._game = definition;
			this.SaveSettings(settings => settings.LastGame = definition.Game.ToString());

			this.OnPropertyChanged(nameof(this.Game));
			this.Write($"The window now manages {definition.DisplayName}.");

			this.RefreshHeroLook(definition);

			this.SelectedMod = null;
			this.RefreshGame();
			this.RefreshProfiles();
		}

		private BitmapSource _heroCornerImage;

		/// <summary>
		/// The selected game's corner accent, already blended over the window background.
		/// See Theme/HeroPalette.cs — this is a plain opaque bitmap, never a live
		/// <c>Opacity</c>.
		///
		/// It is null in every look but <see cref="HeroBackground.Corner"/>.
		/// </summary>
		public BitmapSource HeroCornerImage
		{
			get => this._heroCornerImage;
			private set => this.SetProperty(ref this._heroCornerImage, value);
		}

		private BitmapSource _heroFullImage;

		/// <summary>
		/// The selected game's full-window wash, in the same way as
		/// <see cref="HeroCornerImage"/>. It is null in every look but
		/// <see cref="HeroBackground.Full"/>.
		/// </summary>
		public BitmapSource HeroFullImage
		{
			get => this._heroFullImage;
			private set => this.SetProperty(ref this._heroFullImage, value);
		}

		/// <summary>
		/// Puts the image of one game and the current look into the property that draws it,
		/// and nulls the other one.
		///
		/// <b>Null is how a look hides.</b> An Image with no Source draws nothing and answers
		/// no hit test, so MainWindow needs no Visibility, no trigger, and no converter for
		/// the two elements that these properties feed.
		/// </summary>
		private void RefreshHeroLook(GameDefinition definition)
		{
			Color surfaceBase = ((SolidColorBrush)Application.Current.Resources["SurfaceBase"]).Color;
			HeroBackground look = this.HeroBackground;

			BitmapSource image = HeroPalette.ImageFor(
				definition.Game,
				look,
				definition.HeroImage,
				surfaceBase);

			this.HeroCornerImage = look == HeroBackground.Corner ? image : null;
			this.HeroFullImage = look == HeroBackground.Full ? image : null;
		}

		private ModRowViewModel _selectedMod;

		public ModRowViewModel SelectedMod
		{
			get => this._selectedMod;
			set
			{
				ModRowViewModel previous = this._selectedMod;

				if (!this.SetProperty(ref this._selectedMod, value)) return;

				if (previous != null) previous.IsSelected = false;
				if (value != null) value.IsSelected = true;

				// The four commands that act on one mod. Their buttons carry an icon and no
				// label now, so a button that does nothing tells the user nothing. See
				// docs/roadmap/12-minimal-ui.md, Part F.
				this.NotifySelectionCommands();
				this.NotifySelectedRoute();

				this.LoadVariants(value);
				this.LoadSettings(value);
			}
		}

		/// <summary>
		/// Which route the context menu marks for the selected mod.
		///
		/// <b>The context menu binds to this view model and not to the row.</b> A ContextMenu
		/// sits outside the visual tree of the window, so the menu takes the MainViewModel as
		/// its own DataContext and every item binds by name. See the ModRowTemplate comment.
		///
		/// A mod of another kind marks nothing. Only a Binary mod reads a route.
		/// </summary>
		public bool SelectedModRouteIsInherit =>
			this.SelectedMod != null && this.SelectedMod.ShowsRoute
				&& this.SelectedMod.Route == BinaryRouteChoice.Inherit;

		public bool SelectedModRouteIsNative =>
			this.SelectedMod != null && this.SelectedMod.ShowsRoute
				&& this.SelectedMod.Route == BinaryRouteChoice.Native;

		public bool SelectedModRouteIsBinaryCli =>
			this.SelectedMod != null && this.SelectedMod.ShowsRoute
				&& this.SelectedMod.Route == BinaryRouteChoice.BinaryCli;

		private void NotifySelectedRoute()
		{
			this.OnPropertyChanged(nameof(this.SelectedModRouteIsInherit));
			this.OnPropertyChanged(nameof(this.SelectedModRouteIsNative));
			this.OnPropertyChanged(nameof(this.SelectedModRouteIsBinaryCli));
		}

		/// <summary>
		/// True when one command can act on the selected mod. <c>Remove</c>, <c>Move up</c>,
		/// <c>Move down</c>, and <c>Set game</c> all read this.
		/// </summary>
		private bool CanActOnMod() => this.IsIdle && this._selectedMod != null;

		private void NotifySelectionCommands()
		{
			this.RemoveModCommand.NotifyCanExecuteChanged();
			this.MoveUpCommand.NotifyCanExecuteChanged();
			this.MoveDownCommand.NotifyCanExecuteChanged();
			this.SetModGameCommand.NotifyCanExecuteChanged();

			// SetModRoute reached neither notify list until step 17, M2. The context menu worked
			// by luck: the DataContext binding of that menu resolves on each open, and the
			// command re-read its state then.
			this.SetModRouteCommand.NotifyCanExecuteChanged();
		}

		/// <summary>
		/// Moves the selection by one row and stops at each end. The Up key and the Down key
		/// of the mod panel call this. See step 17, Part G.
		///
		/// The mod list is an <c>ItemsControl</c>, which has no selection of its own. A
		/// <c>ListBox</c> would bring one, and its item container and its mouse handling would
		/// both fight the drag of step 11.
		/// </summary>
		public void MoveSelection(int offset)
		{
			if (this.Mods.Count == 0) return;

			int index = this.SelectedMod is null ? -1 : this.Mods.IndexOf(this.SelectedMod);

			// No selection yet. Down takes the first row and Up takes the last one.
			if (index < 0)
			{
				this.SelectedMod = offset >= 0 ? this.Mods[0] : this.Mods[this.Mods.Count - 1];
				return;
			}

			int wanted = index + offset;

			if (wanted < 0 || wanted >= this.Mods.Count) return;

			this.SelectedMod = this.Mods[wanted];
		}

		/// <summary>
		/// Drops the selection.
		///
		/// <b>Nothing cleared the selection before step 17, Part G.</b> <c>CanActOnMod</c>
		/// therefore reported true forever after the first click, and the three toolbar buttons
		/// that gray out on no selection grayed out once and never again.
		/// </summary>
		public void ClearSelection() => this.SelectedMod = null;

		private bool _busy;

		/// <summary>
		/// True while a long operation runs. Every command that touches the disk stops
		/// while this is true, so no two of them run at once.
		/// </summary>
		public bool IsBusy
		{
			get => this._busy;
			private set
			{
				if (!this.SetProperty(ref this._busy, value)) return;

				this.OnPropertyChanged(nameof(this.IsIdle));
				this.NotifyCommands();
			}
		}

		public bool IsIdle => !this._busy;

		private RunningWork _work;

		/// <summary>
		/// Which operation runs right now. <c>RunAsync</c> sets this at the start and it puts
		/// <c>None</c> back at the end.
		/// </summary>
		public RunningWork Work
		{
			get => this._work;
			private set
			{
				if (!this.SetProperty(ref this._work, value)) return;

				this.OnPropertyChanged(nameof(this.IsDeploying));
				this.OnPropertyChanged(nameof(this.IsReverting));
			}
		}

		/// <summary>True while the deploy runs. The deploy button draws its spinner from this.</summary>
		public bool IsDeploying => this._work == RunningWork.Deploy;

		/// <summary>True while the revert runs. The revert button draws its spinner from this.</summary>
		public bool IsReverting => this._work == RunningWork.Revert;

		private CancellationTokenSource _cancellation;

		/// <summary>
		/// True while an operation runs that the user can stop.
		///
		/// <b>A deploy of a large container mod runs for minutes.</b> Without a Cancel button
		/// the user ends the process, and an ended deploy is the condition that damaged a
		/// vanilla baseline before. See defect 16.
		/// </summary>
		public bool CanCancel => this._cancellation != null && !this._cancellation.IsCancellationRequested;

		/// <summary>Asks the running operation to stop at its next safe point.</summary>
		[RelayCommand(CanExecute = nameof(CanCancel))]
		private void CancelWork()
		{
			CancellationTokenSource source = this._cancellation;

			if (source is null) return;

			this.Write("Cancel asked. The operation stops at its next safe point.");
			this.Status = "Canceling.";

			try
			{
				source.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// The operation finished between the button and this line. Nothing to stop.
			}

			this.OnPropertyChanged(nameof(this.CanCancel));
			this.CancelWorkCommand.NotifyCanExecuteChanged();
		}

		public bool IsGameReady => this._install != null;

		private string _selectedProfileName;

		public string SelectedProfileName
		{
			get => this._selectedProfileName;
			set
			{
				if (!this.SetProperty(ref this._selectedProfileName, value)) return;

				this.LoadProfile(value);
			}
		}

		// ---------------------------------------------------------------- the game

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void BrowseGame()
		{
			string path = this._ask.PickDirectory(
				$"Choose the install directory of {GameCatalog.Demand(this.Game).DisplayName}.", this.GamePath);

			if (path is null) return;

			GameInstallStatus status = this._games.Store(this.Game, path);

			if (!status.IsUsable)
			{
				this._ask.ShowError(status.Message);
				return;
			}

			// The service wrote the file. Read it again, so that the next save merges on top
			// of the new path instead of dropping it.
			this.ReloadSettings();

			this.RefreshGame();
			this.Write($"The game install is {status.Root}.");
		}

		/// <summary>
		/// Searches the common install directories and asks the user to pick one result.
		///
		/// <b>One dialog lists every candidate.</b> The search asked one question for each
		/// candidate until step 17, Part H. Three candidates then meant three modal dialogs,
		/// and a user who answered No to all of them got no message and no log line.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private async Task DetectGameAsync()
		{
			GameDefinition definition = GameCatalog.Demand(this.Game);

			this.Status = "Look for the game.";
			this.Write($"Look for an install of {definition.DisplayName}.");

			IReadOnlyList<string> candidates = await Task.Run(
				() => GameInstallLocator.FindCandidates(definition));

			this.Status = "Ready.";

			if (candidates.Count == 0)
			{
				this._ask.ShowMessage(
					$"No directory here looks like {definition.DisplayName}. Use Browse instead.");
				return;
			}

			var choices = new List<UserChoice>(candidates.Count);

			foreach (string candidate in candidates)
			{
				choices.Add(new UserChoice(candidate, Path.GetFileName(
					Path.TrimEndingDirectorySeparator(candidate)), candidate));
			}

			// Pass no current key. The locator ranks nothing, and the dialog must not suggest
			// that it does. Step 9, fact 6, records the same rule for the ASI loader.
			string answer = this._ask.PickChoice(
				$"Which directory holds {definition.DisplayName}?\n\n" +
				"These are guesses, matched on the directory name alone.",
				choices);

			if (String.IsNullOrEmpty(answer))
			{
				this.Write("The search found " +
					$"{candidates.Count} directories. You chose none of them, so nothing changed.");

				return;
			}

			GameInstallStatus status = this._games.Store(this.Game, answer);

			if (!status.IsUsable)
			{
				this._ask.ShowError(status.Message);
				return;
			}

			this.ReloadSettings();

			this.RefreshGame();
			this.Write($"The game install is {status.Root}.");
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void SetBinary()
		{
			string path = this._ask.PickDirectory(
				"Choose the directory of the Binary 2.8.3 install.", this.BinaryPath);

			if (path is null) return;

			BinaryInstallStatus status = this._binary.Store(path);

			if (!status.IsUsable)
			{
				this._ask.ShowError(status.Message);
				return;
			}

			this.ReloadSettings();

			this.RefreshBinary();
		}

		private void RefreshGame()
		{
			GameInstallResolution resolution = this._games.Resolve(this.Game);

			this._install = resolution.Install;
			this.GamePath = resolution.Status.Root ?? String.Empty;
			this.GameStatus = resolution.IsUsable
				? $"{this._game.DisplayName} is ready."
				: resolution.Status.Message;

			// A Binary mod needs the containers. A drop-in mod does not, so this reports and
			// blocks nothing.
			if (this._install != null)
			{
				IReadOnlyList<string> missing = this._install.MissingContainers();

				if (missing.Count > 0)
				{
					this.Write($"The install holds no {String.Join(", ", missing)}. " +
						"A Binary mod that edits one of those containers cannot deploy.");
				}
			}

			this.OnPropertyChanged(nameof(this.IsGameReady));
			this.RefreshWorkspace();
			this.RefreshDeployedState();
			this.NotifyCommands();
		}

		private void RefreshBinary()
		{
			BinaryInstallResolution resolution = this._binary.Resolve();

			this._binaryInstall = resolution.Install;

			// The path leaves the status line and goes into the box of its own group. Step 17,
			// Part C, gave all four path settings the same shape.
			this.BinaryPath = resolution.IsUsable ? resolution.Install.Root : String.Empty;

			this.BinaryStatus = resolution.IsUsable
				? $"Binary {resolution.Install.Version} is ready."
				: resolution.Status.Message + " Only Binary mods need it.";
		}

		/// <summary>
		/// Reads what the game directory holds now.
		///
		/// Every path out of this method ends at <see cref="RefreshStateSummary"/>, and
		/// <see cref="RefreshGame"/> sets the game line before it calls this. So the status bar
		/// never shows one half of the state.
		/// </summary>
		private void RefreshDeployedState()
		{
			if (this._install is null)
			{
				this.DeployedState = String.Empty;
				this._deployedFingerprint = null;
				this.RefreshPending();
				this.RefreshStateSummary();
				return;
			}

			try
			{
				GameWorkspace workspace = this.Service().WorkspaceOf(this._install);
				WorkspaceState state = workspace.ReadState();

				this.DeployedState = state.IsVanilla
					? "Vanilla."
					: $"Profile \"{state.DeployedProfile}\" deployed, " +
						$"{state.DeployedFileCount} files from mods.";

				// A vanilla directory holds the result of a profile that enables nothing. Name
				// that fingerprint, so an all-off profile against a vanilla game reports no
				// pending change.
				this._deployedFingerprint = state.IsVanilla
					? ProfileFingerprint.Vanilla
					: state.DeployedFingerprint;
			}
			catch (Exception ex)
			{
				this.DeployedState = ex.Message;
				this._deployedFingerprint = null;
			}

			this.RefreshPending();
			this.RefreshStateSummary();
		}

		// ---------------------------------------------------------------- profiles

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void NewProfile()
		{
			string name = this._ask.AskText("Name the new profile.", "New profile");

			if (name is null) return;

			if (this._profiles.Find(this.Game, name) != null)
			{
				this._ask.ShowError($"A profile named \"{name}\" already exists.");
				return;
			}

			this._profiles.Ensure(this.Game, name);
			this.RefreshProfiles(name);
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void RenameProfile()
		{
			if (this._profile is null) return;

			string name = this._ask.AskText("Name the profile.", this._profile.Name);

			if (name is null || name == this._profile.Name) return;

			try
			{
				this._profiles.Rename(this.Game, this._profile.Name, name);
				this.RefreshProfiles(name);
			}
			catch (Exception ex)
			{
				this._ask.ShowError(ex.Message);
			}
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void DeleteProfile()
		{
			if (this._profile is null) return;

			if (this.ProfileNames.Count == 1)
			{
				this._ask.ShowError("This is the only profile. A game keeps at least one.");
				return;
			}

			if (!this._ask.Confirm(
				$"Delete the profile \"{this._profile.Name}\"? The mods stay in the store.",
				"Delete", destructive: true)) return;

			this._profiles.Delete(this.Game, this._profile.Name);
			this.RefreshProfiles();
		}

		private void RefreshProfiles(string select = null)
		{
			IReadOnlyList<Profile> found = this._profiles.List(this.Game);

			if (found.Count == 0)
			{
				this._profiles.Ensure(this.Game, ProfileStore.DefaultProfileName);
				found = this._profiles.List(this.Game);
			}

			this.ProfileNames.Clear();
			foreach (Profile profile in found) this.ProfileNames.Add(profile.Name);

			string wanted = select;

			if (wanted is null || !this.ProfileNames.Contains(wanted))
			{
				this._settings.ActiveProfiles.TryGetValue(this.Game.ToString(), out wanted);
			}

			if (wanted is null || !this.ProfileNames.Contains(wanted)) wanted = this.ProfileNames[0];

			// The setter loads the profile and the mod list.
			this.SelectedProfileName = wanted;
		}

		private void LoadProfile(string name)
		{
			this._profile = this._profiles.Find(this.Game, name);

			if (this._profile is null) return;

			this.SaveSettings(settings => settings.ActiveProfiles[this.Game.ToString()] = name);

			this.RefreshMods();
		}

		private void SaveProfile()
		{
			if (this._profile is null) return;

			this._profiles.Save(this.Game, this._profile);

			// Every change to the profile lands here, so the warning of the action row does
			// as well.
			this.RefreshPending();
		}

		// ---------------------------------------------------------------- mods

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private async Task ImportArchiveAsync()
		{
			string path = this._ask.PickFile(
				"Choose a mod archive.",
				"Mod archives|*.zip;*.rar;*.7z|Every file|*.*");

			if (path != null) await this.ImportAsync(path);
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private async Task ImportFolderAsync()
		{
			// The picker starts where the last import read. See step 17, M3.
			string path = this._ask.PickDirectory(
				"Choose the directory of a mod.", this._settings.LastImportDirectory);

			if (path != null) await this.ImportAsync(path);
		}

		/// <summary>
		/// Imports what the user dropped on the mod list.
		///
		/// <c>ModImporter.Import</c> reads an archive or a directory, so the drop needs no test
		/// of its own. This method returns at once during a long operation, because a drop
		/// carries no CanExecute and the window cannot gray a drop target out.
		///
		/// <b>It imports the first path and no more.</b> One import runs at a time, and the
		/// library statics of Nikki make a second one on a second thread unsafe. See defect 8.
		/// </summary>
		public async Task ImportDropAsync(IReadOnlyList<string> paths)
		{
			if (this.IsBusy || paths is null || paths.Count == 0) return;

			string source = paths[0];

			if (String.IsNullOrWhiteSpace(source)) return;

			if (paths.Count > 1)
			{
				this.Write($"The drop carried {paths.Count} paths. This application imports " +
					$"\"{source}\" and leaves the rest.");
			}

			await this.ImportAsync(source);
		}

		/// <summary>
		/// Imports one archive or one directory, and it shows the work while it runs.
		///
		/// The row goes into <see cref="Imports"/> before the work starts, so the list shows
		/// the mod at once. The finally block drops that row, so a failed import and a
		/// finished import both leave the list clean.
		///
		/// <b>A big archive takes minutes.</b> A solid 7z of a thousand files is the worst
		/// case, and the reason sits in docs/roadmap/98-known-upstream-defects.md, defect 14.
		/// The row and the bar exist because of that wait.
		/// </summary>
		private async Task ImportAsync(string source)
		{
			GameINT game = this.Game;
			var row = new ImportRowViewModel(source);

			// The identifier of the mod that the import added. The background work sets it and
			// the window thread reads it after the wait, so no lock is needed.
			string added = null;

			this.Imports.Add(row);

			// Progress<T> keeps the thread that builds it. This runs on the window thread, so
			// every report of the background thread lands back here.
			ImportStage stage = ImportStage.Unpack;
			bool counted = false;

			var progress = new Progress<ImportProgress>(step =>
			{
				row.Apply(step);
				this.Status = $"{row.Name} · {row.Line}";

				// One log line for each step, and one for the count. The step reports many
				// times each second, and the log must stay readable.
				if (!counted && step.Stage == ImportStage.Unpack && step.Total > 0)
				{
					counted = true;
					this.Write($"The source holds {step.Total} files.");
				}

				if (step.Stage == stage) return;

				stage = step.Stage;
				this.Write($"{ImportRowViewModel.StageText(step.Stage)}.");
			});

			try
			{
				await this.RunAsync($"Import {Path.GetFileName(source)}.", report =>
				{
					ModImportResult result = this._importer.Import(source, game, null, progress);
					added = result.Mod.Id;

					report($"The import added \"{result.Mod.Name}\" of kind {result.Mod.Kind} " +
						$"for {result.Mod.Game}, with {result.Content.Files.Count} files.");

					foreach (string note in result.Notes) report(note);
				});
			}
			finally
			{
				this.Imports.Remove(row);
			}

			// A mod that the user just added starts enabled. Reconcile puts a new entry in the
			// profile switched off, because it also runs for a mod that another game left
			// behind. An import is the one case where the user asked for this mod, so say so
			// before the list draws.
			if (added != null && this._profile != null && this.Game == game)
			{
				this._profile.Ensure(added).Enabled = true;
				this.SaveProfile();
			}

			// The next folder import starts beside this one. A failed import writes nothing,
			// so the picker keeps the place that worked last.
			if (added != null)
			{
				string parent = Directory.Exists(source)
					? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)))
					: Path.GetDirectoryName(Path.GetFullPath(source));

				if (!String.IsNullOrEmpty(parent))
				{
					this.SaveSettings(settings => settings.LastImportDirectory = parent);
				}
			}

			this.RefreshMods();
		}

		[RelayCommand(CanExecute = nameof(CanActOnMod))]
		private void RemoveMod()
		{
			ModRowViewModel row = this.SelectedMod;

			if (row is null) return;

			if (!this._ask.Confirm(
				$"Remove \"{row.Name}\" from the mod store? This deletes its files.",
				"Remove", destructive: true)) return;

			this._store.Remove(row.Id);
			this._profile?.Remove(row.Id);
			this.SaveProfile();
			this.RefreshMods();

			this.Write($"The store no longer holds \"{row.Name}\".");
		}

		/// <summary>
		/// Gives the selected mod the game that the window manages.
		///
		/// The store held mods with no game before metadata version 2, and such a mod shows
		/// under every game. This command ends that. It refuses a Binary mod, because the
		/// manifest of that mod names the game.
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanActOnMod))]
		private void SetModGame()
		{
			ModRowViewModel row = this.SelectedMod;

			if (row is null) return;

			try
			{
				this._store.Assign(row.Mod, this.Game);
				this.Write($"The mod \"{row.Name}\" now belongs to {this.Game}.");
				this.RefreshMods();
			}
			catch (Exception ex)
			{
				this._ask.ShowError(ex.Message);
			}
		}

		/// <summary>
		/// Sets which code applies the selected Binary mod.
		///
		/// The context menu passes the name of the choice, because a menu item cannot carry an
		/// enum value without a converter.
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanActOnMod))]
		private void SetModRoute(string choice)
		{
			ModRowViewModel row = this.SelectedMod;

			if (row is null) return;

			if (!row.ShowsRoute)
			{
				this.Write($"The mod \"{row.Name}\" is of kind {row.Kind}. Only a Binary mod takes a route, " +
					"because only a Binary mod runs an Endscript.");

				return;
			}

			if (!Enum.TryParse(choice, out BinaryRouteChoice value))
			{
				this.Write($"\"{choice}\" names no route. The menu passes one of Inherit, Native, " +
					"or BinaryCli.");

				return;
			}

			row.Route = value;
			this.NotifySelectedRoute();

			this.Write(value == BinaryRouteChoice.Inherit
				? $"The mod \"{row.Name}\" follows the choice of the profile, which is {this.BinaryRoute}."
				: $"The mod \"{row.Name}\" deploys through {row.RouteName}.");
		}

		[RelayCommand(CanExecute = nameof(CanActOnMod))]
		private void MoveUp() => this.MoveSelected(-1);

		[RelayCommand(CanExecute = nameof(CanActOnMod))]
		private void MoveDown() => this.MoveSelected(1);

		private void MoveSelected(int offset)
		{
			ModRowViewModel row = this.SelectedMod;

			if (row is null || this._profile is null) return;
			if (!this._profile.Move(row.Id, offset)) return;

			this.SaveProfile();
			this.ResyncOrder();
		}

		/// <summary>
		/// Moves one mod to an index in the load order, for a drop of the drag reorder. See
		/// <see cref="Profile.MoveTo"/>. Two reasons that this stays a plain method and not a
		/// command: it takes an index that only the drop handler computes, and the
		/// <c>Move up</c> and <c>Move down</c> buttons already cover the keyboard path.
		/// </summary>
		public void MoveModTo(string modId, int index)
		{
			if (this._profile is null) return;
			if (!this._profile.MoveTo(modId, index)) return;

			this.SaveProfile();
			this.ResyncOrder();
		}

		/// <summary>
		/// Moves one row inside the visible list, for the ghost of the drag reorder. It touches
		/// no profile and it saves nothing. Only a drop reaches <see cref="MoveModTo"/>.
		/// </summary>
		public void PreviewMove(int from, int to)
		{
			if (from < 0 || to < 0 || from >= this.Mods.Count || to >= this.Mods.Count) return;
			if (from == to) return;

			this.Mods.Move(from, to);
			this.Renumber();
		}

		/// <summary>
		/// Puts the row back where the drag started. The user pressed Escape, or the drop landed
		/// outside the list.
		/// </summary>
		public void CancelPreview(ModRowViewModel row, int index)
		{
			if (row is null) return;

			int current = this.Mods.IndexOf(row);

			if (current < 0 || index < 0 || index >= this.Mods.Count) return;

			this.PreviewMove(current, index);
		}

		/// <summary>
		/// Brings the visible list back in step with the load order of the profile.
		///
		/// This replaced the <see cref="RefreshMods"/> call of the two reorder paths. Refresh
		/// clears the collection and builds every row again, which destroys and recreates the
		/// container of every row. The software rasterizer draws that as a flash, and the scroll
		/// position goes with it. A reorder changes no mod, so the rows themselves can stay.
		///
		/// <b>Conflict detection reads the load order</b>, so the two refresh calls below are
		/// not optional. A different set of mods, rather than a different order, falls back to
		/// the full refresh.
		/// </summary>
		private void ResyncOrder()
		{
			if (this._profile is null) return;

			var wanted = new List<string>();

			foreach (ProfileEntry entry in this._profile.Entries) wanted.Add(entry.ModId);

			if (!this.HoldsSameMods(wanted))
			{
				this.RefreshMods();
				return;
			}

			for (int index = 0; index < wanted.Count; index++)
			{
				if (Same(this.Mods[index].Id, wanted[index])) continue;

				for (int scan = index + 1; scan < this.Mods.Count; scan++)
				{
					if (!Same(this.Mods[scan].Id, wanted[index])) continue;

					this.Mods.Move(scan, index);
					break;
				}
			}

			this.Renumber();

			this.Status = $"{this.Mods.Count} mods, {this._profile.EnabledCount} enabled.";
			this.RefreshConflicts();
			this.RefreshLoaders();
		}

		/// <summary>True when the visible list and the given order name the same mods.</summary>
		private bool HoldsSameMods(List<string> wanted)
		{
			if (wanted.Count != this.Mods.Count) return false;

			var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (ModRowViewModel row in this.Mods) present.Add(row.Id);

			foreach (string id in wanted)
			{
				if (!present.Contains(id)) return false;
			}

			return true;
		}

		/// <summary>Numbers the visible rows from one. The row template shows this.</summary>
		private void Renumber()
		{
			int order = 1;

			foreach (ModRowViewModel row in this.Mods) row.Order = order++;
		}

		private static bool Same(string left, string right) =>
			String.Equals(left, right, StringComparison.OrdinalIgnoreCase);

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void RefreshMods()
		{
			if (this._profile is null) return;

			// The route belongs to the profile, so a profile switch brings a new value. Assign
			// the field and not the property. The setter saves the profile, and this value just
			// came out of it.
			if (this._binaryRoute != this._profile.BinaryRoute)
			{
				this._binaryRoute = this._profile.BinaryRoute;
				this.OnPropertyChanged(nameof(this.BinaryRoute));
				this.OnPropertyChanged(nameof(this.BinaryRouteIsCli));
			}

			// Only the mods of this game. A profile of one game must never hold a mod of
			// another game, and Reconcile drops any entry that this list does not name.
			IReadOnlyList<InstalledMod> mods = this._store.List(this.Game);
			var ids = new List<string>(mods.Count);

			foreach (InstalledMod mod in mods) ids.Add(mod.Id);

			if (this._profile.Reconcile(ids)) this.SaveProfile();

			var byId = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
			foreach (InstalledMod mod in mods) byId[mod.Id] = mod;

			string selected = this.SelectedMod?.Id;
			this.Mods.Clear();

			int order = 1;

			foreach (ProfileEntry entry in this._profile.Entries)
			{
				if (!byId.TryGetValue(entry.ModId, out InstalledMod mod)) continue;

				this.Mods.Add(new ModRowViewModel(mod, entry, order++, this.OnModToggled));
			}

			foreach (ModRowViewModel row in this.Mods)
			{
				if (row.Id != selected) continue;

				this.SelectedMod = row;
				break;
			}

			this.Status = $"{this.Mods.Count} mods, {this._profile.EnabledCount} enabled.";
			this.RefreshPending();
			this.RefreshConflicts();
			this.RefreshLoaders();
		}

		private void OnModToggled()
		{
			this.SaveProfile();
			this.Status = $"{this.Mods.Count} mods, {this._profile.EnabledCount} enabled.";
			this.RefreshConflicts();
			this.RefreshLoaders();
		}

		// ---------------------------------------------------------------- variants

		/// <summary>
		/// Reads the variants of one mod and their questions.
		///
		/// Only a Binary mod has variants. An ASI mod and a loose-file mod ask nothing, so
		/// the panel says so instead of showing an empty list.
		/// </summary>
		private void LoadVariants(ModRowViewModel row)
		{
			this.Variants.Clear();

			if (row is null)
			{
				this.DetailsHeader = "Select a mod.";
				return;
			}

			if (row.Mod.Kind != ModKind.Binary)
			{
				this.DetailsHeader = $"\"{row.Name}\" is a {row.Kind} mod with {row.FileCount} files. " +
					"It asks nothing.";
				return;
			}

			ProfileEntry entry = this._profile?.Find(row.Id);

			if (entry is null)
			{
				this.DetailsHeader = $"The profile holds no entry for \"{row.Name}\".";
				return;
			}

			try
			{
				ModPackage package = ModPackageReader.Read(row.Mod.ContentRoot);

				foreach (ModVariant variant in package.Variants)
				{
					this.Variants.Add(new VariantRowViewModel(
						variant, entry.Selections.Ensure(variant.Name), this.OnVariantChanged));
				}

				foreach (string problem in package.Problems) this.Write($"{row.Name}: {problem}");

				// The Links boilerplate is confirmed for Underground 2 only. A deviation is
				// information for the person who gathers the samples of a new game, and it
				// blocks nothing.
				foreach (LinkDeviation deviation in ManifestLinkAudit.Run(package, this._game))
				{
					this.Write($"{row.Name}: {deviation}");
				}

				this.DetailsHeader = this.Variants.Count == 1
					? $"\"{row.Name}\" holds one variant. Switch it on to apply it."
					: $"\"{row.Name}\" holds {this.Variants.Count} variants. Switch on any number.";
			}
			catch (Exception ex)
			{
				this.DetailsHeader = $"The mod \"{row.Name}\" did not read. {ex.Message}";
				this.Write($"{row.Name}: {ex.Message}");
			}
		}

		private void OnVariantChanged()
		{
			this.SaveProfile();
			this.RefreshConflicts();
		}

		// ---------------------------------------------------------------- ASI settings

		/// <summary>
		/// Reads the settings files of one mod and builds one panel for each of them.
		///
		/// A Binary mod has no panel here. Its answers live in its script, and the Mod tab
		/// shows those.
		///
		/// <b>The tab is "Mod options" and the window is "Settings".</b> The two shared the
		/// name Settings until step 17, Part B. The types keep the old name, because
		/// <c>AsiSettingsFile</c> names a file format and not a window.
		/// </summary>
		private void LoadSettings(ModRowViewModel row)
		{
			this.SettingsFiles.Clear();
			this.SettingsHeader = "Select a mod.";

			if (row is null) return;

			ProfileEntry entry = this._profile?.Find(row.Id);

			if (entry is null)
			{
				this.SettingsHeader = $"The profile holds no entry for \"{row.Name}\".";
				return;
			}

			try
			{
				AsiLayout layout = AsiLayoutReader.Read(row.Mod.ContentRoot);

				foreach (AsiSettingsFile file in layout.Settings)
				{
					this.SettingsFiles.Add(SettingsFileViewModel.Build(file, entry, this.OnSettingChanged));
				}

				if (this.SettingsFiles.Count == 0)
				{
					this.SettingsHeader = $"\"{row.Name}\" ships no .ini file, so there is nothing " +
						"to change here.";
					return;
				}

				this.SettingsHeader = this.OptionsHeader(row, entry.IniAnswerCount);
			}
			catch (Exception ex)
			{
				this.SettingsHeader = $"The options of \"{row.Name}\" did not read. {ex.Message}";
				this.Write($"{row.Name}: {ex.Message}");
			}
		}

		/// <summary>
		/// The header line of the Mod options tab. Two callers write it, so the words live
		/// here once.
		/// </summary>
		private string OptionsHeader(ModRowViewModel row, int answered)
		{
			return answered == 0
				? $"\"{row.Name}\" ships {this.SettingsFiles.Count} .ini files, all at their shipped values."
				: $"\"{row.Name}\" ships {this.SettingsFiles.Count} .ini files. " +
					$"{answered} options changed. Deploy to apply them.";
		}

		private void OnSettingChanged()
		{
			this.SaveProfile();

			ModRowViewModel row = this.SelectedMod;

			if (row is null) return;

			int answered = this._profile?.Find(row.Id)?.IniAnswerCount ?? 0;

			this.SettingsHeader = this.OptionsHeader(row, answered);

			this.Status = answered == 0
				? "The settings match the mod."
				: $"{answered} settings changed. Deploy to apply them.";
		}

		// ---------------------------------------------------------------- the ASI loader

		/// <summary>
		/// Reads which mod supplies each ASI loader file. It writes nothing, so it runs after
		/// every change of the enabled set.
		/// </summary>
		private void RefreshLoaders()
		{
			this.Loaders.Clear();
			this.LoaderHeader = String.Empty;
			this.LoaderNeedsAnswer = false;

			if (this._profile is null) return;

			try
			{
				ProxyPlan plan = new DeployService(this._store).PlanLoaders(this._profile);

				foreach (ProxyContest contest in plan.Contests) this.Loaders.Add(new LoaderRowViewModel(contest));

				foreach (string note in plan.Unmanaged) this.Write(note);

				if (this.Loaders.Count == 0)
				{
					this.LoaderHeader = "No enabled mod ships an ASI loader.";
					return;
				}

				// The tab strip draws a mark from this. A settled contest and an open one drew
				// the same header until step 17, Part I.
				this.LoaderNeedsAnswer = !plan.IsSettled;

				this.LoaderHeader = plan.IsSettled
					? $"{this.Loaders.Count} loader files, each with one supplier."
					: "More than one mod supplies a loader file. Choose one, then deploy.";
			}
			catch (Exception ex)
			{
				this.LoaderHeader = $"The loader scan failed. {ex.Message}";
			}
		}

		/// <summary>
		/// Asks about every loader that has more than one supplier and no stored answer.
		///
		/// It returns false when the user cancels a dialog. The deploy would then stop with a
		/// message from <c>LoaderPreflight</c>, and a dialog that the user just closed is a
		/// clearer answer than an error.
		///
		/// <b>Keep the first answer until the user changes it.</b> A deploy that already holds a
		/// valid choice asks nothing.
		/// </summary>
		private bool AskForLoaders()
		{
			this.RefreshLoaders();

			foreach (LoaderRowViewModel row in this.Loaders)
			{
				if (!row.NeedsAnswer) continue;

				this.ChooseLoader(row);

				// The choice went into the profile, so read the row again.
				LoaderRowViewModel again = this.FindLoader(row.ProxyName);

				if (again is null || !again.NeedsAnswer) continue;

				this.Write($"{row.ProxyName} still has no supplier, so the deploy did not start.");
				this.Status = $"{row.ProxyName} needs a supplier.";

				return false;
			}

			return true;
		}

		private LoaderRowViewModel FindLoader(string proxyName)
		{
			foreach (LoaderRowViewModel row in this.Loaders)
			{
				if (String.Equals(row.ProxyName, proxyName, StringComparison.OrdinalIgnoreCase)) return row;
			}

			return null;
		}

		/// <summary>
		/// Asks which mod supplies one loader file, and stores the answer.
		///
		/// The dialog lists every enabled mod that supplies the file with the version of each.
		/// It ranks nothing. Version numbers on these files are often absent or wrong, so a
		/// dialog that preselected the highest number would give the user a reason to trust a
		/// number that means nothing.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void ChooseLoader(LoaderRowViewModel row)
		{
			if (row is null || this._profile is null) return;

			string answer = this._ask.PickChoice(
				$"Which mod supplies {row.ProxyName}?\n\n" +
				"This file runs the plugins of every mod. A version that forwards wrongly " +
				"breaks sound or input, so the choice stays yours.",
				row.Choices(), row.SupplierId);

			// Null means that the user cancelled. An empty string means "ask me again".
			if (answer is null) return;

			this._profile.ChooseLoader(row.ProxyName, answer);
			this.SaveProfile();

			this.Write(answer.Length == 0
				? $"{row.ProxyName}: the profile holds no supplier again. The next deploy stops and asks."
				: $"{row.ProxyName}: the profile now names \"{answer}\". Deploy to place that file.");

			this.Status = "The loader choice changed. Deploy to apply it.";

			this.RefreshLoaders();
		}

		// ---------------------------------------------------------------- conflicts

		/// <summary>
		/// Reads the conflicts of the current selection. It writes nothing, so it can run
		/// after every change.
		///
		/// A conflict never blocks a deploy. Load order already decides the winner, and this
		/// list exists so that the user can see the decision and reorder the mods.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void RefreshConflicts()
		{
			// The check runs on a background thread and the result comes back later. Nothing
			// waits for it, because the panel is the only thing that it changes.
			_ = this.RefreshConflictsAsync();
		}

		/// <summary>
		/// Counts the conflict checks that this window started. Only the newest one writes
		/// its result.
		/// </summary>
		private int _conflictRun;

		/// <summary>
		/// How many conflicts the last check found. The Conflicts tab header shows this as a
		/// suffix while it is above zero. See step 17, Part I.
		/// </summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasConflicts))]
		private int _conflictCount;

		public bool HasConflicts => this.ConflictCount > 0;

		/// <summary>
		/// When the last check ran, as the user reads it. The Check again button needs a state
		/// to refresh, and this line is that state. See step 17, Part J.
		/// </summary>
		[ObservableProperty]
		private string _conflictsCheckedAt = "The check has not run yet.";

		/// <summary>
		/// Reads the conflicts on a background thread and shows the result.
		///
		/// <b>The check is slow enough to freeze the window.</b> It reads every mod folder and
		/// it walks every script. One Most Wanted profile with two Binary mods needs about
		/// 900 ms, and the window calls this after every click on a checkbox.
		///
		/// The check reads a copy of the profile. The user can click again while it runs, and
		/// a read of the live profile would then race the change.
		/// </summary>
		private async Task RefreshConflictsAsync()
		{
			int run = ++this._conflictRun;

			this.Conflicts.Clear();
			this.ConflictCount = 0;

			if (this._install is null || this._profile is null) return;

			GameInstall install = this._install;
			DeployService service = this.Service();
			Profile snapshot;

			try
			{
				snapshot = ProfileStore.Clone(this._profile);
			}
			catch (Exception ex)
			{
				this.Conflicts.Add(ex.Message);
				return;
			}

			this.Conflicts.Add("Check the mods for conflicts.");

			IReadOnlyList<string> lines;
			int found;

			try
			{
				(lines, found) = await Task.Run(() => Describe(service, install, snapshot))
					.ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				// A selection that is half finished is normal while the user works. Report
				// it in the panel and never as a dialog.
				lines = new[] { ex.Message };
				found = 0;
			}

			// A later click started another check. That one writes the panel.
			if (run != this._conflictRun) return;

			this.Conflicts.Clear();

			foreach (string line in lines) this.Conflicts.Add(line);

			this.ConflictCount = found;
			this.ConflictsCheckedAt = $"The check ran at {DateTime.Now:HH:mm:ss}.";
		}

		/// <summary>
		/// Runs the conflict check and turns the report into the lines of the panel. This runs
		/// on a background thread, so it touches nothing that the window owns.
		///
		/// The count comes back beside the lines, because the Conflicts tab header shows it.
		/// See step 17, Part I.
		/// </summary>
		private static (IReadOnlyList<string> Lines, int Count) Describe(DeployService service,
			GameInstall install, Profile profile)
		{
			var lines = new List<string>();
			int count = 0;

			try
			{
				ConflictReport report = service.CheckConflicts(install, profile);

				count = report.Conflicts.Count;

				lines.Add(report.Summary());

				foreach (ConflictEntry entry in report.Conflicts) lines.Add(entry.ToString());

				// A refused command and a path outside staging both stop the deploy. Put
				// them above the warnings, because the user has to act on them.
				foreach (string line in report.Rejections) lines.Add($"The deploy stops. {line}");

				foreach (string line in report.Escapes) lines.Add($"The deploy stops. {line}");

				foreach (string line in report.Warnings) lines.Add($"Warning. {line}");

				foreach (string line in report.Unchecked) lines.Add($"Not checked. {line}");

				foreach (string line in report.Approximate)
				{
					lines.Add($"The mod \"{line}\" uses an 'if' command. The check walked both " +
						"branches, so a conflict against it is possible, not certain.");
				}

				if (report.Conflicts.Count > 0)
				{
					lines.Add("The last mod in the load order wins a field conflict. " +
						"Move a mod to change the winner. Load order does not settle an " +
						"existence conflict.");
				}
			}
			catch (Exception ex)
			{
				// A selection that is half finished is normal while the user works. Report
				// it in the panel and never as a dialog.
				lines.Add(ex.Message);
			}

			return (lines, count);
		}

		// ---------------------------------------------------------------- deploy

		[RelayCommand(CanExecute = nameof(CanDeploy))]
		private async Task DeployAsync()
		{
			GameInstall install = this._install;
			Profile profile = this._profile;
			bool full = this.FullVerify;

			// A deploy asks no question of its own. Settle every loader contest here, before
			// the deploy starts, and stop when the user cancels.
			if (!this.AskForLoaders()) return;

			await this.RunAsync($"Deploy the profile \"{profile.Name}\".", (report, token) =>
			{
				DeployResult result = this.Service().Deploy(install, profile, full, report, token);

				foreach (DeployOverride collision in result.Report.Overrides)
				{
					report($"Load order: {collision}");
				}

				foreach (ContainerWrite container in result.Report.Containers)
				{
					report($"Container: {container}");
				}

				foreach (SettingsWrite settings in result.Report.Settings)
				{
					report($"Settings: {settings}");
				}

				foreach (LoaderChoice loader in result.Report.Loaders)
				{
					report($"Loader: {loader}");
				}
			}, RunningWork.Deploy);

			this.RefreshDeployedState();
			this.RefreshLoaders();
		}

		[RelayCommand(CanExecute = nameof(CanDeploy))]
		private async Task RevertAsync()
		{
			GameInstall install = this._install;

			if (!this._ask.Confirm("Put the vanilla state back into the game directory?",
				"Revert", destructive: true)) return;

			await this.RunAsync("Revert to vanilla.",
				report => this.Service().Revert(install, report), RunningWork.Revert);

			this.RefreshDeployedState();
		}

		private bool CanDeploy() => this.IsIdle && this.IsGameReady && this._profile != null;

		// ---------------------------------------------------------------- the mod store

		/// <summary>
		/// Points the store and the importer at the directory that the settings name.
		///
		/// Call this at start and after every change of the setting. The two objects hold a
		/// path and nothing else, so a rebuild costs nothing.
		/// </summary>
		private void OpenStore()
		{
			this._store = new ModStore(this._settings.ResolveModStore());
			this._importer = new ModImporter(this._store, AppPaths.ImportDirectory);

			// The path leaves the status line and goes into the box of its own group. See step
			// 17, Part C.
			this.ModStorePath = this._store.Root;

			this.ModStoreStatus = this._settings.ModStoreIsDefault
				? "The default place."
				: "Set in the settings.";

			this.OnPropertyChanged(nameof(this.ModStoreIsDefault));
			this.UseDefaultModStoreCommand.NotifyCanExecuteChanged();
		}

		/// <summary>
		/// True while the mod store sits at the default place. The settings window hides the
		/// "Use the default place" button while this is true.
		/// </summary>
		public bool ModStoreIsDefault => this._settings.ModStoreIsDefault;

		/// <summary>
		/// Moves the mod store, or points this application at a store that already exists.
		///
		/// <b>The volume of the store decides the cost of every deploy.</b> A hard link cannot
		/// cross a volume, so a store on the volume of the game gets hard links and a store
		/// anywhere else falls through to Copy. A user who keeps a large library on another
		/// volume than the game pays that on every deploy.
		///
		/// <b>One press opens the picker.</b> A choice window stood in front of the picker
		/// until step 17, Part D, so a user who wanted another directory answered two dialogs.
		/// The default place is a button of its own now.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void SetModStore()
		{
			string target = this._ask.PickDirectory(
				"Choose the directory for the mod store.", this._store.Root);

			if (String.IsNullOrWhiteSpace(target)) return;

			this.MoveStore(target);
		}

		/// <summary>Puts the mod store back under our own application data.</summary>
		[RelayCommand(CanExecute = nameof(CanUseDefaultModStore))]
		private void UseDefaultModStore() => this.MoveStore(AppPaths.ModsDirectory);

		private bool CanUseDefaultModStore() => this.IsIdle && !this.ModStoreIsDefault;

		private void MoveStore(string target)
		{
			ModStore current = this._store;

			if (FileTree.IsSameOrInside(target, current.Root) && FileTree.IsSameOrInside(current.Root, target))
			{
				this._ask.ShowMessage($"The mod store already sits at {current.Root}.");
				return;
			}

			string problem = ModStoreRelocator.Problem(target, this._install?.Root);

			if (problem.Length > 0)
			{
				this._ask.ShowError(problem);
				return;
			}

			int count = current.List().Count;

			// A user who already moved the directory by hand points at it instead of moving it.
			// Both cases are legitimate, so ask which one this is.
			if (count > 0)
			{
				bool move = this._ask.Confirm(
					$"The store at {current.Root} holds {count} mods.\n\n" +
					$"Move them to {target}?\n\n" +
					"No leaves them where they are and reads the new directory instead. " +
					"Your profiles survive either answer.",
					"Move them");

				if (move)
				{
					try
					{
						ModStoreMoveReport report = ModStoreRelocator.Move(
							current, target, this._install?.Root, this.Write);

						foreach (string kept in report.Kept) this.Write($"  stayed behind: {kept}");

						if (report.Kept.Count > 0)
						{
							this._ask.ShowError(
								$"{report.Kept.Count} of {count} mods stayed at {current.Root}. " +
								$"{String.Join(" ", report.Kept)} Import them again, or move the " +
								"directories by hand.");
						}
					}
					catch (Exception ex)
					{
						this._ask.ShowError($"The mod store did not move. {ex.Message}");
						return;
					}
				}
			}

			// Store the new place only after the move, so a failed move leaves the setting on
			// the directory that still holds the mods.
			string store =
				FileTree.IsSameOrInside(target, AppPaths.ModsDirectory)
					&& FileTree.IsSameOrInside(AppPaths.ModsDirectory, target)
				? null
				: target;

			this.SaveSettings(settings => settings.ModStoreOverride = store);

			this.OpenStore();
			this.Write($"The mod store is now {this._store.Root}.");

			this.RefreshMods();
			this.ReportStoreVolume();
		}

		/// <summary>
		/// Says which method the next deploy will use for the files of a mod. A user who moved
		/// the store to save time has to be able to see whether it worked.
		/// </summary>
		private void ReportStoreVolume()
		{
			if (this._install is null) return;

			try
			{
				string staging = this.Service().WorkspaceOf(this._install).StagingDirectory;

				Directory.CreateDirectory(this._store.Root);
				Directory.CreateDirectory(staging);

				LinkProbeResult probe = LinkSupport.ProbeBetween(this._store.Root, staging);

				this.Write($"A deploy from this store uses {probe.Best}.");

				if (probe.Best == LinkKind.HardLink)
				{
					this.Status = "The store sits on the volume of the game. Deploys use hard links.";
					return;
				}

				foreach (LinkProbe entry in probe.Probes)
				{
					if (entry.Kind == LinkKind.HardLink && !entry.Works) this.Write($"  {entry}");
				}

				this.Status = $"A deploy from this store uses {probe.Best}, so it writes every byte.";
			}
			catch (Exception ex)
			{
				this.Write($"The link probe of the new store failed. {ex.Message}");
			}
		}

		// ---------------------------------------------------------------- the workspace

		/// <summary>The directory that holds the vanilla copy and the staging copy.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(HasWorkspacePath))]
		private string _workspacePath = String.Empty;

		public bool HasWorkspacePath => this.WorkspacePath.Length > 0;

		/// <summary>
		/// True while the workspace sits beside the game install. The settings window hides the
		/// "Use the default place" button while this is true.
		/// </summary>
		public bool WorkspaceIsDefault => String.IsNullOrWhiteSpace(this._settings.WorkRootOverride);

		/// <summary>One line that says where the workspace sits and why the place matters.</summary>
		[ObservableProperty]
		private string _workspaceStatus = String.Empty;

		private void RefreshWorkspace()
		{
			bool isDefault = this.WorkspaceIsDefault;

			this.OnPropertyChanged(nameof(this.WorkspaceIsDefault));
			this.UseDefaultWorkRootCommand.NotifyCanExecuteChanged();

			if (this._install is null)
			{
				this.WorkspacePath = isDefault ? String.Empty : this._settings.WorkRootOverride;
				this.WorkspaceStatus = isDefault
					? "Goes beside the game install. Set the game install to see the path."
					: "Set in the settings. Set the game install to see the full path.";

				return;
			}

			try
			{
				this.WorkspacePath = this.Service().WorkspaceOf(this._install).Root;
				this.WorkspaceStatus = isDefault
					? "Beside the game install. This is the default, and the fast place."
					: "Set in the settings. Off the volume of the game, every deploy copies every byte.";
			}
			catch (Exception ex)
			{
				this.WorkspacePath = String.Empty;
				this.WorkspaceStatus = ex.Message;
			}
		}

		/// <summary>
		/// Moves the workspace of every game.
		///
		/// <b>The workspace holds the only vanilla copy of an install.</b> A move while the game
		/// directory holds a deployed profile points this application at an empty workspace, and
		/// <c>Revert</c> then throws because no vanilla copy exists. So this command refuses that
		/// case and asks the user to revert first.
		///
		/// The old directory stays on disk. This application deletes nothing that it did not
		/// write in the same operation, and the vanilla copy is the last way back.
		///
		/// <b>One press opens the picker.</b> A choice window stood in front of the picker
		/// until step 17, Part D. The default place is a button of its own now.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void SetWorkRoot()
		{
			if (!this.WorkspaceIsSafeToMove()) return;

			string target = this._ask.PickDirectory(
				"Choose the directory for the workspace.", this.WorkspacePath);

			if (String.IsNullOrWhiteSpace(target)) return;

			this.ApplyWorkRoot(target);
		}

		/// <summary>
		/// Puts the workspace beside the game install again.
		///
		/// <b>The guard runs here as well.</b> The workspace holds the only vanilla copy, and
		/// this command moves it in the same way that <see cref="SetWorkRoot"/> does.
		/// </summary>
		[RelayCommand(CanExecute = nameof(CanUseDefaultWorkRoot))]
		private void UseDefaultWorkRoot()
		{
			if (!this.WorkspaceIsSafeToMove()) return;

			this.ApplyWorkRoot(null);
		}

		private bool CanUseDefaultWorkRoot() => this.IsIdle && !this.WorkspaceIsDefault;

		/// <summary>
		/// Stores the new place and reports the move. A null target means the default place.
		/// </summary>
		private void ApplyWorkRoot(string target)
		{
			string previous = this.WorkspacePath;

			this.SaveSettings(settings => settings.WorkRootOverride = target);

			this.RefreshWorkspace();

			this.Write(this.WorkspacePath.Length > 0
				? $"The workspace is now {this.WorkspacePath}."
				: "The workspace goes beside the game install again.");

			if (previous.Length > 0 && previous != this.WorkspacePath)
			{
				this.Write($"The old workspace stays at {previous}. Delete it by hand when you " +
					"no longer need it.");
			}
		}

		/// <summary>
		/// True when the game directory holds the vanilla state, so a move of the workspace
		/// loses no way back. It reports the reason and returns false in every other case.
		/// </summary>
		private bool WorkspaceIsSafeToMove()
		{
			if (this._install is null) return true;

			WorkspaceState state;

			try
			{
				state = this.Service().WorkspaceOf(this._install).ReadState();
			}
			catch (Exception ex)
			{
				this._ask.ShowError($"This application could not read the workspace. {ex.Message}");
				return false;
			}

			if (state.IsVanilla) return true;

			this._ask.ShowError(
				$"The game directory holds the profile \"{state.DeployedProfile}\", and the " +
				"workspace holds the only vanilla copy. Revert first, then move the workspace.");

			return false;
		}

		/// <summary>
		/// Lists the directories that a user can look at but cannot change.
		///
		/// The staging directory is the one that a user asks for. A deploy that the verify
		/// stopped leaves it in place, and the failure is only readable from inside it.
		///
		/// <b>One directory gets one row in one window.</b> This list held the game install,
		/// the workspace, and the mod store until step 17, Part A. The settings window owns
		/// those three, because a user can change all three there.
		///
		/// <c>CanExecute</c> reads <c>IsIdle</c>, because the code below reads the store and
		/// calls <c>WorkspaceOf</c>. A running deploy changes both.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void ShowFolders()
		{
			var rows = new List<FolderRow>();

			if (this._install != null)
			{
				GameWorkspace workspace = this.Service().WorkspaceOf(this._install);

				rows.Add(new FolderRow("Staging copy",
					"What the next swap puts into the game directory. A deploy that the verify " +
					"stopped leaves its result here.",
					workspace.StagingDirectory));

				rows.Add(new FolderRow("Vanilla copy",
					"The pristine state of the install. A revert restores this.",
					workspace.VanillaDirectory));
			}
			else
			{
				rows.Add(new FolderRow("Staging copy",
					"No game install is set, so there is no staging copy.", null));

				rows.Add(new FolderRow("Vanilla copy",
					"No game install is set, so there is no vanilla copy.", null));
			}

			rows.Add(new FolderRow("Application data",
				"The settings file, the profiles, and the logs.", AppPaths.Root));

			rows.Add(new FolderRow("Logs", "The deploy report and the error log.",
				AppPaths.LogDirectory));

			this._ask.ShowFolders(rows);
		}

		/// <summary>
		/// Builds the service for one operation. It carries the Binary install, because the
		/// container engine needs the hash lists of that install.
		/// </summary>
		private DeployService Service()
		{
			return new DeployService(this._store, this._binaryInstall, this._settings.WorkRootOverride);
		}

		// ---------------------------------------------------------------- updates

		/// <summary>
		/// The update check. It is null until the first use, because building it reads the
		/// install state and a start must not wait for that.
		/// </summary>
		private UpdateService _updates;

		private UpdateService Updates() => this._updates ??= new UpdateService();

		/// <summary>The release that a check found and a download brought in.</summary>
		private UpdateInfo _readyUpdate;

		/// <summary>
		/// The quiet check that a start runs. It writes one line and it downloads nothing.
		///
		/// <b>This never opens a dialog, and it never shows the error banner.</b> A machine with
		/// no network reaches this on every start, and a start must not stop to report that. It
		/// also downloads nothing, because a user who did not ask for a download must not wait
		/// for one. The line tells them where the button is.
		///
		/// The window calls this after it opens. A call from the constructor would show a dialog
		/// before a window exists to own it.
		/// </summary>
		public async Task CheckForUpdatesAtStartAsync()
		{
			if (!this.CheckForUpdatesAtStart) return;

			try
			{
				UpdateService updates = this.Updates();

				// A build with no installer cannot update itself. Say nothing at start. The
				// button says it when the user presses it.
				if (!updates.IsInstalled) return;

				UpdateInfo update = await updates.CheckAsync();

				if (update is null)
				{
					this.Write($"Version {AppVersion.Display} is the newest release.");

					return;
				}

				this.Write($"Version {update.TargetFullRelease.Version} is available. " +
					"Open the settings and press Check now to install it.");
			}
			catch (Exception ex)
			{
				this.Write($"The update check did not reach GitHub. {ex.Message}");
			}
		}

		/// <summary>
		/// Asks GitHub for a newer release, downloads it, and offers a restart.
		///
		/// The download runs inside RunTaskAsync, so the log, the cancel button, and the error
		/// banner behave as they do for a deploy. <b>The restart runs after that, on the UI
		/// thread.</b> ApplyAndRestart ends the process, and a call inside the background work
		/// would kill the thread that reports the result.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private async Task CheckForUpdatesAsync()
		{
			UpdateService updates = this.Updates();

			// A build out of a publish directory has no package and no feed. Say so, and never
			// open a dialog for it. A developer sees this line every day.
			if (!updates.IsInstalled)
			{
				this.Write("This build carries no installer, so it cannot update itself. " +
					"Install it from a release to use this button.");

				return;
			}

			this._readyUpdate = null;

			await this.RunTaskAsync("Check for updates.", async (report, token) =>
			{
				UpdateInfo update = await updates.CheckAsync();

				if (update is null)
				{
					report($"Version {AppVersion.Display} is the newest release.");

					return;
				}

				string version = update.TargetFullRelease.Version.ToString();

				report($"Version {version} is available. The download starts now.");

				int last = -1;

				await updates.DownloadAsync(update, percent =>
				{
					// Report every tenth. Velopack reports each percent, and 100 lines of
					// progress would push the rest of the log out of view.
					if (percent < 100 && percent / 10 == last / 10) return;

					last = percent;
					report($"Downloaded {percent} percent.");
				}, token);

				report($"Version {version} is ready.");

				this._readyUpdate = update;
			});

			if (this._readyUpdate is null) return;

			string ready = this._readyUpdate.TargetFullRelease.Version.ToString();

			if (!this._ask.Confirm(
				$"Version {ready} is ready. Start it now?\n\n" +
				"The application restarts. Your mods, profiles, and settings stay as they are.",
				"Restart"))
			{
				this.Write("The update waits. It applies the next time that this application starts.");

				return;
			}

			// This never returns.
			this.Updates().ApplyAndRestart(this._readyUpdate);
		}

		// ---------------------------------------------------------------- plumbing

		/// <summary>
		/// Runs one operation on a background thread and writes its lines to the log.
		///
		/// Every disk operation goes through here. IsBusy blocks the commands while it
		/// runs, so no two operations touch the staging copy at once.
		///
		/// The kind names the operation for the buttons. Work carries that name while the
		/// operation runs, and the button of a deploy or a revert draws a spinner from it.
		/// </summary>
		private Task RunAsync(string title, Action<Action<string>> work,
			RunningWork kind = RunningWork.Other)
		{
			return this.RunAsync(title, (report, token) => work(report), kind);
		}

		/// <summary>
		/// The same, for an operation that the user can cancel. The work delegate reads the
		/// token and throws <c>OperationCanceledException</c> at its next safe point.
		/// </summary>
		private Task RunAsync(string title, Action<Action<string>, CancellationToken> work,
			RunningWork kind = RunningWork.Other)
		{
			// Task.Run keeps the disk work off the UI thread. The work here is synchronous, so
			// this method is the one that moves it.
			return this.RunTaskAsync(title, (report, token) => Task.Run(() => work(report, token)),
				kind);
		}

		/// <summary>
		/// The same, for work that is already asynchronous. An update check waits on the
		/// network, so it needs no thread of its own.
		///
		/// <b>The name differs from RunAsync on purpose, and it is not an overload.</b> An
		/// async lambda also fits <c>Action</c>, so an overload would let
		/// <c>async (report, token) => ...</c> bind to the method above. That produces an
		/// async void call. The exception would then leave the try block below and reach
		/// nobody, and the log and the banner would show a run that succeeded.
		/// </summary>
		private async Task RunTaskAsync(string title,
			Func<Action<string>, CancellationToken, Task> work,
			RunningWork kind = RunningWork.Other)
		{
			if (this.IsBusy) return;

			// Name the operation before IsBusy raises its own change. The buttons then read one
			// state and never a half of it.
			this.Work = kind;
			this.IsBusy = true;
			this.Status = title;
			this.Write(title);

			// A new run speaks for itself. The banner of an earlier failure must not linger
			// beside a run that has not failed yet.
			this.HasDeployError = false;
			this.DeployError = String.Empty;

			var progress = new Progress<string>(this.Write);
			Action<string> report = line => ((IProgress<string>)progress).Report(line);

			var source = new CancellationTokenSource();
			this._cancellation = source;
			this.OnPropertyChanged(nameof(this.CanCancel));
			this.CancelWorkCommand.NotifyCanExecuteChanged();

			try
			{
				await work(report, source.Token);

				this.Status = "Ready.";
			}
			catch (OperationCanceledException)
			{
				// A cancel is what the user asked for, so it is no failure. No banner and no
				// dialog. The game directory did not change.
				this.Write("CANCELED. The game directory did not change.");
				this.Status = "The last operation was canceled.";
			}
			catch (Exception ex)
			{
				this.Write($"FAILED. {ex.Message}");
				this.Status = "The last operation failed.";

				// Set the banner before the dialog. The dialog is modal and blocks here until
				// the user closes it, and the banner has to be in place under it already.
				this.DeployError = $"{title} {ex.Message}";
				this.HasDeployError = true;

				this._ask.ShowError(ex.Message);
			}
			finally
			{
				this._cancellation = null;
				source.Dispose();

				this.IsBusy = false;
				this.Work = RunningWork.None;

				this.OnPropertyChanged(nameof(this.CanCancel));
				this.CancelWorkCommand.NotifyCanExecuteChanged();
			}
		}

		/// <summary>Clears the banner that RunAsync sets for a failed run.</summary>
		[RelayCommand]
		private void DismissDeployError()
		{
			this.HasDeployError = false;
			this.DeployError = String.Empty;
		}

		private void Write(string line)
		{
			if (String.IsNullOrEmpty(line)) return;

			this.Log.Add(line);

			// A long run writes thousands of lines. Keep the tail.
			while (this.Log.Count > 2000) this.Log.RemoveAt(0);
		}

		private void NotifyCommands()
		{
			this.BrowseGameCommand.NotifyCanExecuteChanged();
			this.DetectGameCommand.NotifyCanExecuteChanged();
			this.SetBinaryCommand.NotifyCanExecuteChanged();
			this.NewProfileCommand.NotifyCanExecuteChanged();
			this.RenameProfileCommand.NotifyCanExecuteChanged();
			this.DeleteProfileCommand.NotifyCanExecuteChanged();
			this.ImportArchiveCommand.NotifyCanExecuteChanged();
			this.ImportFolderCommand.NotifyCanExecuteChanged();
			this.RemoveModCommand.NotifyCanExecuteChanged();
			this.SetModGameCommand.NotifyCanExecuteChanged();
			this.MoveUpCommand.NotifyCanExecuteChanged();
			this.MoveDownCommand.NotifyCanExecuteChanged();
			this.RefreshModsCommand.NotifyCanExecuteChanged();
			this.RefreshConflictsCommand.NotifyCanExecuteChanged();
			this.DeployCommand.NotifyCanExecuteChanged();
			this.RevertCommand.NotifyCanExecuteChanged();

			// The config window of step 12 shows these, so their buttons have to gray out
			// during a long operation like every other button does.
			this.SetModStoreCommand.NotifyCanExecuteChanged();
			this.UseDefaultModStoreCommand.NotifyCanExecuteChanged();
			this.SetWorkRootCommand.NotifyCanExecuteChanged();
			this.UseDefaultWorkRootCommand.NotifyCanExecuteChanged();
			this.ChooseLoaderCommand.NotifyCanExecuteChanged();
			this.CheckForUpdatesCommand.NotifyCanExecuteChanged();

			// ShowFolders reads the store and calls WorkspaceOf. A running deploy changes both.
			// See step 17, M1.
			this.ShowFoldersCommand.NotifyCanExecuteChanged();
		}
	}
}
