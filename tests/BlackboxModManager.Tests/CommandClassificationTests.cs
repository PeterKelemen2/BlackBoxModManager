using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlackboxModManager.Core.Mods;
using Endscript.Enums;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers step 8. Every verb of the endscript vocabulary carries a classification, the
	/// conflict check covers each category, and a filesystem command cannot leave the staging
	/// copy.
	///
	/// Every test here reads text only. No test needs a game, a Binary install, or Wine.
	/// </summary>
	public class CommandClassificationTests
	{
		// ------------------------------------------------------------------ the vocabulary

		[Fact]
		public void EveryVerbOfTheVocabularyCarriesAClassification()
		{
			// The catalog and the enum must hold the same set. A library update that adds a
			// verb fails here, and not silently at deploy time.
			var missing = new List<eCommandType>();

			foreach (eCommandType verb in Enum.GetValues<eCommandType>())
			{
				if (!CommandCatalog.All.ContainsKey(verb)) missing.Add(verb);
			}

			Assert.Empty(missing);
			Assert.Equal(Enum.GetValues<eCommandType>().Length, CommandCatalog.All.Count);
		}

		[Fact]
		public void TheVocabularyHolds48Verbs()
		{
			Assert.Equal(48, CommandCatalog.All.Count);
		}

		[Fact]
		public void OnlyTheInvalidVerbIsUnclassified()
		{
			// eCommandType.invalid is the type that the parser gives to a word it does not
			// know. Every other entry names a real verb and needs a real category.
			IReadOnlyList<eCommandType> open = CommandCatalog.Of(CommandCategory.Unclassified);

			Assert.Equal(new[] { eCommandType.invalid }, open.ToArray());
		}

		[Fact]
		public void AVerbOutsideTheEnumReadsAsUnclassifiedAndNotAsSafe()
		{
			// The backstop for the day the library grows a verb. Never treat an unknown verb
			// as conflict free.
			CommandFacts facts = CommandCatalog.Lookup((eCommandType)9999);

			Assert.Equal(CommandCategory.Unclassified, facts.Category);
			Assert.Equal(CommandSupport.Warn, facts.Support);
			Assert.False(facts.HasKey);
		}

		[Fact]
		public void EveryCategoryHoldsAtLeastOneVerb()
		{
			foreach (CommandCategory category in Enum.GetValues<CommandCategory>())
			{
				Assert.NotEmpty(CommandCatalog.Of(category));
			}
		}

		// ------------------------------------------------------------------ keys per category

		[Fact]
		public void AScalarWriteKeepsItsFieldKey()
		{
			ResolvedScript resolved = Script(
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT Manufacturer PEUGEOT");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.Equal(CommandCategory.ScalarFieldWrite, edit.Category);
			Assert.Equal(new[] { "CarTypeInfos", "PEUGOT", "Manufacturer" }, edit.Key.Segments.ToArray());
			Assert.Equal("PEUGEOT", edit.Value);
		}

		[Fact]
		public void AnExistenceChangeKeysOnTheCollectionPath()
		{
			ResolvedScript resolved = Script(
				"add_collection GLOBAL\\GLOBALB.LZC CarTypeInfos NEWCAR",
				"remove_collection GLOBAL\\GLOBALB.LZC CarTypeInfos OLDCAR");

			Assert.All(resolved.Edits, e => Assert.Equal(CommandCategory.ExistenceChange, e.Category));
			Assert.Equal(new[] { "CarTypeInfos", "NEWCAR" }, resolved.Edits[0].Key.Segments.ToArray());
			Assert.False(resolved.Edits[0].Removes);
			Assert.True(resolved.Edits[1].Removes);
		}

		[Fact]
		public void ACopyKeysOnTheNewNameAndNotOnTheSource()
		{
			ResolvedScript resolved = Script("copy_collection GLOBAL\\GLOBALB.LZC CarTypeInfos OLD NEW");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.Equal(new[] { "CarTypeInfos", "NEW" }, edit.Key.Segments.ToArray());
			Assert.Equal("OLD", edit.Value);
		}

		[Fact]
		public void ATextureReplacementKeysOnTheTexturePath()
		{
			ResolvedScript resolved = Script(
				"replace_texture GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL 0x1234 art\\new.dds");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.Equal(CommandCategory.TextureOperation, edit.Category);
			Assert.Equal(new[] { "TPKBlocks", "GLOBAL", "0x1234" }, edit.Key.Segments.ToArray());
			Assert.Equal("art\\new.dds", edit.Value);
		}

		[Fact]
		public void ADeleteKeysOnTheContainerAlone()
		{
			ResolvedScript resolved = Script("delete GLOBAL\\GLOBALB.LZC");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.True(edit.Key.IsContainer);
			Assert.True(edit.Removes);
		}

		[Fact]
		public void AStaticWriteKeysOnTheManagerAndNotOnACollection()
		{
			ResolvedScript resolved = Script("static GLOBAL\\GLOBALB.LZC CarTypeInfos WhatGame 2");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.Equal(CommandCategory.ScalarFieldWrite, edit.Category);
			Assert.Equal(new[] { "CarTypeInfos", "WhatGame" }, edit.Key.Segments.ToArray());
			Assert.Equal("2", edit.Value);
		}

		[Fact]
		public void AControlFlowVerbReachesNoEditList()
		{
			ResolvedScript resolved = Script(
				"combobox \"a\" \"b\" \"Pick one\"",
				"\"a\"",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
				"\"b\"",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 2",
				"end");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.Equal("1", edit.Value);
			Assert.False(resolved.IsApproximate);
		}

		// ------------------------------------------------------------------ the prefix rule

		[Fact]
		public void ARemovalBeatsAFieldWriteInsideTheRemovedCollection()
		{
			// The two keys never match. The removal path is a prefix of the write path, and
			// that is the case the current key scheme used to miss.
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "remove_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT" },
				new[] { "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT Manufacturer X" });

			ModConflict conflict = Assert.Single(conflicts);

			Assert.Equal(ConflictKind.Coverage, conflict.Kind);
			Assert.Equal(ConflictCertainty.Certain, conflict.Certainty);
		}

		[Fact]
		public void ADeletedContainerBeatsEveryEditInsideIt()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "delete GLOBAL\\GLOBALB.LZC" },
				new[]
				{
					"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
					"add_collection GLOBAL\\GLOBALB.LZC CarTypeInfos NEW",
				});

			Assert.Equal(2, conflicts.Count);
			Assert.All(conflicts, c => Assert.Equal(ConflictKind.Coverage, c.Kind));
		}

		[Fact]
		public void ARemovalOfAnUnrelatedCollectionIsNoConflict()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "remove_collection GLOBAL\\GLOBALB.LZC CarTypeInfos MAZDA" },
				new[] { "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos PEUGOT Manufacturer X" });

			Assert.Empty(conflicts);
		}

		[Fact]
		public void OneSegmentThatStartsWithAnotherIsNoPrefix()
		{
			// A join on an empty string made ("CAR", "A") and ("CARA") one key. The key must
			// keep the segment boundary.
			var outer = new EditKey("F", new[] { "CAR" });
			var inner = new EditKey("F", new[] { "CARA" });

			Assert.False(outer.Covers(inner));
			Assert.True(outer.Covers(new EditKey("F", new[] { "CAR", "A" })));
		}

		// ------------------------------------------------------------------ existence pairs

		[Fact]
		public void TwoModsThatAddOneCollectionConflict()
		{
			// Manager.Add calls CreationCheck and throws on a duplicate name, so the second
			// command fails and the deploy stops.
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "add_collection GLOBAL\\GLOBALB.LZC CarTypeInfos NEWCAR" },
				new[] { "add_collection GLOBAL\\GLOBALB.LZC CarTypeInfos NEWCAR" });

			ModConflict conflict = Assert.Single(conflicts);

			Assert.Equal(ConflictKind.Existence, conflict.Kind);
			Assert.Contains("duplicate", conflict.Reason, StringComparison.Ordinal);
		}

		[Fact]
		public void OneModThatAddsAndOneThatRemovesConflict()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "add_collection GLOBAL\\GLOBALB.LZC CarTypeInfos NEWCAR" },
				new[] { "remove_collection GLOBAL\\GLOBALB.LZC CarTypeInfos NEWCAR" });

			Assert.Equal(ConflictKind.Existence, Assert.Single(conflicts).Kind);
		}

		[Fact]
		public void TwoTextureReplacementsWithOneSourceAreNoConflict()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "replace_texture GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL 0x1 art\\a.dds" },
				new[] { "replace_texture GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL 0x1 art\\a.dds" });

			Assert.Empty(conflicts);
		}

		[Fact]
		public void TwoTextureReplacementsWithTwoSourcesConflict()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "replace_texture GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL 0x1 art\\a.dds" },
				new[] { "replace_texture GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL 0x1 art\\b.dds" });

			Assert.Single(conflicts);
		}

		// ------------------------------------------------------------------ opaque commands

		[Fact]
		public void AnOpaqueCommandOnOneTexturePackReportsAPossibleConflict()
		{
			// bind_textures reads a directory listing at deploy time. The tool cannot name the
			// textures that it changes, so it must report the pair and say so.
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "bind_textures override GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL art" },
				new[] { "replace_texture GLOBAL\\GLOBALB.LZC TPKBlocks GLOBAL 0x1 art\\b.dds" });

			ModConflict conflict = Assert.Single(conflicts);

			Assert.Equal(ConflictKind.Opaque, conflict.Kind);
			Assert.Equal(ConflictCertainty.Possible, conflict.Certainty);
		}

		[Fact]
		public void AnOpaqueCommandProducesAWarning()
		{
			ResolvedScript resolved = Script("import override GLOBAL\\GLOBALB.LZC CarTypeInfos data\\car.bin");

			ScriptWarning warning = Assert.Single(resolved.Warnings);

			Assert.Equal(eCommandType.import, warning.Verb);
			Assert.True(resolved.Edits[0].Opaque);
		}

		// ------------------------------------------------------------------ the if command

		[Fact]
		public void AConditionalEditMakesAPossibleConflictAndNotACertainOne()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1" },
				new[]
				{
					"if collection_exists GLOBAL\\GLOBALB.LZC CarTypeInfos A",
					"do",
					"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 2",
					"end",
				});

			ModConflict conflict = Assert.Single(conflicts);

			Assert.Equal(ConflictCertainty.Possible, conflict.Certainty);
		}

		[Fact]
		public void AnIfWithNoElseBlockWarns()
		{
			// ProcessScript jumps to Options[Choice].Start with no fallback. A missing branch
			// ends the deploy when the check picks that branch.
			ResolvedScript resolved = Script(
				"if collection_exists GLOBAL\\GLOBALB.LZC CarTypeInfos A",
				"do",
				"update_collection GLOBAL\\GLOBALB.LZC CarTypeInfos A B 1",
				"end");

			Assert.Contains(resolved.Warnings, w => w.Verb == eCommandType.@if);
			Assert.True(resolved.IsApproximate);
		}

		// ------------------------------------------------------------------ the path sandbox

		[Fact]
		public void APathInsideStagingPasses()
		{
			ResolvedScript resolved = Script(Roots(), "create_folder absolute CARS\\NEW");

			Assert.Empty(resolved.Escapes());
			Assert.Equal(CommandCategory.FilesystemEffect, resolved.Edits[0].Category);
		}

		[Fact]
		public void APathThatClimbsOutOfStagingFails()
		{
			ResolvedScript resolved = Script(Roots(), "erase_file absolute ..\\..\\important.txt");

			(ResolvedEdit Edit, PathEffect Path) escape = Assert.Single(resolved.Escapes());

			Assert.True(escape.Path.Writes);
			Assert.Contains("outside", escape.Path.Violation, StringComparison.Ordinal);
		}

		[Fact]
		public void ARootedPathDropsTheStagingDirectoryAndFails()
		{
			// Path.Combine returns the second argument alone when it is rooted. The library
			// calls Path.Combine and nothing else, so a rooted path writes where it names.
			string root = Path.GetPathRoot(Path.GetTempPath());
			string rooted = Path.Combine(root, "somewhere", "evil.txt");

			ResolvedScript resolved = Script(Roots(), $"create_file override absolute \"{rooted}\"");

			Assert.Single(resolved.Escapes());
		}

		[Fact]
		public void AMoveOutOfStagingFails()
		{
			ResolvedScript resolved = Script(Roots(),
				"move_file override relative absolute art\\a.dds ..\\..\\a.dds");

			Assert.Single(resolved.Escapes());
		}

		[Fact]
		public void TheWordAllNamesTheFiveMemoryFilesAndEveryOneStaysInStaging()
		{
			ResolvedScript resolved = Script(Roots(), "unlock_memory all");

			ResolvedEdit edit = Assert.Single(resolved.Edits);

			Assert.Equal(5, edit.Paths.Count);
			Assert.All(edit.Paths, p => Assert.True(p.IsSafe));
			Assert.All(edit.Paths, p => Assert.True(p.Writes));
			Assert.Empty(resolved.Escapes());
		}

		[Fact]
		public void WithNoStagingDirectoryThePathCarriesNoVerdict()
		{
			// A missing root is not a pass. The result carries no resolved path, so a caller
			// that needs the verdict has to ask again with the root.
			ResolvedScript resolved = Script("erase_file absolute ..\\..\\important.txt");

			PathEffect path = Assert.Single(resolved.Edits[0].Paths);

			Assert.Null(path.Resolved);
			Assert.True(path.IsSafe);
		}

		[Fact]
		public void TwoModsThatWriteOnePathConflict()
		{
			IReadOnlyList<ModConflict> conflicts = Compare(
				new[] { "create_folder absolute CARS\\NEW" },
				new[] { "erase_folder absolute CARS\\NEW" });

			Assert.Equal(ConflictKind.Filesystem, Assert.Single(conflicts).Kind);
		}

		// ------------------------------------------------------------------ refused commands

		[Fact]
		public void StopErrorsIsRefused()
		{
			// The command tells the manager to drop every later error. Our rule is that one
			// error entry fails the deploy, and this command defeats that rule.
			ResolvedScript resolved = Script("stop_errors true");

			ResolvedEdit edit = Assert.Single(resolved.Rejected);

			Assert.Equal(eCommandType.stop_errors, edit.Verb);
		}

		[Fact]
		public void SpeedReflectIsRefused()
		{
			// The command copies SpeedReflect.asi out of our own directory. SpeedReflect is
			// GPL-3.0 and this application does not ship it.
			ResolvedScript resolved = Script("speedreflect auto");

			Assert.Equal(eCommandType.speedreflect, Assert.Single(resolved.Rejected).Verb);
		}

		[Fact]
		public void AManifestKeyInAScriptIsRefused()
		{
			// EndScriptParser has no class for 'generate', so the line parses to an unknown
			// verb. The flattener names the word and stops.
			ScriptParseException error = Assert.Throws<ScriptParseException>(() => Script("generate"));

			Assert.Contains("generate", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void UnlockMemoryIsSupportedBecauseItWritesToDisk()
		{
			// MemoryUnlock.FastUnlock writes a 16 byte header over a file of the game
			// directory. Staging covers it and the revert restores it.
			CommandFacts facts = CommandCatalog.Lookup(eCommandType.unlock_memory);

			Assert.Equal(CommandSupport.Supported, facts.Support);
			Assert.Equal(CommandCategory.FilesystemEffect, facts.Category);
		}

		// ------------------------------------------------------------------ helpers

		/// <summary>Roots that point at directories which need not exist.</summary>
		private static SandboxRoots Roots()
		{
			string root = Path.Combine(Path.GetTempPath(), "step8");

			return new SandboxRoots(Path.Combine(root, "staging"), Path.Combine(root, "mod"));
		}

		private static ResolvedScript Script(params string[] lines) => Script(null, lines);

		private static ResolvedScript Script(SandboxRoots roots, params string[] lines)
		{
			using var temp = new TempDirectory();

			temp.WriteManifest("Mod.end", "Underground2", "Script.end");
			temp.WriteScript("Script.end", lines);

			ModVariant variant = ModPackageReader.Read(temp.Path).Variants[0];

			return ScriptFlattener.Resolve(variant, (VariantSelection)null, roots);
		}

		/// <summary>
		/// Resolves two hand-built variants and compares them. The first list is the earlier
		/// mod in load order.
		/// </summary>
		private static IReadOnlyList<ModConflict> Compare(string[] left, string[] right)
		{
			SandboxRoots roots = Roots();

			var scripts = new[]
			{
				Rename(Script(roots, left), "left"),
				Rename(Script(roots, right), "right"),
			};

			return ConflictDetector.Find(scripts);
		}

		private static ResolvedScript Rename(ResolvedScript script, string name)
		{
			return new ResolvedScript(name, script.Edits, script.Answers, script.Notes,
				script.Warnings, script.IsApproximate);
		}
	}
}
