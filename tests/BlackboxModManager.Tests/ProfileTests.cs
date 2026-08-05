using System;
using System.IO;
using BlackboxModManager.Core.Profiles;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the profile model and the profile store of step 5.4.
	/// </summary>
	public class ProfileTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ProfileStore _store;

		public ProfileTests()
		{
			this._store = new ProfileStore(Path.Combine(this._temp.Path, "profiles"));
		}

		public void Dispose() => this._temp.Dispose();

		[Fact]
		public void TheOrderOfTheEntriesIsTheLoadOrder()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("first").Enabled = true;
			profile.Ensure("second").Enabled = true;
			profile.Ensure("third").Enabled = false;

			Assert.Equal(new[] { "first", "second" }, profile.EnabledInOrder());
		}

		[Fact]
		public void MoveChangesTheLoadOrder()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;

			Assert.True(profile.Move("b", -1));
			Assert.Equal(new[] { "b", "a" }, profile.EnabledInOrder());
		}

		[Fact]
		public void AMovePastTheEndDoesNothing()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;

			Assert.False(profile.Move("a", -1));
			Assert.False(profile.Move("a", 1));
			Assert.False(profile.Move("missing", 1));
		}

		[Fact]
		public void MoveToPutsTheEntryBeforeTheRowAtTheGivenIndex()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;
			profile.Ensure("c").Enabled = true;

			Assert.True(profile.MoveTo("a", 2));
			Assert.Equal(new[] { "b", "a", "c" }, profile.EnabledInOrder());
		}

		[Fact]
		public void MoveToTheEndAppendsTheEntry()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;
			profile.Ensure("c").Enabled = true;

			Assert.True(profile.MoveTo("a", 3));
			Assert.Equal(new[] { "b", "c", "a" }, profile.EnabledInOrder());
		}

		[Fact]
		public void MoveToTheFrontMovesAnEntryFromTheEnd()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;
			profile.Ensure("c").Enabled = true;

			Assert.True(profile.MoveTo("c", 0));
			Assert.Equal(new[] { "c", "a", "b" }, profile.EnabledInOrder());
		}

		[Fact]
		public void MoveToClampsAnIndexPastTheEnd()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;

			Assert.True(profile.MoveTo("a", 50));
			Assert.Equal(new[] { "b", "a" }, profile.EnabledInOrder());
		}

		[Fact]
		public void MoveToANegativeIndexClampsToTheFront()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;

			Assert.True(profile.MoveTo("b", -5));
			Assert.Equal(new[] { "b", "a" }, profile.EnabledInOrder());
		}

		[Fact]
		public void MoveToTheSameSpotDoesNothing()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("a").Enabled = true;
			profile.Ensure("b").Enabled = true;

			Assert.False(profile.MoveTo("a", 0));
			Assert.False(profile.MoveTo("a", 1));
			Assert.False(profile.MoveTo("missing", 1));
		}

		[Fact]
		public void ReconcileDropsAModThatLeftTheStoreAndAddsANewOne()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("gone").Enabled = true;
			profile.Ensure("stays").Enabled = true;

			Assert.True(profile.Reconcile(new[] { "stays", "fresh" }));

			Assert.Null(profile.Find("gone"));
			Assert.NotNull(profile.Find("stays"));
			Assert.False(profile.Find("fresh").Enabled);
			Assert.Equal(new[] { "stays" }, profile.EnabledInOrder());
		}

		[Fact]
		public void ReconcileKeepsTheOrderOfTheEntriesThatStay()
		{
			var profile = new Profile("Test", "Underground2");
			profile.Ensure("b").Enabled = true;
			profile.Ensure("a").Enabled = true;

			profile.Reconcile(new[] { "a", "b" });

			Assert.Equal(new[] { "b", "a" }, profile.EnabledInOrder());
		}

		[Fact]
		public void TheStoreReadsBackWhatItWrote()
		{
			var profile = new Profile("My Setup", "Underground2");
			profile.Ensure("one").Enabled = true;
			profile.Ensure("two").Enabled = false;
			profile.Find("one").Selections.Ensure("Install").Choose(0, "Enabled");

			this._store.Save(GameINT.Underground2, profile);
			Profile read = this._store.Find(GameINT.Underground2, "My Setup");

			Assert.NotNull(read);
			Assert.Equal("My Setup", read.Name);
			Assert.Equal(new[] { "one" }, read.EnabledInOrder());
			Assert.Equal("Enabled", read.Find("one").Selections.For("Install").Answer(0));
		}

		[Fact]
		public void TheListHoldsEveryProfileOfOneGame()
		{
			this._store.Ensure(GameINT.Underground2, "One");
			this._store.Ensure(GameINT.Underground2, "Two");

			Assert.Equal(2, this._store.List(GameINT.Underground2).Count);
			Assert.Empty(this._store.List(GameINT.MostWanted));
		}

		[Fact]
		public void RenameKeepsTheEntriesAndDropsTheOldFile()
		{
			Profile profile = this._store.Ensure(GameINT.Underground2, "Before");
			profile.Ensure("one").Enabled = true;
			this._store.Save(GameINT.Underground2, profile);

			this._store.Rename(GameINT.Underground2, "Before", "After");

			Assert.Null(this._store.Find(GameINT.Underground2, "Before"));
			Assert.Equal(new[] { "one" }, this._store.Find(GameINT.Underground2, "After").EnabledInOrder());
		}

		[Fact]
		public void RenameToAnExistingNameFails()
		{
			this._store.Ensure(GameINT.Underground2, "One");
			this._store.Ensure(GameINT.Underground2, "Two");

			Assert.Throws<ArgumentException>(() => this._store.Rename(GameINT.Underground2, "One", "Two"));
		}

		[Theory]
		[InlineData("My Setup", "My Setup")]
		[InlineData("bad/name", "bad name")]
		[InlineData("", ProfileStore.DefaultProfileName)]
		public void TheFileNameKeepsOnlySafeCharacters(string name, string expected)
		{
			Assert.Equal(expected, ProfileStore.FileName(name));
		}
	}
}
