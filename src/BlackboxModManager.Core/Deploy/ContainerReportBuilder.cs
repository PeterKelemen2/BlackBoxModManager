using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Builds the container list that a deploy reports, from the containers that the manifests
	/// declare and the containers that the scripts actually target.
	///
	/// <b>The manifest list is not the whole list.</b> A script creates a container with
	/// <c>new</c> and writes it with <c>delete</c>, and no manifest names that container. The
	/// verify step trusts this list to know which containers differ from the vanilla state on
	/// purpose. A container that this builder omits fails the verify with "no mod supplied it,"
	/// even though the engine already wrote it. See defect 16.
	/// </summary>
	public static class ContainerReportBuilder
	{
		/// <summary>
		/// Returns one ContainerWrite for every path in merged.Files or gate.Containers, with
		/// no duplicate. A path that the manifest declares keeps its manifest contributors. A
		/// path that only a script names gets its contributors from the gate.
		/// </summary>
		public static IReadOnlyList<ContainerWrite> Build(MergedLoad merged, GateResult gate)
		{
			if (merged is null) throw new ArgumentNullException(nameof(merged));
			if (gate is null) throw new ArgumentNullException(nameof(gate));

			var writes = new List<ContainerWrite>(merged.Files.Count + gate.Containers.Count);
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (string file in merged.Files)
			{
				if (!seen.Add(PathKey.Normalize(file))) continue;

				writes.Add(new ContainerWrite(file, merged.Contributors[file]));
			}

			foreach (string file in gate.Containers)
			{
				string key = PathKey.Normalize(file);

				if (!seen.Add(key)) continue;

				IReadOnlyList<string> contributors = gate.ContainerContributors.TryGetValue(key,
					out IReadOnlyList<string> owners) ? owners : Array.Empty<string>();

				writes.Add(new ContainerWrite(file, contributors));
			}

			return writes;
		}

		/// <summary>
		/// Returns one ScriptWrite for every full path in gate.WritePaths that is no container,
		/// with the path made relative to the staging directory.
		///
		/// <b>A container is not the only thing that a script writes.</b> The gate resolves every
		/// write of every filesystem command. Those files carry no edit key, so Build never sees
		/// them, and the verify then reports each one as a change that no mod supplied. The mod
		/// NFSMWRV-1024x-Advanced runs <c>unlock_memory all</c>, and that stopped a clean deploy
		/// with three problems. See defect 16.
		/// </summary>
		public static IReadOnlyList<ScriptWrite> BuildScriptWrites(string stagingDirectory,
			GateResult gate, IReadOnlyList<ContainerWrite> containers)
		{
			if (gate is null) throw new ArgumentNullException(nameof(gate));

			var known = new HashSet<string>(StringComparer.Ordinal);

			if (containers != null)
			{
				foreach (ContainerWrite write in containers) known.Add(PathKey.Normalize(write.RelativePath));
			}

			var writes = new List<ScriptWrite>(gate.WritePaths.Count);

			foreach (string full in gate.WritePaths)
			{
				string relative = Staging.BaselineVerifier.RelativeTo(stagingDirectory, full);

				// A path that resolves outside the staging copy never gets here. The gate stops
				// the deploy for one of those before it writes.
				if (relative is null) continue;

				if (!known.Add(PathKey.Normalize(relative))) continue;

				writes.Add(new ScriptWrite(relative, gate.WritePathContributors.TryGetValue(full,
					out IReadOnlyList<string> owners) ? owners : Array.Empty<string>()));
			}

			return writes;
		}
	}
}
