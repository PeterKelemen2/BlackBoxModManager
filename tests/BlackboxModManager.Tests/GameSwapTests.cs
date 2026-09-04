using System;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Staging;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the swap that puts a prepared directory in the place of the game directory.
	///
	/// <b>This is the one operation that changes the game install.</b> The first move takes
	/// the live game directory aside. A failure of that move must leave the game directory
	/// where it is. An earlier version read every <c>IOException</c> as a move across a
	/// volume, copied the tree, and then deleted the source. A locked file raises that same
	/// exception, so the recovery path deleted the live game directory. See step 19, Part 1.
	/// </summary>
	public class GameSwapTests
	{
		/// <summary>Builds a directory that the swap can move into the place of the game.</summary>
		private static string Prepared(GameWorkspace workspace, string content)
		{
			string prepared = Path.Combine(workspace.Root, "staging");

			Directory.CreateDirectory(prepared);
			File.WriteAllText(Path.Combine(prepared, "SPEED2.EXE"), content);

			return prepared;
		}

		[Fact]
		public void ASwapOnOneVolumeRenamesTheDirectory()
		{
			using var fake = new FakeGame();

			GameInstall install = fake.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			string prepared = Prepared(workspace, "the deployed game");

			GameSwap.Swap(workspace, prepared);

			Assert.Equal("the deployed game", fake.Read("SPEED2.EXE"));
			Assert.False(Directory.Exists(prepared));
			Assert.False(Directory.Exists(workspace.PreviousDirectory));
		}

		/// <summary>
		/// A locked file stops the first move. The game directory must survive it.
		/// </summary>
		[WindowsFact]
		public void ALockedFileLeavesTheGameDirectoryInPlace()
		{
			using var fake = new FakeGame();

			GameInstall install = fake.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			string prepared = Prepared(workspace, "the deployed game");

			// The game holds its own executable open while it runs. FileShare.None is what
			// Windows gives that handle.
			using (new FileStream(fake.FullPath("SPEED2.EXE"), FileMode.Open, FileAccess.Read,
				FileShare.None))
			{
				SwapException error = Assert.Throws<SwapException>(
					() => GameSwap.Swap(workspace, prepared));

				Assert.Contains(install.Root, error.Message);
				Assert.Contains("The game directory did not change.", error.Message);
			}

			// The live directory kept its name and its content.
			Assert.True(Directory.Exists(install.Root));
			Assert.Equal("the game", fake.Read("SPEED2.EXE"));
			Assert.Equal("container b", fake.Read("GLOBAL/GlobalB.lzc"));

			// The prepared directory is still there, so the next deploy has something to
			// swap in.
			Assert.True(Directory.Exists(prepared));
		}

		[Fact]
		public void TwoPathsUnderOneRootShareAVolume()
		{
			using var temp = new TempDirectory();

			string left = Path.Combine(temp.Path, "game");
			string right = Path.Combine(temp.Path, "game.blackbox");

			Assert.True(FileTree.SameVolume(left, right));
		}

		/// <summary>
		/// The workspace and the swap ask one question through one method. Two copies of the
		/// rule would let a swap copy where the workspace expects a rename.
		/// </summary>
		[Fact]
		public void TheWorkspaceReadsTheSharedVolumeRule()
		{
			using var fake = new FakeGame();

			GameInstall install = fake.Install();
			var workspace = new GameWorkspace(install);

			Assert.Equal(FileTree.SameVolume(workspace.Root, install.Root),
				workspace.SharesVolumeWithGame());
		}
	}
}
