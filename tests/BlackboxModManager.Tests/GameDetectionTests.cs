using System.IO;
using BlackboxModManager.Core.Games;
using Nikki.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the game validator of step 5.2. The locator itself needs a real machine, so
	/// these tests cover the part that decides whether a directory is an install.
	/// </summary>
	public class GameDetectionTests
	{
		[Fact]
		public void AGoodDirectoryPassesEveryCheck()
		{
			using var game = new FakeGame();

			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, game.Root);

			Assert.Equal(GameInstallCheck.Ok, status.Check);
			Assert.True(status.IsUsable);
			Assert.Equal(game.Root, status.Install.Root);
		}

		[Fact]
		public void AMissingDirectoryNamesTheCheck()
		{
			GameInstallStatus status = GameInstallValidator.Validate(
				GameINT.Underground2, Path.Combine(Path.GetTempPath(), "no-such-game-directory"));

			Assert.Equal(GameInstallCheck.DirectoryMissing, status.Check);
			Assert.Null(status.Install);
		}

		[Fact]
		public void AnEmptyPathNamesTheCheck()
		{
			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, null);

			Assert.Equal(GameInstallCheck.NoPath, status.Check);
		}

		[Fact]
		public void ADirectoryWithNoExecutableIsNotAnInstall()
		{
			using var temp = new TempDirectory();

			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, temp.Path);

			Assert.Equal(GameInstallCheck.ExecutableMissing, status.Check);
		}

		[Fact]
		public void AnInstallWithNoContentNamesTheFilesThatAreAbsent()
		{
			using var temp = new TempDirectory();
			File.WriteAllText(Path.Combine(temp.Path, "SPEED2.EXE"), "the game");

			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Underground2, temp.Path);

			Assert.Equal(GameInstallCheck.ContentMissing, status.Check);
			Assert.Contains("GLOBAL/GLOBALA.BUN", status.MissingContent);
			Assert.Contains("CARS/", status.MissingContent);
		}

		/// <summary>
		/// The manifests write GLOBAL\GLOBALB.LZC and the disk holds GLOBAL/GlobalB.lzc.
		/// The validator must find the file on a case-sensitive filesystem too.
		/// </summary>
		[Fact]
		public void TheLookupIgnoresTheSeparatorAndTheLetterCase()
		{
			using var game = new FakeGame();

			GameDefinition definition = GameCatalog.Demand(GameINT.Underground2);

			Assert.Contains("GLOBAL/GlobalB.lzc", definition.MarkerFiles);
			Assert.True(GameInstallValidator.Validate(GameINT.Underground2, game.Root).IsUsable);
		}

		[Fact]
		public void AGameWithNoDefinitionReportsUnknown()
		{
			GameInstallStatus status = GameInstallValidator.Validate(GameINT.Carbon, Path.GetTempPath());

			Assert.Equal(GameInstallCheck.UnknownGame, status.Check);
		}
	}
}
