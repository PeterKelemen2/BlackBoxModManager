using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// What the game directory holds right now. The workspace writes this after every swap.
	/// </summary>
	public sealed class WorkspaceState
	{
		public int Version { get; set; } = 1;

		/// <summary>The profile that the last deploy applied. This is null after a revert.</summary>
		public string DeployedProfile { get; set; }

		public DateTimeOffset? Deployed { get; set; }

		/// <summary>The number of files that the last deploy put in place.</summary>
		public int DeployedFileCount { get; set; }

		[JsonIgnore]
		public bool IsVanilla => String.IsNullOrEmpty(this.DeployedProfile);
	}

	/// <summary>
	/// The directories that one game install needs.
	///
	/// The workspace sits beside the game install by default. That matters. A hard link
	/// cannot cross a volume, and a directory move across a volume is a full copy. Both the
	/// staging build and the swap are cheap only on the volume of the game.
	///
	/// The layout is four entries under one directory.
	///
	/// 1. vanilla, the pristine state of the install.
	/// 2. staging, the copy that a deploy writes to.
	/// 3. previous, the live directory that a swap set aside.
	/// 4. vanilla.json and state.json, the bookkeeping.
	/// </summary>
	public sealed class GameWorkspace
	{
		/// <summary>The name of the workspace directory is the game directory plus this text.</summary>
		public const string WorkspaceSuffix = ".blackbox";

		public GameInstall Install { get; }

		/// <summary>The workspace directory itself.</summary>
		public string Root { get; }

		/// <summary>The pristine copy of the install. A revert restores this.</summary>
		public string VanillaDirectory => Path.Combine(this.Root, "vanilla");

		/// <summary>The copy that a deploy writes to. Never the live install.</summary>
		public string StagingDirectory => Path.Combine(this.Root, "staging");

		/// <summary>
		/// The live directory that a swap moved out of the way. A failed swap moves it back.
		/// </summary>
		public string PreviousDirectory => Path.Combine(this.Root, "previous");

		public string SnapshotFile => Path.Combine(this.Root, "vanilla.json");

		public string StateFile => Path.Combine(this.Root, "state.json");

		public bool HasVanilla => Directory.Exists(this.VanillaDirectory) && File.Exists(this.SnapshotFile);

		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		public GameWorkspace(GameInstall install, string workRootOverride = null)
		{
			this.Install = install ?? throw new ArgumentNullException(nameof(install));

			string parent = String.IsNullOrWhiteSpace(workRootOverride)
				? Path.GetDirectoryName(install.Root)
				: Path.GetFullPath(workRootOverride);

			if (String.IsNullOrEmpty(parent))
			{
				throw new ArgumentException(
					$"The game install {install.Root} has no parent directory, so the workspace has no place to go. " +
					"Set a work root in the settings.", nameof(install));
			}

			this.Root = Path.Combine(parent, install.DirectoryName + WorkspaceSuffix);

			// A workspace inside the install would make a swap delete the game.
			if (FileTree.IsSameOrInside(this.Root, install.Root))
			{
				throw new ArgumentException(
					$"The workspace {this.Root} sits inside the game install {install.Root}. " +
					"Choose a work root outside the game directory.", nameof(workRootOverride));
			}
		}

		public void Create()
		{
			Directory.CreateDirectory(this.Root);
		}

		/// <summary>
		/// True when the workspace and the game install sit on one volume. A workspace on
		/// another volume still works, and every build and every swap then copies every
		/// byte.
		/// </summary>
		public bool SharesVolumeWithGame()
		{
			try
			{
				string a = Path.GetPathRoot(Path.GetFullPath(this.Root));
				string b = Path.GetPathRoot(Path.GetFullPath(this.Install.Root));

				return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception)
			{
				return false;
			}
		}

		public WorkspaceState ReadState()
		{
			try
			{
				if (!File.Exists(this.StateFile)) return new WorkspaceState();

				return JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(this.StateFile), Options)
					?? new WorkspaceState();
			}
			catch (Exception)
			{
				// A damaged state file must not block a revert. Report vanilla, which is the
				// safe answer, because a revert from a vanilla directory is harmless.
				return new WorkspaceState();
			}
		}

		public void WriteState(WorkspaceState state)
		{
			if (state is null) throw new ArgumentNullException(nameof(state));

			this.Create();

			string temporary = this.StateFile + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
			File.Move(temporary, this.StateFile, true);
		}

		public VanillaSnapshot ReadSnapshot() => SnapshotReader.Load(this.SnapshotFile);

		public override string ToString() => $"workspace of {this.Install.DirectoryName} at {this.Root}";
	}
}
