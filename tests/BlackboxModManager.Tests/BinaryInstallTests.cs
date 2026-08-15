using System;
using System.IO;
using BlackboxModManager.Core;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the validator, the settings store, and the hash list paths of step 2.
	///
	/// Each test builds a fake Binary install in a temporary directory. No test reads the
	/// real install, so these run on any machine.
	/// </summary>
	public class BinaryInstallTests : IDisposable
	{
		private readonly string _root;

		public BinaryInstallTests()
		{
			this._root = Path.Combine(Path.GetTempPath(), $"binary-test-{Guid.NewGuid():N}");
			Directory.CreateDirectory(this._root);
		}

		public void Dispose()
		{
			if (Directory.Exists(this._root)) Directory.Delete(this._root, true);
		}

		// ------------------------------------------------------------------ validator

		[Fact]
		public void ANullPathReportsNoPath()
		{
			BinaryInstallStatus status = BinaryInstallValidator.Validate(null);

			Assert.Equal(BinaryInstallCheck.NoPath, status.Check);
			Assert.False(status.IsUsable);
			Assert.Null(status.Install);
			Assert.NotEmpty(status.Message);
		}

		[Fact]
		public void AMissingDirectoryReportsDirectoryMissing()
		{
			string missing = Path.Combine(this._root, "not-here");

			BinaryInstallStatus status = BinaryInstallValidator.Validate(missing);

			Assert.Equal(BinaryInstallCheck.DirectoryMissing, status.Check);
			Assert.Contains(missing, status.Message);
		}

		[Fact]
		public void ADirectoryWithoutTheExecutableReportsExecutableMissing()
		{
			BinaryInstallStatus status = BinaryInstallValidator.Validate(this._root);

			Assert.Equal(BinaryInstallCheck.ExecutableMissing, status.Check);
		}

		[Fact]
		public void AnInstallWithoutMainKeysReportsTheDirectory()
		{
			File.WriteAllText(Path.Combine(this._root, BinaryInstallValidator.ExecutableName), String.Empty);

			BinaryInstallStatus status = BinaryInstallValidator.Validate(this._root);

			Assert.Equal(BinaryInstallCheck.MainKeysDirectoryMissing, status.Check);
		}

		[Fact]
		public void AMissingHashListIsNamedInTheResult()
		{
			BuildInstall(skip: GameINT.Carbon);

			BinaryInstallStatus status = BinaryInstallValidator.Validate(this._root);

			Assert.Equal(BinaryInstallCheck.HashListMissing, status.Check);
			Assert.Equal(new[] { GameINT.Carbon }, status.MissingHashLists);
			Assert.Contains("carbon.txt", status.Message);
		}

		[Fact]
		public void AGoodInstallPasses()
		{
			BuildInstall();

			BinaryInstallStatus status = BinaryInstallValidator.Validate(this._root);

			Assert.Equal(BinaryInstallCheck.Ok, status.Check);
			Assert.True(status.IsUsable);
			Assert.Empty(status.MissingHashLists);
			Assert.NotNull(status.Install);
			Assert.Equal(status.Root, status.Install.Root);
		}

		[Fact]
		public void AnUnreadableVersionIsAWarningAndNotAFailure()
		{
			// The fake install holds an empty Binary.exe and no Binary.dll, so no version
			// can be read. The install must still pass, with a warning.
			BuildInstall();

			BinaryInstallStatus status = BinaryInstallValidator.Validate(this._root);

			Assert.True(status.IsUsable);
			Assert.Null(status.Version);
			Assert.NotEmpty(status.VersionWarning);
		}

		[Fact]
		public void ATrailingSeparatorDoesNotChangeTheResult()
		{
			BuildInstall();

			BinaryInstallStatus status = BinaryInstallValidator.Validate(this._root + Path.DirectorySeparatorChar);

			Assert.True(status.IsUsable);
			Assert.Equal(Path.TrimEndingDirectorySeparator(this._root), status.Root);
		}

		// ------------------------------------------------------------------ hash list paths

		[Fact]
		public void EachGameMapsToTheLowerCaseFileNameOfBinary()
		{
			Assert.Equal("underground1.txt", HashListPaths.FileName(GameINT.Underground1));
			Assert.Equal("underground2.txt", HashListPaths.FileName(GameINT.Underground2));
			Assert.Equal("mostwanted.txt", HashListPaths.FileName(GameINT.MostWanted));
			Assert.Equal("carbon.txt", HashListPaths.FileName(GameINT.Carbon));
			Assert.Equal("prostreet.txt", HashListPaths.FileName(GameINT.Prostreet));
			Assert.Equal("undercover.txt", HashListPaths.FileName(GameINT.Undercover));
		}

		[Fact]
		public void GameIntNoneHasNoHashList()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => HashListPaths.FileName(GameINT.None));
		}

		[Fact]
		public void TheCustomHashListStaysUnderOurOwnData()
		{
			// Defect 7: Save overwrites this file and creates its directory. It must never
			// point into the Binary install.
			string custom = HashListPaths.CustomHashList(GameINT.Underground2);

			Assert.StartsWith(AppPaths.Root, custom, StringComparison.Ordinal);
			Assert.EndsWith("underground2.txt", custom, StringComparison.Ordinal);
		}

		// ------------------------------------------------------------------ settings

		[Fact]
		public void TheStoreReadsBackWhatItWrote()
		{
			string file = Path.Combine(this._root, "settings.json");

			SettingsStore.Save(file, new Settings { BinaryInstallDirectory = @"C:\Games\Binary" });

			Assert.Equal(@"C:\Games\Binary", SettingsStore.Load(file).BinaryInstallDirectory);
		}

		/// <summary>
		/// FullVerify is the one deploy option that the config window of step 12 holds. The
		/// window writes it on every change, so the file has to carry it back.
		/// </summary>
		[Fact]
		public void TheStoreReadsBackTheFullVerifyAnswer()
		{
			string file = Path.Combine(this._root, "verify.json");

			SettingsStore.Save(file, new Settings { FullVerify = true });

			Assert.True(SettingsStore.Load(file).FullVerify);
		}

		/// <summary>
		/// A settings file that step 9 wrote holds no FullVerify key. It must read as false,
		/// which is the value that the window used before step 12 moved the check box.
		/// </summary>
		[Fact]
		public void ASettingsFileWithNoFullVerifyKeyReadsAsFalse()
		{
			string file = Path.Combine(this._root, "older.json");
			File.WriteAllText(file, "{ \"Version\": 2 }");

			Assert.False(SettingsStore.Load(file).FullVerify);
		}

		[Fact]
		public void AMissingSettingsFileGivesFreshSettings()
		{
			Settings settings = SettingsStore.Load(Path.Combine(this._root, "absent.json"));

			Assert.NotNull(settings);
			Assert.Null(settings.BinaryInstallDirectory);
		}

		[Fact]
		public void ADamagedSettingsFileGivesFreshSettingsAndDoesNotThrow()
		{
			string file = Path.Combine(this._root, "damaged.json");
			File.WriteAllText(file, "{ this is not json");

			Settings settings = SettingsStore.Load(file);

			Assert.NotNull(settings);
			Assert.Null(settings.BinaryInstallDirectory);
		}

		// ------------------------------------------------------------------ service

		[Fact]
		public void TheServiceStoresOnlyAPathThatPasses()
		{
			string file = Path.Combine(this._root, "settings.json");
			var service = new BinaryInstallService(file);

			BinaryInstallStatus rejected = service.Store(Path.Combine(this._root, "not-here"));

			Assert.False(rejected.IsUsable);
			Assert.Null(SettingsStore.Load(file).BinaryInstallDirectory);
		}

		[Fact]
		public void TheServiceReadsBackAStoredPath()
		{
			BuildInstall();

			string file = Path.Combine(this._root, "settings.json");
			var service = new BinaryInstallService(file);

			Assert.True(service.Store(this._root).IsUsable);

			BinaryInstallResolution resolution = service.Resolve();

			Assert.Equal(BinaryInstallSource.Settings, resolution.Source);
			Assert.True(resolution.IsUsable);
			Assert.Equal(Path.TrimEndingDirectorySeparator(this._root), resolution.Install.Root);
		}

		[Fact]
		public void AnOverrideWinsOverTheStoredPath()
		{
			BuildInstall();

			string file = Path.Combine(this._root, "settings.json");
			var service = new BinaryInstallService(file);
			service.Store(this._root);

			BinaryInstallResolution resolution = service.Resolve(Path.Combine(this._root, "not-here"));

			Assert.Equal(BinaryInstallSource.Override, resolution.Source);
			Assert.False(resolution.IsUsable);
		}

		[Fact]
		public void ForgetClearsTheStoredPath()
		{
			BuildInstall();

			string file = Path.Combine(this._root, "settings.json");
			var service = new BinaryInstallService(file);
			service.Store(this._root);

			service.Forget();

			Assert.Equal(BinaryInstallSource.None, service.Resolve().Source);
		}

		// ------------------------------------------------------------------ gate

		[Fact]
		public void TheStaticsRefuseToChangeWithoutTheGate()
		{
			// Defect 8: the hash list properties are process-global.
			Assert.Throws<InvalidOperationException>(
				() => ProfileHashLists.Apply("main.txt", "custom.txt", GameINT.Underground2));
		}

		[Fact]
		public void TheGateReportsThatTheCallingThreadHoldsIt()
		{
			Assert.False(LibraryGate.IsHeldByCurrentThread);

			using (LibraryGate.Enter())
			{
				Assert.True(LibraryGate.IsHeldByCurrentThread);
			}

			Assert.False(LibraryGate.IsHeldByCurrentThread);
		}

		[Fact]
		public void AMissingMainHashListFailsBeforeAnyContainerWrites()
		{
			using (LibraryGate.Enter())
			{
				Assert.Throws<FileNotFoundException>(
					() => ProfileHashLists.Apply(Path.Combine(this._root, "absent.txt"),
						Path.Combine(this._root, "out.txt"), GameINT.Underground2));
			}
		}

		[Fact]
		public void AnEmptyCustomHashListFailsAtTheAssignmentAndNotInsideSave()
		{
			BuildInstall();

			using (LibraryGate.Enter())
			{
				Assert.Throws<ArgumentException>(
					() => ProfileHashLists.Apply(HashListPaths.MainHashList(this._root, GameINT.Underground2),
						null, GameINT.Underground2));
			}
		}

		[Fact]
		public void ApplySetsThePairOnTheProfileClassOfTheGame()
		{
			BuildInstall();

			string main = HashListPaths.MainHashList(this._root, GameINT.Underground2);
			string custom = Path.Combine(this._root, "out", "underground2.txt");

			using (LibraryGate.Enter())
			{
				ProfileHashLists.Apply(main, custom, GameINT.Underground2);

				(string Main, string Custom) live = ProfileHashLists.Current(GameINT.Underground2);

				Assert.Equal(main, live.Main);
				Assert.Equal(custom, live.Custom);

				// Apply creates the output directory, so a permission problem surfaces here
				// and not after the containers already wrote.
				Assert.True(Directory.Exists(Path.GetDirectoryName(custom)));
			}
		}

		// ------------------------------------------------------------------ helpers

		/// <summary>
		/// Builds a directory that looks like a Binary install. Pass a game in skip to leave
		/// its hash list out.
		/// </summary>
		private void BuildInstall(GameINT skip = GameINT.None)
		{
			File.WriteAllText(Path.Combine(this._root, BinaryInstallValidator.ExecutableName), String.Empty);

			string mainKeys = HashListPaths.MainKeysDirectory(this._root);
			Directory.CreateDirectory(mainKeys);

			foreach (GameINT game in HashListPaths.SupportedGames)
			{
				if (game == skip) continue;

				File.WriteAllText(Path.Combine(mainKeys, HashListPaths.FileName(game)), "01_WHEEL_MADCATZ\n");
			}
		}
	}
}
