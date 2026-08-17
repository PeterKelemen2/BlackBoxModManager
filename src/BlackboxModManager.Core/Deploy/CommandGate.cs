using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;
using Endscript.Enums;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// What the gate read.
	/// </summary>
	public sealed class GateResult
	{
		/// <summary>
		/// One entry for each variant, in the order that the gate read them. The order is the
		/// load order, so entry <c>i</c> belongs to variant <c>i</c>.
		///
		/// <b>The engine runs these and does not read the scripts again.</b> A second walk
		/// costs one text parse for each appended file, and a large mod appends hundreds.
		/// </summary>
		public IReadOnlyList<ResolvedScript> Scripts { get; }

		/// <summary>
		/// Every container that a command names, as the script wrote the path.
		///
		/// <b>The engine makes each of these private before the pass.</b> The manifest names
		/// only the containers of the merged load. A script also creates a container with
		/// <c>new</c> and writes it with <c>delete</c>, and that container is in no manifest.
		/// See <c>ContainerDeployEngine.Prepare</c>.
		/// </summary>
		public IReadOnlyList<string> Containers { get; }

		/// <summary>
		/// Every full path in the staging copy that a filesystem command writes.
		///
		/// <b>A container is not the only thing that a script writes.</b> The command
		/// <c>unlock_memory</c> writes a header over five memory files, and <c>move_file</c>
		/// and <c>copy_file</c> write a target that no manifest names. None of those carries
		/// an edit key, so the Containers list holds none of them. The engine makes each of
		/// these private too. See defect 16.
		/// </summary>
		public IReadOnlyList<string> WritePaths { get; }

		/// <summary>
		/// Which variants ran a command against each entry of Containers, keyed by the
		/// normalized path.
		///
		/// <b>The verify needs this for the containers that no manifest names.</b> A container
		/// that a manifest declares gets its contributors from MergedLoad. A container that
		/// only a script names, such as the per-car VINYLS.BIN of a vinyl mod, has no manifest
		/// entry to read that from. This map is the only record of who touched it.
		/// </summary>
		public IReadOnlyDictionary<string, IReadOnlyList<string>> ContainerContributors { get; }

		/// <summary>
		/// Which variants wrote each entry of WritePaths, keyed by the full path.
		///
		/// <b>The verify needs this for a file that no manifest and no edit key names.</b> The
		/// command <c>unlock_memory</c> writes the memory files of the game, and this map is the
		/// only record of which mod asked for that. See defect 16.
		/// </summary>
		public IReadOnlyDictionary<string, IReadOnlyList<string>> WritePathContributors { get; }

		public GateResult(IReadOnlyList<ResolvedScript> scripts, IReadOnlyList<string> containers,
			IReadOnlyList<string> writePaths = null,
			IReadOnlyDictionary<string, IReadOnlyList<string>> containerContributors = null,
			IReadOnlyDictionary<string, IReadOnlyList<string>> writePathContributors = null)
		{
			this.Scripts = scripts ?? Array.Empty<ResolvedScript>();
			this.Containers = containers ?? Array.Empty<string>();
			this.WritePaths = writePaths ?? Array.Empty<string>();
			this.ContainerContributors = containerContributors ??
				new Dictionary<string, IReadOnlyList<string>>();
			this.WritePathContributors = writePathContributors ??
				new Dictionary<string, IReadOnlyList<string>>();
		}
	}

	/// <summary>
	/// Stops a deploy that would run a command that this application refuses, or that would
	/// write outside the staging copy.
	///
	/// <b>The gate runs inside the deploy engine and not only in the preflight.</b> The
	/// preflight tells the user what is wrong. The gate is the guarantee. A caller that skips
	/// the preflight must not be able to skip the rule.
	///
	/// The gate reads every script one time and returns the result. The engine runs that
	/// result, so the rule sits beside the code that writes and the parse happens one time.
	/// </summary>
	public static class CommandGate
	{
		/// <summary>
		/// Tests every enabled variant. It throws on the first variant that fails, and the
		/// message names the mod, the file, the line, and the command.
		/// </summary>
		public static GateResult Check(IReadOnlyList<EnabledVariant> variants, string stagingDirectory,
			Action<string> log = null, ScriptResolutionCache cache = null)
		{
			if (variants is null) throw new ArgumentNullException(nameof(variants));

			if (String.IsNullOrEmpty(stagingDirectory))
			{
				throw new ArgumentException("The staging directory is empty.", nameof(stagingDirectory));
			}

			Action<string> write = log ?? (line => { });
			ScriptResolutionCache resolver = cache ?? new ScriptResolutionCache(stagingDirectory);

			var refused = new List<string>();
			var outside = new List<string>();
			var scripts = new List<ResolvedScript>(variants.Count);
			var containers = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var containerContributors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			var writePaths = new List<string>();
			var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var writePathContributors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			int warned = 0;

			foreach (EnabledVariant variant in variants)
			{
				// A script that this call cannot read stops the deploy on its own, further
				// down. Let the exception travel, because the engine names the variant.
				ResolvedScript resolved = resolver.Resolve(variant);

				scripts.Add(resolved);

				foreach (ResolvedEdit edit in resolved.Rejected)
				{
					refused.Add($"The mod \"{variant.Label}\" runs the command \"{edit.Verb}\" at " +
						$"{edit.Where}. This application does not run that command. {edit.Facts.Note}");
				}

				foreach ((ResolvedEdit Edit, PathEffect Path) escape in resolved.Escapes())
				{
					outside.Add($"The mod \"{variant.Label}\" runs the command \"{escape.Edit.Verb}\" at " +
						$"{escape.Edit.Where}, and that command leaves the staging copy. " +
						escape.Path.Violation);
				}

				// Collect the container of every edit. The engine gives each one a private
				// copy, so that a write cannot reach the vanilla copy or the game.
				foreach (ResolvedEdit edit in resolved.Edits)
				{
					string target = edit.Key?.TargetFile;

					if (String.IsNullOrWhiteSpace(target)) continue;

					string key = PathKey.Normalize(target);

					if (seen.Add(key)) containers.Add(target);

					if (!containerContributors.TryGetValue(key, out List<string> owners))
					{
						owners = new List<string>();
						containerContributors[key] = owners;
					}

					if (!owners.Contains(variant.Label)) owners.Add(variant.Label);
				}

				// Collect every path in the game directory that a filesystem command writes.
				// These carry no edit key, so the loop above never sees them. The check above
				// already proved that each one stays inside the staging copy.
				foreach (ResolvedEdit edit in resolved.Edits)
				{
					foreach (PathEffect path in edit.Paths)
					{
						if (!path.Writes) continue;
						if (path.Anchor != PathAnchor.GameDirectory) continue;
						if (String.IsNullOrEmpty(path.Resolved)) continue;

						if (seenPaths.Add(path.Resolved)) writePaths.Add(path.Resolved);

						if (!writePathContributors.TryGetValue(path.Resolved, out List<string> writers))
						{
							writers = new List<string>();
							writePathContributors[path.Resolved] = writers;
						}

						if (!writers.Contains(variant.Label)) writers.Add(variant.Label);
					}
				}

				warned += resolved.Warnings.Count;

				foreach (string line in Summarize(resolved.Warnings)) write($"  warning: {variant.Label}: {line}");
			}

			if (outside.Count > 0)
			{
				// Report this one first. A path outside staging reaches the real system, and
				// the revert never undoes it.
				throw new DeployServiceException(
					$"{outside.Count} commands write outside the staging copy, so the deploy stopped " +
					$"before it changed anything. {String.Join(" ", outside)}");
			}

			if (refused.Count > 0)
			{
				throw new DeployServiceException(
					$"{refused.Count} commands need support that this application does not have, so the " +
					$"deploy stopped before it changed anything. {String.Join(" ", refused)}");
			}

			write($"The command gate read {variants.Count} variants. It refused nothing and it found " +
				$"{warned} commands that the conflict check cannot compare.");

			var readOnlyContributors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, List<string>> entry in containerContributors)
			{
				readOnlyContributors[entry.Key] = entry.Value;
			}

			var readOnlyWriters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, List<string>> entry in writePathContributors)
			{
				readOnlyWriters[entry.Key] = entry.Value;
			}

			return new GateResult(scripts, containers, writePaths, readOnlyContributors, readOnlyWriters);
		}

		/// <summary>
		/// Turns the warnings of one variant into one line for each verb.
		///
		/// A mod that edits one container for each car repeats one warning for each file. The
		/// Recompiled Vinyls mod produces 92 lines of two texts. One line for each verb keeps
		/// the count and the reason, and it names the first place and the last place.
		/// </summary>
		private static IReadOnlyList<string> Summarize(IReadOnlyList<ScriptWarning> warnings)
		{
			var order = new List<eCommandType>();
			var groups = new Dictionary<eCommandType, List<ScriptWarning>>();

			foreach (ScriptWarning warning in warnings)
			{
				if (!groups.TryGetValue(warning.Verb, out List<ScriptWarning> group))
				{
					group = new List<ScriptWarning>();
					groups.Add(warning.Verb, group);
					order.Add(warning.Verb);
				}

				group.Add(warning);
			}

			var lines = new List<string>(order.Count);

			foreach (eCommandType verb in order)
			{
				List<ScriptWarning> group = groups[verb];
				ScriptWarning first = group[0];

				if (group.Count == 1)
				{
					lines.Add(first.ToString());
					continue;
				}

				ScriptWarning last = group[group.Count - 1];

				lines.Add($"the command \"{verb}\" {first.Reason} It runs {group.Count} times, " +
					$"from {first.SourceFile} line {first.SourceLine} " +
					$"to {last.SourceFile} line {last.SourceLine}.");
			}

			return lines;
		}
	}
}
