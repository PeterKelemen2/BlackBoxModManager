using System;
using System.Collections.Generic;
using System.Linq;
using CoreExtensions.Text;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// The text rules of an endscript. One place, so that our parsing matches the parsing
	/// of the library exactly.
	/// </summary>
	public static class ScriptText
	{
		/// <summary>
		/// Splits one script line into tokens.
		///
		/// This calls the tokenizer of the library. It toggles on a quote and splits only on
		/// the space character. It does not treat a tab as a separator, and it strips the
		/// quotes from the tokens that it emits. A plain Split on a space breaks every
		/// combobox line, because an option name can hold spaces.
		/// </summary>
		public static string[] Tokenize(string line)
		{
			if (String.IsNullOrWhiteSpace(line)) return Array.Empty<string>();

			return line.SmartSplitString().ToArray();
		}

		/// <summary>
		/// True when the parser of the library skips this line. It skips an empty line, a
		/// comment, and a brace on its own.
		/// </summary>
		public static bool IsSkipped(string line)
		{
			string trimmed = line?.Trim() ?? String.Empty;

			return String.IsNullOrWhiteSpace(trimmed)
				|| trimmed.StartsWith("//", StringComparison.Ordinal)
				|| trimmed.StartsWith("#", StringComparison.Ordinal)
				|| trimmed.StartsWith("{", StringComparison.Ordinal)
				|| trimmed.StartsWith("}", StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Compares a container path the way the game data does.
	///
	/// The scripts write GLOBAL\GLOBALB.LZC and the file on disk is GLOBAL/GlobalB.lzc.
	/// Both name the same target. Normalize the separator, then compare without case.
	/// </summary>
	public static class PathKey
	{
		public static string Normalize(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) return String.Empty;

			return path.Trim().Replace('\\', '/').ToLowerInvariant();
		}

		public static bool Same(string left, string right)
		{
			return Normalize(left) == Normalize(right);
		}

		public static readonly IEqualityComparer<string> Comparer = StringComparer.Ordinal;
	}
}
