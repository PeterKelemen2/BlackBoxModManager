using System;
using System.Collections.Generic;
using Endscript.Commands;
using Endscript.Interfaces;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// The result of resolving one variant into a linear edit list.
	/// </summary>
	public sealed class ResolvedScript
	{
		public string Variant { get; }

		/// <summary>Every command of the selected branches, in execution order.</summary>
		public IReadOnlyList<ResolvedEdit> Edits { get; }

		/// <summary>The answer that the resolver gave to each question, in order.</summary>
		public IReadOnlyList<string> Answers { get; }

		/// <summary>Assumptions that the resolver made. A missing answer produces one.</summary>
		public IReadOnlyList<ResolverNote> Notes { get; }

		public ResolvedScript(string variant, IReadOnlyList<ResolvedEdit> edits,
			IReadOnlyList<string> answers, IReadOnlyList<ResolverNote> notes)
		{
			this.Variant = variant;
			this.Edits = edits;
			this.Answers = answers;
			this.Notes = notes;
		}

		/// <summary>Only the commands that carry a conflict key.</summary>
		public IEnumerable<ResolvedEdit> KeyedEdits
		{
			get
			{
				foreach (ResolvedEdit edit in this.Edits)
				{
					if (edit.Kind == EditKind.KeyedEdit) yield return edit;
				}
			}
		}
	}

	/// <summary>
	/// Resolves the jump targets of every selectable command.
	///
	/// This repeats what EndScriptManager.CommandChase does. We do it ourselves for two
	/// reasons. CommandChase needs a loaded profile, and this layer must work with no game
	/// directory present. CommandChase also turns every failure into one message that names
	/// nothing.
	/// </summary>
	internal static class ScriptChaser
	{
		public static void Chase(BaseCommand[] commands)
		{
			var stack = new Stack<ISelectable>();

			for (int i = 0; i < commands.Length; ++i)
			{
				BaseCommand command = commands[i];

				if (command is ISelectable selectable)
				{
					stack.Push(selectable);
				}
				else if (command is OptionalCommand optional)
				{
					if (stack.Count == 0)
					{
						// An unknown verb outside every option block. The library would
						// execute it and record "cannot be recognized" with no context.
						throw new ScriptParseException(
							$"The command \"{optional.Option}\" is not a known verb.",
							command.Filename, command.Line, command.Index, null);
					}

					ISelectable peek = stack.Peek();

					if (peek.Contains(optional.Option)) peek[optional.Option].Start = i;
				}
				else if (command is EndCommand)
				{
					if (stack.Count == 0)
					{
						throw new ScriptParseException(
							"This 'end' command closes nothing.",
							command.Filename, command.Line, command.Index, null);
					}

					stack.Peek().LastCommand = i;
					stack.Pop();
				}
			}

			if (stack.Count > 0)
			{
				var open = (BaseCommand)stack.Peek();

				throw new ScriptParseException(
					"This selectable statement has no closing 'end' command.",
					open.Filename, open.Line, open.Index, null);
			}
		}
	}

	/// <summary>
	/// Walks a parsed script and emits the commands of the selected branches only.
	///
	/// The walk repeats the control flow of EndScriptManager.ProcessScript and executes
	/// nothing. The result is what a deploy would apply, which is what conflict detection
	/// needs before anything touches a container.
	/// </summary>
	public static class ScriptFlattener
	{
		/// <summary>
		/// Guards against a jump loop in a malformed script. No real script comes near this.
		/// </summary>
		private const int MaxSteps = 2_000_000;

		/// <summary>
		/// Reads the script of a variant and resolves it in one call. This is the entry
		/// point for step 5 and step 6.
		/// </summary>
		public static ResolvedScript Resolve(ModVariant variant, VariantSelection selection)
		{
			if (variant is null) throw new ArgumentNullException(nameof(variant));

			if (!variant.IsInstallable)
			{
				throw new InvalidOperationException(
					$"The variant \"{variant.Name}\" is not installable. {variant.Problem}");
			}

			string scriptPath = ModPath.Resolve(variant.Manifest.ThisDir, variant.Manifest.Endscript);

			ScriptAppendGraph.Walk(scriptPath);

			return Flatten(variant, ScriptReader.Parse(scriptPath), selection);
		}

		public static ResolvedScript Resolve(ModVariant variant, ModSelections selections)
		{
			return Resolve(variant, selections?.For(variant?.Name));
		}

		public static ResolvedScript Flatten(ModVariant variant, BaseCommand[] commands, VariantSelection selection)
		{
			if (variant is null) throw new ArgumentNullException(nameof(variant));
			if (commands is null) throw new ArgumentNullException(nameof(commands));

			var resolver = new SelectionResolver(variant, selection);

			ScriptChaser.Chase(commands);

			var edits = new List<ResolvedEdit>();
			var answers = new List<string>();
			var stack = new Stack<ISelectable>();

			int index = 0;
			int ordinal = 0;
			int steps = 0;

			while (index < commands.Length)
			{
				if (++steps > MaxSteps)
				{
					throw new ScriptParseException(
						"The script jumps in a loop and never ends.", variant.Name, String.Empty, 0, null);
				}

				BaseCommand command = commands[index];

				if (command is EndCommand)
				{
					if (stack.Count == 0)
					{
						throw new ScriptParseException(
							"This 'end' command closes nothing.",
							command.Filename, command.Line, command.Index, null);
					}

					stack.Pop();
				}
				else if (command is ISelectable selectable)
				{
					int choice = Choose(selectable, resolver, ref ordinal, answers, variant);

					selectable.Choice = choice;
					stack.Push(selectable);

					Endscript.Helpers.OptionState option = selectable.Options[choice];

					if (option.Start == -1)
					{
						throw new ScriptParseException(
							$"The script has no block named \"{option.Name}\" for this question.",
							command.Filename, command.Line, command.Index, null);
					}

					// Continue at the command after the block header.
					index = option.Start;
				}
				else if (stack.Count > 0 && command is OptionalCommand optional && stack.Peek().Contains(optional.Option))
				{
					// The next block of the same question starts here, so the chosen block
					// ended. Jump past the closing 'end'.
					ISelectable peek = stack.Peek();

					if (peek.LastCommand == -1)
					{
						throw new ScriptParseException(
							"This selectable statement has no closing 'end' command.",
							command.Filename, command.Line, command.Index, null);
					}

					index = peek.LastCommand;
					stack.Pop();
				}
				else if (command is OptionalCommand unknown)
				{
					// Not a block header of the enclosing question, so it is a verb that we
					// do not know. A skipped edit produces an install that is wrong in a way
					// the user cannot see. Name the file and the line and stop.
					throw new ScriptParseException(
						$"The command \"{unknown.Option}\" is not a known verb.",
						command.Filename, command.Line, command.Index, null);
				}
				else
				{
					edits.Add(EditKeyExtractor.Extract(command));
				}

				++index;
			}

			return new ResolvedScript(variant.Name, edits, answers, resolver.Notes);
		}

		private static int Choose(ISelectable selectable, SelectionResolver resolver, ref int ordinal,
			List<string> answers, ModVariant variant)
		{
			if (selectable is IfStatementCommand)
			{
				// An if statement asks the user nothing. ProcessScript evaluates it against
				// the loaded containers, which this layer does not have. A static walk
				// cannot know the answer.
				throw new ScriptParseException(
					$"The mod \"{variant.Name}\" uses an 'if' command. This layer cannot resolve one " +
					"without the loaded containers. See step 8.",
					((BaseCommand)selectable).Filename, ((BaseCommand)selectable).Line,
					((BaseCommand)selectable).Index, null);
			}

			int choice = resolver.Resolve(selectable, ordinal);
			answers.Add(selectable.Options[choice].Name);
			++ordinal;

			return choice;
		}
	}
}
