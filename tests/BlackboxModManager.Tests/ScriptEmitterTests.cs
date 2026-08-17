using System;
using System.IO;
using BlackboxModManager.Core.Mods;
using Endscript.Commands;
using Endscript.Enums;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the script that the CLI route writes when a mod asks a question.
	///
	/// <b>The one rule is that the output must parse back to the same commands.</b> Binary
	/// reads an answer from its own console, so the route hands it a script that asks nothing.
	/// A script that loses one edit produces an install that is wrong in a way the user cannot
	/// see.
	///
	/// Every mod here is hand-built, so the tests need no example mod on disk.
	/// </summary>
	public class ScriptEmitterTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();

		public void Dispose() => this._temp.Dispose();

		private ModVariant Plain()
		{
			this._temp.WriteManifest("Install.end", "Underground2", "script.end");
			this._temp.WriteScript("script.end",
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 4",
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA WhatGame -0.19500002",
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Padding ""a value with spaces""");

			return ModPackageReader.Read(this._temp.Path).Variants[0];
		}

		/// <summary>
		/// A mod that asks one question and puts the two branches in appended files. This is the
		/// shape of the real camera mod.
		/// </summary>
		private ModVariant Asking()
		{
			this._temp.WriteManifest("Install.end", "Underground2", "Main/script.end");
			// A block header is a bare line that holds the option name, and one 'end' closes the
			// whole question. An append resolves against the directory of the launcher script,
			// which is Main here, so the path holds no Main segment of its own.
			this._temp.WriteScript("Main/script.end",
				"combobox Fast Slow \"Pick a speed\"",
				"Fast",
				"append fast.end",
				"Slow",
				"append slow.end",
				"end");

			this._temp.WriteScript("Main/fast.end",
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 9",
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Padding 1");

			this._temp.WriteScript("Main/slow.end",
				@"update_collection GLOBAL\GLOBALB.LZC CarTypeInfos SUPRA Manufacturer 2");

			return ModPackageReader.Read(this._temp.Path).Variants[0];
		}

		[Fact]
		public void TheOutputStartsWithTheHeaderThatTheParserDemands()
		{
			string text = ScriptEmitter.Emit(ScriptFlattener.Resolve(this.Plain(), (VariantSelection)null));

			Assert.StartsWith("[VERSN2]\n", text);
		}

		[Fact]
		public void TheOutputHoldsOneLineForEveryEdit()
		{
			ResolvedScript resolved = ScriptFlattener.Resolve(this.Plain(), (VariantSelection)null);
			string[] lines = ScriptEmitter.Emit(resolved).Split('\n', StringSplitOptions.RemoveEmptyEntries);

			Assert.Equal(3, resolved.Edits.Count);
			Assert.Equal(3, ScriptEmitter.CountOf(resolved));

			// The header, then one line for each command.
			Assert.Equal(4, lines.Length);
		}

		/// <summary>
		/// The test that carries the feature. Write the file, read it back with the real parser,
		/// and compare the command sequence.
		/// </summary>
		[Fact]
		public void TheOutputParsesBackToTheSameCommands()
		{
			ResolvedScript resolved = ScriptFlattener.Resolve(this.Plain(), (VariantSelection)null);

			string path = this._temp.File("generated.end");
			File.WriteAllText(path, ScriptEmitter.Emit(resolved));

			BaseCommand[] parsed = ScriptReader.Parse(path);

			Assert.Equal(resolved.Edits.Count, parsed.Length);

			for (int i = 0; i < parsed.Length; ++i)
			{
				Assert.Equal(resolved.Edits[i].Verb, parsed[i].Type);
				Assert.Equal(resolved.Edits[i].Text, parsed[i].Line);
			}
		}

		/// <summary>
		/// A float must survive as text. A round trip through a number turns -0.19500002 into
		/// something else, and the game then reads a value that the mod never wrote.
		/// </summary>
		[Fact]
		public void AFloatKeepsItsOriginalText()
		{
			string text = ScriptEmitter.Emit(ScriptFlattener.Resolve(this.Plain(), (VariantSelection)null));

			Assert.Contains("-0.19500002", text);
		}

		[Fact]
		public void AQuotedArgumentKeepsItsQuotes()
		{
			string text = ScriptEmitter.Emit(ScriptFlattener.Resolve(this.Plain(), (VariantSelection)null));

			Assert.Contains("\"a value with spaces\"", text);
		}

		/// <summary>
		/// The answered question becomes the plain commands of its branch. No question and no
		/// append may survive. An append that survived would apply the file a second time.
		/// </summary>
		[Fact]
		public void AnAnsweredQuestionBecomesThePlainCommandsOfItsBranch()
		{
			ModVariant variant = this.Asking();
			var selection = new VariantSelection(variant.Name);
			selection.Choose(0, "Fast");

			ResolvedScript resolved = ScriptFlattener.Resolve(variant, selection);

			Assert.Single(resolved.Answers);
			Assert.Equal("Fast", resolved.Answers[0]);
			Assert.Equal(2, resolved.Edits.Count);

			string path = this._temp.File("generated.end");
			File.WriteAllText(path, ScriptEmitter.Emit(resolved));

			BaseCommand[] parsed = ScriptReader.Parse(path);

			Assert.Equal(2, parsed.Length);

			foreach (BaseCommand command in parsed)
			{
				Assert.NotEqual(eCommandType.combobox, command.Type);
				Assert.NotEqual(eCommandType.checkbox, command.Type);
				Assert.NotEqual(eCommandType.append, command.Type);
			}
		}

		[Fact]
		public void TwoAnswersGiveTwoDifferentScripts()
		{
			ModVariant variant = this.Asking();

			var fast = new VariantSelection(variant.Name);
			fast.Choose(0, "Fast");

			var slow = new VariantSelection(variant.Name);
			slow.Choose(0, "Slow");

			string first = ScriptEmitter.Emit(ScriptFlattener.Resolve(variant, fast));
			string second = ScriptEmitter.Emit(ScriptFlattener.Resolve(variant, slow));

			Assert.NotEqual(first, second);
			Assert.Contains("Manufacturer 9", first);
			Assert.Contains("Manufacturer 2", second);
			Assert.DoesNotContain("Manufacturer 9", second);
		}

		[Fact]
		public void EveryLineKeepsTheTextThatTheModWrote()
		{
			ResolvedScript resolved = ScriptFlattener.Resolve(this.Plain(), (VariantSelection)null);
			string text = ScriptEmitter.Emit(resolved);

			foreach (ResolvedEdit edit in resolved.Edits) Assert.Contains(edit.Text, text);
		}
	}
}
