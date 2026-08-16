using System;
using System.IO;
using Endscript.Commands;
using Endscript.Core;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Thrown when a script does not parse. It names the file and the line, which the
	/// library exception does not.
	/// </summary>
	public sealed class ScriptParseException : Exception
	{
		public string File { get; }

		public string Text { get; }

		public int Line { get; }

		public ScriptParseException(string message, string file, string text, int line, Exception inner)
			: base(message, inner)
		{
			this.File = file;
			this.Text = text ?? String.Empty;
			this.Line = line;
		}
	}

	/// <summary>
	/// Parses a script with the library parser and turns its failures into messages that
	/// name a place.
	/// </summary>
	public static class ScriptReader
	{
		/// <summary>
		/// Reads a launcher script into a flat command array. The parser splices every
		/// append inline, so the result already holds the commands of every appended file.
		///
		/// Call ScriptAppendGraph.Walk first. The parser has no cycle guard.
		/// </summary>
		public static BaseCommand[] Parse(string scriptPath)
		{
			// A script that states a version reads Endscript.Version.Value during the parse.
			// The library leaves that static null. See defect 15.
			EndscriptVersion.Ensure();

			var parser = new EndScriptParser(scriptPath);

			try
			{
				BaseCommand[] commands = parser.Read();

				// Read returns null for a VERSN3 description file. That is a menu, not a
				// script, and it holds no commands.
				return commands ?? Array.Empty<BaseCommand>();
			}
			catch (Exception ex)
			{
				string where = String.IsNullOrEmpty(parser.CurrentFile)
					? Path.GetFileName(scriptPath)
					: parser.CurrentFile;

				string message = parser.CurrentIndex > 0
					? $"The script {where} did not parse at line {parser.CurrentIndex}. {ex.Message}"
					: $"The script {where} did not parse. {ex.Message}";

				throw new ScriptParseException(message, where, parser.CurrentLine, parser.CurrentIndex, ex);
			}
		}
	}
}
