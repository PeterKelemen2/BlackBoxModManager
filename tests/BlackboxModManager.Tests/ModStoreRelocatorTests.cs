using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the move of the mod store.
	///
	/// <b>This code must never lose a mod.</b> The store is the library of the user, and a
	/// failed move has to leave every mod readable at one of the two places.
	/// </summary>
	public class ModStoreRelocatorTests : IDisposable
	{
		private readonly TempDirectory _temp = new TempDirectory();
		private readonly ModStore _store;
		private readonly ModImporter _importer;

		public ModStoreRelocatorTests()
		{
			this._store = new ModStore(Path.Combine(this._temp.Path, "mods"));
			this._importer = new ModImporter(this._store, Path.Combine(this._temp.Path, "import"));
		}

		public void Dispose() => this._temp.Dispose();

		// ------------------------------------------------------------------ the setting

		[Fact]
		public void AnEmptyOverrideResolvesToTheDefaultPlace()
		{
			var settings = new Settings();

			Assert.True(settings.ModStoreIsDefault);
			Assert.Equal(AppPaths.ModsDirectory, settings.ResolveModStore());
		}

		[Fact]
		public void AnOverrideResolvesToAFullPath()
		{
			var settings = new Settings { ModStoreOverride = Path.Combine(this._temp.Path, "elsewhere") + Path.DirectorySeparatorChar };

			Assert.False(settings.ModStoreIsDefault);
			Assert.Equal(Path.Combine(this._temp.Path, "elsewhere"), settings.ResolveModStore());
		}

		[Fact]
		public void AWhitespaceOverrideReadsAsNoOverride()
		{
			// A user who clears the field must not end up with a store at the working directory.
			Assert.Equal(AppPaths.ModsDirectory, new Settings { ModStoreOverride = "   " }.ResolveModStore());
		}

		// ------------------------------------------------------------------ the check

		[Fact]
		public void AWritableDirectoryPassesTheCheck()
		{
			Assert.Equal(String.Empty, ModStoreRelocator.Problem(Path.Combine(this._temp.Path, "target")));
		}

		[Fact]
		public void ADirectoryInsideTheGameInstallFailsTheCheck()
		{
			// A game reinstall deletes its own directory, and that would take the library of the
			// user with it.
			string game = Path.Combine(this._temp.Path, "game");
			Directory.CreateDirectory(game);

			string problem = ModStoreRelocator.Problem(Path.Combine(game, "mods"), game);

			Assert.Contains("inside the game install", problem, StringComparison.Ordinal);
		}

		[Fact]
		public void ADirectoryInsideAWorkspaceFailsTheCheck()
		{
			// A deploy deletes the staging directory and rebuilds it.
			string problem = ModStoreRelocator.Problem(
				Path.Combine(this._temp.Path, "Game.blackbox", "staging", "mods"));

			Assert.Contains("workspace", problem, StringComparison.Ordinal);
		}

		[Fact]
		public void AnEmptyPathFailsTheCheck()
		{
			Assert.NotEqual(String.Empty, ModStoreRelocator.Problem("  "));
		}

		// ------------------------------------------------------------------ the move

		[Fact]
		public void AMoveCarriesEveryModAndItsContent()
		{
			InstalledMod first = this.Import("Alpha", ("scripts/a.asi", "plugin a"));
			InstalledMod second = this.Import("Beta", ("readme.txt", "notes"));

			string target = Path.Combine(this._temp.Path, "moved");

			ModStoreMoveReport report = ModStoreRelocator.Move(this._store, target);

			Assert.Equal(2, report.Moved.Count);
			Assert.Empty(report.Kept);

			var moved = new ModStore(target);

			Assert.Equal(2, moved.List().Count);
			Assert.Equal("plugin a",
				File.ReadAllText(Path.Combine(moved.Find(first.Id).ContentRoot, "scripts", "a.asi")));
			Assert.Equal("notes",
				File.ReadAllText(Path.Combine(moved.Find(second.Id).ContentRoot, "readme.txt")));

			// Nothing stays behind at the old place.
			Assert.Empty(this._store.List());
		}

		[Fact]
		public void AProfileStillFindsItsModsAfterAMove()
		{
			// The profile names a mod by its identifier and never by a path, so a move must not
			// touch it.
			InstalledMod mod = this.Import("Alpha", ("scripts/a.asi", "plugin a"));

			var profile = new Profile("Test", "Underground2");
			profile.Ensure(mod.Id).Enabled = true;

			string target = Path.Combine(this._temp.Path, "moved");

			ModStoreRelocator.Move(this._store, target);

			var moved = new ModStore(target);

			Assert.False(profile.Reconcile(Ids(moved.List())),
				"The profile changed, so the move did not keep the identifier of the mod.");
			Assert.NotNull(moved.Find(mod.Id));
		}

		[Fact]
		public void AModThatTheTargetAlreadyHoldsStaysBehindAndIsReported()
		{
			InstalledMod mod = this.Import("Alpha", ("scripts/a.asi", "plugin a"));

			string target = Path.Combine(this._temp.Path, "moved");

			// A directory of the same name at the target. Overwriting it would destroy whatever
			// it holds.
			Directory.CreateDirectory(Path.Combine(target, Path.GetFileName(mod.Root)));

			ModStoreMoveReport report = ModStoreRelocator.Move(this._store, target);

			Assert.Empty(report.Moved);
			Assert.Single(report.Kept);

			// The mod is still readable where it was.
			Assert.NotNull(this._store.Find(mod.Id));
		}

		[Fact]
		public void AMoveToTheSameDirectoryIsRefused()
		{
			this.Import("Alpha", ("scripts/a.asi", "plugin a"));

			ModStoreMoveException error = Assert.Throws<ModStoreMoveException>(
				() => ModStoreRelocator.Move(this._store, this._store.Root));

			Assert.Contains("already sits at", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void AMoveIntoTheStoreItselfIsRefused()
		{
			this.Import("Alpha", ("scripts/a.asi", "plugin a"));

			ModStoreMoveException error = Assert.Throws<ModStoreMoveException>(
				() => ModStoreRelocator.Move(this._store, Path.Combine(this._store.Root, "inner")));

			Assert.Contains("overlap", error.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void AMoveIntoTheGameInstallIsRefused()
		{
			string game = Path.Combine(this._temp.Path, "game");
			Directory.CreateDirectory(game);

			this.Import("Alpha", ("scripts/a.asi", "plugin a"));

			Assert.Throws<ModStoreMoveException>(
				() => ModStoreRelocator.Move(this._store, Path.Combine(game, "mods"), game));

			// The refusal happens before anything moves.
			Assert.Single(this._store.List());
		}

		[Fact]
		public void AnEmptyStoreMovesWithNoComplaint()
		{
			ModStoreMoveReport report = ModStoreRelocator.Move(
				this._store, Path.Combine(this._temp.Path, "moved"));

			Assert.Empty(report.Moved);
			Assert.Empty(report.Kept);
		}

		[Fact]
		public void TheReportNamesBothDirectories()
		{
			this.Import("Alpha", ("scripts/a.asi", "plugin a"));

			string target = Path.Combine(this._temp.Path, "moved");

			ModStoreMoveReport report = ModStoreRelocator.Move(this._store, target);

			Assert.Equal(this._store.Root, report.From);
			Assert.Equal(target, report.To);
			Assert.Contains(target, report.Summary(), StringComparison.Ordinal);
		}

		// ------------------------------------------------------------------ helpers

		private InstalledMod Import(string name, params (string Path, string Content)[] files)
		{
			string root = Path.Combine(this._temp.Path, "source", name);

			foreach ((string path, string content) in files)
			{
				string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllText(full, content);
			}

			return this._importer.Import(root, GameINT.Underground2, name).Mod;
		}

		private static IEnumerable<string> Ids(IReadOnlyList<InstalledMod> mods) => mods.Select(m => m.Id);
	}
}
