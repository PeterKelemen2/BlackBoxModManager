using System;
using System.Collections.Generic;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// Where a Binary install path came from. The caller reports this, so that the user can
	/// see whether the tool used a stored answer or a value from this run.
	/// </summary>
	public enum BinaryInstallSource
	{
		None = 0,

		/// <summary>The caller passed the path for this run only.</summary>
		Override,

		/// <summary>The settings file held the path.</summary>
		Settings,
	}

	/// <summary>
	/// Resolves the Binary install for one run. It joins the settings store, the locator,
	/// and the validator.
	///
	/// This class never guesses. When no confirmed path exists, it reports the candidates
	/// and leaves the answer to the user. Block, do not degrade.
	/// </summary>
	public sealed class BinaryInstallService
	{
		private readonly string _settingsFile;

		public BinaryInstallService() : this(AppPaths.SettingsFile) { }

		public BinaryInstallService(string settingsFile)
		{
			this._settingsFile = settingsFile;
		}

		/// <summary>
		/// Resolves and validates. Pass a path in overridePath to use it for this run only.
		/// Pass null to read the stored answer.
		/// </summary>
		public BinaryInstallResolution Resolve(string overridePath = null)
		{
			if (!String.IsNullOrWhiteSpace(overridePath))
			{
				return new BinaryInstallResolution(
					BinaryInstallSource.Override, BinaryInstallValidator.Validate(overridePath), null);
			}

			Settings settings = SettingsStore.Load(this._settingsFile);

			if (!String.IsNullOrWhiteSpace(settings.BinaryInstallDirectory))
			{
				return new BinaryInstallResolution(
					BinaryInstallSource.Settings, BinaryInstallValidator.Validate(settings.BinaryInstallDirectory), null);
			}

			// No stored answer. Offer what the machine holds, and let the user confirm one.
			return new BinaryInstallResolution(
				BinaryInstallSource.None, BinaryInstallValidator.Validate(null), BinaryInstallLocator.FindCandidates());
		}

		/// <summary>
		/// Validates a path and stores it when it passes. A path that fails is not stored,
		/// so the settings file never holds a value that the validator rejects.
		/// </summary>
		public BinaryInstallStatus Store(string path)
		{
			BinaryInstallStatus status = BinaryInstallValidator.Validate(path);

			if (!status.IsUsable) return status;

			Settings settings = SettingsStore.Load(this._settingsFile);
			settings.BinaryInstallDirectory = status.Root;
			SettingsStore.Save(this._settingsFile, settings);

			return status;
		}

		/// <summary>
		/// Removes the stored path. The next run asks again.
		/// </summary>
		public void Forget()
		{
			Settings settings = SettingsStore.Load(this._settingsFile);
			settings.BinaryInstallDirectory = null;
			SettingsStore.Save(this._settingsFile, settings);
		}
	}

	public sealed class BinaryInstallResolution
	{
		public BinaryInstallSource Source { get; }

		public BinaryInstallStatus Status { get; }

		/// <summary>
		/// The directories that the locator found. These are suggestions that the user has to
		/// confirm. The list is empty unless Source is None.
		/// </summary>
		public IReadOnlyList<string> Candidates { get; }

		public bool IsUsable => this.Status.IsUsable;

		public BinaryInstall Install => this.Status.Install;

		internal BinaryInstallResolution(BinaryInstallSource source, BinaryInstallStatus status,
			IReadOnlyList<string> candidates)
		{
			this.Source = source;
			this.Status = status;
			this.Candidates = candidates ?? Array.Empty<string>();
		}
	}
}
