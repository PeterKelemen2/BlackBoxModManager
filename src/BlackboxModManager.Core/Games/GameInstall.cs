using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Mods;
using Nikki.Core;

namespace BlackboxModManager.Core.Games
{
	/// <summary>
	/// Names the check that decided the result. The order matches the order that
	/// GameInstallValidator runs the checks in.
	/// </summary>
	public enum GameInstallCheck
	{
		/// <summary>Every check passed. The install is usable.</summary>
		Ok = 0,

		/// <summary>No path is set. The user has not chosen a directory yet.</summary>
		NoPath,

		/// <summary>This application does not manage the game yet.</summary>
		UnknownGame,

		/// <summary>The directory does not exist. A stored path can go stale between sessions.</summary>
		DirectoryMissing,

		/// <summary>The directory holds no game executable.</summary>
		ExecutableMissing,

		/// <summary>The executable is there and the game content is not. See MissingContent.</summary>
		ContentMissing,
	}

	/// <summary>
	/// The result of one validation run. This is a typed result and not a boolean, because
	/// the user needs to know which check failed.
	/// </summary>
	public sealed class GameInstallStatus
	{
		public GameInstallCheck Check { get; }

		public GameINT Game { get; }

		/// <summary>The path that we validated. This is null when Check is NoPath.</summary>
		public string Root { get; }

		/// <summary>The definition that we validated against. This is null for an unknown game.</summary>
		public GameDefinition Definition { get; }

		/// <summary>
		/// The markers that the directory does not hold. This is empty unless Check is
		/// ContentMissing.
		/// </summary>
		public IReadOnlyList<string> MissingContent { get; }

		/// <summary>One sentence that names the failure. This is empty when Check is Ok.</summary>
		public string Message { get; }

		public bool IsUsable => this.Check == GameInstallCheck.Ok;

		internal GameInstallStatus(GameInstallCheck check, GameINT game, string root,
			GameDefinition definition, IReadOnlyList<string> missing, string message)
		{
			this.Check = check;
			this.Game = game;
			this.Root = root;
			this.Definition = definition;
			this.MissingContent = missing ?? Array.Empty<string>();
			this.Message = message ?? String.Empty;
		}

		/// <summary>
		/// The validated install. This is null when IsUsable is false. Take the install from
		/// here, never from a raw path, so that no caller can skip the validator.
		/// </summary>
		public GameInstall Install =>
			this.IsUsable ? new GameInstall(this.Root, this.Definition) : null;
	}

	/// <summary>
	/// A game install that passed every check. Only GameInstallStatus creates one.
	///
	/// This type names the live install of the user. <b>Never write into it.</b> A deploy
	/// builds a staging copy, verifies it, and then swaps it in.
	/// </summary>
	public sealed class GameInstall
	{
		public string Root { get; }

		public GameDefinition Definition { get; }

		public GameINT Game => this.Definition.Game;

		internal GameInstall(string root, GameDefinition definition)
		{
			this.Root = root;
			this.Definition = definition;
		}

		/// <summary>The full path of the game executable on this machine.</summary>
		public string ExecutablePath => ModPath.Resolve(this.Root, this.Definition.Executable);

		/// <summary>
		/// The directory name of the install. The workspace of the game takes its name from
		/// this value.
		/// </summary>
		public string DirectoryName => Path.GetFileName(this.Root);

		public override string ToString() => $"{this.Definition.DisplayName} at {this.Root}";
	}
}
