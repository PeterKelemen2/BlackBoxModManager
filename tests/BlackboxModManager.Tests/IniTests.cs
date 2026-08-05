using System;
using System.Collections.Generic;
using System.Linq;
using BlackboxModManager.Core.Asi;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers part A of step 9: the reader, the writer, and the editor guess.
	///
	/// Every test reads text only. No test needs a game, a Binary install, or Wine.
	/// </summary>
	public class IniTests
	{
		// ------------------------------------------------------------------ the reader

		[Fact]
		public void TheReaderGroupsEveryKeyUnderItsSection()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			Assert.Equal(new[] { "MAIN", "MISC" }, document.Sections.Select(s => s.Name).ToArray());
			Assert.Equal(5, document.Sections[0].Entries.Count);
			Assert.Equal(3, document.Sections[1].Entries.Count);
		}

		[Fact]
		public void TheReaderKeepsEveryLine()
		{
			// The writer needs every line. A reader that drops a blank line or a comment makes
			// the deployed file differ from the original in more than the changed values.
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			Assert.Equal(AsiFixture.SettingsText, document.Text());
			Assert.Contains(document.Lines, l => l.Kind == IniLineKind.Blank);
			Assert.Contains(document.Lines, l => l.Kind == IniLineKind.Comment);
		}

		[Fact]
		public void ATrailingCommentIsTheHelpTextOfItsKey()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniEntry entry = document.Find(new IniKey("MAIN", "FMVWidescreenMode"));

			Assert.Equal("1", entry.Value);
			Assert.Equal("FMVs will appear in fullscreen for 16:9. (1 = Cropped | 2 = Stretched)", entry.Comment);
		}

		[Fact]
		public void AKeyWithNoCommentCarriesNone()
		{
			// The row shows the question mark marker only when the key has a comment.
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			Assert.Equal(String.Empty, document.Find(new IniKey("MAIN", "NoComment")).Comment);
		}

		[Fact]
		public void TheReaderAcceptsBothCommentCharactersAndRemembersWhichOneTheFileUsed()
		{
			IniDocument document = IniReader.Parse("[A]\nx = 1 ; semicolon\ny = 2 # hash\n");

			Assert.Equal(';', document.Lines[1].CommentChar);
			Assert.Equal("semicolon", document.Lines[1].Comment);
			Assert.Equal('#', document.Lines[2].CommentChar);
			Assert.Equal("hash", document.Lines[2].Comment);
		}

		[Fact]
		public void AKeyOutsideEverySectionLandsInAnUnnamedSection()
		{
			// The sample has none. The case is legal and it must not crash.
			IniDocument document = IniReader.Parse("Loose = 1\n[MAIN]\nInside = 2\n");

			Assert.True(document.Sections[0].IsUnnamed);
			Assert.Equal("Loose", document.Sections[0].Entries[0].Key.Key);
			Assert.Equal("1", document.Find(new IniKey(String.Empty, "Loose")).Value);
		}

		[Fact]
		public void ADuplicateKeyStaysInTheModelAndWarns()
		{
			// Two lines with one key make an edit ambiguous. Keep both, edit the first, warn.
			IniDocument document = IniReader.Parse("[MAIN]\nFixHUD = 1\nFixHUD = 0\n");

			Assert.Equal(2, document.Sections[0].Entries.Count);
			Assert.False(document.Sections[0].Entries[0].IsDuplicate);
			Assert.True(document.Sections[0].Entries[1].IsDuplicate);
			Assert.Single(document.Warnings);
			Assert.Equal("1", document.Find(new IniKey("MAIN", "FixHUD")).Value);
		}

		[Fact]
		public void ALineWithNoEqualSignPassesThroughAndWarns()
		{
			IniDocument document = IniReader.Parse("[MAIN]\nthis line is broken\n");

			Assert.Equal(IniLineKind.Unknown, document.Lines[1].Kind);
			Assert.Single(document.Warnings);
		}

		[Fact]
		public void ACommentCharacterInsideQuotesIsPartOfTheValue()
		{
			IniDocument document = IniReader.Parse("[MAIN]\nName = \"a;b\"\n");

			Assert.Equal("\"a;b\"", document.Find(new IniKey("MAIN", "Name")).Value);
		}

		[Fact]
		public void AnEmptyValueReadsAsEmptyAndNotAsMissing()
		{
			IniDocument document = IniReader.Parse("[MAIN]\nEmpty =    ; nothing here\n");

			IniEntry entry = document.Find(new IniKey("MAIN", "Empty"));

			Assert.Equal(String.Empty, entry.Value);
			Assert.Equal("nothing here", entry.Comment);
		}

		[Fact]
		public void TheKeyComparisonIgnoresLetterCase()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			Assert.NotNull(document.Find(new IniKey("main", "fixhud")));
			Assert.Equal(new IniKey("MAIN", "FixHUD"), IniKey.Parse("main/fixhud"));
			Assert.Equal("MAIN/FixHUD", new IniKey("MAIN", "FixHUD").ToString());
		}

		// ------------------------------------------------------------------ the editor guess

		[Theory]
		[InlineData("0", IniValueKind.Flag)]
		[InlineData("1", IniValueKind.Flag)]
		[InlineData("4", IniValueKind.Integer)]
		[InlineData("-1", IniValueKind.Integer)]
		[InlineData("10.0", IniValueKind.Decimal)]
		[InlineData("-0.195", IniValueKind.Decimal)]
		[InlineData("SAVEGAMES", IniValueKind.Text)]
		[InlineData("", IniValueKind.Text)]
		public void TheEditorTypeComesFromTheValueAlone(string value, IniValueKind expected)
		{
			Assert.Equal(expected, IniValue.Classify(value));
		}

		[Fact]
		public void ACommentThatReadsLikeAListDoesNotChangeTheEditor()
		{
			// (1 = Cropped | 2 = Stretched) is a human sentence. A drop-down built from it
			// would lock the user out of a legal value.
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			Assert.Equal(IniValueKind.Flag, document.Find(new IniKey("MAIN", "FMVWidescreenMode")).ValueKind);
		}

		[Fact]
		public void ADecimalReadsWithTheInvariantCulture()
		{
			// A comma-decimal locale would read 10.0 as one hundred, and the deployed file
			// would then hold the wrong number.
			Assert.Equal(IniValueKind.Decimal, IniValue.Classify("10.0"));
			Assert.Equal(IniValueKind.Text, IniValue.Classify("10,0"));
		}

		// ------------------------------------------------------------------ the writer

		[Fact]
		public void TheWriterChangesOneValueAndNothingElse()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniWriteResult result = Apply(document, ("MAIN", "FixHUD", "0"));

			string[] before = Lines(AsiFixture.SettingsText);
			string[] after = Lines(result.Text);

			Assert.Equal(before.Length, after.Length);

			var different = new List<int>();

			for (int i = 0; i < before.Length; ++i)
			{
				if (before[i] != after[i]) different.Add(i);
			}

			int line = Assert.Single(different);

			Assert.Contains("FixHUD", after[line], StringComparison.Ordinal);
			Assert.Equal(new[] { new IniKey("MAIN", "FixHUD") }, result.Changed.ToArray());
		}

		[Fact]
		public void TheWriterKeepsTheCommentAndTheColumn()
		{
			// A value that grows or shrinks must not push the comment out of its column.
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniWriteResult result = Apply(document, ("MAIN", "ResX", "1920"));

			IniDocument again = IniReader.Parse(result.Text);
			IniEntry entry = again.Find(new IniKey("MAIN", "ResX"));

			Assert.Equal("1920", entry.Value);
			Assert.Equal("Use this option to control the horizontal resolution.", entry.Comment);
			Assert.Equal(Column(AsiFixture.SettingsText, "ResX"), Column(result.Text, "ResX"));
		}

		[Fact]
		public void TheWriterKeepsTheLineTerminatorOfTheFile()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniWriteResult result = Apply(document, ("MAIN", "FixHUD", "0"));

			// Every line of the fixture ends with a carriage return and a line feed. Not one
			// line feed of the result may stand on its own.
			Assert.Contains("\r\n", result.Text, StringComparison.Ordinal);

			for (int i = 0; i < result.Text.Length; ++i)
			{
				if (result.Text[i] != '\n') continue;

				Assert.True(i > 0 && result.Text[i - 1] == '\r',
					$"The line feed at index {i} carries no carriage return.");
			}
		}

		[Fact]
		public void AValueThatDoesNotChangeProducesNoDifference()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniWriteResult result = Apply(document, ("MAIN", "FixHUD", "1"));

			Assert.Equal(AsiFixture.SettingsText, result.Text);
			Assert.Empty(result.Changed);
		}

		[Fact]
		public void TheWriterEditsTheFirstLineOfADuplicatedKey()
		{
			IniDocument document = IniReader.Parse("[MAIN]\nFixHUD = 1\nFixHUD = 0\n");

			IniWriteResult result = Apply(document, ("MAIN", "FixHUD", "9"));

			Assert.Equal("[MAIN]\nFixHUD = 9\nFixHUD = 0\n", result.Text);
		}

		[Fact]
		public void AKeyThatTheFileDoesNotHoldLandsInTheSkippedList()
		{
			// A mod update that renames a key reaches this. The deploy reports it and carries on.
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniWriteResult result = Apply(document, ("MAIN", "GoneInVersionTwo", "1"));

			Assert.Equal(AsiFixture.SettingsText, result.Text);
			Assert.Equal(new[] { new IniKey("MAIN", "GoneInVersionTwo") }, result.Skipped.ToArray());
		}

		[Fact]
		public void TheWriterStripsACommentCharacterOutOfAValue()
		{
			// A semicolon inside the value would turn the rest of the line into a comment and
			// lose the real one.
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			IniWriteResult result = Apply(document, ("MISC", "CustomUserFilesDirectoryInGameDir", "A;B"));

			IniEntry entry = IniReader.Parse(result.Text).Find(
				new IniKey("MISC", "CustomUserFilesDirectoryInGameDir"));

			Assert.Equal("AB", entry.Value);
			Assert.Equal("Use '0' to disable.", entry.Comment);
		}

		[Fact]
		public void TheWriterStripsALineTerminatorOutOfAValue()
		{
			IniDocument document = IniReader.Parse("[MAIN]\nName = a\n");

			IniWriteResult result = Apply(document, ("MAIN", "Name", "a\nEvil = 1"));

			Assert.Single(IniReader.Parse(result.Text).Sections[0].Entries);
		}

		[Fact]
		public void AnEmptyAnswerMapReturnsTheFileUnchanged()
		{
			IniDocument document = IniReader.Parse(AsiFixture.SettingsText);

			Assert.Equal(AsiFixture.SettingsText, IniWriter.Apply(document, null).Text);
		}

		// ------------------------------------------------------------------ helpers

		private static IniWriteResult Apply(IniDocument document,
			params (string Section, string Key, string Value)[] values)
		{
			var map = new Dictionary<IniKey, string>();

			foreach ((string section, string key, string value) in values)
			{
				map[new IniKey(section, key)] = value;
			}

			return IniWriter.Apply(document, map);
		}

		private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

		/// <summary>The column of the comment character on the line that holds this key.</summary>
		private static int Column(string text, string key)
		{
			foreach (string line in Lines(text))
			{
				if (!line.TrimStart().StartsWith(key, StringComparison.Ordinal)) continue;

				return line.IndexOf(';');
			}

			return -1;
		}
	}
}
