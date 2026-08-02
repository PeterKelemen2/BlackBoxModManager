using System;
using System.IO;
using System.Linq;
using BlackboxModManager.Core.Mods;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the guards that stand between us and a silent wrong install: an append loop,
	/// an unknown verb, and the tokenizer.
	/// </summary>
	public class ScriptGuardTests
	{
		// ------------------------------------------------------------------ append graph

		[Fact]
		public void TheGraphListsTheLauncherAndEveryAppendedFile()
		{
			string script = ModPath.Resolve(ExampleMods.Camera, @"Main\script.end");

			string[] names = ScriptAppendGraph.Walk(script).Select(Path.GetFileName).ToArray();

			Assert.Equal(
				new[] { "script.end", "[1]_Camera_MOD_NFSMW_TO_U2.end", "[0]_Restore_Camera_Settings.end" },
				names);
		}

		[Fact]
		public void AnAppendLoopIsNamedInsteadOfEndingTheStack()
		{
			// EndScriptParser.RecursiveRead keeps no visited set. A loop makes it recurse
			// until the stack ends, and that failure names no file.
			using var temp = new TempDirectory();
			temp.WriteScript("a.end", "append \"b.end\"");
			temp.WriteScript("b.end", "append \"a.end\"");

			ScriptAppendException error = Assert.Throws<ScriptAppendException>(
				() => ScriptAppendGraph.Walk(temp.File("a.end")));

			Assert.Contains("a.end -> b.end -> a.end", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void AScriptThatAppendsItselfIsALoop()
		{
			using var temp = new TempDirectory();
			temp.WriteScript("self.end", "append \"self.end\"");

			Assert.Throws<ScriptAppendException>(() => ScriptAppendGraph.Walk(temp.File("self.end")));
		}

		[Fact]
		public void TwoBranchesThatAppendOneFileAreNotALoop()
		{
			using var temp = new TempDirectory();
			temp.WriteScript("root.end", "append \"left.end\"", "append \"right.end\"");
			temp.WriteScript("left.end", "append \"shared.end\"");
			temp.WriteScript("right.end", "append \"shared.end\"");
			temp.WriteScript("shared.end", "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			Assert.Equal(4, ScriptAppendGraph.Walk(temp.File("root.end")).Count);
		}

		[Fact]
		public void AMissingAppendTargetNamesTheFile()
		{
			using var temp = new TempDirectory();
			temp.WriteScript("root.end", "append \"gone.end\"");

			ScriptAppendException error = Assert.Throws<ScriptAppendException>(
				() => ScriptAppendGraph.Walk(temp.File("root.end")));

			Assert.Contains("gone.end", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void AnAppendWithTheWrongTokenCountNamesTheLine()
		{
			using var temp = new TempDirectory();
			temp.WriteScript("root.end", "append one.end two.end");

			ScriptAppendException error = Assert.Throws<ScriptAppendException>(
				() => ScriptAppendGraph.Walk(temp.File("root.end")));

			Assert.Equal(2, error.Line);
		}

		[Fact]
		public void EveryAppendResolvesAgainstTheLauncherDirectory()
		{
			// The parser combines every append with the directory of the launcher, not with
			// the directory of the file that holds the append. Match that exactly.
			using var temp = new TempDirectory();
			temp.WriteScript("root.end", "append \"inner/one.end\"");
			temp.WriteScript(Path.Combine("inner", "one.end"), "append \"inner/two.end\"");
			temp.WriteScript(Path.Combine("inner", "two.end"), "update_collection GLOBAL\\GLOBALB.LZC A B C 1");

			Assert.Equal(3, ScriptAppendGraph.Walk(temp.File("root.end")).Count);
		}

		// ------------------------------------------------------------------ unknown verbs

		[Fact]
		public void AnUnknownVerbStopsTheFlattenAndNamesTheLine()
		{
			// A skipped edit produces an install that is wrong in a way the user cannot see.
			using var temp = new TempDirectory();
			temp.WriteManifest("Mod.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
				"frobnicate GLOBAL\\GLOBALB.LZC everything",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A C 2");

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];

			ScriptParseException error = Assert.Throws<ScriptParseException>(
				() => ScriptFlattener.Resolve(variant, (VariantSelection)null));

			Assert.Contains("frobnicate", error.Message, StringComparison.Ordinal);
			Assert.Equal(3, error.Line);
		}

		[Fact]
		public void AnOptionBlockHeaderIsNotAnUnknownVerb()
		{
			// A block header and an unknown verb both parse to OptionalCommand. Only the
			// enclosing question can tell them apart.
			using var temp = new TempDirectory();
			temp.WriteManifest("Mod.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end",
				"combobox \"first thing\" \"second thing\" \"Pick one\"",
				"\"first thing\"",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
				"\"second thing\"",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 2",
				"end");

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];
			ResolvedScript resolved = ScriptFlattener.Resolve(variant, (VariantSelection)null);

			Assert.Single(resolved.Edits);
			Assert.Equal("1", resolved.Edits[0].Value);
		}

		[Fact]
		public void ASelectableWithNoClosingEndIsRejected()
		{
			using var temp = new TempDirectory();
			temp.WriteManifest("Mod.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end",
				"combobox \"a\" \"b\" \"Pick one\"",
				"\"a\"",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1");

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];

			Assert.Throws<ScriptParseException>(() => ScriptFlattener.Resolve(variant, (VariantSelection)null));
		}

		[Fact]
		public void AnIfCommandIsReportedAsUnresolvableAndNotGuessed()
		{
			// ProcessScript evaluates an if against the loaded containers. A static walk
			// has none, so it must say so rather than pick a branch.
			using var temp = new TempDirectory();
			temp.WriteManifest("Mod.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end",
				"if collection_exists GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT",
				"do",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT A 1",
				"else",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT A 2",
				"end");

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];

			ScriptParseException error = Assert.Throws<ScriptParseException>(
				() => ScriptFlattener.Resolve(variant, (VariantSelection)null));

			Assert.Contains("'if' command", error.Message, StringComparison.Ordinal);
		}

		// ------------------------------------------------------------------ tokenizer

		[Fact]
		public void TheTokenizerKeepsAQuotedNameWhole()
		{
			string[] tokens = ScriptText.Tokenize("combobox \"Install Camera Mod [NFSMW TO U2]\" \"Restore it\" \"Pick one\"");

			Assert.Equal(
				new[] { "combobox", "Install Camera Mod [NFSMW TO U2]", "Restore it", "Pick one" },
				tokens);
		}

		[Fact]
		public void TheTokenizerStripsTheQuotes()
		{
			Assert.Equal(new[] { "append", "MOD/URL.end" }, ScriptText.Tokenize("append \"MOD/URL.end\""));
		}

		[Fact]
		public void AnEmptyLineGivesNoTokens()
		{
			Assert.Empty(ScriptText.Tokenize("   "));
			Assert.Empty(ScriptText.Tokenize(null));
		}

		[Theory]
		[InlineData("")]
		[InlineData("   ")]
		[InlineData("// a comment")]
		[InlineData("# another comment")]
		[InlineData("{")]
		[InlineData("}")]
		public void TheParserSkipsTheseLines(string line)
		{
			Assert.True(ScriptText.IsSkipped(line));
		}

		[Fact]
		public void ARealCommandIsNotSkipped()
		{
			Assert.False(ScriptText.IsSkipped("update_collection GLOBAL\\GLOBALB.LZC A B C 1"));
		}

		// ------------------------------------------------------------------ path resolution

		[Fact]
		public void ABackslashPathFromAManifestResolvesOnThisMachine()
		{
			// A manifest writes "MOD\URL.end" and the file sits at "MOD/URL.end".
			string resolved = ModPath.Resolve(ExampleMods.OneLap, @"MOD\URL.end");

			Assert.True(File.Exists(resolved));
		}

		[Fact]
		public void APathWithTheWrongLetterCaseResolves()
		{
			string resolved = ModPath.Resolve(ExampleMods.OneLap, @"mod\url.END");

			Assert.True(File.Exists(resolved));
		}

		[Fact]
		public void APathThatMatchesNothingComesBackAsThePlainJoin()
		{
			// The caller then reports a path that the mod actually asked for.
			string resolved = ModPath.Resolve(ExampleMods.OneLap, @"MOD\absent.end");

			Assert.False(File.Exists(resolved));
			Assert.EndsWith("absent.end", resolved, StringComparison.Ordinal);
		}
	}
}
