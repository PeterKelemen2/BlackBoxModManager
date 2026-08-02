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

		public DeployedFile(string relativePath, string modId, LinkKind kind, bool overridesGameFile)
		{
			this.RelativePath = relativePath;
			this.ModId = modId;
			this.Kind = kind;
			this.OverridesGameFile = overridesGameFile;
		}

		public override string ToString() => $"{this.RelativePath} from {this.ModId} by {this.Kind}";
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

		public int FileCount => this.Files.Count;

		public DeployReport(IReadOnlyList<DeployedFile> files, IReadOnlyList<DeployOverride> overrides,
			IReadOnlyDictionary<LinkKind, int> methods, string methodNote,
			IReadOnlyList<ContainerWrite> containers = null)
		{
			this.Files = files ?? Array.Empty<DeployedFile>();
			this.Overrides = overrides ?? Array.Empty<DeployOverride>();
			this.Methods = methods ?? new Dictionary<LinkKind, int>();
			this.MethodNote = methodNote ?? String.Empty;
			this.Containers = containers ?? Array.Empty<ContainerWrite>();
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
