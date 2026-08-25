using System;
using System.IO;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Staging;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the swap that puts a prepared directory into the place of the game directory.
	///
	/// The rule that these tests defend: <b>a failed swap leaves every file of the game where
	/// it was.</b> README.md states that promise, and the first version of GameSwap broke it.
	///
	/// These tests read paths and text files, so they run on native Linux with no game.
	/// </summary>
	public class GameSwapTests : IDisposable
	{
		private readonly FakeGame _game;

		public GameSwapTests()
		{
			this._game = new FakeGame();
		}

		public void Dispose()
		{
			this._game.Dispose();
		}

		/// <summary>Builds a directory that stands in for a finished staging build.</summary>
		private string Prepared(string marker)
		{
			string path = Path.Combine(this._game.Parent, "staging-stand-in");

			Directory.CreateDirectory(path);
			File.WriteAllText(Path.Combine(path, "SPEED2.EXE"), marker);

			return path;
		}

		[Fact]
		public void TheSwapPutsThePreparedDirectoryInPlace()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			string prepared = this.Prepared("the deployed game");

			GameSwap.Swap(workspace, prepared);

			Assert.Equal("the deployed game", this._game.Read("SPEED2.EXE"));

			// The swap removes what it set aside once it has finished.
			Assert.False(Directory.Exists(workspace.PreviousDirectory));

			// The prepared directory moved. It did not stay behind as a copy.
			Assert.False(Directory.Exists(prepared));
		}

		/// <summary>
		/// <b>The regression test of the defect that a real deploy hit.</b>
		///
		/// A game under C:\Program Files (x86) gave a standard user no right to delete. The
		/// rename of the game directory failed, and the old code answered that by copying the
		/// game and then deleting the original one file at a time. It stopped on the first file
		/// and left the install half removed, with no message that said so.
		///
		/// This test reproduces a first move that cannot happen. It uses an absent parent for
		/// the target, because that fails on every platform. The property under test is the
		/// same for any cause: <b>the game directory keeps every file.</b>
		/// </summary>
		[Fact]
		public void AFirstMoveThatFailsDeletesNothing()
		{
			GameInstall install = this._game.Install();

			// The workspace directory is never created, so the parent of PreviousDirectory does
			// not exist and the first move cannot land.
			var workspace = new GameWorkspace(install);

			Assert.False(Directory.Exists(workspace.Root));

			string prepared = this.Prepared("the deployed game");

			SwapException error = Assert.Throws<SwapException>(
				() => GameSwap.Swap(workspace, prepared));

			// The message has to name the directory and say that nothing changed.
			Assert.Contains(install.Root, error.Message);
			Assert.Contains("did not change", error.Message);

			// Every file of the game survives, including the read-only one.
			Assert.True(this._game.Has("SPEED2.EXE"));
			Assert.True(this._game.Has("server.dll"));
			Assert.True(this._game.Has("GLOBAL/GLOBALA.BUN"));
			Assert.True(this._game.Has("GLOBAL/GlobalB.lzc"));
			Assert.True(this._game.Has("CARS/car.bin"));
			Assert.True(this._game.Has("TRACKS/track.bin"));
			Assert.True(this._game.Has("FRONTEND/front.bin"));

			// The game is still vanilla. The prepared content never reached it.
			Assert.Equal("the game", this._game.Read("SPEED2.EXE"));

			// The swap left no half-built copy behind either.
			Assert.False(Directory.Exists(workspace.PreviousDirectory));
		}

		[Fact]
		public void AnAbsentPreparedDirectoryChangesNothing()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			string absent = Path.Combine(this._game.Parent, "not-there");

			SwapException error = Assert.Throws<SwapException>(
				() => GameSwap.Swap(workspace, absent));

			Assert.Contains("did not change", error.Message);
			Assert.Equal("the game", this._game.Read("SPEED2.EXE"));
		}

		/// <summary>
		/// A prepared directory inside the game directory would make the swap delete the game.
		/// The guard runs before the first move.
		/// </summary>
		[Fact]
		public void APreparedDirectoryInsideTheGameIsRefused()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			string inside = Path.Combine(install.Root, "staging-by-mistake");
			Directory.CreateDirectory(inside);

			Assert.Throws<SwapException>(() => GameSwap.Swap(workspace, inside));

			Assert.True(this._game.Has("SPEED2.EXE"));
			Assert.Equal("the game", this._game.Read("SPEED2.EXE"));
		}

		/// <summary>
		/// The log names each move. A user who reports a failed deploy pastes that log, and the
		/// swap is where a deploy touches the install of the user.
		/// </summary>
		[Fact]
		public void TheSwapNamesEachMoveInTheLog()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			var lines = new System.Collections.Generic.List<string>();

			GameSwap.Swap(workspace, this.Prepared("the deployed game"), lines.Add);

			Assert.Contains(lines, line => line.Contains(workspace.PreviousDirectory));
			Assert.Contains(lines, line => line.Contains("Move"));
		}
	}
}
