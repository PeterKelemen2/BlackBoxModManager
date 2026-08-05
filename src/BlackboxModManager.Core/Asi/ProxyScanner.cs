using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Asi
{
	/// <summary>
	/// One mod that supplies one loader file.
	/// </summary>
	public sealed class ProxyCandidate
	{
		public string ModId { get; }

		public string ModName { get; }

		/// <summary>The path inside the game directory, with a forward slash separator.</summary>
		public string RelativePath { get; }

		public long Bytes { get; }

		public ProxyIdentity Identity { get; }

		/// <summary>The position of the mod in the load order, from one.</summary>
		public int Order { get; }

		public ProxyCandidate(string modId, string modName, string relativePath, long bytes,
			ProxyIdentity identity, int order)
		{
			this.ModId = modId;
			this.ModName = modName;
			this.RelativePath = relativePath;
			this.Bytes = bytes;
			this.Identity = identity;
			this.Order = order;
		}

		/// <summary>The three facts that a user needs: the mod, the size, and the version.</summary>
		public string Describe() =>
			$"\"{this.ModName}\", {this.Bytes} bytes, {this.Identity.Describe()}";

		public override string ToString() => this.Describe();
	}

	/// <summary>
	/// Every mod that supplies one loader name, and which one the profile chose.
	/// </summary>
	public sealed class ProxyContest
	{
		/// <summary>The loader file name, such as <c>dinput8.dll</c>.</summary>
		public string ProxyName { get; }

		/// <summary>Every enabled mod that supplies the file, in load order.</summary>
		public IReadOnlyList<ProxyCandidate> Candidates { get; }

		/// <summary>
		/// The mod that the profile named, or null when the profile holds no answer or the
		/// stored answer no longer applies.
		/// </summary>
		public ProxyCandidate Chosen { get; }

		/// <summary>
		/// Why the stored answer does not apply, or an empty string. A mod that left the
		/// profile, a mod that the user switched off, and a mod that no longer holds the file
		/// all produce a reason here.
		/// </summary>
		public string Reason { get; }

		/// <summary>True when more than one mod supplies the file.</summary>
		public bool IsContested => this.Candidates.Count > 1;

		/// <summary>
		/// True when the deploy needs an answer from the user. One candidate needs none, and a
		/// contest with a valid stored answer needs none either.
		/// </summary>
		public bool NeedsAnswer => this.IsContested && this.Chosen is null;

		/// <summary>
		/// True when every candidate is the same file. The dialog says so, and the user can
		/// then pick any one of them without further thought.
		/// </summary>
		public bool AllSameFile
		{
			get
			{
				if (this.Candidates.Count < 2) return false;

				string first = this.Candidates[0].Identity.Hash;

				if (first.Length == 0) return false;

				foreach (ProxyCandidate candidate in this.Candidates)
				{
					if (candidate.Identity.Hash != first) return false;
				}

				return true;
			}
		}

		public ProxyContest(string proxyName, IReadOnlyList<ProxyCandidate> candidates,
			ProxyCandidate chosen, string reason = null)
		{
			this.ProxyName = proxyName;
			this.Candidates = candidates ?? Array.Empty<ProxyCandidate>();
			this.Chosen = chosen;
			this.Reason = reason ?? String.Empty;
		}

		/// <summary>
		/// The mod that the deploy places the file from. This is the stored answer, or the one
		/// candidate when only one exists, or null when the user still has to answer.
		///
		/// <b>Never pick a loader automatically for a contest.</b> A proxy DLL forwards to the
		/// real system library, and a version that forwards wrongly breaks sound or input
		/// rather than the plugin. That is why the user chooses.
		/// </summary>
		public ProxyCandidate Supplier => this.Chosen ?? (this.Candidates.Count == 1 ? this.Candidates[0] : null);

		/// <summary>Every candidate that the deploy skips.</summary>
		public IEnumerable<ProxyCandidate> Skipped
		{
			get
			{
				ProxyCandidate winner = this.Supplier;

				if (winner is null) yield break;

				foreach (ProxyCandidate candidate in this.Candidates)
				{
					if (candidate.ModId != winner.ModId) yield return candidate;
				}
			}
		}

		public override string ToString()
		{
			ProxyCandidate winner = this.Supplier;

			return winner is null
				? $"{this.ProxyName}: {this.Candidates.Count} candidates and no answer"
				: $"{this.ProxyName}: \"{winner.ModName}\" of {this.Candidates.Count} candidates";
		}
	}

	/// <summary>
	/// What the loader scan found across the enabled mods.
	/// </summary>
	public sealed class ProxyPlan
	{
		public IReadOnlyList<ProxyContest> Contests { get; }

		/// <summary>
		/// The loader names that a mod supplies and that this application does not manage. The
		/// window shows these so that a user who hits the case can report it.
		/// </summary>
		public IReadOnlyList<string> Unmanaged { get; }

		public ProxyPlan(IReadOnlyList<ProxyContest> contests, IReadOnlyList<string> unmanaged = null)
		{
			this.Contests = contests ?? Array.Empty<ProxyContest>();
			this.Unmanaged = unmanaged ?? Array.Empty<string>();
		}

		/// <summary>The contests that the user still has to answer.</summary>
		public IEnumerable<ProxyContest> Open
		{
			get
			{
				foreach (ProxyContest contest in this.Contests)
				{
					if (contest.NeedsAnswer) yield return contest;
				}
			}
		}

		/// <summary>True when the deploy can run with no further question.</summary>
		public bool IsSettled
		{
			get
			{
				foreach (ProxyContest contest in this.Contests)
				{
					if (contest.NeedsAnswer) return false;
				}

				return true;
			}
		}

		public ProxyContest Find(string proxyName)
		{
			foreach (ProxyContest contest in this.Contests)
			{
				if (String.Equals(contest.ProxyName, proxyName, StringComparison.OrdinalIgnoreCase)) return contest;
			}

			return null;
		}

		/// <summary>
		/// The paths that the deploy must not place, keyed by mod identifier. The link engine
		/// reads this and skips those files.
		/// </summary>
		public IReadOnlyDictionary<string, IReadOnlySet<string>> SkipByMod()
		{
			var skip = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

			foreach (ProxyContest contest in this.Contests)
			{
				foreach (ProxyCandidate candidate in contest.Skipped)
				{
					if (!skip.TryGetValue(candidate.ModId, out HashSet<string> paths))
					{
						paths = new HashSet<string>(StringComparer.Ordinal);
						skip[candidate.ModId] = paths;
					}

					paths.Add(PathKey.Normalize(candidate.RelativePath));
				}
			}

			var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

			foreach (KeyValuePair<string, HashSet<string>> entry in skip) result[entry.Key] = entry.Value;

			return result;
		}
	}

	/// <summary>
	/// Finds every loader file across the enabled mods and reads the answer of the profile.
	///
	/// The game directory holds exactly one file at each loader path. Several mods ship one
	/// each, and one loader then runs the plugins of every mod whatever mod supplied it. Before
	/// this step the last mod in the load order won and no log line mentioned it.
	/// </summary>
	public static class ProxyScanner
	{
		/// <summary>
		/// Scans the mods in load order. Pass the mods that the profile switched on, in that
		/// order, and the stored answers.
		/// </summary>
		public static ProxyPlan Scan(IReadOnlyList<InstalledMod> mods,
			IReadOnlyDictionary<string, string> choices, IReadOnlySet<string> proxyNames = null)
		{
			if (mods is null) throw new ArgumentNullException(nameof(mods));

			IReadOnlySet<string> names = proxyNames ?? ProxyNames.Default;

			// One list of candidates per loader name, in load order.
			var order = new List<string>();
			var byName = new Dictionary<string, List<ProxyCandidate>>(StringComparer.OrdinalIgnoreCase);
			var unmanaged = new List<string>();
			int position = 0;

			foreach (InstalledMod mod in mods)
			{
				++position;

				if (!Directory.Exists(mod.ContentRoot)) continue;

				foreach (string relative in FileTree.Files(mod.ContentRoot))
				{
					string file = Path.GetFileName(relative);

					if (!names.Contains(file))
					{
						// A name that a real mod uses and this application does not manage yet.
						if (ProxyNames.Known.Contains(file))
						{
							unmanaged.Add($"The mod \"{mod.Name}\" supplies {relative}. That name is an " +
								"ASI loader and this application does not manage it yet, so the last mod " +
								"of the load order wins it.");
						}

						continue;
					}

					if (!byName.TryGetValue(file, out List<ProxyCandidate> list))
					{
						order.Add(file);
						list = new List<ProxyCandidate>();
						byName[file] = list;
					}

					string full = FileTree.Combine(mod.ContentRoot, relative);
					long bytes = Length(full);

					list.Add(new ProxyCandidate(mod.Id, mod.Name, relative, bytes,
						ProxyIdentityReader.Read(full), position));
				}
			}

			var contests = new List<ProxyContest>(order.Count);

			foreach (string name in order)
			{
				contests.Add(Resolve(name, byName[name], choices));
			}

			return new ProxyPlan(contests, unmanaged);
		}

		/// <summary>
		/// Matches the stored answer against the candidates.
		///
		/// <b>Keep the first answer until the user changes it.</b> A deploy that already holds
		/// a valid choice asks nothing. A deploy where the chosen mod is gone, is switched off,
		/// or no longer holds the file asks again, and the reason says which of the three
		/// happened.
		/// </summary>
		private static ProxyContest Resolve(string name, IReadOnlyList<ProxyCandidate> candidates,
			IReadOnlyDictionary<string, string> choices)
		{
			string stored = null;

			choices?.TryGetValue(name, out stored);

			if (String.IsNullOrEmpty(stored)) return new ProxyContest(name, candidates, null);

			foreach (ProxyCandidate candidate in candidates)
			{
				if (String.Equals(candidate.ModId, stored, StringComparison.OrdinalIgnoreCase))
				{
					return new ProxyContest(name, candidates, candidate);
				}
			}

			return new ProxyContest(name, candidates, null,
				$"The profile chose the mod \"{stored}\" for {name}. That mod is switched off, it left " +
				"the store, or it no longer holds the file. Choose again.");
		}

		private static long Length(string path)
		{
			try
			{
				return new FileInfo(path).Length;
			}
			catch (Exception)
			{
				return 0;
			}
		}
	}
}
