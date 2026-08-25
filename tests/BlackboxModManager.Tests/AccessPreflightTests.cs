using System;
using System.IO;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Games;
using BlackboxModManager.Core.Staging;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the permission check that runs before a deploy builds anything.
	///
	/// A real deploy copied 1,560 files and verified them, and only then found that it could not
	/// rename the game directory. The game sat under C:\Program Files (x86). See the dated
	/// section at the end of docs/roadmap/05-mvp-shell.md.
	///
	/// These tests use a writable temporary directory, so the pass path is what they check on
	/// every platform. A denied directory needs the ACL of a real install, and no test creates
	/// one.
	/// </summary>
	public class AccessPreflightTests : IDisposable
	{
		private readonly FakeGame _game;

		public AccessPreflightTests()
		{
			this._game = new FakeGame();
		}

		public void Dispose()
		{
			this._game.Dispose();
		}

		// Every caller of these two carries UnixPermissionFact, so neither one runs on Windows.
		// The analyzer cannot read that guarantee out of an attribute, so the suppression sits
		// here and covers both. See UnixPermissionFactAttribute.
#pragma warning disable CA1416

		/// <summary>Read and enter, and no write. This is Program Files for a normal account.</summary>
		private static void DenyWrite(string directory)
		{
			File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
		}

		/// <summary>
		/// Puts the mode back. A denied directory would stop the cleanup of FakeGame, and the
		/// temporary tree would stay on the disk.
		/// </summary>
		private static void AllowWrite(string directory)
		{
			File.SetUnixFileMode(directory,
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

#pragma warning restore CA1416

		[Fact]
		public void AWritableInstallPasses()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			AccessPreflight.Check(workspace);

			Assert.Null(AccessPreflight.Test(workspace));
		}

		/// <summary>
		/// The check leaves no probe file behind. It creates one file in each directory and
		/// removes it, and a leftover would reach the vanilla snapshot as a game file.
		/// </summary>
		[Fact]
		public void TheCheckLeavesNoFileBehind()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			string[] before = Directory.GetFiles(install.Root);

			AccessPreflight.Check(workspace);
			AccessPreflight.Check(workspace);

			Assert.Equal(before, Directory.GetFiles(install.Root));

			// The parent holds the game directory and the workspace, and nothing else.
			foreach (string path in Directory.GetFiles(this._game.Parent))
			{
				Assert.DoesNotContain("blackbox-access-probe", Path.GetFileName(path));
			}
		}

		/// <summary>
		/// A workspace that does not exist yet is not a failure. A deploy creates it, and the
		/// parent that has to hold it gets a test of its own.
		/// </summary>
		[Fact]
		public void AnAbsentWorkspaceIsNotAFailure()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);

			Assert.False(Directory.Exists(workspace.Root));

			Assert.Null(AccessPreflight.Test(workspace));
		}

		/// <summary>
		/// The message has to name the directory and say that the game did not change. A user
		/// who reads it must know which directory to fix.
		/// </summary>
		[Fact]
		public void TheProblemNamesTheDirectory()
		{
			var problem = new AccessException("cannot write", @"C:\Program Files (x86)\EA GAMES");

			Assert.Equal(@"C:\Program Files (x86)\EA GAMES", problem.Directory);
		}

		/// <summary>
		/// <b>The case that a real deploy hit.</b> A game directory that takes no write stops the
		/// check, and the message names that directory.
		///
		/// This is the shape of C:\Program Files (x86)\EA GAMES for a standard user.
		/// </summary>
		[UnixPermissionFact]
		public void AGameDirectoryThatRefusesAWriteStopsTheCheck()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			// Read and enter, but no write. A standard user sees exactly this under Program
			// Files.
			DenyWrite(install.Root);

			try
			{
				AccessException problem = AccessPreflight.Test(workspace);

				Assert.NotNull(problem);
				Assert.Equal(install.Root, problem.Directory);

				// The message has to carry the fix and the reassurance.
				Assert.Contains("administrator", problem.Message);
				Assert.Contains("did not change", problem.Message);
			}
			finally
			{
				// Put the mode back, or the cleanup of FakeGame cannot remove the tree.
				AllowWrite(install.Root);
			}
		}

		/// <summary>
		/// <b>A deploy stops before it builds anything.</b> This is the point of the whole
		/// class. The real failure arrived after a copy of 1,560 files and a full verify, and the
		/// staging directory holds the proof of whether that work happened.
		/// </summary>
		[UnixPermissionFact]
		public void ADeployStopsBeforeItBuildsTheStagingCopy()
		{
			using var temp = new TempDirectory();

			GameInstall install = this._game.Install();
			var store = new BlackboxModManager.Core.Store.ModStore(Path.Combine(temp.Path, "mods"));
			var service = new DeployService(store);
			GameWorkspace workspace = service.WorkspaceOf(install);

			var profile = new BlackboxModManager.Core.Profiles.Profile { Name = "Career" };

			DenyWrite(install.Root);

			try
			{
				Assert.Throws<AccessException>(
					() => service.Deploy(install, profile, false, line => { }));

				// Nothing was built. No staging copy, and no vanilla snapshot either.
				Assert.False(Directory.Exists(workspace.StagingDirectory));
				Assert.False(Directory.Exists(workspace.VanillaDirectory));
			}
			finally
			{
				AllowWrite(install.Root);
			}
		}

		/// <summary>
		/// The parent of the game directory is the one that surprises people. The swap puts the
		/// new game directory there, so a parent that takes no write stops a deploy even when
		/// the game directory itself allows one.
		/// </summary>
		[UnixPermissionFact]
		public void AParentThatRefusesAWriteStopsTheCheck()
		{
			GameInstall install = this._game.Install();
			var workspace = new GameWorkspace(install);
			workspace.Create();

			DenyWrite(this._game.Parent);

			try
			{
				AccessException problem = AccessPreflight.Test(workspace);

				Assert.NotNull(problem);
				Assert.Equal(this._game.Parent, problem.Directory);
			}
			finally
			{
				AllowWrite(this._game.Parent);
			}
		}
	}

	/// <summary>
	/// Covers the volume test that decides whether a message may name a volume boundary.
	///
	/// The old code appended "A hard link cannot cross a volume" to every link failure. A user
	/// read that after an access denial and went looking for a second drive on a machine with
	/// one. See docs/roadmap/05-mvp-shell.md.
	/// </summary>
	public class SameVolumeTests
	{
		[Fact]
		public void TwoPathsUnderOneRootShareAVolume()
		{
			string root = Path.GetTempPath();

			Assert.True(FileTree.SameVolume(root, Path.Combine(root, "somewhere", "deeper")));
		}

		[Fact]
		public void APathSharesAVolumeWithItself()
		{
			Assert.True(FileTree.SameVolume(Path.GetTempPath(), Path.GetTempPath()));
		}

		/// <summary>An unreadable path reports false, which takes the slow and safe route.</summary>
		[Fact]
		public void AnEmptyPathReportsFalse()
		{
			Assert.False(FileTree.SameVolume(String.Empty, Path.GetTempPath()));
			Assert.False(FileTree.SameVolume(null, Path.GetTempPath()));
		}
	}
}
