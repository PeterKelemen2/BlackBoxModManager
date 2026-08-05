using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// What a filesystem command wants to do to one path.
	/// </summary>
	public sealed class PathEffect
	{
		/// <summary>The path exactly as the script wrote it.</summary>
		public string Written { get; }

		public PathAnchor Anchor { get; }

		public bool Writes { get; }

		/// <summary>
		/// The full path that the library builds, or null when no root was given. The
		/// preflight resolves paths only when it knows the staging directory.
		/// </summary>
		public string Resolved { get; }

		/// <summary>
		/// The reason that this path fails the sandbox test, or an empty string when it
		/// passes. An unresolved path always passes, because the test needs a root.
		/// </summary>
		public string Violation { get; }

		public bool IsSafe => this.Violation.Length == 0;

		public PathEffect(string written, PathAnchor anchor, bool writes, string resolved, string violation)
		{
			this.Written = written ?? String.Empty;
			this.Anchor = anchor;
			this.Writes = writes;
			this.Resolved = resolved;
			this.Violation = violation ?? String.Empty;
		}

		public override string ToString()
		{
			string what = this.Writes ? "writes" : "reads";

			return $"{what} {this.Written} under the {Name(this.Anchor)}";
		}

		private static string Name(PathAnchor anchor)
		{
			return anchor switch
			{
				PathAnchor.ModDirectory => "mod directory",
				PathAnchor.GameDirectory => "game directory",
				_ => "directory that the command names",
			};
		}
	}

	/// <summary>
	/// The two directories that a filesystem command may touch.
	/// </summary>
	public sealed class SandboxRoots
	{
		/// <summary>The staging copy of the game. A command may write here.</summary>
		public string GameDirectory { get; }

		/// <summary>
		/// The directory of the mod in the store. A command may read here. A write here
		/// changes the stored mod, and the revert never undoes it.
		/// </summary>
		public string ModDirectory { get; }

		public SandboxRoots(string gameDirectory, string modDirectory)
		{
			this.GameDirectory = gameDirectory;
			this.ModDirectory = modDirectory;
		}
	}

	/// <summary>
	/// Keeps a filesystem command inside the staging copy and inside the mod directory.
	///
	/// <b>A filesystem command escapes staging.</b> <c>create_file</c>, <c>erase_file</c>,
	/// <c>move_file</c>, and the folder commands act on a path and not on a container. The
	/// deploy logic and the revert logic see containers only, so a path outside staging
	/// reaches the real system and stays there.
	///
	/// Two forms escape. A path that holds <c>..</c> climbs out of the root.
	/// <c>Path.Combine</c> also drops the root when the second argument is rooted, so
	/// <c>C:\Windows\System32</c> as a path writes to that place and not under the game.
	/// </summary>
	public static class PathSandbox
	{
		/// <summary>
		/// Builds the path effect of one path argument, and tests it against the roots.
		///
		/// Pass null roots when no game directory exists yet. The result then carries no
		/// resolved path and no violation. Never treat that as a pass. Call this again with
		/// the roots before the deploy writes anything.
		/// </summary>
		public static PathEffect Describe(PathArgument argument, string[] tokens, SandboxRoots roots)
		{
			if (argument is null) throw new ArgumentNullException(nameof(argument));
			if (tokens is null) throw new ArgumentNullException(nameof(tokens));

			if (argument.PathToken >= tokens.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(argument),
					$"The command has {tokens.Length} tokens and the path is token {argument.PathToken}.");
			}

			string written = tokens[argument.PathToken];
			PathAnchor anchor = Anchor(argument, tokens);

			if (roots is null) return new PathEffect(written, anchor, argument.Writes, null, null);

			string root = anchor == PathAnchor.ModDirectory ? roots.ModDirectory : roots.GameDirectory;

			if (String.IsNullOrEmpty(root))
			{
				return new PathEffect(written, anchor, argument.Writes, null, null);
			}

			// A rooted path never reaches the root of the anchor, because Path.Combine
			// returns the second argument alone. Report that first, because no full path
			// under the anchor describes it.
			if (IsRooted(written))
			{
				string what = argument.Writes ? "writes to" : "reads";

				return new PathEffect(written, anchor, argument.Writes, written,
					$"The command {what} {written}. That path names its own root, so it never " +
					$"reaches {root}.");
			}

			// The library calls Path.Combine and nothing else. Repeat that, and convert the
			// separator first. The scripts come from Windows and write a backslash. A native
			// Linux run keeps a backslash inside a file name, and the test then misses an
			// escape that the same script makes under Wine.
			string resolved = Path.GetFullPath(Path.Combine(root, Separators(written)));
			string violation = Test(resolved, root, anchor, argument.Writes, roots);

			return new PathEffect(written, anchor, argument.Writes, resolved, violation);
		}

		/// <summary>
		/// Converts every separator of a path that a script wrote into the separator of this
		/// machine.
		/// </summary>
		private static string Separators(string path)
		{
			return path.Replace('\\', Path.DirectorySeparatorChar)
				.Replace('/', Path.DirectorySeparatorChar);
		}

		/// <summary>
		/// True when the path names its own root. This applies the Windows rules and the rules
		/// of this machine, because the deploy runs under Wine and the tests run on Linux.
		/// A drive letter, a leading separator, and a UNC name all count.
		/// </summary>
		private static bool IsRooted(string path)
		{
			if (String.IsNullOrEmpty(path)) return false;

			if (path[0] == '\\' || path[0] == '/') return true;

			if (path.Length >= 2 && path[1] == ':') return true;

			return Path.IsPathRooted(path);
		}

		/// <summary>
		/// Reads the anchor of a path argument. The word <c>relative</c> means the mod
		/// directory. The word <c>absolute</c> means the game directory, and not the root of
		/// the filesystem.
		/// </summary>
		private static PathAnchor Anchor(PathArgument argument, string[] tokens)
		{
			if (argument.Anchor != PathAnchor.ByTypeToken) return argument.Anchor;

			if (argument.TypeToken < 0 || argument.TypeToken >= tokens.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(argument),
					$"The command has {tokens.Length} tokens and the anchor is token {argument.TypeToken}.");
			}

			return String.Equals(tokens[argument.TypeToken], "relative", StringComparison.Ordinal)
				? PathAnchor.ModDirectory
				: PathAnchor.GameDirectory;
		}

		private static string Test(string resolved, string root, PathAnchor anchor, bool writes,
			SandboxRoots roots)
		{
			if (FileTree.IsSameOrInside(resolved, root)) return String.Empty;

			// The path left its own root. A read outside is still a read of a file that the
			// mod does not own. A write outside is the case that this class exists to stop.
			string what = writes ? "writes to" : "reads";

			// Name the other root when the path landed there, because that reads as a mod
			// that mixed up the two anchor words.
			if (!String.IsNullOrEmpty(roots.ModDirectory)
				&& anchor == PathAnchor.GameDirectory
				&& FileTree.IsSameOrInside(resolved, roots.ModDirectory))
			{
				return $"The command {what} {resolved}. That path is in the mod directory and the " +
					"command names the game directory.";
			}

			if (!String.IsNullOrEmpty(roots.GameDirectory)
				&& anchor == PathAnchor.ModDirectory
				&& FileTree.IsSameOrInside(resolved, roots.GameDirectory))
			{
				return $"The command {what} {resolved}. That path is in the game directory and the " +
					"command names the mod directory.";
			}

			return $"The command {what} {resolved}. That path is outside {root}.";
		}

		/// <summary>
		/// Returns the paths of one edit that fail the test. An empty result means that every
		/// path passed, or that the caller gave no roots.
		/// </summary>
		public static IReadOnlyList<PathEffect> Violations(ResolvedEdit edit)
		{
			if (edit is null) throw new ArgumentNullException(nameof(edit));

			var list = new List<PathEffect>();

			foreach (PathEffect effect in edit.Paths)
			{
				if (!effect.IsSafe) list.Add(effect);
			}

			return list;
		}
	}
}
