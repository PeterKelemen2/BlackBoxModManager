using System;
using System.IO;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// Prepares one file of the staging copy for a write.
	///
	/// <b>Read this before you write anything into a staging directory.</b>
	///
	/// TreeReplicator builds the staging copy with hard links. A linked file and its source
	/// are one file with two names. The source is the vanilla copy, and the vanilla copy
	/// shares its own content with the live install. A write into a staging file therefore
	/// reaches the vanilla baseline and the game of the user.
	///
	/// MakePrivate breaks that share. It replaces the linked name with a private copy of
	/// the same content. A write after that call reaches the staging copy only.
	///
	/// The link engine of step 5 never needs this call, because it deletes the target and
	/// then creates a new name. A delete breaks a link and leaves the other name alone.
	/// <b>Step 6 writes containers in place through Nikki, so step 6 must call this method
	/// for every file that the load names.</b>
	/// </summary>
	public static class StagingFiles
	{
		/// <summary>
		/// Gives one file a private copy of its content, if it shares that content.
		///
		/// It returns true when it made a copy. It returns false when the file already
		/// stands alone, or when the file does not exist.
		/// </summary>
		public static bool MakePrivate(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("The path is empty.", nameof(path));

			if (!File.Exists(path)) return false;

			// A private copy costs one read and one write. Making one for a file that
			// already stands alone is safe, and we cannot count the names of a file
			// through the base class library on both platforms. Always make the copy.
			string temporary = path + ".blackbox-private";

			try
			{
				File.Copy(path, temporary, true);

				var copy = new FileInfo(temporary);
				if (copy.IsReadOnly) copy.IsReadOnly = false;

				var original = new FileInfo(path);
				if (original.IsReadOnly) original.IsReadOnly = false;

				// Move over the old name. The old name loses its share of the content, and
				// the vanilla copy keeps it.
				File.Move(temporary, path, true);

				return true;
			}
			catch (Exception)
			{
				try
				{
					if (File.Exists(temporary)) File.Delete(temporary);
				}
				catch (Exception)
				{
					// A leftover temporary file wastes disk space and breaks nothing.
				}

				throw;
			}
		}

		/// <summary>
		/// Calls MakePrivate for one relative path under a staging directory.
		/// </summary>
		public static bool MakePrivate(string stagingDirectory, string relativePath)
		{
			return MakePrivate(Files.FileTree.Combine(stagingDirectory, relativePath));
		}
	}
}
