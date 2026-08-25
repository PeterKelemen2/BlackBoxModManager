using System;
using System.IO;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// Thrown when a swap cannot finish. The message names the state that the game
	/// directory is in.
	/// </summary>
	public sealed class SwapException : Exception
	{
		public SwapException(string message, Exception inner = null) : base(message, inner) { }
	}

	/// <summary>
	/// Puts a prepared directory in the place of the game directory.
	///
	/// A swap is two moves. The live directory goes aside, and the prepared directory takes
	/// its name. On one volume a move is a rename, so the window in which no game directory
	/// exists is as short as the filesystem allows.
	///
	/// A move across a volume copies every byte. The workspace sits beside the game by
	/// default for this reason.
	/// </summary>
	public static class GameSwap
	{
		/// <summary>
		/// Swaps the prepared directory into the game directory.
		///
		/// <b>A failure never leaves the game directory half removed on one volume.</b> The
		/// first move takes the live directory out of the way, and on one volume that move is
		/// a rename that either happens or does not. A failure after the first move puts the
		/// live directory back. A failure that leaves the game directory absent throws a
		/// message that names the directory that holds the content.
		/// </summary>
		public static void Swap(GameWorkspace workspace, string prepared, Action<string> log = null)
		{
			if (workspace is null) throw new ArgumentNullException(nameof(workspace));
			if (String.IsNullOrWhiteSpace(prepared)) throw new ArgumentException("The prepared directory is empty.", nameof(prepared));

			string live = workspace.Install.Root;
			string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(prepared));
			string aside = workspace.PreviousDirectory;

			if (!Directory.Exists(source))
			{
				throw new SwapException($"The prepared directory {source} does not exist. The game directory did not change.");
			}

			if (FileTree.IsSameOrInside(source, live))
			{
				throw new SwapException($"The prepared directory {source} sits inside the game directory {live}.");
			}

			// A leftover from an earlier run would block the first move.
			FileTree.Delete(aside);

			log?.Invoke($"Move the game directory to {aside}.");
			SetAside(live, aside, workspace.SharesVolumeWithGame(), log);

			try
			{
				log?.Invoke($"Move {source} to the game directory.");
				MoveOrCopy(source, live, log);
			}
			catch (Exception ex)
			{
				try
				{
					MoveOrCopy(aside, live, log);
				}
				catch (Exception restore)
				{
					throw new SwapException(
						$"The swap failed and the game directory did not come back. " +
						$"The content of the game sits in {aside}. Move that directory to {live} by hand. " +
						$"The first error was: {ex.Message} The second error was: {restore.Message}", ex);
				}

				throw new SwapException(
					$"The swap failed and the game directory came back unchanged. {ex.Message}", ex);
			}

			log?.Invoke("Remove the directory that the swap set aside.");
			FileTree.Delete(aside);
		}

		/// <summary>
		/// Takes the live game directory out of the way.
		///
		/// <b>On one volume this renames and it never copies.</b> A rename moves the whole
		/// directory or nothing at all, so a failure leaves every file of the game in place.
		/// The copy route deletes the source file by file, and
		/// <c>Directory.Delete(path, true)</c> is not atomic. A denied file part way through
		/// that walk would leave the install of the user half removed, and which file that is
		/// depends on the order that the filesystem lists them in.
		///
		/// That happened. A game under C:\Program Files (x86) gives a standard user no right
		/// to delete, the rename failed, and the copy route then started to remove the real
		/// install. It stopped on the first file only because the name of that file sorts
		/// first. A denied name that sorted last would have taken the game with it.
		///
		/// The copy route stays for a workspace on another volume, where a rename cannot work
		/// and a copy is the only way.
		/// </summary>
		private static void SetAside(string live, string aside, bool oneVolume, Action<string> log)
		{
			try
			{
				Directory.Move(live, aside);

				return;
			}
			catch (Exception ex) when (oneVolume)
			{
				// One volume, so a rename was possible and it still failed. The cause is a
				// permission or an open handle, and neither one gets better by deleting the
				// game one file at a time. Stop while the directory is whole.
				throw new SwapException(
					$"The game directory {live} did not move to {aside}, so the game directory did not change. " +
					$"Close the game and any program that reads that directory, then try again. " +
					$"A game under Program Files needs this application to run as administrator. {ex.Message}", ex);
			}
			catch (IOException ex)
			{
				// Another volume. A rename cannot cross one, so the copy below is the only
				// route. Report the reason, because this costs a full copy of the game.
				log?.Invoke($"The rename failed, so the swap copies every byte. {ex.Message}");
			}

			TreeReplicator.Build(live, aside);

			try
			{
				FileTree.Delete(live);
			}
			catch (Exception ex)
			{
				// The copy finished, so every file exists in aside. The delete did not, so the
				// game directory holds an unknown part of itself. Name both places.
				throw new SwapException(
					$"The game directory {live} copied to {aside}, and the removal of the original stopped part way. " +
					$"Every file of the game is in {aside}. The swap stopped and it changed nothing else. " +
					$"Compare the two directories before you deploy again. {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Moves a directory. A move across a volume copies every byte and then removes the
		/// source, because the filesystem cannot rename across a volume.
		///
		/// This runs for the second move and for the restore. Both read a directory that this
		/// application built, and never the live install of the user.
		/// </summary>
		private static void MoveOrCopy(string from, string to, Action<string> log)
		{
			try
			{
				Directory.Move(from, to);
				return;
			}
			catch (IOException ex)
			{
				// A move across a volume fails here. Report the reason and fall through to the
				// copy. A silent catch here hid the cause of every swap failure.
				log?.Invoke($"The rename of {from} failed, so the swap copies every byte. {ex.Message}");
			}

			TreeReplicator.Build(from, to);
			FileTree.Delete(from);
		}
	}
}
