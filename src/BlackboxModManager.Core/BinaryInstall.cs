using System;
using System.Collections.Generic;
using Nikki.Core;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// Names the check that decided the result. The order matches the order that
	/// BinaryInstallValidator runs the checks in.
	/// </summary>
	public enum BinaryInstallCheck
	{
		/// <summary>Every check passed. The install is usable.</summary>
		Ok = 0,

		/// <summary>No path is set. The user has not answered the first-run question yet.</summary>
		NoPath,

		/// <summary>The directory does not exist. A stored path can go stale between sessions.</summary>
		DirectoryMissing,

		/// <summary>The directory holds no Binary.exe, so it is not a Binary install.</summary>
		ExecutableMissing,

		/// <summary>The install holds no mainkeys directory.</summary>
		MainKeysDirectoryMissing,

		/// <summary>One or more per-game hash lists are absent. See MissingHashLists.</summary>
		HashListMissing,
	}

	/// <summary>
	/// The result of one validation run. This is a typed result and not a boolean, because
	/// the user needs to know which check failed.
	/// </summary>
	public sealed class BinaryInstallStatus
	{
		/// <summary>The version that we developed against. A different version is a warning.</summary>
		public static readonly Version ExpectedVersion = new Version(2, 8, 3, 0);

		public BinaryInstallCheck Check { get; }

		/// <summary>The path that we validated. This is null when Check is NoPath.</summary>
		public string Root { get; }

		/// <summary>The version that we read from the install. This is null when we could not read it.</summary>
		public Version Version { get; }

		/// <summary>The games whose hash list is absent. This is empty on a good install.</summary>
		public IReadOnlyList<GameINT> MissingHashLists { get; }

		/// <summary>One sentence that names the failure. This is empty when Check is Ok.</summary>
		public string Message { get; }

		/// <summary>
		/// The version does not match ExpectedVersion, or we could not read it. This is a
		/// warning and not a failure. Our expectations come from 2.8.3, so we say so and continue.
		/// </summary>
		public string VersionWarning { get; }

		public bool IsUsable => this.Check == BinaryInstallCheck.Ok;

		internal BinaryInstallStatus(BinaryInstallCheck check, string root, Version version,
			IReadOnlyList<GameINT> missing, string message, string versionWarning)
		{
			this.Check = check;
			this.Root = root;
			this.Version = version;
			this.MissingHashLists = missing ?? Array.Empty<GameINT>();
			this.Message = message ?? String.Empty;
			this.VersionWarning = versionWarning ?? String.Empty;
		}

		/// <summary>
		/// The validated install. This is null when IsUsable is false. Take the install from
		/// here, never from a raw path, so that no caller can skip the validator.
		/// </summary>
		public BinaryInstall Install => this.IsUsable ? new BinaryInstall(this.Root, this.Version) : null;
	}

	/// <summary>
	/// A Binary install that passed every check. Only BinaryInstallStatus creates one.
	/// </summary>
	public sealed class BinaryInstall
	{
		public string Root { get; }

		public Version Version { get; }

		internal BinaryInstall(string root, Version version)
		{
			this.Root = root;
			this.Version = version;
		}

		public string MainHashList(GameINT game) => HashListPaths.MainHashList(this.Root, game);

		public override string ToString() => $"Binary {this.Version?.ToString() ?? "of an unknown version"} at {this.Root}";
	}
}
