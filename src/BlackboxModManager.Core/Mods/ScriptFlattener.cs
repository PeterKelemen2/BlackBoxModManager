using System;
using System.Collections.Generic;
using Endscript.Commands;
using Endscript.Enums;
using Endscript.Helpers;
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

		/// <summary>
		/// The commands that the conflict check cannot compare against another mod. An
		/// unclassified verb produces one. So does a command that reads a directory listing
		/// at deploy time.
		/// </summary>
		public IReadOnlyList<ScriptWarning> Warnings { get; }

		/// <summary>
		/// True when an <c>if</c> command made this walk cover both branches. The edit list
		/// then holds more edits than the deploy applies, and every extra edit carries
		/// <c>Conditional</c>. A conflict against one of those is possible and not certain.
		/// </summary>
		public bool IsApproximate { get; }

		public ResolvedScript(string variant, IReadOnlyList<ResolvedEdit> edits,
			IReadOnlyList<string> answers, IReadOnlyList<ResolverNote> notes,
			IReadOnlyList<ScriptWarning> warnings = null, bool isApproximate = false)
		{
			this.Variant = variant;
			this.Edits = edits ?? Array.Empty<ResolvedEdit>();
			this.Answers = answers ?? Array.Empty<string>();
			this.Notes = notes ?? Array.Empty<ResolverNote>();
			this.Warnings = warnings ?? Array.Empty<ScriptWarning>();
			this.IsApproximate = isApproximate;
		}

		/// <summary>Only the commands that write one value into one field.</summary>
		public IEnumerable<ResolvedEdit> KeyedEdits => this.Category(CommandCategory.ScalarFieldWrite);

		/// <summary>Every command that carries a key on a container.</summary>
		public IEnumerable<ResolvedEdit> ContainerEdits
		{
			get
			{
				foreach (ResolvedEdit edit in this.Edits)
				{
					if (edit.Key != null) yield return edit;
				}
			}
		}

		/// <summary>Every command that reads a path or writes one.</summary>
		public IEnumerable<ResolvedEdit> FilesystemEdits => this.Category(CommandCategory.FilesystemEffect);

		/// <summary>
		/// Every command that this application refuses to run. The deploy has to stop before
		/// it writes anything.
		/// </summary>
		public IEnumerable<ResolvedEdit> Rejected
		{
			get
			{
				foreach (ResolvedEdit edit in this.Edits)
				{
					if (edit.Support == CommandSupport.Reject) yield return edit;
				}
			}
		}

		/// <summary>Every path that leaves the staging copy or the mod directory.</summary>
		public IEnumerable<(ResolvedEdit Edit, PathEffect Path)> Escapes()
		{
			foreach (ResolvedEdit edit in this.Edits)
			{
				foreach (PathEffect path in edit.Paths)
				{
					if (!path.IsSafe) yield return (edit, path);
				}
			}
		}

		private IEnumerable<ResolvedEdit> Category(CommandCategory category)
		{
			foreach (ResolvedEdit edit in this.Edits)
			{
				if (edit.Category == category) yield return edit;
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
	/// Walks a parsed script and emits the commands that a deploy would apply.
	///
	/// The walk repeats the control flow of EndScriptManager.ProcessScript and executes
	/// nothing. The result is what a deploy would apply, which is what conflict detection
	/// needs before anything touches a container.
	///
	/// <b>An if command is the one case that the walk cannot resolve.</b> ProcessScript reads
	/// the loaded containers and picks the branch. This layer holds no containers, so it walks
	/// both branches and marks every edit inside as conditional. The result then covers more
	/// than the deploy applies, and it covers no less. A walk that skipped the whole variant
	/// would report no conflict at all, and silence is the failure that step 8 prevents.
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
		///
		/// Pass the sandbox roots when the staging copy exists. Every filesystem command then
		/// carries a resolved path and a sandbox verdict. Pass null to read the script alone.
		/// </summary>
		public static ResolvedScript Resolve(ModVariant variant, VariantSelection selection,
			SandboxRoots roots = null)
		{
			if (variant is null) throw new ArgumentNullException(nameof(variant));

			if (!variant.IsInstallable)
			{
				throw new InvalidOperationException(
					$"The variant \"{variant.Name}\" is not installable. {variant.Problem}");
			}

			string scriptPath = ModPath.Resolve(variant.Manifest.ThisDir, variant.Manifest.Endscript);

			ScriptAppendGraph.Walk(scriptPath);

			return Flatten(variant, ScriptReader.Parse(scriptPath), selection, roots);
		}

		public static ResolvedScript Resolve(ModVariant variant, ModSelections selections,
			SandboxRoots roots = null)
		{
			return Resolve(variant, selections?.For(variant?.Name), roots);
		}

		public static ResolvedScript Flatten(ModVariant variant, BaseCommand[] commands,
			VariantSelection selection, SandboxRoots roots = null)
		{
			if (variant is null) throw new ArgumentNullException(nameof(variant));
			if (commands is null) throw new ArgumentNullException(nameof(commands));

			ScriptChaser.Chase(commands);

			var walker = new Walker(variant, commands, new SelectionResolver(variant, selection), roots);

			walker.Walk(0, commands.Length, false);

			return walker.Result();
		}

		/// <summary>
		/// One block of a selectable statement. The range holds the commands of the block and
		/// it holds neither the header nor the closing 'end'.
		/// </summary>
		private readonly struct Block
		{
			public Block(string name, int from, int to)
			{
				this.Name = name;
				this.From = from;
				this.To = to;
			}

			public string Name { get; }

			/// <summary>The first command of the block.</summary>
			public int From { get; }

			/// <summary>One past the last command of the block.</summary>
			public int To { get; }
		}

		private sealed class Walker
		{
			private readonly ModVariant _variant;
			private readonly BaseCommand[] _commands;
			private readonly SelectionResolver _resolver;
			private readonly SandboxRoots _roots;
			private readonly List<ResolvedEdit> _edits = new List<ResolvedEdit>();
			private readonly List<string> _answers = new List<string>();
			private readonly List<ScriptWarning> _warnings = new List<ScriptWarning>();

			private int _ordinal;
			private int _steps;
			private bool _approximate;

			public Walker(ModVariant variant, BaseCommand[] commands, SelectionResolver resolver,
				SandboxRoots roots)
			{
				this._variant = variant;
				this._commands = commands;
				this._resolver = resolver;
				this._roots = roots;
			}

			public ResolvedScript Result()
			{
				return new ResolvedScript(this._variant.Name, this._edits, this._answers,
					this._resolver.Notes, this._warnings, this._approximate);
			}

			/// <summary>
			/// Walks the commands of one range. Set conditional to true for the branches of an
			/// if command.
			/// </summary>
			public void Walk(int from, int to, bool conditional)
			{
				for (int i = from; i < to; ++i)
				{
					if (++this._steps > MaxSteps)
					{
						throw new ScriptParseException(
							"The script jumps in a loop and never ends.", this._variant.Name, String.Empty, 0, null);
					}

					BaseCommand command = this._commands[i];

					if (command is EndCommand)
					{
						// Chase pairs every 'end' with a statement, and this walk enters a
						// block below its own 'end'. A reachable 'end' therefore closes
						// nothing.
						throw new ScriptParseException(
							"This 'end' command closes nothing.",
							command.Filename, command.Line, command.Index, null);
					}

					if (command is ISelectable selectable)
					{
						this.Enter(selectable, i, conditional);

						// Continue after the closing 'end' of the statement.
						i = selectable.LastCommand;

						continue;
					}

					if (command is OptionalCommand unknown)
					{
						// A block header never reaches this point, because Blocks bounds every
						// range at the next header. So this is a verb that we do not know. A
						// skipped edit produces an install that is wrong in a way the user
						// cannot see. Name the file and the line and stop.
						throw new ScriptParseException(
							$"The command \"{unknown.Option}\" is not a known verb.",
							command.Filename, command.Line, command.Index, null);
					}

					this.Emit(command, conditional);
				}
			}

			private void Emit(BaseCommand command, bool conditional)
			{
				ResolvedEdit edit = EditKeyExtractor.Extract(command, this._roots, conditional);

				this._edits.Add(edit);

				ScriptWarning warning = EditKeyExtractor.Warn(edit);

				if (warning != null) this._warnings.Add(warning);
			}

			/// <summary>
			/// Handles one selectable statement. An if statement walks every branch. A
			/// question walks the branch that the stored answer names.
			/// </summary>
			private void Enter(ISelectable selectable, int index, bool conditional)
			{
				var command = (BaseCommand)selectable;
				IReadOnlyList<Block> blocks = Blocks(selectable, command);

				if (selectable is IfStatementCommand)
				{
					// ProcessScript reads the loaded containers here. This layer holds none.
					this._approximate = true;

					if (blocks.Count < selectable.Options.Length)
					{
						// ProcessScript jumps to Options[Choice].Start with no fallback, so a
						// missing branch ends the deploy when the check picks that branch.
						this._warnings.Add(new ScriptWarning(eCommandType.@if,
							"has no block for every branch. The deploy stops when the check " +
							"picks the branch that the script does not hold.",
							command.Filename, command.Index, command.Line));
					}

					foreach (Block block in blocks) this.Walk(block.From, block.To, true);

					return;
				}

				int choice = this._resolver.Resolve(selectable, this._ordinal);
				string name = selectable.Options[choice].Name;

				if (conditional)
				{
					// ProcessScript counts only the questions that it reaches. An if command
					// encloses this question, so the deploy may never ask it. The ordinal of
					// every later question then differs from the ordinal here.
					this._warnings.Add(new ScriptWarning(command.Type,
						$"asks the question \"{selectable.Description}\" inside an 'if' block. " +
						"The stored answers cannot line up with the deploy.",
						command.Filename, command.Index, command.Line));
				}

				this._answers.Add(name);
				++this._ordinal;

				foreach (Block block in blocks)
				{
					if (block.Name != name) continue;

					this.Walk(block.From, block.To, conditional);

					return;
				}

				throw new ScriptParseException(
					$"The script has no block named \"{name}\" for this question.",
					command.Filename, command.Line, command.Index, null);
			}

			/// <summary>
			/// Reads the block of every option out of the jump targets that Chase wrote.
			///
			/// Never read the block order out of the option order. A checkbox always reports
			/// 'disabled' first and 'enabled' second, and a script can write the two blocks
			/// the other way round.
			/// </summary>
			private static IReadOnlyList<Block> Blocks(ISelectable selectable, BaseCommand command)
			{
				int last = selectable.LastCommand;

				if (last < 0)
				{
					throw new ScriptParseException(
						"This selectable statement has no closing 'end' command.",
						command.Filename, command.Line, command.Index, null);
				}

				var found = new List<Block>(selectable.Options.Length);

				foreach (OptionState option in selectable.Options)
				{
					// Start holds the index of the block header. The block starts after it.
					if (option.Start >= 0) found.Add(new Block(option.Name, option.Start + 1, last));
				}

				found.Sort((left, right) => left.From.CompareTo(right.From));

				// A block ends where the next block starts. The last block ends at the 'end'.
				var blocks = new List<Block>(found.Count);

				for (int i = 0; i < found.Count; ++i)
				{
					int to = i + 1 < found.Count ? found[i + 1].From - 1 : last;

					blocks.Add(new Block(found[i].Name, found[i].From, to));
				}

				return blocks;
			}
		}
	}
}
