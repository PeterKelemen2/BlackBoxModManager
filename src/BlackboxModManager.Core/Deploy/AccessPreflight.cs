using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Staging;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// Thrown when the account that runs this application cannot finish a deploy.
	///
	/// The window catches this type by itself. It offers a restart with administrator rights,
	/// because that is the answer for a game under Program Files.
	/// </summary>
	public sealed class AccessException : Exception
	{
		/// <summary>The directory that refused the test.</summary>
		public string Directory { get; }

		public AccessException(string message, string directory, Exception inner = null)
			: base(message, inner)
		{
			this.Directory = directory;
		}
	}

	/// <summary>
	/// Tests whether this account can finish a deploy, before the deploy builds anything.
	///
	/// <b>The swap is the step that needs the rights, and it is the last step.</b> A deploy
	/// reads the install, builds a staging copy, and verifies it. None of that touches the game
	/// directory. Then the swap renames two directories, and only there does a permission
	/// matter. A real deploy copied 1,560 files and verified them before it found that it could
	/// not rename anything. This class asks the same question in milliseconds.
	///
	/// The tests create a file and remove it again. They never rename the game directory. A
	/// rename that worked and then failed to go back would break the install of the user, which
	/// is the thing this class exists to prevent.
	/// </summary>
	public static class AccessPreflight
	{
		/// <summary>The name of each probe file. A leftover is harmless and this names it.</summary>
		private const string ProbePrefix = ".blackbox-access-probe-";

		/// <summary>
		/// Checks every directory that a deploy or a revert writes to. It throws
		/// <see cref="AccessException"/> for the first one that refuses.
		///
		/// Two directories matter, and the second one surprises people.
		///
		/// 1. The game directory. The swap renames it, and a rename needs the right to remove
		///    the entry.
		/// 2. <b>The parent of the game directory.</b> The swap puts the new game directory
		///    there. A game under Program Files fails here even when the game directory itself
		///    allows a write.
		/// </summary>
		public static void Check(GameWorkspace workspace, Action<string> log = null)
		{
			if (workspace is null) throw new ArgumentNullException(nameof(workspace));

			string game = workspace.Install.Root;
			string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game)));

			foreach ((string directory, string role) in Targets(workspace, game, parent))
			{
				string error = Refuses(directory);

				if (error is null) continue;

				throw new AccessException(
					$"This account cannot write in {directory}, so a deploy cannot finish. " +
					$"That directory {role} " +
					"Start this application as administrator, or move the game out of Program Files. " +
					$"The game directory did not change. {error}",
					directory);
			}

			log?.Invoke("The account can write every directory that a deploy needs.");
		}

		/// <summary>
		/// The same check, as a question rather than an exception. The window asks this before
		/// it starts a deploy, so that it can offer a restart with administrator rights.
		/// </summary>
		public static AccessException Test(GameWorkspace workspace)
		{
			try
			{
				Check(workspace);

				return null;
			}
			catch (AccessException problem)
			{
				return problem;
			}
		}

		private static IEnumerable<(string Directory, string Role)> Targets(
			GameWorkspace workspace, string game, string parent)
		{
			// The parent comes first. It is the one that a game under Program Files refuses,
			// and it is the one that nobody expects.
			if (!String.IsNullOrEmpty(parent))
			{
				yield return (parent, "receives the game directory that the swap puts in place.");
			}

			yield return (game, "is the game directory, and the swap renames it.");

			// The workspace holds the vanilla copy, the staging copy, and the directory that the
			// swap sets aside. A deploy writes gigabytes here.
			yield return (workspace.Root, "holds the workspace of this application.");
		}

		/// <summary>
		/// Creates a file in the directory and removes it. It returns null when that works, and
		/// the message of the failure when it does not.
		///
		/// A directory that does not exist yet reports null. The workspace is the case, and a
		/// deploy creates it. A parent that cannot take the workspace fails its own test above.
		/// </summary>
		private static string Refuses(string directory)
		{
			try
			{
				if (!Directory.Exists(directory)) return null;

				string probe = Path.Combine(directory, $"{ProbePrefix}{Guid.NewGuid():N}");

				// Create, then remove. A create alone would leave the file behind, and the
				// remove is half of what the swap needs anyway.
				using (FileStream stream = File.Create(probe)) { }

				File.Delete(probe);

				return null;
			}
			catch (Exception ex)
			{
				return ex.Message;
			}
		}
	}
}
