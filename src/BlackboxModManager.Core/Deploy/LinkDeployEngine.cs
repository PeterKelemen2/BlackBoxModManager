using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Thrown when a file cannot reach the staging copy by any method.
	/// </summary>
	public sealed class DeployException : Exception
	{
		public string RelativePath { get; }

		public string ModId { get; }

		public DeployException(string message, string relativePath, string modId, Exception inner = null)
			: base(message, inner)
		{
			this.RelativePath = relativePath;
			this.ModId = modId;
		}
	}

	/// <summary>
	/// Puts drop-in mods into the staging copy.
	///
	/// This engine handles an ASI plugin and a loose file. Both are the same problem. The
	/// mod holds files at the paths that they take inside the game directory, and the
	/// engine puts each one in place.
	///
	/// The method chain is hard link, then symbolic link, then copy. A probe against the
	/// two real directories chooses the first one. A file that the chosen method rejects
	/// falls to the next method, and the report says so.
	/// </summary>
	public sealed class LinkDeployEngine : IDeployEngine
	{
		public string Name => "link engine";

		public IReadOnlySet<ModKind> Kinds { get; } = new HashSet<ModKind> { ModKind.Asi, ModKind.LooseFiles };

		/// <summary>
		/// The methods in the order that this engine tries them. Copy is last, and copy
		/// always works.
		/// </summary>
		public static IReadOnlyList<LinkKind> Chain { get; } = new[]
		{
			LinkKind.HardLink, LinkKind.SymbolicLink, LinkKind.Copy,
		};

		public DeployReport Deploy(DeployContext context, IReadOnlyList<InstalledMod> mods)
		{
			if (context is null) throw new ArgumentNullException(nameof(context));
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			if (!Directory.Exists(context.StagingDirectory))
			{
				throw new DeployException(
					$"The staging directory {context.StagingDirectory} does not exist.", null, null);
			}

			// The live install must never receive a write. Prove it here, where the message
			// can still name the cause.
			if (FileTree.IsSameOrInside(context.StagingDirectory, context.Game.Root))
			{
				throw new DeployException(
					$"The staging directory {context.StagingDirectory} is the game install, or sits inside it. " +
					"A deploy writes only to a staging copy.", null, null);
			}

			var files = new List<DeployedFile>();
			var overrides = new List<DeployOverride>();
			var methods = new Dictionary<LinkKind, int>();

			// Which mod supplied a path so far. The last writer wins, and this map names
			// the one that lost.
			var writers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			LinkKind best = LinkKind.Copy;
			string methodNote = String.Empty;

			foreach (InstalledMod mod in mods)
			{
				if (!this.Kinds.Contains(mod.Kind))
				{
					throw new DeployException(
						$"The {this.Name} does not deploy the mod \"{mod.Name}\", which is of kind {mod.Kind}.",
						null, mod.Id);
				}

				if (!Directory.Exists(mod.ContentRoot))
				{
					throw new DeployException(
						$"The mod \"{mod.Name}\" holds no content directory at {mod.ContentRoot}.", null, mod.Id);
				}

				// Probe once per mod. Two mods can sit on one volume and the answer is then
				// the same, and the probe costs one small file.
				LinkProbeResult probe = LinkSupport.ProbeBetween(mod.ContentRoot, context.StagingDirectory);
				best = probe.Best;

				if (best != LinkKind.HardLink && methodNote.Length == 0)
				{
					methodNote = Explain(probe, mod);
					context.Log($"{mod.Name}: {methodNote}");
				}

				IReadOnlyList<string> content = FileTree.Files(mod.ContentRoot);
				context.Log($"{mod.Name}: {content.Count} files by {best}.");

				foreach (string relative in content)
				{
					string source = FileTree.Combine(mod.ContentRoot, relative);
					string target = FileTree.Combine(context.StagingDirectory, relative);

					bool replaced = File.Exists(target);

					if (writers.TryGetValue(Key(relative), out string earlier))
					{
						overrides.Add(new DeployOverride(relative, earlier, mod.Id));
					}

					LinkKind used = Place(source, target, relative, mod, best);

					writers[Key(relative)] = mod.Id;
					files.Add(new DeployedFile(relative, mod.Id, used, replaced));
					methods[used] = methods.TryGetValue(used, out int count) ? count + 1 : 1;
				}
			}

			return new DeployReport(files, overrides, methods, methodNote);
		}

		/// <summary>
		/// Puts one file in place and returns the method that worked.
		///
		/// It removes the file that is there first. A delete breaks a hard link and leaves
		/// the other name alone. A write through the link would change the vanilla copy and
		/// the mod store instead.
		/// </summary>
		private static LinkKind Place(string source, string target, string relative,
			InstalledMod mod, LinkKind best)
		{
			FileTree.CreateParent(target);
			Remove(target);

			// The game writes to some of its files. Those need a private copy, whatever the
			// probe allows. See DeployPolicy.
			bool mustCopy = DeployPolicy.NeedsCopy(relative);
			var failures = new List<string>();

			foreach (LinkKind kind in Chain)
			{
				if (kind != LinkKind.Copy && (mustCopy || Rank(kind) < Rank(best))) continue;

				try
				{
					LinkSupport.Create(kind, source, target);
					return kind;
				}
				catch (Exception ex)
				{
					failures.Add($"{kind}: {ex.Message}");
					Remove(target);
				}
			}

			throw new DeployException(
				$"The file {relative} of the mod \"{mod.Name}\" did not reach the staging copy. " +
				String.Join(" ", failures),
				relative, mod.Id);
		}

		/// <summary>
		/// The position of a method in the chain. A smaller number is cheaper.
		/// </summary>
		private static int Rank(LinkKind kind)
		{
			for (int i = 0; i < Chain.Count; ++i)
			{
				if (Chain[i] == kind) return i;
			}

			return Chain.Count;
		}

		private static string Explain(LinkProbeResult probe, InstalledMod mod)
		{
			foreach (LinkProbe entry in probe.Probes)
			{
				if (entry.Kind == LinkKind.HardLink && !entry.Works)
				{
					return $"a hard link from the mod store does not work, so the deploy uses {probe.Best}. " +
						$"{entry.Error} A hard link cannot cross a volume. " +
						$"The mod store is at {mod.ContentRoot}.";
				}
			}

			return $"the deploy uses {probe.Best}.";
		}

		private static void Remove(string path)
		{
			var file = new FileInfo(path);

			// Exists reports false for a symbolic link whose target is gone. LinkTarget
			// answers for that case, and a leftover link would block the write.
			if (!file.Exists && file.LinkTarget is null) return;

			try
			{
				if (file.Exists && file.IsReadOnly) file.IsReadOnly = false;
			}
			catch (Exception)
			{
				// The delete below reports the real problem, and it names the path.
			}

			File.Delete(path);
		}

		private static string Key(string relative) => PathKey.Normalize(relative);
	}
}
