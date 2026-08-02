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
		/// A failure after the first move puts the live directory back. A failure that
		/// leaves the game directory absent throws a message that names the directory that
		/// holds the content.
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
			Move(live, aside);

			try
			{
				log?.Invoke($"Move {source} to the game directory.");
				Move(source, live);
			}
			catch (Exception ex)
			{
				try
				{
					Move(aside, live);
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
		/// Moves a directory. A move across a volume copies every byte and then removes the
		/// source, because the filesystem cannot rename across a volume.
		/// </summary>
		private static void Move(string from, string to)
		{
			try
			{
				Directory.Move(from, to);
				return;
			}
			catch (IOException)
			{
				// A move across a volume fails here. Fall through to the copy.
			}

			TreeReplicator.Build(from, to);
			FileTree.Delete(from);
		}
	}
}
