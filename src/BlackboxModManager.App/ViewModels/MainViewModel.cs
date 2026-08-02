using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using BlackboxModManager.App.Services;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Deploy;
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
		private readonly ModStore _store = new ModStore();
		private readonly ModImporter _importer;

		private Settings _settings;
		private Profile _profile;
		private GameInstall _install;
		private BinaryInstall _binaryInstall;

		/// <summary>
		/// The game that the window manages. GameCatalog holds one entry today, and step 7
		/// adds the others.
		/// </summary>
		public GameINT Game { get; } = GameINT.Underground2;

		public ObservableCollection<ModRowViewModel> Mods { get; } = new ObservableCollection<ModRowViewModel>();

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

		public MainViewModel(IUserInteraction ask)
		{
			this._ask = ask ?? throw new ArgumentNullException(nameof(ask));
			this._importer = new ModImporter(this._store, AppPaths.ImportDirectory);

			AppPaths.CreateRoot();
			this._settings = SettingsStore.Load();

			this.Write($"The application data directory is {AppPaths.Root}.");
			this.Write($"The mod store is {AppPaths.ModsDirectory}.");

			this.RefreshGame();
			this.RefreshBinary();
			this.RefreshProfiles();
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

		[ObservableProperty]
		private string _detailsHeader = "Select a mod to see what it offers.";

		private ModRowViewModel _selectedMod;

		public ModRowViewModel SelectedMod
		{
			get => this._selectedMod;
			set
			{
				if (!this.SetProperty(ref this._selectedMod, value)) return;

				this.LoadVariants(value);
			}
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

			this.RefreshBinary();
		}

		private void RefreshGame()
		{
			GameInstallResolution resolution = this._games.Resolve(this.Game);

			this._install = resolution.Install;
			this.GamePath = resolution.Status.Root ?? String.Empty;
			this.GameStatus = resolution.IsUsable
				? $"{GameCatalog.Demand(this.Game).DisplayName} is ready."
				: resolution.Status.Message;

			this.OnPropertyChanged(nameof(this.IsGameReady));
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

		private void RefreshDeployedState()
		{
			if (this._install is null)
			{
				this.DeployedState = String.Empty;
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

			if (!this._ask.Confirm($"Delete the profile \"{this._profile.Name}\"? The mods stay in the store.")) return;

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

			this._settings.ActiveProfiles[this.Game.ToString()] = name;
			SettingsStore.Save(this._settings);

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

		private async Task ImportAsync(string source)
		{
			await this.RunAsync($"Import {Path.GetFileName(source)}.", report =>
			{
				ModImportResult result = this._importer.Import(source);

				report($"The import added \"{result.Mod.Name}\" of kind {result.Mod.Kind}, " +
					$"with {result.Content.Files.Count} files.");

				foreach (string note in result.Content.Notes) report(note);

				if (result.Mod.Kind == ModKind.Binary)
				{
					report("This build does not deploy a Binary mod. Step 6 adds that engine.");
				}
			});

			this.RefreshMods();
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void RemoveMod()
		{
			ModRowViewModel row = this.SelectedMod;

			if (row is null) return;

			if (!this._ask.Confirm($"Remove \"{row.Name}\" from the mod store? This deletes its files.")) return;

			this._store.Remove(row.Id);
			this._profile?.Remove(row.Id);
			this.SaveProfile();
			this.RefreshMods();

			this.Write($"The store no longer holds \"{row.Name}\".");
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void MoveUp() => this.MoveSelected(-1);

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void MoveDown() => this.MoveSelected(1);

		private void MoveSelected(int offset)
		{
			ModRowViewModel row = this.SelectedMod;

			if (row is null || this._profile is null) return;
			if (!this._profile.Move(row.Id, offset)) return;

			this.SaveProfile();
			this.RefreshMods();

			foreach (ModRowViewModel candidate in this.Mods)
			{
				if (candidate.Id != row.Id) continue;

				this.SelectedMod = candidate;
				break;
			}
		}

		[RelayCommand(CanExecute = nameof(IsIdle))]
		private void RefreshMods()
		{
			if (this._profile is null) return;

			IReadOnlyList<InstalledMod> mods = this._store.List();
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
		}

		private void OnModToggled()
		{
			this.SaveProfile();
			this.Status = $"{this.Mods.Count} mods, {this._profile.EnabledCount} enabled.";
			this.RefreshConflicts();
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

				foreach (string line in report.Unchecked) this.Conflicts.Add($"Not checked. {line}");

				if (report.Conflicts.Count > 0)
				{
					this.Conflicts.Add("The last mod in the load order wins. Move a mod to change the winner.");
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
			});

			this.RefreshDeployedState();
		}

		[RelayCommand(CanExecute = nameof(CanDeploy))]
		private async Task RevertAsync()
		{
			GameInstall install = this._install;

			if (!this._ask.Confirm("Put the vanilla state back into the game directory?")) return;

			await this.RunAsync("Revert to vanilla.", report => this.Service().Revert(install, report));

			this.RefreshDeployedState();
		}

		private bool CanDeploy() => this.IsIdle && this.IsGameReady && this._profile != null;

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
		/// </summary>
		private async Task RunAsync(string title, Action<Action<string>> work)
		{
			if (this.IsBusy) return;

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
			this.MoveUpCommand.NotifyCanExecuteChanged();
			this.MoveDownCommand.NotifyCanExecuteChanged();
			this.RefreshModsCommand.NotifyCanExecuteChanged();
			this.RefreshConflictsCommand.NotifyCanExecuteChanged();
			this.DeployCommand.NotifyCanExecuteChanged();
			this.RevertCommand.NotifyCanExecuteChanged();
		}
	}
}
