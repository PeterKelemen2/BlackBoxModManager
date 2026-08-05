using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BlackboxModManager.Core.Asi
{
	/// <summary>
	/// What one line of an <c>.ini</c> file is.
	/// </summary>
	public enum IniLineKind
	{
		/// <summary>The line holds no character other than whitespace.</summary>
		Blank = 0,

		/// <summary>The whole line is a comment.</summary>
		Comment,

		/// <summary>The line opens a section.</summary>
		Section,

		/// <summary>The line holds one key and one value.</summary>
		Entry,

		/// <summary>
		/// The line is none of the above. A line with text and no equal sign lands here. The
		/// writer passes it through and changes nothing in it.
		/// </summary>
		Unknown,
	}

	/// <summary>
	/// Names one option across a section and a key.
	///
	/// The comparison ignores letter case, because the files are inconsistent about it. The
	/// text form is <c>SECTION/Key</c>, and that form goes into the profile file.
	/// </summary>
	public sealed class IniKey : IEquatable<IniKey>
	{
		/// <summary>The section name. This is empty for a key above the first section.</summary>
		public string Section { get; }

		public string Key { get; }

		private readonly string _normalized;

		public IniKey(string section, string key)
		{
			this.Section = section ?? String.Empty;
			this.Key = key ?? String.Empty;
			this._normalized = $"{this.Section.Trim().ToLowerInvariant()}/{this.Key.Trim().ToLowerInvariant()}";
		}

		/// <summary>
		/// Reads the text form. A value with no slash reads as a key with no section, which
		/// is what an old profile file would hold.
		/// </summary>
		public static IniKey Parse(string text)
		{
			if (text is null) throw new ArgumentNullException(nameof(text));

			int slash = text.IndexOf('/');

			return slash < 0
				? new IniKey(String.Empty, text)
				: new IniKey(text.Substring(0, slash), text.Substring(slash + 1));
		}

		public bool Equals(IniKey other) => other != null && this._normalized == other._normalized;

		public override bool Equals(object obj) => this.Equals(obj as IniKey);

		public override int GetHashCode() => this._normalized.GetHashCode(StringComparison.Ordinal);

		public override string ToString() => $"{this.Section}/{this.Key}";
	}

	/// <summary>
	/// One line of the file, with the raw text that the reader read.
	///
	/// <b>The raw text is the truth.</b> The writer changes the value inside the raw text and
	/// leaves every other character alone. A writer that rebuilds a line from the fields
	/// throws away the comment, the alignment, and the spelling of the key.
	/// </summary>
	public sealed class IniLine
	{
		/// <summary>The line, with no line terminator.</summary>
		public string Raw { get; }

		/// <summary>The line terminator that followed this line, or an empty string at the end.</summary>
		public string Terminator { get; }

		public IniLineKind Kind { get; }

		/// <summary>The line number, from one.</summary>
		public int Number { get; }

		/// <summary>The section that holds this line. Empty above the first section.</summary>
		public string Section { get; }

		/// <summary>The key, for an entry line. Empty otherwise.</summary>
		public string Key { get; }

		/// <summary>The value, trimmed, for an entry line. Empty otherwise.</summary>
		public string Value { get; }

		/// <summary>
		/// The comment that follows the value on this line, with no comment marker and
		/// trimmed. Empty when the line carries none.
		/// </summary>
		public string Comment { get; }

		/// <summary>
		/// The marker that opened the comment, or an empty string for none.
		///
		/// Three markers exist and one file uses one of them. The Widescreen Fix uses
		/// <c>;</c> and Extra Options uses <c>//</c>. The writer needs the marker of the line
		/// to keep the comment in its column.
		/// </summary>
		public string CommentMarker { get; }

		/// <summary>Where the value starts inside <see cref="Raw"/>.</summary>
		public int ValueStart { get; }

		/// <summary>How many characters the value takes inside <see cref="Raw"/>.</summary>
		public int ValueLength { get; }

		/// <summary>
		/// The whitespace between the end of the value and the comment marker. The writer
		/// keeps the comment in its column by changing this run.
		/// </summary>
		public int PadLength { get; }

		public IniLine(string raw, string terminator, IniLineKind kind, int number, string section,
			string key = null, string value = null, string comment = null, string commentMarker = null,
			int valueStart = -1, int valueLength = 0, int padLength = 0)
		{
			this.Raw = raw ?? String.Empty;
			this.Terminator = terminator ?? String.Empty;
			this.Kind = kind;
			this.Number = number;
			this.Section = section ?? String.Empty;
			this.Key = key ?? String.Empty;
			this.Value = value ?? String.Empty;
			this.Comment = comment ?? String.Empty;
			this.CommentMarker = commentMarker ?? String.Empty;
			this.ValueStart = valueStart;
			this.ValueLength = valueLength;
			this.PadLength = padLength;
		}

		/// <summary>True when a comment marker follows the value on this line.</summary>
		public bool HasComment => this.CommentMarker.Length > 0;

		public override string ToString() => $"{this.Number}: {this.Raw}";
	}

	/// <summary>
	/// One option of the file.
	/// </summary>
	public sealed class IniEntry
	{
		public IniKey Key { get; }

		public string Value { get; }

		/// <summary>The trailing comment of the line, or an empty string.</summary>
		public string Comment { get; }

		public int LineNumber { get; }

		/// <summary>
		/// True when an earlier line of the same section already held this key. A duplicate
		/// key is legal and it makes an edit ambiguous. The writer edits the first line only.
		/// </summary>
		public bool IsDuplicate { get; }

		/// <summary>The editor that this value asks for. See <see cref="IniValue"/>.</summary>
		public IniValueKind ValueKind { get; }

		public IniEntry(IniKey key, string value, string comment, int lineNumber, bool isDuplicate)
		{
			this.Key = key;
			this.Value = value ?? String.Empty;
			this.Comment = comment ?? String.Empty;
			this.LineNumber = lineNumber;
			this.IsDuplicate = isDuplicate;
			this.ValueKind = IniValue.Classify(this.Value);
		}

		public override string ToString() => $"{this.Key} = {this.Value}";
	}

	/// <summary>
	/// One section of the file, with its options in file order.
	/// </summary>
	public sealed class IniSection
	{
		/// <summary>
		/// The name in the brackets. This is empty for the section above the first bracket
		/// line. A key outside every section is legal, and the sample file has none.
		/// </summary>
		public string Name { get; }

		public IReadOnlyList<IniEntry> Entries { get; }

		/// <summary>True for the section that holds the keys above the first bracket line.</summary>
		public bool IsUnnamed => this.Name.Length == 0;

		public IniSection(string name, IReadOnlyList<IniEntry> entries)
		{
			this.Name = name ?? String.Empty;
			this.Entries = entries ?? Array.Empty<IniEntry>();
		}

		public override string ToString() => $"[{this.Name}] with {this.Entries.Count} keys";
	}

	/// <summary>
	/// One parsed <c>.ini</c> file.
	/// </summary>
	public sealed class IniDocument
	{
		/// <summary>Every line of the file, in order, with its raw text.</summary>
		public IReadOnlyList<IniLine> Lines { get; }

		/// <summary>The sections in file order. The unnamed section comes first when it exists.</summary>
		public IReadOnlyList<IniSection> Sections { get; }

		/// <summary>
		/// What the user has to know about this file. A duplicate key produces one entry. A
		/// line that the reader could not read produces one.
		/// </summary>
		public IReadOnlyList<string> Warnings { get; }

		public IniDocument(IReadOnlyList<IniLine> lines, IReadOnlyList<IniSection> sections,
			IReadOnlyList<string> warnings)
		{
			this.Lines = lines ?? Array.Empty<IniLine>();
			this.Sections = sections ?? Array.Empty<IniSection>();
			this.Warnings = warnings ?? Array.Empty<string>();
		}

		/// <summary>Every option of the file, in file order.</summary>
		public IEnumerable<IniEntry> Entries
		{
			get
			{
				foreach (IniSection section in this.Sections)
				{
					foreach (IniEntry entry in section.Entries) yield return entry;
				}
			}
		}

		/// <summary>
		/// The first option with this key, or null. The first one is the one that the writer
		/// edits, so a caller that shows a value must read the same one.
		/// </summary>
		public IniEntry Find(IniKey key)
		{
			if (key is null) return null;

			foreach (IniEntry entry in this.Entries)
			{
				if (entry.Key.Equals(key) && !entry.IsDuplicate) return entry;
			}

			return null;
		}

		/// <summary>The file text, exactly as the reader read it.</summary>
		public string Text()
		{
			var text = new StringBuilder();

			foreach (IniLine line in this.Lines) text.Append(line.Raw).Append(line.Terminator);

			return text.ToString();
		}

		public override string ToString() =>
			$"{this.Sections.Count} sections, {this.Lines.Count} lines";
	}

	/// <summary>
	/// Reads an <c>.ini</c> file that an ASI plugin holds.
	///
	/// The reader keeps every line. It never drops a blank line, a comment, or a line that it
	/// cannot read. <see cref="IniWriter"/> needs all of them to write the file back with one
	/// difference per changed value.
	///
	/// Four rules describe the format.
	///
	/// 1. A line in brackets starts a section. Every key below it belongs to that section.
	/// 2. A <c>key = value</c> line holds one option.
	/// 3. A comment starts at <c>;</c>, at <c>#</c>, or at <c>//</c>. The reader accepts all
	///    three and it remembers which one the line used.
	/// 4. A comment on the same line as a key is the help text of that key.
	/// </summary>
	public static class IniReader
	{
		/// <summary>
		/// The markers that open a comment.
		///
		/// <b>All three are real.</b> The Widescreen Fix of Underground 2 uses <c>;</c> and
		/// Extra Options uses <c>//</c>. No plugin declares which one it reads, so this
		/// application accepts every one of them and it keeps the marker of each line.
		///
		/// Order matters for the scan. Put a longer marker before a shorter one that starts
		/// it, or the shorter one wins and the reader reports the wrong length.
		/// </summary>
		public static IReadOnlyList<string> CommentMarkers { get; } = new[] { "//", ";", "#" };

		public static IniDocument Read(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("The path is empty.", nameof(path));

			return Parse(File.ReadAllText(path));
		}

		public static IniDocument Parse(string text)
		{
			if (text is null) throw new ArgumentNullException(nameof(text));

			var lines = new List<IniLine>();
			var warnings = new List<string>();

			// One list of entries per section, in the order that the sections appear. The
			// unnamed section exists only when a key sits above the first bracket line.
			var order = new List<string>();
			var bySection = new Dictionary<string, List<IniEntry>>(StringComparer.OrdinalIgnoreCase);
			var seen = new HashSet<IniKey>();

			string section = String.Empty;
			int number = 0;

			foreach ((string raw, string terminator) in Split(text))
			{
				++number;

				IniLine line = ReadLine(raw, terminator, number, section);

				if (line.Kind == IniLineKind.Section)
				{
					section = line.Key;

					if (!bySection.ContainsKey(section))
					{
						order.Add(section);
						bySection[section] = new List<IniEntry>();
					}
					else
					{
						warnings.Add($"Line {number} opens the section [{section}] a second time. " +
							"The keys of both blocks read as one section.");
					}
				}
				else if (line.Kind == IniLineKind.Entry)
				{
					var key = new IniKey(line.Section, line.Key);
					bool duplicate = !seen.Add(key);

					if (duplicate)
					{
						warnings.Add($"Line {number} repeats the key \"{line.Key}\" of the section " +
							$"[{line.Section}]. The editor changes the first line only.");
					}

					if (!bySection.TryGetValue(line.Section, out List<IniEntry> entries))
					{
						order.Add(line.Section);
						entries = new List<IniEntry>();
						bySection[line.Section] = entries;
					}

					entries.Add(new IniEntry(key, line.Value, line.Comment, number, duplicate));
				}
				else if (line.Kind == IniLineKind.Unknown)
				{
					warnings.Add($"Line {number} holds no key and no section. " +
						$"The deploy writes it back unchanged. The line is \"{line.Raw.Trim()}\".");
				}

				lines.Add(line);
			}

			var sections = new List<IniSection>(order.Count);

			foreach (string name in order) sections.Add(new IniSection(name, bySection[name]));

			return new IniDocument(lines, sections, warnings);
		}

		/// <summary>
		/// Splits the text into lines and keeps the terminator of each one. A file that mixes
		/// terminators keeps both, and the writer then reproduces the file byte for byte.
		/// </summary>
		private static IEnumerable<(string Raw, string Terminator)> Split(string text)
		{
			int start = 0;

			for (int i = 0; i < text.Length; ++i)
			{
				if (text[i] != '\n') continue;

				bool carriage = i > start && text[i - 1] == '\r';
				int end = carriage ? i - 1 : i;

				yield return (text.Substring(start, end - start), carriage ? "\r\n" : "\n");

				start = i + 1;
			}

			// A file that ends with a terminator produces no trailing empty line. A file that
			// does not end with one produces its last line here.
			if (start < text.Length) yield return (text.Substring(start), String.Empty);
		}

		private static IniLine ReadLine(string raw, string terminator, int number, string section)
		{
			string trimmed = raw.Trim();

			if (trimmed.Length == 0)
			{
				return new IniLine(raw, terminator, IniLineKind.Blank, number, section);
			}

			string opener = MarkerAt(trimmed, 0);

			if (opener != null)
			{
				return new IniLine(raw, terminator, IniLineKind.Comment, number, section,
					comment: trimmed.Substring(opener.Length).Trim(), commentMarker: opener);
			}

			if (trimmed[0] == '[')
			{
				int close = trimmed.IndexOf(']');

				// A bracket line with no closing bracket is not a section. Pass it through.
				if (close < 1) return new IniLine(raw, terminator, IniLineKind.Unknown, number, section);

				string name = trimmed.Substring(1, close - 1).Trim();

				// A section header can carry a comment of its own. Extra Options writes
				// "[Hotkeys] // Look at ... for key values". The bracket name ends at the
				// closing bracket, so the comment changes nothing about the name.
				return new IniLine(raw, terminator, IniLineKind.Section, number, name, key: name);
			}

			int equals = raw.IndexOf('=');

			if (equals < 0) return new IniLine(raw, terminator, IniLineKind.Unknown, number, section);

			string key = raw.Substring(0, equals).Trim();

			if (key.Length == 0) return new IniLine(raw, terminator, IniLineKind.Unknown, number, section);

			// Everything after the equal sign is the value, up to the comment.
			(int comment, string marker) = CommentStart(raw, equals + 1);
			int end = comment < 0 ? raw.Length : comment;

			int valueStart = equals + 1;

			while (valueStart < end && IsSpace(raw[valueStart])) ++valueStart;

			int valueEnd = end;

			while (valueEnd > valueStart && IsSpace(raw[valueEnd - 1])) --valueEnd;

			string commentText = comment < 0
				? String.Empty
				: raw.Substring(comment + marker.Length).Trim();

			return new IniLine(raw, terminator, IniLineKind.Entry, number, section,
				key: key,
				value: raw.Substring(valueStart, valueEnd - valueStart),
				comment: commentText,
				commentMarker: marker,
				valueStart: valueStart,
				valueLength: valueEnd - valueStart,
				padLength: comment < 0 ? 0 : comment - valueEnd);
		}

		/// <summary>
		/// Where the trailing comment starts and which marker opened it. The index is -1 and
		/// the marker is an empty string when the line carries no comment.
		///
		/// A comment marker inside quotes is part of the value.
		/// </summary>
		private static (int Index, string Marker) CommentStart(string raw, int from)
		{
			bool quoted = false;

			for (int i = from; i < raw.Length; ++i)
			{
				if (raw[i] == '"')
				{
					quoted = !quoted;
					continue;
				}

				if (quoted) continue;

				string marker = MarkerAt(raw, i);

				if (marker != null) return (i, marker);
			}

			return (-1, String.Empty);
		}

		/// <summary>
		/// The comment marker that starts at this position, or null. It tests the longer
		/// markers first, so <c>//</c> never reads as a single character.
		/// </summary>
		private static string MarkerAt(string text, int index)
		{
			foreach (string marker in CommentMarkers)
			{
				if (String.CompareOrdinal(text, index, marker, 0, marker.Length) == 0
					&& index + marker.Length <= text.Length)
				{
					return marker;
				}
			}

			return null;
		}

		private static bool IsSpace(char c) => c == ' ' || c == '\t';
	}

	/// <summary>
	/// Writes an <c>.ini</c> file back with new values.
	///
	/// <b>The writer never rebuilds a line.</b> It replaces the characters of the value inside
	/// the raw line. Every comment, every blank line, and the spelling of every key therefore
	/// survive. A user who compares the deployed file to the original sees one difference per
	/// changed value.
	///
	/// The writer keeps the comment in its column when it can. It grows or shrinks the
	/// whitespace between the value and the comment character by the change in the length of
	/// the value, and it always leaves one space.
	/// </summary>
	public static class IniWriter
	{
		/// <summary>
		/// Returns the file text with the given values applied. A key that the file does not
		/// hold changes nothing, and it lands in the skipped list.
		///
		/// It edits the first line of a duplicated key and leaves the later lines alone.
		/// </summary>
		public static IniWriteResult Apply(IniDocument document, IReadOnlyDictionary<IniKey, string> values)
		{
			if (document is null) throw new ArgumentNullException(nameof(document));

			if (values is null || values.Count == 0)
			{
				return new IniWriteResult(document.Text(), Array.Empty<IniKey>(), Array.Empty<IniKey>());
			}

			var changed = new List<IniKey>();
			var applied = new HashSet<IniKey>();
			var text = new StringBuilder();

			foreach (IniLine line in document.Lines)
			{
				text.Append(Rewrite(line, values, applied, changed)).Append(line.Terminator);
			}

			var skipped = new List<IniKey>();

			foreach (KeyValuePair<IniKey, string> entry in values)
			{
				if (!applied.Contains(entry.Key)) skipped.Add(entry.Key);
			}

			return new IniWriteResult(text.ToString(), changed, skipped);
		}

		private static string Rewrite(IniLine line, IReadOnlyDictionary<IniKey, string> values,
			HashSet<IniKey> applied, List<IniKey> changed)
		{
			if (line.Kind != IniLineKind.Entry) return line.Raw;

			var key = new IniKey(line.Section, line.Key);

			// The first line of a duplicated key wins. A later line of the same key finds the
			// key in this set and stays as it is.
			if (applied.Contains(key)) return line.Raw;

			if (!values.TryGetValue(key, out string value)) return line.Raw;

			applied.Add(key);

			string clean = Clean(value);

			if (clean == line.Value) return line.Raw;

			changed.Add(key);

			var rebuilt = new StringBuilder();

			rebuilt.Append(line.Raw, 0, line.ValueStart);
			rebuilt.Append(clean);

			int after = line.ValueStart + line.ValueLength;

			if (line.HasComment)
			{
				// Keep the comment in its column. One space is the floor, because a value
				// that touches the comment marker reads as part of the value.
				int pad = Math.Max(1, line.PadLength - (clean.Length - line.ValueLength));

				rebuilt.Append(' ', pad);
				rebuilt.Append(line.Raw, after + line.PadLength, line.Raw.Length - after - line.PadLength);
			}
			else
			{
				rebuilt.Append(line.Raw, after, line.Raw.Length - after);
			}

			return rebuilt.ToString();
		}

		/// <summary>
		/// Removes what a value must never hold. A line terminator would split one option
		/// into two lines, and a comment marker would turn the rest of the value into a
		/// comment.
		///
		/// One slash is legal in a value and two are not. So a run of slashes collapses to
		/// one rather than disappearing, and a path such as <c>save/profile</c> survives.
		/// </summary>
		private static string Clean(string value)
		{
			if (String.IsNullOrEmpty(value)) return String.Empty;

			var text = new StringBuilder(value.Length);

			foreach (char c in value)
			{
				if (c == '\r' || c == '\n' || c == ';' || c == '#') continue;

				// Two slashes open a comment. Keep the first one and drop the rest of the run.
				if (c == '/' && text.Length > 0 && text[^1] == '/') continue;

				text.Append(c);
			}

			return text.ToString().Trim();
		}
	}

	/// <summary>
	/// What the writer produced.
	/// </summary>
	public sealed class IniWriteResult
	{
		public string Text { get; }

		/// <summary>The keys whose value the writer changed.</summary>
		public IReadOnlyList<IniKey> Changed { get; }

		/// <summary>
		/// The keys that the caller asked for and the file does not hold. A mod update that
		/// renames a key produces these. The deploy reports them and carries on.
		/// </summary>
		public IReadOnlyList<IniKey> Skipped { get; }

		public IniWriteResult(string text, IReadOnlyList<IniKey> changed, IReadOnlyList<IniKey> skipped)
		{
			this.Text = text ?? String.Empty;
			this.Changed = changed ?? Array.Empty<IniKey>();
			this.Skipped = skipped ?? Array.Empty<IniKey>();
		}
	}
}
