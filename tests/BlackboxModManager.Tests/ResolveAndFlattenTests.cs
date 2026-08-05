using System;
using System.Collections.Generic;
using System.Linq;
using BlackboxModManager.Core.Mods;
using Endscript.Enums;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers 4.3 selection persistence, 4.4 non-interactive resolution, and 4.5 resolved
	/// command extraction.
	/// </summary>
	public class ResolveAndFlattenTests
	{
		private static ModVariant OneLap(string name) => ModPackageReader.Read(ExampleMods.OneLap).Find(name);

		private static ModVariant Camera() => ModPackageReader.Read(ExampleMods.Camera).Variants[0];

		// ------------------------------------------------------------------ flatten, no question

		[Fact]
		public void TheUrlVariantFlattensToItsFiftyOneCommands()
		{
			ResolvedScript resolved = ScriptFlattener.Resolve(OneLap("1 Lap URL Races"), (VariantSelection)null);

			Assert.Equal(51, resolved.Edits.Count);
			Assert.All(resolved.Edits, e => Assert.Equal(eCommandType.update_incareer, e.Verb));
			Assert.Empty(resolved.Answers);
			Assert.Empty(resolved.Notes);
		}

		[Fact]
		public void EveryUrlEditCarriesAConflictKey()
		{
			ResolvedScript resolved = ScriptFlattener.Resolve(OneLap("1 Lap URL Races"), (VariantSelection)null);

			Assert.Equal(51, resolved.KeyedEdits.Count());
		}

		[Fact]
		public void AKeyHoldsTheTargetFileAndTheNamePathButNotTheValue()
		{
			// update_incareer GLOBAL\GLOBALB.LZC GCareers Main GCareerRaces S4_URL_1 Stages STAGE1 NumberOfLaps 1
			ResolvedScript resolved = ScriptFlattener.Resolve(OneLap("1 Lap URL Races"), (VariantSelection)null);
			ResolvedEdit first = resolved.Edits[0];

			Assert.Equal(@"GLOBAL\GLOBALB.LZC", first.Key.TargetFile);
			Assert.Equal(
				new[] { "GCareers", "Main", "GCareerRaces", "S4_URL_1", "Stages", "STAGE1", "NumberOfLaps" },
				first.Key.Segments.ToArray());
			Assert.Equal("1", first.Value);
		}

		// ------------------------------------------------------------------ flatten, one question

		[Fact]
		public void TheCameraVariantWithNoStoredAnswerTakesTheFirstOptionAndSaysSo()
		{
			// A deploy must never block on a prompt.
			ResolvedScript resolved = ScriptFlattener.Resolve(Camera(), (VariantSelection)null);

			Assert.Equal("Install Camera Mod [NFSMW TO U2]", Assert.Single(resolved.Answers));

			ResolverNote note = Assert.Single(resolved.Notes);
			Assert.Equal(0, note.Ordinal);
			Assert.Contains("No selection is stored", note.Reason, StringComparison.Ordinal);
		}

		[Fact]
		public void TheCameraBranchesFlattenToTheirOwnCommandsAndNothingElse()
		{
			// The parsed script holds 1198 commands across both branches. A resolved run
			// applies one branch only.
			ModVariant variant = Camera();

			ResolvedScript install = Resolve(variant, "Install Camera Mod [NFSMW TO U2]");
			ResolvedScript restore = Resolve(variant, "Restore original camera settings");

			Assert.Equal(450, install.Edits.Count);
			Assert.Equal(744, restore.Edits.Count);

			Assert.All(install.Edits, e => Assert.Equal("[1]_Camera_MOD_NFSMW_TO_U2.end", e.SourceFile));
			Assert.All(restore.Edits, e => Assert.Equal("[0]_Restore_Camera_Settings.end", e.SourceFile));
		}

		[Fact]
		public void AStoredAnswerResolvesWithNoPrompt()
		{
			ModVariant variant = Camera();
			var selections = new ModSelections();
			selections.Ensure(variant.Name).Choose(0, "Restore original camera settings");

			ResolvedScript resolved = ScriptFlattener.Resolve(variant, selections);

			Assert.Equal("Restore original camera settings", Assert.Single(resolved.Answers));
			Assert.Empty(resolved.Notes);
		}

		[Fact]
		public void ApplyDefaultsFillsTheFirstOptionOfEveryUnansweredQuestion()
		{
			ModVariant variant = Camera();
			var selections = new ModSelections();

			selections.ApplyDefaults(variant);

			Assert.Equal("Install Camera Mod [NFSMW TO U2]", selections.For(variant.Name).Answer(0));
		}

		[Fact]
		public void ApplyDefaultsLeavesAnExistingAnswerAlone()
		{
			ModVariant variant = Camera();
			var selections = new ModSelections();
			selections.Ensure(variant.Name).Choose(0, "Restore original camera settings");

			selections.ApplyDefaults(variant);

			Assert.Equal("Restore original camera settings", selections.For(variant.Name).Answer(0));
		}

		// ------------------------------------------------------------------ selection failures

		[Fact]
		public void AnAnswerThatNoLongerExistsFailsAndNamesTheModAndTheOptions()
		{
			// This is what a mod update that renames an option must produce. A stored index
			// would silently walk the wrong branch instead.
			ModVariant variant = Camera();

			ModSelectionException error = Assert.Throws<ModSelectionException>(
				() => Resolve(variant, "Install the old thing"));

			Assert.Equal(variant.Name, error.Variant);
			Assert.Contains("Install the old thing", error.Message, StringComparison.Ordinal);
			Assert.Contains("script.end line 2", error.Message, StringComparison.Ordinal);
			Assert.Contains("Choose option you needeed", error.Message, StringComparison.Ordinal);
			Assert.Contains("Restore original camera settings", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void AnAnswerIsMatchedByNameAndNotByPosition()
		{
			// Index 1 of the question is "Restore original camera settings". Store the name
			// and the resolver must reach that branch whatever its position becomes.
			ResolvedScript resolved = Resolve(Camera(), "Restore original camera settings");

			Assert.Equal(744, resolved.Edits.Count);
		}

		[Fact]
		public void AnEmptyOptionNameIsRejectedWhenItIsStored()
		{
			var selection = new VariantSelection("x");

			Assert.Throws<ArgumentException>(() => selection.Choose(0, " "));
		}

		// ------------------------------------------------------------------ values

		[Fact]
		public void AFloatKeepsItsOriginalText()
		{
			// A default ToString round trip corrupts -0.19500002 and 2.746582.
			ResolvedScript resolved = Resolve(Camera(), "Install Camera Mod [NFSMW TO U2]");

			var values = new HashSet<string>(resolved.Edits.Select(e => e.Value), StringComparer.Ordinal);

			Assert.Contains("-5.9", values);
			Assert.Contains("1.7", values);
		}

		[Fact]
		public void AValueReadsAsANumberWithTheInvariantCulture()
		{
			var edit = new ResolvedEdit(eCommandType.update_collection,
				CommandCatalog.Lookup(eCommandType.update_collection),
				new EditKey("A", new[] { "B" }), "-0.19500002", "f", 1, "line");

			Assert.True(edit.TryReadNumber(out double number));
			Assert.Equal(-0.19500002, number, 8);
		}

		// ------------------------------------------------------------------ key comparison

		[Fact]
		public void TwoSpellingsOfOneTargetProduceOneKey()
		{
			var left = new EditKey(@"GLOBAL\GLOBALB.LZC", new[] { "CarTypeInfos", "PEUGOT" });
			var right = new EditKey("global/globalb.lzc", new[] { "cartypeinfos", "peugot" });

			Assert.Equal(left, right);
			Assert.Equal(left.GetHashCode(), right.GetHashCode());
		}

		[Fact]
		public void ADifferentNamePathProducesADifferentKey()
		{
			var left = new EditKey("A", new[] { "One", "Two" });
			var right = new EditKey("A", new[] { "One", "Three" });

			Assert.NotEqual(left, right);
		}

		// ------------------------------------------------------------------ conflicts

		[Fact]
		public void TheAllVariantAndTheUrlVariantAgreeAndProduceNoConflict()
		{
			// ALL is the union of the other four. A user may legitimately enable ALL and
			// URL together. Every shared key then carries the same value.
			ResolvedScript all = ScriptFlattener.Resolve(OneLap("1 Lap ALL Races"), (VariantSelection)null);
			ResolvedScript url = ScriptFlattener.Resolve(OneLap("1 Lap URL Races"), (VariantSelection)null);

			Assert.Empty(ConflictDetector.Find(new[] { all, url }));
		}

		[Fact]
		public void TheAllVariantReallyDoesCoverTheUrlVariant()
		{
			// Guards the test above. Without this, an empty overlap would also pass.
			ResolvedScript all = ScriptFlattener.Resolve(OneLap("1 Lap ALL Races"), (VariantSelection)null);
			ResolvedScript url = ScriptFlattener.Resolve(OneLap("1 Lap URL Races"), (VariantSelection)null);

			var allKeys = new HashSet<EditKey>(all.KeyedEdits.Select(e => e.Key));

			Assert.All(url.KeyedEdits, e => Assert.Contains(e.Key, allKeys));
		}

		[Fact]
		public void TheTwoCameraBranchesDisagreeAndProduceConflicts()
		{
			ModVariant variant = Camera();

			ResolvedScript install = Resolve(variant, "Install Camera Mod [NFSMW TO U2]");
			ResolvedScript restore = Resolve(variant, "Restore original camera settings");

			IReadOnlyList<ModConflict> conflicts = ConflictDetector.Find(new[] { install, restore });

			Assert.NotEmpty(conflicts);
			Assert.All(conflicts, c => Assert.NotEqual(c.LeftValue, c.RightValue));
		}

		[Fact]
		public void OneVariantNeverConflictsWithItself()
		{
			ResolvedScript url = ScriptFlattener.Resolve(OneLap("1 Lap URL Races"), (VariantSelection)null);

			Assert.Empty(ConflictDetector.Find(new[] { url }));
		}

		// ------------------------------------------------------------------ helpers

		private static ResolvedScript Resolve(ModVariant variant, string answer)
		{
			var selection = new VariantSelection(variant.Name);
			selection.Choose(0, answer);

			return ScriptFlattener.Resolve(variant, selection);
		}
	}
}
