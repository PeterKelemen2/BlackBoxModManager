using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using BlackboxModManager.App.Services;
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
			// settings file is where this value just came from.
			this._fullVerify = this._settings.FullVerify;

			this.OpenStore();

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

		[ObservableProperty]
		private string _gamePath = String.Empty;

		[ObservableProperty]
		private string _binaryStatus = String.Empty;

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

		[ObservableProperty]
		private string _detailsHeader = "Select a mod to see what it offers.";

		[ObservableProperty]
		private string _settingsHeader = "Select a mod to see its settings.";

		[ObservableProperty]
		private string _loaderHeader = String.Empty;

		[ObservableProperty]
		private string _modStoreStatus = String.Empty;

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

			this.SelectedMod = null;
			this.RefreshGame();
			this.RefreshProfiles();
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

				this.LoadVariants(value);
				this.LoadSettings(value);
			}
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
		}

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

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private async Task DetectGameAsync()
		{
			GameDefinition definition = GameCatalog.Demand(this.Game);
			this.Status = "Look for the game.";

			IReadOnlyList<string> candidates = await Task.Run(
				() => GameInstallLocator.FindCandidates(definition));

			this.Status = "Ready.";

			if (candidates.Count == 0)
			{
				this._ask.ShowMessage(
					$"This machine holds no directory that looks like {definition.DisplayName}. " +
					"Use Browse to give the path.");
				return;
			}

			// Every result is a suggestion. The user confirms one.
			foreach (string candidate in candidates)
			{
				if (!this._ask.Confirm($"Is this the install?\n\n{candidate}")) continue;

				GameInstallStatus status = this._games.Store(this.Game, candidate);

				if (!status.IsUsable)
				{
					this._ask.ShowError(status.Message);
					return;
				}

				this.ReloadSettings();

				this.RefreshGame();
				this.Write($"The game install is {status.Root}.");
				return;
			}
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void SetBinary()
		{
			string path = this._ask.PickDirectory("Choose the directory of the Binary 2.8.3 install.");

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

			this.BinaryStatus = resolution.IsUsable
				? $"Binary {resolution.Install.Version} at {resolution.Install.Root}."
				: resolution.Status.Message + " A Binary mod needs it. A drop-in mod does not.";
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
				this.RefreshStateSummary();
				return;
			}

			try
			{
				GameWorkspace workspace = this.Service().WorkspaceOf(this._install);
				WorkspaceState state = workspace.ReadState();

				this.DeployedState = state.IsVanilla
					? "The game directory holds the vanilla state."
					: $"The game directory holds the profile \"{state.DeployedProfile}\", " +
						$"with {state.DeployedFileCount} files from mods.";
			}
			catch (Exception ex)
			{
				this.DeployedState = ex.Message;
			}

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
			string path = this._ask.PickDirectory("Choose the directory of a mod.");

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

					report($"The import added \"{result.Mod.Name}\" of kind {result.Mod.Kind} " +
						$"for {result.Mod.Game}, with {result.Content.Files.Count} files.");

					foreach (string note in result.Notes) report(note);
				});
			}
			finally
			{
				this.Imports.Remove(row);
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
				this.DetailsHeader = "Select a mod to see what it offers.";
				return;
			}

			if (row.Mod.Kind != ModKind.Binary)
			{
				this.DetailsHeader = $"\"{row.Name}\" is a {row.Kind} mod. It holds {row.FileCount} files " +
					"and it asks no question. The link engine puts its files in place.";
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
					: $"\"{row.Name}\" holds {this.Variants.Count} variants. Switch on any number of them.";
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
		/// A Binary mod has no settings panel. Its answers live in its script, and the Mod tab
		/// shows those.
		/// </summary>
		private void LoadSettings(ModRowViewModel row)
		{
			this.SettingsFiles.Clear();
			this.SettingsHeader = "Select a mod to see its settings.";

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
					this.SettingsHeader = $"\"{row.Name}\" ships no .ini file, so it has no settings " +
						"that this window can change.";
					return;
				}

				int answered = entry.IniAnswerCount;

				this.SettingsHeader = answered == 0
					? $"\"{row.Name}\" ships {this.SettingsFiles.Count} settings files. " +
						"Every value is the one that the mod ships."
					: $"\"{row.Name}\" ships {this.SettingsFiles.Count} settings files. " +
						$"The profile changes {answered} options. A change needs a new deploy.";
			}
			catch (Exception ex)
			{
				this.SettingsHeader = $"The settings of \"{row.Name}\" did not read. {ex.Message}";
				this.Write($"{row.Name}: {ex.Message}");
			}
		}

		private void OnSettingChanged()
		{
			this.SaveProfile();

			ModRowViewModel row = this.SelectedMod;

			if (row is null) return;

			int answered = this._profile?.Find(row.Id)?.IniAnswerCount ?? 0;

			this.SettingsHeader = answered == 0
				? $"\"{row.Name}\" ships {this.SettingsFiles.Count} settings files. " +
					"Every value is the one that the mod ships."
				: $"\"{row.Name}\" ships {this.SettingsFiles.Count} settings files. " +
					$"The profile changes {answered} options. A change needs a new deploy.";

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

				this.LoaderHeader = plan.IsSettled
					? $"{this.Loaders.Count} loader files. Every one of them has a supplier."
					: "A loader file has more than one supplier and the profile names none of them. " +
						"Choose one, then deploy. This application never picks a loader for you.";
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
				"This file is the ASI loader. One of them runs the plugins of every mod. A version " +
				"that forwards wrongly breaks sound or input rather than a plugin, so this " +
				"application never chooses for you.",
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
			this.Conflicts.Clear();

			if (this._install is null || this._profile is null) return;

			try
			{
				ConflictReport report = this.Service().CheckConflicts(this._install, this._profile);

				this.Conflicts.Add(report.Summary());

				foreach (ConflictEntry entry in report.Conflicts) this.Conflicts.Add(entry.ToString());

				// A refused command and a path outside staging both stop the deploy. Put
				// them above the warnings, because the user has to act on them.
				foreach (string line in report.Rejections) this.Conflicts.Add($"The deploy stops. {line}");

				foreach (string line in report.Escapes) this.Conflicts.Add($"The deploy stops. {line}");

				foreach (string line in report.Warnings) this.Conflicts.Add($"Warning. {line}");

				foreach (string line in report.Unchecked) this.Conflicts.Add($"Not checked. {line}");

				foreach (string line in report.Approximate)
				{
					this.Conflicts.Add($"The mod \"{line}\" uses an 'if' command. The check walked both " +
						"branches, so a conflict against it is possible and not certain.");
				}

				if (report.Conflicts.Count > 0)
				{
					this.Conflicts.Add("The last mod in the load order wins a field conflict. " +
						"Move a mod to change the winner. An existence conflict makes a command fail, " +
						"and load order does not settle it.");
				}
			}
			catch (Exception ex)
			{
				// A selection that is half finished is normal while the user works. Report
				// it in the panel and never as a dialog.
				this.Conflicts.Add(ex.Message);
			}
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

			await this.RunAsync($"Deploy the profile \"{profile.Name}\".", report =>
			{
				DeployResult result = this.Service().Deploy(install, profile, full, report);

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

			this.ModStoreStatus = this._settings.ModStoreIsDefault
				? $"The mod store is at the default place: {this._store.Root}"
				: $"The mod store is at {this._store.Root}";
		}

		/// <summary>
		/// Moves the mod store, or points this application at a store that already exists.
		///
		/// <b>The volume of the store decides the cost of every deploy.</b> A hard link cannot
		/// cross a volume, so a store on the volume of the game gets hard links and a store
		/// anywhere else falls through to Copy. A user who keeps a large library on another
		/// volume than the game pays that on every deploy.
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void SetModStore()
		{
			var choices = new List<UserChoice>
			{
				new UserChoice("pick", "Choose another directory",
					"Pick a directory. This application then offers to move the mods that the store " +
					"holds today."),
			};

			if (!this._settings.ModStoreIsDefault)
			{
				choices.Add(new UserChoice("default", "Use the default place",
					$"Go back to {AppPaths.ModsDirectory}."));
			}

			string answer = this._ask.PickChoice(
				$"The mod store is at {this._store.Root}.\n\n" +
				"A hard link cannot cross a volume. A store on the volume of the game makes every " +
				"deploy cost almost no disk space. A store anywhere else makes every deploy copy " +
				"every byte of every mod.",
				choices);

			if (answer is null) return;

			string target = answer == "default"
				? AppPaths.ModsDirectory
				: this._ask.PickDirectory("Choose the directory for the mod store.", this._store.Root);

			if (String.IsNullOrWhiteSpace(target)) return;

			this.MoveStore(target);
		}

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
					"Choose No to leave them where they are and read the new directory instead. " +
					"The profiles name a mod by its identifier, so they survive either answer.",
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
					this.Status = "The mod store shares the volume of the game. A deploy uses hard links.";
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
		private string _workspacePath = String.Empty;

		/// <summary>One line that says where the workspace sits and why the place matters.</summary>
		[ObservableProperty]
		private string _workspaceStatus = String.Empty;

		private void RefreshWorkspace()
		{
			bool isDefault = String.IsNullOrWhiteSpace(this._settings.WorkRootOverride);

			if (this._install is null)
			{
				this.WorkspacePath = isDefault ? String.Empty : this._settings.WorkRootOverride;
				this.WorkspaceStatus = isDefault
					? "The workspace goes beside the game install. Set the game install to see the path."
					: "The settings name this place. Set the game install to see the full path.";

				return;
			}

			try
			{
				this.WorkspacePath = this.Service().WorkspaceOf(this._install).Root;
				this.WorkspaceStatus = isDefault
					? "The workspace sits beside the game install. This is the default, and it is " +
						"the fast place."
					: "The settings name this place. A workspace off the volume of the game makes " +
						"every deploy copy every byte.";
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
		/// </summary>
		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void SetWorkRoot()
		{
			if (!this.WorkspaceIsSafeToMove()) return;

			var choices = new List<UserChoice>
			{
				new UserChoice("pick", "Choose another directory",
					"Pick a directory. The next deploy builds a new workspace there."),
			};

			if (!String.IsNullOrWhiteSpace(this._settings.WorkRootOverride))
			{
				choices.Add(new UserChoice("default", "Use the default place",
					"Put the workspace beside the game install again."));
			}

			string answer = this._ask.PickChoice(
				(this.WorkspacePath.Length > 0
					? $"The workspace is at {this.WorkspacePath}.\n\n"
					: String.Empty) +
				"A deploy builds the staging copy here and then swaps it with the game directory. " +
				"Both steps are cheap only on the volume of the game. Move this only when that " +
				"volume has no free space.",
				choices);

			if (answer is null) return;

			string target = answer == "default"
				? null
				: this._ask.PickDirectory("Choose the directory for the workspace.", this.WorkspacePath);

			if (answer != "default" && String.IsNullOrWhiteSpace(target)) return;

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
				$"The game directory holds the profile \"{state.DeployedProfile}\". " +
				"The workspace holds the only vanilla copy of this install. " +
				"Revert to vanilla first, then move the workspace.");

			return false;
		}

		/// <summary>
		/// Lists every directory of this application, so that the user can look at one.
		///
		/// The staging directory is the one that a user asks for. A deploy that the verify
		/// stopped leaves it in place, and the failure is only readable from inside it.
		/// </summary>
		[RelayCommand]
		private void ShowFolders()
		{
			var rows = new List<FolderRow>
			{
				new FolderRow("Application data", "Settings, profiles, the mod store, and the logs.",
					AppPaths.Root),
				new FolderRow("Mod store",
					"One directory per imported mod. A deploy reads from here. The volume of this " +
					"directory decides whether a deploy can use hard links.",
					this._store.Root),
				new FolderRow("Logs", "The deploy report and the error log.", AppPaths.LogDirectory),
			};

			if (this._install is null)
			{
				rows.Add(new FolderRow("Game install", "No game install is set, so there is no workspace.", null));

				this._ask.ShowFolders(rows);

				return;
			}

			GameWorkspace workspace = this.Service().WorkspaceOf(this._install);

			rows.Insert(0, new FolderRow("Game install",
				"The live directory. Only the swap of a deploy changes this.", this._install.Root));

			rows.Insert(1, new FolderRow("Workspace",
				"The vanilla copy, the staging copy, and the state of this game.", workspace.Root));

			rows.Insert(2, new FolderRow("Staging copy",
				"What the next swap puts into the game directory. A deploy that the verify stopped " +
				"leaves the result here, and nothing reached the game.", workspace.StagingDirectory));

			rows.Insert(3, new FolderRow("Vanilla copy",
				"The pristine state of the install. A revert restores this.", workspace.VanillaDirectory));

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
		private async Task RunAsync(string title, Action<Action<string>> work,
			RunningWork kind = RunningWork.Other)
		{
			if (this.IsBusy) return;

			// Name the operation before IsBusy raises its own change. The buttons then read one
			// state and never a half of it.
			this.Work = kind;
			this.IsBusy = true;
			this.Status = title;
			this.Write(title);

			var progress = new Progress<string>(this.Write);
			Action<string> report = line => ((IProgress<string>)progress).Report(line);

			try
			{
				await Task.Run(() => work(report));

				this.Status = "Ready.";
			}
			catch (Exception ex)
			{
				this.Write($"FAILED. {ex.Message}");
				this.Status = "The last operation failed.";
				this._ask.ShowError(ex.Message);
			}
			finally
			{
				this.IsBusy = false;
				this.Work = RunningWork.None;
			}
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

			// The config window of step 12 shows these three, so their buttons have to gray out
			// during a long operation like every other button does.
			this.SetModStoreCommand.NotifyCanExecuteChanged();
			this.SetWorkRootCommand.NotifyCanExecuteChanged();
			this.ChooseLoaderCommand.NotifyCanExecuteChanged();
		}
	}
}
