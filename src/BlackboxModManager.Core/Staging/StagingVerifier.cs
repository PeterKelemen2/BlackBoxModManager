using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// What the verify found.
	/// </summary>
	public sealed class VerificationResult
	{
		/// <summary>One sentence per problem. An empty list means that the copy is good.</summary>
		public IReadOnlyList<string> Problems { get; }

		public int CheckedFiles { get; }

		public bool IsClean => this.Problems.Count == 0;

		public VerificationResult(IReadOnlyList<string> problems, int checkedFiles)
		{
			this.Problems = problems ?? Array.Empty<string>();
			this.CheckedFiles = checkedFiles;
		}
	}

	/// <summary>
	/// Checks the staging copy before the swap.
	///
	/// The check answers two questions. Does the staging copy still hold every vanilla
	/// file, and does every deployed file hold the content that its mod supplied.
	///
	/// The quick check compares the length of a vanilla file and the hash of a deployed
	/// file. A game install holds gigabytes that no deploy touched, and a hash of all of it
	/// costs minutes for no new answer. The full check hashes everything. Use the full
	/// check when a deploy produced a result that nobody can explain.
	/// </summary>
	public static class StagingVerifier
	{
		public static VerificationResult Verify(string stagingDirectory, VanillaSnapshot snapshot,
			DeployReport report, ModStore store, bool full = false, Action<string> log = null)
		{
			if (String.IsNullOrWhiteSpace(stagingDirectory)) throw new ArgumentException("The staging directory is empty.", nameof(stagingDirectory));
			if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
			if (report is null) throw new ArgumentNullException(nameof(report));
			if (store is null) throw new ArgumentNullException(nameof(store));

			var problems = new List<string>();

			// Two mods can supply one path. The later one in the load order wrote last, so
			// it is the one that the staging copy must match. Keep the last writer only.
			var deployed = new Dictionary<string, DeployedFile>(StringComparer.OrdinalIgnoreCase);

			foreach (DeployedFile file in report.Files) deployed[PathKey.Normalize(file.RelativePath)] = file;

			// A container that the container engine rewrote differs from the vanilla state on
			// purpose, and it matches no file in a mod store. There is nothing to compare it
			// against, so the check on it is existence and a length above zero.
			var containers = new Dictionary<string, ContainerWrite>(StringComparer.OrdinalIgnoreCase);

			foreach (ContainerWrite write in report.Containers)
			{
				containers[PathKey.Normalize(write.RelativePath)] = write;
			}

			// A file that a filesystem command of a script wrote differs from the vanilla state
			// on purpose too, and it matches nothing in a mod store either. The command
			// unlock_memory writes a header over the memory files of the game, and move_file
			// writes a target that no manifest names. See defect 16.
			var written = new Dictionary<string, ScriptWrite>(StringComparer.OrdinalIgnoreCase);

			foreach (ScriptWrite write in report.ScriptWrites)
			{
				written[PathKey.Normalize(write.RelativePath)] = write;
			}

			log?.Invoke(full
				? "Verify the staging copy. The full check hashes every file."
				: "Verify the staging copy.");

			// 1. The vanilla files. A deployed file replaced its vanilla twin on purpose,
			// so leave those out of this pass.
			IReadOnlyList<SnapshotDifference> differences = SnapshotReader.Compare(
				snapshot, stagingDirectory, full, relative =>
				{
					string key = PathKey.Normalize(relative);

					return deployed.ContainsKey(key) || containers.ContainsKey(key)
						|| written.ContainsKey(key);
				});

			foreach (SnapshotDifference difference in differences)
			{
				problems.Add(difference.Kind switch
				{
					SnapshotDifferenceKind.Missing =>
						$"The staging copy does not hold the game file {difference.RelativePath}.",
					SnapshotDifferenceKind.Changed =>
						$"The game file {difference.RelativePath} in the staging copy differs from the vanilla state, and no mod supplied it.",
					_ =>
						$"The staging copy holds {difference.RelativePath}, which is neither a game file nor a file that a mod supplied.",
				});
			}

			// 2. The deployed files. These are the files that the deploy wrote, and their
			// content must match the mod that supplied them.
			int checkedFiles = snapshot.Count;

			foreach (DeployedFile file in deployed.Values)
			{
				string target = FileTree.Combine(stagingDirectory, file.RelativePath);
				++checkedFiles;

				if (!File.Exists(target))
				{
					problems.Add($"The staging copy does not hold {file.RelativePath}, which the mod \"{file.ModId}\" supplied.");
					continue;
				}

				InstalledMod mod = store.Find(file.ModId);

				if (mod is null)
				{
					problems.Add($"The mod \"{file.ModId}\" left the store during the deploy.");
					continue;
				}

				string source = FileTree.Combine(mod.ContentRoot, file.RelativePath);

				if (!File.Exists(source))
				{
					problems.Add($"The mod \"{file.ModId}\" no longer holds {file.RelativePath}.");
					continue;
				}

				// A settings file that took the answers of the profile differs from the mod
				// store on purpose. There is nothing to compare it against, so the check on it
				// is existence and a length above zero.
				if (file.Edited)
				{
					if (new FileInfo(target).Length == 0)
					{
						problems.Add($"The settings file {file.RelativePath} of the mod \"{file.ModId}\" " +
							"is empty in the staging copy. The deploy applied answers to it.");
					}

					continue;
				}

				if (!FileHash.SameContent(source, target))
				{
					problems.Add($"The staging copy of {file.RelativePath} differs from the copy in the mod \"{file.ModId}\".");
				}
			}

			// 3. The containers. A container that Save wrote has to exist and to hold bytes.
			// A container that saves without error can still fail to load in the game, and
			// no check here can answer that. Only a run of the game answers it.
			foreach (ContainerWrite write in containers.Values)
			{
				string target = ModPath.Resolve(stagingDirectory, write.RelativePath);
				++checkedFiles;

				if (!File.Exists(target))
				{
					problems.Add($"The staging copy does not hold the container {write.RelativePath}, " +
						$"which {String.Join(", ", write.Contributors)} edited.");
					continue;
				}

				if (new FileInfo(target).Length == 0)
				{
					problems.Add($"The container {write.RelativePath} in the staging copy is empty. " +
						$"{String.Join(", ", write.Contributors)} edited it.");
				}
			}

			// 4. The files of a filesystem command carry no check of their own.
			//
			// <b>A path in this list is a path that a script may write, not one that it did.</b>
			// The static walk enters both branches of every <c>if</c>, and a real mod guards
			// almost every filesystem command with one. NFSMWRV-1024x-Advanced holds 97
			// <c>move_file</c> commands behind <c>if file_exists</c>, and the answers of the user
			// decide how many run. So an absent file is normal here and a present file needs no
			// comparison. The one thing that matters is that the pass above left them alone.
			checkedFiles += written.Count;

			log?.Invoke(problems.Count == 0
				? $"The verify checked {checkedFiles} files and found no problem."
				: $"The verify found {problems.Count} problems.");

			return new VerificationResult(problems, checkedFiles);
		}
	}
}
