using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace BlackboxModManager.App.Services
{
	/// <summary>
	/// The update check. This is the only class that calls Velopack.
	///
	/// The feed is the releases page of the repository on GitHub. `tools/pack.ps1` writes the
	/// packages, and the release workflow uploads them. See
	/// docs/roadmap/16-release-and-update.md.
	/// </summary>
	public sealed class UpdateService
	{
		/// <summary>The repository that holds the releases.</summary>
		public const string RepositoryUrl = "https://github.com/PeterKelemen2/BlackBoxModManager";

		private readonly UpdateManager _manager;

		public UpdateService()
		{
			// No access token.
			//
			// GitHub allows 60 requests each hour for one address without one. A person presses
			// the button a few times in a day, and a start checks one time. That fits.
			//
			// Never ship a token to raise the limit. The token would reach every user, and a
			// token in a public build is a token that somebody else uses.
			var source = new GithubSource(RepositoryUrl, null, this.WantsPrerelease());

			this._manager = new UpdateManager(source);
		}

		/// <summary>
		/// True when Velopack manages this copy of the application.
		///
		/// A build that runs out of a publish directory reports false. `tools/run-app.ps1`
		/// produces exactly that, so a developer sees false every day. <b>Test this before
		/// every other member of this class.</b>
		/// </summary>
		public bool IsInstalled => this._manager.IsInstalled;

		/// <summary>
		/// The version that Velopack recorded for this install, or null when it manages no
		/// install.
		///
		/// This is the value that a check compares against the feed. It comes from the package
		/// metadata. <c>AppVersion.Display</c> comes from the assembly, and a check must never
		/// use that one.
		/// </summary>
		public SemanticVersion CurrentVersion => this._manager.CurrentVersion;

		/// <summary>
		/// Asks the feed for a newer release. It returns null when this build is current.
		/// </summary>
		public Task<UpdateInfo> CheckAsync() => this._manager.CheckForUpdatesAsync();

		/// <summary>Downloads a release that <see cref="CheckAsync"/> found.</summary>
		public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken token)
		{
			return this._manager.DownloadUpdatesAsync(update, progress, token);
		}

		/// <summary>
		/// Puts the downloaded release in place and starts the new build.
		///
		/// <b>This ends the process, and it never returns.</b> Call it on the UI thread, and
		/// only after every other operation has finished.
		/// </summary>
		public void ApplyAndRestart(UpdateInfo update)
		{
			this._manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
		}

		/// <summary>
		/// Whether the feed should offer a prerelease.
		///
		/// This follows the build that runs. A user on 0.1.0-alpha.1 sees 0.1.0-alpha.2, and a
		/// user on 1.0.0 never sees an alpha. The rule needs no edit at 1.0.
		///
		/// This runs before the constructor sets the field, so it reads a manager of its own.
		/// A throwaway manager costs nothing, because the constructor of UpdateManager opens no
		/// connection.
		/// </summary>
		private bool WantsPrerelease()
		{
			try
			{
				var probe = new UpdateManager(new GithubSource(RepositoryUrl, null, false));

				return probe.IsInstalled && probe.CurrentVersion?.IsPrerelease == true;
			}
			catch (Exception)
			{
				// A build that Velopack does not manage cannot check for an update at all, so
				// the answer here changes nothing.
				return false;
			}
		}
	}
}
