using System;
using System.Collections.Generic;
using Nikki.Core;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// Where a game install path came from. The UI reports this, so that the user can see
	/// whether the tool used a stored answer or a value from this run.
	/// </summary>
	public enum GameInstallSource
	{
		None = 0,

		/// <summary>The caller passed the path for this run only.</summary>
		Override,

		/// <summary>The settings file held the path.</summary>
		Settings,
	}

	public sealed class GameInstallResolution
	{
		public GameInstallSource Source { get; }

		public GameInstallStatus Status { get; }

		/// <summary>
		/// The directories that the locator found. These are suggestions that the user has to
		/// confirm. The list is empty unless Source is None.
		/// </summary>
		public IReadOnlyList<string> Candidates { get; }

		public bool IsUsable => this.Status.IsUsable;

		public GameInstall Install => this.Status.Install;

		internal GameInstallResolution(GameInstallSource source, GameInstallStatus status,
			IReadOnlyList<string> candidates)
		{
			this.Source = source;
			this.Status = status;
			this.Candidates = candidates ?? Array.Empty<string>();
		}
	}

	/// <summary>
	/// Resolves the install of one game. It joins the settings store, the locator, and the
	/// validator.
	///
	/// This class never guesses. When no confirmed path exists, it reports the candidates
	/// and leaves the answer to the user. Block, do not degrade.
	///
	/// The UI must ask in a dialog. Console.ReadLine never returns on a Wine console. See
	/// 00-test-environment.md.
	/// </summary>
	public sealed class GameInstallService
	{
		private readonly string _settingsFile;

		public GameInstallService() : this(AppPaths.SettingsFile) { }

		public GameInstallService(string settingsFile)
		{
			this._settingsFile = settingsFile;
		}

		/// <summary>
		/// Resolves and validates one game. Pass a path in overridePath to use it for this
		/// run only. Pass null to read the stored answer.
		/// </summary>
		public GameInstallResolution Resolve(GameINT game, string overridePath = null)
		{
			if (!String.IsNullOrWhiteSpace(overridePath))
			{
				return new GameInstallResolution(
					GameInstallSource.Override, GameInstallValidator.Validate(game, overridePath), null);
			}

			Settings settings = SettingsStore.Load(this._settingsFile);

			if (settings.GameDirectories.TryGetValue(Key(game), out string stored)
				&& !String.IsNullOrWhiteSpace(stored))
			{
				return new GameInstallResolution(
					GameInstallSource.Settings, GameInstallValidator.Validate(game, stored), null);
			}

			// No stored answer. Offer what the machine holds, and let the user confirm one.
			GameDefinition definition = GameCatalog.Find(game);
			IReadOnlyList<string> candidates = definition is null
				? Array.Empty<string>()
				: GameInstallLocator.FindCandidates(definition);

			return new GameInstallResolution(
				GameInstallSource.None, GameInstallValidator.Validate(game, null), candidates);
		}

		/// <summary>
		/// Resolves every game that this application manages, in catalog order. One entry
		/// comes back per game, and an entry can report that the game has no path yet.
		///
		/// This reads the settings file only. It runs no scan, so the window can call it on
		/// every game switch.
		/// </summary>
		public IReadOnlyList<GameInstallResolution> ResolveAll()
		{
			var found = new List<GameInstallResolution>();
			Settings settings = SettingsStore.Load(this._settingsFile);

			foreach (GameDefinition definition in GameCatalog.All)
			{
				settings.GameDirectories.TryGetValue(Key(definition.Game), out string stored);

				GameInstallSource source = String.IsNullOrWhiteSpace(stored)
					? GameInstallSource.None
					: GameInstallSource.Settings;

				found.Add(new GameInstallResolution(
					source, GameInstallValidator.Validate(definition.Game, stored), null));
			}

			return found;
		}

		/// <summary>
		/// Scans the machine once and returns the candidates of every game. This is the slow
		/// operation. Run it on a background thread.
		/// </summary>
		public IReadOnlyDictionary<GameINT, IReadOnlyList<string>> DetectAll()
		{
			return GameInstallLocator.FindAll();
		}

		/// <summary>
		/// Validates a path and stores it when it passes. A path that fails is not stored,
		/// so the settings file never holds a value that the validator rejects.
		/// </summary>
		public GameInstallStatus Store(GameINT game, string path)
		{
			GameInstallStatus status = GameInstallValidator.Validate(game, path);

			if (!status.IsUsable) return status;

			Settings settings = SettingsStore.Load(this._settingsFile);
			settings.GameDirectories[Key(game)] = status.Root;
			SettingsStore.Save(this._settingsFile, settings);

			return status;
		}

		/// <summary>
		/// Removes the stored path of one game. The next run asks again.
		/// </summary>
		public void Forget(GameINT game)
		{
			Settings settings = SettingsStore.Load(this._settingsFile);
			settings.GameDirectories.Remove(Key(game));
			SettingsStore.Save(this._settingsFile, settings);
		}

		private static string Key(GameINT game) => game.ToString();
	}
}
