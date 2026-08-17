using System;
using System.Collections.Generic;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// One file that a deploy put into the staging copy.
	/// </summary>
	public sealed class DeployedFile
	{
		/// <summary>The path inside the game directory, with a forward slash separator.</summary>
		public string RelativePath { get; }

		/// <summary>The store identifier of the mod that supplied the file.</summary>
		public string ModId { get; }

		/// <summary>The method that put the file in place.</summary>
		public LinkKind Kind { get; }

		/// <summary>True when this file replaced a file of the vanilla install.</summary>
		public bool OverridesGameFile { get; }

		/// <summary>
		/// True when the deploy changed the content after it placed the file. A settings file
		/// that carries an answer of the profile lands here.
		///
		/// <b>The verify cannot compare an edited file against the mod store.</b> It differs
		/// from the store copy on purpose, so the check on it is existence and a length above
		/// zero. See StagingVerifier.
		/// </summary>
		public bool Edited { get; }

		public DeployedFile(string relativePath, string modId, LinkKind kind, bool overridesGameFile,
			bool edited = false)
		{
			this.RelativePath = relativePath;
			this.ModId = modId;
			this.Kind = kind;
			this.OverridesGameFile = overridesGameFile;
			this.Edited = edited;
		}

		public override string ToString() =>
			this.Edited
				? $"{this.RelativePath} from {this.ModId} by {this.Kind}, with settings applied"
				: $"{this.RelativePath} from {this.ModId} by {this.Kind}";
	}

	/// <summary>
	/// One settings file that the deploy rewrote with the answers of the profile.
	/// </summary>
	public sealed class SettingsWrite
	{
		public string RelativePath { get; }

		public string ModId { get; }

		/// <summary>The options whose value the deploy changed, as <c>SECTION/Key</c>.</summary>
		public IReadOnlyList<string> Changed { get; }

		/// <summary>
		/// The options that the profile named and the file does not hold. A mod update that
		/// renames a key produces these.
		/// </summary>
		public IReadOnlyList<string> Skipped { get; }

		public SettingsWrite(string relativePath, string modId, IReadOnlyList<string> changed,
			IReadOnlyList<string> skipped)
		{
			this.RelativePath = relativePath;
			this.ModId = modId;
			this.Changed = changed ?? Array.Empty<string>();
			this.Skipped = skipped ?? Array.Empty<string>();
		}

		public override string ToString()
		{
			string tail = this.Skipped.Count == 0
				? String.Empty
				: $" It does not hold {String.Join(", ", this.Skipped)}.";

			return $"{this.RelativePath} of \"{this.ModId}\": {this.Changed.Count} options changed.{tail}";
		}
	}

	/// <summary>
	/// One ASI loader file, with the mod that supplied it and the mods that lost.
	/// </summary>
	public sealed class LoaderChoice
	{
		/// <summary>The loader file name, such as <c>dinput8.dll</c>.</summary>
		public string ProxyName { get; }

		public string WinnerModId { get; }

		public string WinnerModName { get; }

		/// <summary>The version or the short hash of the file that the deploy placed.</summary>
		public string WinnerVersion { get; }

		/// <summary>The mods whose copy the deploy skipped, by name.</summary>
		public IReadOnlyList<string> Skipped { get; }

		public LoaderChoice(string proxyName, string winnerModId, string winnerModName,
			string winnerVersion, IReadOnlyList<string> skipped)
		{
			this.ProxyName = proxyName;
			this.WinnerModId = winnerModId;
			this.WinnerModName = winnerModName;
			this.WinnerVersion = winnerVersion;
			this.Skipped = skipped ?? Array.Empty<string>();
		}

		public override string ToString()
		{
			string tail = this.Skipped.Count == 0
				? " No other mod supplies it."
				: $" The deploy skipped the copy of {String.Join(", ", this.Skipped)}.";

			return $"{this.ProxyName} comes from \"{this.WinnerModName}\", {this.WinnerVersion}.{tail}";
		}
	}

	/// <summary>
	/// Two mods supply one file. The later mod in the load order wins.
	/// </summary>
	public sealed class DeployOverride
	{
		public string RelativePath { get; }

		public string LoserModId { get; }

		public string WinnerModId { get; }

		public DeployOverride(string relativePath, string loserModId, string winnerModId)
		{
			this.RelativePath = relativePath;
			this.LoserModId = loserModId;
			this.WinnerModId = winnerModId;
		}

		public override string ToString() =>
			$"{this.RelativePath}: \"{this.WinnerModId}\" replaces \"{this.LoserModId}\"";
	}

	/// <summary>
	/// One container that the deploy rewrote inside the staging copy.
	///
	/// A container is not a file that a mod supplied. The engine loads the container of the
	/// game, applies the edits of every mod to it in memory, and writes it back. The result
	/// therefore matches no file in the mod store, and it differs from the vanilla state on
	/// purpose. The verify has to know that.
	/// </summary>
	public sealed class ContainerWrite
	{
		/// <summary>The path inside the game directory, as the manifests spell it.</summary>
		public string RelativePath { get; }

		/// <summary>The variants that named this container in their manifest.</summary>
		public IReadOnlyList<string> Contributors { get; }

		public ContainerWrite(string relativePath, IReadOnlyList<string> contributors)
		{
			this.RelativePath = relativePath;
			this.Contributors = contributors ?? Array.Empty<string>();
		}

		public override string ToString() =>
			$"{this.RelativePath} for {String.Join(", ", this.Contributors)}";
	}

	/// <summary>
	/// One file in the game directory that a filesystem command of a script wrote, and which is
	/// no container.
	///
	/// <b>The verify needs this list.</b> The command <c>unlock_memory</c> writes a short header
	/// over the memory files of the game, and <c>move_file</c> and <c>copy_file</c> write a
	/// target. None of those files is in a manifest, none carries an edit key, and none matches
	/// anything in a mod store. Without this list the verify reports each one as "differs from
	/// the vanilla state, and no mod supplied it," and it stops a deploy that did what the mod
	/// asked for. See defect 16.
	/// </summary>
	public sealed class ScriptWrite
	{
		/// <summary>The path inside the game directory.</summary>
		public string RelativePath { get; }

		/// <summary>The variants whose script wrote this path.</summary>
		public IReadOnlyList<string> Contributors { get; }

		public ScriptWrite(string relativePath, IReadOnlyList<string> contributors)
		{
			this.RelativePath = relativePath;
			this.Contributors = contributors ?? Array.Empty<string>();
		}

		public override string ToString() =>
			$"{this.RelativePath} for {String.Join(", ", this.Contributors)}";
	}

	/// <summary>
	/// What one deploy did. The UI shows this, so that a user can understand a slow deploy
	/// and a surprising result.
	/// </summary>
	public sealed class DeployReport
	{
		public IReadOnlyList<DeployedFile> Files { get; }

		/// <summary>
		/// The containers that the deploy rewrote. This is empty when no Binary mod is on.
		/// </summary>
		public IReadOnlyList<ContainerWrite> Containers { get; }

		/// <summary>
		/// The collisions, in the order that they happened. An empty list is the normal
		/// case, and it is not a promise that the mods agree. Two mods can edit one
		/// container without sharing a file.
		/// </summary>
		public IReadOnlyList<DeployOverride> Overrides { get; }

		/// <summary>
		/// The methods that the deploy used, with a count for each one. A user who sees a
		/// large Copy count knows why the deploy took disk space and time.
		/// </summary>
		public IReadOnlyDictionary<LinkKind, int> Methods { get; }

		/// <summary>
		/// The reason that the cheapest method did not work. This is empty when the deploy
		/// used the cheapest method for every file.
		/// </summary>
		public string MethodNote { get; }

		/// <summary>
		/// The settings files that the deploy rewrote with the answers of the profile. This is
		/// empty when the profile changed no option.
		/// </summary>
		public IReadOnlyList<SettingsWrite> Settings { get; }

		/// <summary>
		/// The ASI loader files, with the mod that supplied each one. This is empty when no
		/// enabled mod ships a loader.
		/// </summary>
		public IReadOnlyList<LoaderChoice> Loaders { get; }

		/// <summary>
		/// The files that a filesystem command of a script wrote, and which are no containers.
		/// This is empty when no enabled script runs such a command.
		/// </summary>
		public IReadOnlyList<ScriptWrite> ScriptWrites { get; }

		public int FileCount => this.Files.Count;

		public DeployReport(IReadOnlyList<DeployedFile> files, IReadOnlyList<DeployOverride> overrides,
			IReadOnlyDictionary<LinkKind, int> methods, string methodNote,
			IReadOnlyList<ContainerWrite> containers = null,
			IReadOnlyList<SettingsWrite> settings = null,
			IReadOnlyList<LoaderChoice> loaders = null,
			IReadOnlyList<ScriptWrite> scriptWrites = null)
		{
			this.Files = files ?? Array.Empty<DeployedFile>();
			this.Overrides = overrides ?? Array.Empty<DeployOverride>();
			this.Methods = methods ?? new Dictionary<LinkKind, int>();
			this.MethodNote = methodNote ?? String.Empty;
			this.Containers = containers ?? Array.Empty<ContainerWrite>();
			this.Settings = settings ?? Array.Empty<SettingsWrite>();
			this.Loaders = loaders ?? Array.Empty<LoaderChoice>();
			this.ScriptWrites = scriptWrites ?? Array.Empty<ScriptWrite>();
		}

		/// <summary>
		/// One line that names the counts. The UI puts this in the status bar.
		/// </summary>
		public string Summary()
		{
			var parts = new List<string>(3);

			foreach (LinkKind kind in new[] { LinkKind.HardLink, LinkKind.SymbolicLink, LinkKind.Copy })
			{
				if (this.Methods.TryGetValue(kind, out int count) && count > 0) parts.Add($"{count} by {kind}");
			}

			string containers = this.Containers.Count == 0
				? String.Empty
				: $" It rewrote {this.Containers.Count} containers.";

			if (parts.Count == 0)
			{
				return containers.Length == 0
					? "The deploy put no file in place."
					: $"The deploy put no file in place.{containers}";
			}

			return $"The deploy put {this.FileCount} files in place: {String.Join(", ", parts)}.{containers}";
		}
	}
}
