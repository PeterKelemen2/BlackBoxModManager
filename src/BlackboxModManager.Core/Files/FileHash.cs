using System;
using System.IO;
using System.IO.Hashing;

namespace BlackboxModManager.Core.Files
{
	/// <summary>
	/// Identifies a file by the hash of its content.
	///
	/// <b>Never identify a file by its size and its modification time.</b> Archive
	/// extraction resets the timestamp, a copy resets it again, and two different files of
	/// the same size are common in game data. Only the content answers the question.
	///
	/// XxHash128 is not a cryptographic hash. We compare our own files against our own
	/// snapshot. We do not defend against a crafted collision.
	/// </summary>
	public static class FileHash
	{
		/// <summary>The buffer of one read. A game install holds files of several hundred megabytes.</summary>
		private const int BufferSize = 1 << 20;

		/// <summary>
		/// Reads one file and returns its hash as lowercase hexadecimal.
		/// </summary>
		public static string Compute(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("The path is empty.", nameof(path));

			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
				BufferSize, FileOptions.SequentialScan);

			return Compute(stream);
		}

		public static string Compute(Stream stream)
		{
			if (stream is null) throw new ArgumentNullException(nameof(stream));

			var hash = new XxHash128();
			hash.Append(stream);

			return Convert.ToHexStringLower(hash.GetCurrentHash());
		}

		/// <summary>
		/// Tests whether two files hold the same content. It compares the length first,
		/// because a different length answers the question with no read.
		/// </summary>
		public static bool SameContent(string left, string right)
		{
			var a = new FileInfo(left);
			var b = new FileInfo(right);

			if (!a.Exists || !b.Exists) return false;
			if (a.Length != b.Length) return false;

			return Compute(left) == Compute(right);
		}
	}
}
