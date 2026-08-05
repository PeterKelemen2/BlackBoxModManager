using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// What one engine needs to know about the deploy that runs.
	///
	/// The staging directory is the only directory that an engine writes to.
	/// <b>No engine writes into GameInstall.Root.</b> The swap happens after every engine
	/// finishes and after the verify passes.
	/// </summary>
	public sealed class DeployContext
	{
		public GameInstall Game { get; }

		/// <summary>The copy that the engines write to.</summary>
		public string StagingDirectory { get; }

		public Profile Profile { get; }

		public ModStore Store { get; }

		/// <summary>
		/// The Binary install that holds the hash lists. This is null when the user has not
		/// set one. Only the container engine needs it, and that engine reports the need.
		/// </summary>
		public BinaryInstall Binary { get; }

		/// <summary>
		/// Where the engine writes its progress. The UI shows these lines. This is never
		/// null, so an engine can call it with no check.
		/// </summary>
		public Action<string> Log { get; }

		/// <summary>
		/// Which mod supplies each ASI loader file. This is null when the caller built no plan,
		/// and the link engine then places every copy and the last mod of the load order wins.
		///
		/// <b>DeployService builds the plan and refuses an unanswered contest.</b> The engine
		/// reads the plan and skips the copy of every mod that lost. See step 9.
		/// </summary>
		public ProxyPlan Proxies { get; }

		public DeployContext(GameInstall game, string stagingDirectory, Profile profile,
			ModStore store, BinaryInstall binary = null, Action<string> log = null,
			ProxyPlan proxies = null)
		{
			this.Binary = binary;
			this.Proxies = proxies;

			this.Game = game ?? throw new ArgumentNullException(nameof(game));
			this.Profile = profile ?? throw new ArgumentNullException(nameof(profile));
			this.Store = store ?? throw new ArgumentNullException(nameof(store));

			if (String.IsNullOrWhiteSpace(stagingDirectory))
			{
				throw new ArgumentException("The staging directory is empty.", nameof(stagingDirectory));
			}

			this.StagingDirectory = stagingDirectory;
			this.Log = log ?? (line => { });
		}
	}

	/// <summary>
	/// One way to put mods into the staging copy.
	///
	/// Two engines exist. The link engine handles the drop-in kinds, and it is in this
	/// step. <b>Step 6 adds the container engine.</b> That engine shares the staging copy,
	/// the snapshot, and the revert path, and it shares no part of the link strategy. The
	/// interface exists to keep that boundary.
	/// </summary>
	public interface IDeployEngine
	{
		/// <summary>A name for a log line and for an error message.</summary>
		string Name { get; }

		/// <summary>The mod kinds that this engine deploys.</summary>
		IReadOnlySet<ModKind> Kinds { get; }

		/// <summary>
		/// Puts the given mods into the staging copy, in load order. The caller passes only
		/// the mods whose kind this engine claims.
		/// </summary>
		DeployReport Deploy(DeployContext context, IReadOnlyList<InstalledMod> mods);
	}
}
