using System.IO;
using BlackboxModManager.Core;
using BlackboxModManager.Core.Games;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the merge that the settings file needs.
	///
	/// More than one object writes this file. The window writes the last game, the active
	/// profile, and the two path overrides. <c>GameInstallService</c> and
	/// <c>BinaryInstallService</c> write the install directories. Every writer has to keep
	/// the keys of every other writer.
	/// </summary>
	public class SettingsStoreTests
	{
		[Fact]
		public void UpdateKeepsAKeyThatAnotherWriterAddedAfterTheRead()
		{
			using var temp = new TempDirectory();
			string file = temp.File("settings.json");

			// The window reads the file at start.
			Settings atStart = SettingsStore.Load(file);
			Assert.Empty(atStart.GameDirectories);

			// Another writer stores a game directory.
			SettingsStore.Update(file, settings => settings.GameDirectories["MostWanted"] = @"C:\Games\MW");

			// The window then saves one of its own keys.
			SettingsStore.Update(file, settings => settings.LastGame = "Underground2");

			Settings onDisk = SettingsStore.Load(file);

			Assert.Equal("Underground2", onDisk.LastGame);
			Assert.Equal(@"C:\Games\MW", onDisk.GameDirectories["MostWanted"]);
		}

		/// <summary>
		/// The defect that this test locks down. A save of a copy from an earlier read deletes
		/// the game directory. Keep this test as the record of why <c>Update</c> exists.
		/// </summary>
		[Fact]
		public void ASaveOfAnOldCopyDropsTheKeyThatAnotherWriterAdded()
		{
			using var temp = new TempDirectory();
			string file = temp.File("settings.json");

			Settings atStart = SettingsStore.Load(file);

			SettingsStore.Update(file, settings => settings.GameDirectories["MostWanted"] = @"C:\Games\MW");

			atStart.LastGame = "Underground2";
			SettingsStore.Save(file, atStart);

			Assert.Empty(SettingsStore.Load(file).GameDirectories);
		}

		/// <summary>
		/// The path that the user reported. Set the directory of one game, switch to another
		/// game, and switch back. The directory has to survive both switches.
		/// </summary>
		[Fact]
		public void AGameDirectorySurvivesEveryGameSwitch()
		{
			using var temp = new TempDirectory();
			using var game = new FakeGame();

			string file = temp.File("settings.json");
			var service = new GameInstallService(file);

			// The window reads the file at start, as the view model does.
			SettingsStore.Update(file, settings => settings.LastGame = GameINT.Underground2.ToString());

			// The user sets the directory of the game that the window shows.
			Assert.True(service.Store(GameINT.Underground2, game.Root).IsUsable);

			// The user switches to another game and back. Each switch writes the last game.
			SettingsStore.Update(file, settings => settings.LastGame = GameINT.MostWanted.ToString());
			SettingsStore.Update(file, settings => settings.LastGame = GameINT.Underground2.ToString());

			GameInstallResolution resolution = service.Resolve(GameINT.Underground2);

			Assert.Equal(GameInstallSource.Settings, resolution.Source);
			Assert.True(resolution.IsUsable);
			Assert.Equal(game.Root, resolution.Install.Root);
		}

		[Fact]
		public void UpdateWritesAFileThatDoesNotExistYet()
		{
			using var temp = new TempDirectory();
			string file = Path.Combine(temp.Path, "nested", "settings.json");

			SettingsStore.Update(file, settings => settings.LastGame = "Underground2");

			Assert.True(File.Exists(file));
			Assert.Equal("Underground2", SettingsStore.Load(file).LastGame);
		}

		[Fact]
		public void UpdateReturnsWhatItWrote()
		{
			using var temp = new TempDirectory();
			string file = temp.File("settings.json");

			Settings written = SettingsStore.Update(file, settings => settings.FullVerify = true);

			Assert.True(written.FullVerify);
			Assert.True(SettingsStore.Load(file).FullVerify);
		}

		/// <summary>
		/// A game directory and a Binary directory come from two different services. Neither
		/// one may drop the key of the other.
		/// </summary>
		[Fact]
		public void TheTwoInstallServicesDoNotDropEachOther()
		{
			using var temp = new TempDirectory();
			using var game = new FakeGame();

			string file = temp.File("settings.json");

			new GameInstallService(file).Store(GameINT.Underground2, game.Root);
			new BinaryInstallService(file).Store(temp.Path);

			Settings onDisk = SettingsStore.Load(file);

			Assert.True(onDisk.GameDirectories.ContainsKey(GameINT.Underground2.ToString()));
		}

		/// <summary>
		/// Forget removes one key and no other. A user who clears one game keeps every other
		/// game.
		/// </summary>
		[Fact]
		public void ForgetClearsOneGameAndKeepsTheRest()
		{
			using var temp = new TempDirectory();
			using var game = new FakeGame();

			string file = temp.File("settings.json");
			var service = new GameInstallService(file);

			service.Store(GameINT.Underground2, game.Root);
			SettingsStore.Update(file, settings => settings.LastGame = "Underground2");

			service.Forget(GameINT.Underground2);

			Settings onDisk = SettingsStore.Load(file);

			Assert.Empty(onDisk.GameDirectories);
			Assert.Equal("Underground2", onDisk.LastGame);
		}
	}
}
