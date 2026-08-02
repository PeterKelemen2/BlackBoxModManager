using System;
using System.Collections.Generic;
using System.IO;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Thrown when a script cannot be read as a graph of append commands.
	/// </summary>
	public sealed class ScriptAppendException : Exception
	{
		public string File { get; }

		public int Line { get; }

		public ScriptAppendException(string message, string file, int line) : base(message)
		{
			this.File = file;
			this.Line = line;
		}
	}

	/// <summary>
	/// Walks the append graph of a script before the library parser runs.
	///
	/// EndScriptParser.RecursiveRead splices every append inline. It keeps no visited set
	/// and no depth cap, so a cycle makes it recurse until the stack ends. That failure
	/// gives no file name and no line. This class finds the cycle first and names both.
	///
	/// It resolves every append against the directory of the launcher script, not against
	/// the directory of the file that holds the append. That is what the parser does.
	/// </summary>
	public static class ScriptAppendGraph
	{
		/// <summary>
		/// A script deeper than this is a mistake, not a design. The camera mod reaches
		/// depth 2.
		/// </summary>
		public const int MaxDepth = 32;

		/// <summary>
		/// Returns every script file that the launcher pulls in, the launcher first.
		/// Throws ScriptAppendException on a cycle, on a missing file, or past the depth cap.
		/// </summary>
		public static IReadOnlyList<string> Walk(string launcherPath)
		{
			if (String.IsNullOrWhiteSpace(launcherPath)) throw new ArgumentException("The path is empty.", nameof(launcherPath));

			string full = Path.GetFullPath(launcherPath);
			string root = Path.GetDirectoryName(full);

			var visited = new List<string>();
			var onPath = new List<string>();

			Visit(full, root, visited, onPath, 0);

			return visited;
		}

		private static void Visit(string path, string root, List<string> visited, List<string> onPath, int depth)
		{
			if (depth > MaxDepth)
			{
				throw new ScriptAppendException(
					$"The append chain is deeper than {MaxDepth} files. The last file is {path}.", path, 0);
			}

			if (Contains(onPath, path))
			{
				// Name the whole loop. A bare "cycle detected" leaves the user searching.
				var loop = new List<string>();
				bool started = false;

				foreach (string entry in onPath)
				{
					if (!started && Same(entry, path)) started = true;
					if (started) loop.Add(Path.GetFileName(entry));
				}

				loop.Add(Path.GetFileName(path));

				throw new ScriptAppendException(
					$"The append commands make a loop: {String.Join(" -> ", loop)}.", path, 0);
			}

			if (!File.Exists(path))
			{
				throw new ScriptAppendException($"The script {path} does not exist.", path, 0);
			}

			// A file that two branches both append is not a cycle. Read it once.
			if (Contains(visited, path)) return;

			visited.Add(path);
			onPath.Add(path);

			string[] lines = File.ReadAllLines(path);

			// Line 0 holds the version header. The parser starts at line 1.
			for (int i = 1; i < lines.Length; ++i)
			{
				string line = lines[i].Trim();

				if (ScriptText.IsSkipped(line)) continue;

				string[] tokens = ScriptText.Tokenize(line);

				if (tokens.Length == 0 || tokens[0] != "append") continue;

				if (tokens.Length != 2)
				{
					throw new ScriptAppendException(
						$"An append command needs exactly 2 tokens and this one has {tokens.Length}.", path, i + 1);
				}

				Visit(ModPath.Resolve(root, tokens[1]), root, visited, onPath, depth + 1);
			}

			onPath.RemoveAt(onPath.Count - 1);
		}

		private static bool Contains(List<string> list, string path)
		{
			foreach (string entry in list)
			{
				if (Same(entry, path)) return true;
			}

			return false;
		}

		private static bool Same(string left, string right)
		{
			// Wine resolves letter case, so two spellings can name one file.
			return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
		}
	}
}
