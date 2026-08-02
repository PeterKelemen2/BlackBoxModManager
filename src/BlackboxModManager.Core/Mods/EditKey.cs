using System;
using System.Collections.Generic;
using System.Globalization;
using Endscript.Commands;
using Endscript.Enums;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// What one command in a resolved script does.
	/// </summary>
	public enum EditKind
	{
		/// <summary>
		/// The command writes one value into one container field. It carries a conflict key.
		/// </summary>
		KeyedEdit = 0,

		/// <summary>
		/// A known verb that carries no single field key. An import, a file operation, or a
		/// texture bind. Step 8 classifies these.
		/// </summary>
		Other,
	}

	/// <summary>
	/// Names one container field across mods.
	///
	/// The key is the target file plus the path of names that leads to the field. It is not
	/// the value. Two mods that write the same key with different values conflict. Two mods
	/// that write the same key with the same value do not.
	///
	/// **Never build a conflict key on the Files list of a manifest.** That list is a
	/// load-and-verify superset. Every 1 Lap manifest declares GLOBALA.BUN, which no
	/// command touches, so a key on Files reports a conflict between any two mods that
	/// merely load the same container.
	///
	/// **Never build one on Links either.** All four inspected manifests carry identical
	/// Links, written by two unrelated authors. It is per-game boilerplate that Binary
	/// emits, and a key on it flags every pair of Underground 2 mods.
	/// </summary>
	public sealed class EditKey : IEquatable<EditKey>
	{
		/// <summary>The target container, as the script wrote it.</summary>
		public string TargetFile { get; }

		/// <summary>The name path to the field, as the script wrote it.</summary>
		public IReadOnlyList<string> Segments { get; }

		private readonly string _normalized;

		public EditKey(string targetFile, IReadOnlyList<string> segments)
		{
			this.TargetFile = targetFile ?? String.Empty;
			this.Segments = segments ?? Array.Empty<string>();

			// Compare the separator and the letter case the way the game data does.
			// GLOBAL\GLOBALB.LZC and GLOBAL/GlobalB.lzc name one target.
			var parts = new List<string>(this.Segments.Count + 1) { PathKey.Normalize(this.TargetFile) };

			foreach (string segment in this.Segments) parts.Add(segment.Trim().ToLowerInvariant());

			this._normalized = String.Join("", parts);
		}

		public bool Equals(EditKey other) => other != null && this._normalized == other._normalized;

		public override bool Equals(object obj) => this.Equals(obj as EditKey);

		public override int GetHashCode() => this._normalized.GetHashCode(StringComparison.Ordinal);

		public override string ToString() => $"({this.TargetFile}, [{String.Join(", ", this.Segments)}])";
	}

	/// <summary>
	/// One command from a resolved script, with its conflict key when it has one.
	/// </summary>
	public sealed class ResolvedEdit
	{
		public eCommandType Verb { get; }

		public EditKind Kind { get; }

		/// <summary>Null when Kind is Other.</summary>
		public EditKey Key { get; }

		/// <summary>
		/// The value exactly as the script wrote it. Never round trip this through a
		/// number. Observed floats include -0.19500002 and 2.746582, and a default
		/// ToString of a parsed value corrupts both.
		/// </summary>
		public string Value { get; }

		public string SourceFile { get; }

		public int SourceLine { get; }

		public string Text { get; }

		public ResolvedEdit(eCommandType verb, EditKind kind, EditKey key, string value,
			string sourceFile, int sourceLine, string text)
		{
			this.Verb = verb;
			this.Kind = kind;
			this.Key = key;
			this.Value = value ?? String.Empty;
			this.SourceFile = sourceFile ?? String.Empty;
			this.SourceLine = sourceLine;
			this.Text = text ?? String.Empty;
		}

		/// <summary>
		/// Reads the value as a number, for a UI that wants to show one. Parses with the
		/// invariant culture, because a comma-decimal locale reads -0.19500002 wrong.
		/// Returns false when the value is not a number, which is normal.
		/// </summary>
		public bool TryReadNumber(out double number)
		{
			return Double.TryParse(this.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}

		public override string ToString() =>
			this.Kind == EditKind.KeyedEdit ? $"{this.Key} = {this.Value}" : $"{this.Verb} at {this.SourceFile}:{this.SourceLine}";
	}

	/// <summary>
	/// Turns a command into a conflict key.
	/// </summary>
	public static class EditKeyExtractor
	{
		/// <summary>
		/// The verbs that write one value into one field. Their shape is the same for all
		/// of them: the verb, then the target file, then a name path, then the value.
		///
		/// Do not read an argument count from this set. update_collection accepts 6 or 8
		/// tokens and update_incareer accepts 8 or 10. The extractor reads the general
		/// shape instead, so a longer form needs no change here.
		///
		/// Step 8 classifies the rest of the vocabulary. Add a verb here only after the
		/// source confirms that it follows this shape.
		/// </summary>
		public static readonly IReadOnlySet<eCommandType> KeyedVerbs = new HashSet<eCommandType>
		{
			eCommandType.update_collection,
			eCommandType.update_incareer,
			eCommandType.update_string,
			eCommandType.update_texture,
		};

		/// <summary>
		/// The smallest token count that the general shape needs: the verb, the file, one
		/// name, and the value.
		/// </summary>
		private const int MinimumTokens = 4;

		public static ResolvedEdit Extract(BaseCommand command)
		{
			if (command is null) throw new ArgumentNullException(nameof(command));

			eCommandType verb = command.Type;

			if (!KeyedVerbs.Contains(verb))
			{
				return new ResolvedEdit(verb, EditKind.Other, null, null,
					command.Filename, command.Index, command.Line);
			}

			string[] tokens = ScriptText.Tokenize(command.Line);

			if (tokens.Length < MinimumTokens)
			{
				throw new ScriptParseException(
					$"The command {verb} needs at least {MinimumTokens} tokens and this one has {tokens.Length}.",
					command.Filename, command.Line, command.Index, null);
			}

			// The target file is the first argument. The value is the last token.
			// Everything between the two is the name path.
			string target = tokens[1];
			string value = tokens[^1];

			var segments = new List<string>(tokens.Length - 3);

			for (int i = 2; i < tokens.Length - 1; ++i) segments.Add(tokens[i]);

			return new ResolvedEdit(verb, EditKind.KeyedEdit, new EditKey(target, segments), value,
				command.Filename, command.Index, command.Line);
		}
	}
}
