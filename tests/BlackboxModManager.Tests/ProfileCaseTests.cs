using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the letter case of every key that a profile file holds.
	///
	/// <b>System.Text.Json drops the comparer of a declared instance.</b> The declaration
	/// asks for <c>StringComparer.OrdinalIgnoreCase</c>, and the reader builds a new
	/// Dictionary with the default ordinal comparer. So a key matched without letter case
	/// before a save, and with letter case after a load. An ini answer and a loader choice
	/// both went missing after a restart. See step 19, Part 2.
	/// </summary>
	public class ProfileCaseTests
	{
		private const GameINT Game = GameINT.Underground2;

		private static Profile Written(string root)
		{
			var store = new ProfileStore(root);
			var profile = new Profile("Case", Game.ToString());

			ProfileEntry entry = profile.Ensure("a-mod");
			entry.Enabled = true;
			entry.EnsureIni("Scripts/Fix.ini")["GRAPHICS/Width"] = "1920";
			entry.Selections.Ensure("BigWheels").Enabled = true;

			profile.ChooseLoader("dinput8.dll", "a-mod");

			store.Save(Game, profile);

			return store.Find(Game, "Case");
		}

		[Fact]
		public void AnIniAnswerReadsBackWithAnyLetterCase()
		{
			using var temp = new TempDirectory();

			Profile onDisk = Written(temp.Path);
			ProfileEntry entry = onDisk.Find("a-mod");

			Assert.Equal("1920", entry.IniFor("scripts/fix.ini")["graphics/width"]);
		}

		[Fact]
		public void ALoaderChoiceReadsBackWithAnyLetterCase()
		{
			using var temp = new TempDirectory();

			Profile onDisk = Written(temp.Path);

			Assert.Equal("a-mod", onDisk.LoaderChoice("DINPUT8.DLL"));
		}

		[Fact]
		public void AVariantReadsBackWithAnyLetterCase()
		{
			using var temp = new TempDirectory();

			Profile onDisk = Written(temp.Path);
			ModSelections selections = onDisk.Find("a-mod").Selections;

			Assert.True(selections.IsEnabled("bigwheels"));
			Assert.NotNull(selections.For("BIGWHEELS"));
		}

		/// <summary>
		/// A background conflict check reads a clone. A clone that compares its keys with
		/// letter case makes the check and the live profile disagree.
		/// </summary>
		[Fact]
		public void ACloneKeepsTheComparerOfEveryMap()
		{
			using var temp = new TempDirectory();

			Profile copy = ProfileStore.Clone(Written(temp.Path));
			ProfileEntry entry = copy.Find("A-MOD");

			Assert.Equal("1920", entry.IniFor("scripts/fix.ini")["graphics/width"]);
			Assert.Equal("a-mod", copy.LoaderChoice("DINPUT8.DLL"));
			Assert.True(entry.Selections.IsEnabled("bigwheels"));
		}

		/// <summary>
		/// A file that holds no Selections object still reads. The reader filled that gap
		/// before Normalize existed, and it must still fill it.
		/// </summary>
		[Fact]
		public void AnEntryWithNoSelectionsReadsAsAnEmptySet()
		{
			var profile = new Profile("Case", Game.ToString());
			ProfileEntry entry = profile.Ensure("a-mod");
			entry.Selections = null;

			profile.Normalize();

			Assert.NotNull(entry.Selections);
			Assert.Equal(0, entry.Selections.Count);
		}
	}
}
