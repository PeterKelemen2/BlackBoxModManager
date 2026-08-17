using System;
using System.Collections.Generic;
using BlackboxModManager.Core.Mods;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Resolves the script of a variant one time and hands the same result to every caller.
	///
	/// <b>The resolve is not cheap.</b> <c>ScriptFlattener.Resolve</c> walks the append graph
	/// with <c>ScriptAppendGraph.Walk</c> and then parses it again with <c>ScriptReader.Parse</c>.
	/// Both read every file of the graph, and one real mod appends 158 files. A deploy called
	/// the pair twice, once in the conflict preflight and once in the command gate, so it read
	/// about 632 files where 158 answer everything.
	///
	/// <b>One cache serves one staging directory.</b> The resolve turns every path of a
	/// filesystem command into a full path under that directory, so a result from one directory
	/// says nothing about another. The constructor takes the directory and the cache keeps it.
	/// </summary>
	public sealed class ScriptResolutionCache
	{
		private readonly string _stagingDirectory;
		private readonly Dictionary<EnabledVariant, ResolvedScript> _map;

		public string StagingDirectory => this._stagingDirectory;

		/// <summary>How many resolves this cache answered from its own store.</summary>
		public int Hits { get; private set; }

		/// <summary>How many resolves this cache ran.</summary>
		public int Misses { get; private set; }

		public ScriptResolutionCache(string stagingDirectory)
		{
			this._stagingDirectory = stagingDirectory;
			this._map = new Dictionary<EnabledVariant, ResolvedScript>();
		}

		/// <summary>
		/// Returns the resolved script of one variant. The first call runs the resolve and every
		/// later call returns the same object.
		///
		/// A resolve that throws is not stored, so the next caller sees the same failure.
		/// </summary>
		public ResolvedScript Resolve(EnabledVariant variant)
		{
			if (variant is null) throw new ArgumentNullException(nameof(variant));

			if (this._map.TryGetValue(variant, out ResolvedScript stored))
			{
				++this.Hits;
				return stored;
			}

			var roots = new SandboxRoots(this._stagingDirectory, variant.Variant.Manifest.ThisDir);
			ResolvedScript resolved = ScriptFlattener.Resolve(variant.Variant, variant.Selection, roots);

			this._map[variant] = resolved;
			++this.Misses;

			return resolved;
		}
	}
}
