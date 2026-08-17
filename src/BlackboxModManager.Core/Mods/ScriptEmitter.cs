using System;
using System.Collections.Generic;
using System.Text;
using Endscript.Enums;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Writes a resolved script back out as one Endscript file.
	///
	/// <b>This exists for the CLI route and for nothing else.</b> Binary reads the answer to a
	/// question from its own console, and this application cannot reach that console. So a mod
	/// that asks a question needs a script that asks nothing. The emitter writes the commands
	/// of the branches that the stored answers picked, and it writes no question.
	///
	/// <b>Every line is the line that the mod wrote.</b> ResolvedEdit.Text holds the original
	/// text of the command. The emitter copies that text and builds no command of its own, so
	/// the float form, the quoting, and the separator cannot drift. A rebuild from the parsed
	/// arguments would corrupt a value such as -0.19500002.
	/// </summary>
	public static class ScriptEmitter
	{
		/// <summary>The header that EndScriptParser demands on the first line.</summary>
		public const string Header = "[VERSN2]";

		/// <summary>
		/// The name of the file that the CLI route writes beside the launcher of the mod.
		///
		/// The name starts with a dot so that it sorts away from the files of the mod. The
		/// engine deletes the file after the run.
		/// </summary>
		public const string GeneratedFileName = ".blackbox-cli.end";

		/// <summary>
		/// The verbs that control the walk and that carry no edit.
		///
		/// EndScriptParser splices an <c>append</c> and never returns one. ScriptFlattener
		/// never emits a question, an <c>if</c>, or an <c>end</c>. So this set should stay
		/// empty in practice. It exists because a silent duplicate of an appended file would
		/// apply every edit of that file twice, and a later change to either class must not be
		/// able to cause that.
		/// </summary>
		private static readonly IReadOnlySet<eCommandType> Control = new HashSet<eCommandType>
		{
			eCommandType.append,
			eCommandType.combobox,
			eCommandType.checkbox,
			eCommandType.@if,
			eCommandType.end,
		};

		/// <summary>
		/// The text of one Endscript file that applies the resolved edits, in order.
		/// </summary>
		public static string Emit(ResolvedScript resolved)
		{
			if (resolved is null) throw new ArgumentNullException(nameof(resolved));

			var text = new StringBuilder();

			// The parser reads the first line and rejects any other value. It then starts the
			// command loop at line 1, so this line must be present and must stand alone.
			text.Append(Header).Append('\n');

			foreach (ResolvedEdit edit in resolved.Edits)
			{
				if (Control.Contains(edit.Verb)) continue;
				if (String.IsNullOrWhiteSpace(edit.Text)) continue;

				text.Append(edit.Text).Append('\n');
			}

			return text.ToString();
		}

		/// <summary>
		/// The count of edits that <see cref="Emit"/> writes. The engine logs this.
		/// </summary>
		public static int CountOf(ResolvedScript resolved)
		{
			if (resolved is null) throw new ArgumentNullException(nameof(resolved));

			int count = 0;

			foreach (ResolvedEdit edit in resolved.Edits)
			{
				if (Control.Contains(edit.Verb)) continue;
				if (String.IsNullOrWhiteSpace(edit.Text)) continue;

				++count;
			}

			return count;
		}
	}
}
