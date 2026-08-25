using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Deploy;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Staging
{
	/// <summary>
	/// What one replication did.
	/// </summary>
	public sealed class ReplicationReport
	{
		public int Linked { get; }

		public int Copied { get; }

		public long Bytes { get; }

		/// <summary>Why the replication copied instead of linking. This is empty when it linked.</summary>
		public string Note { get; }

		public int FileCount => this.Linked + this.Copied;

		public ReplicationReport(int linked, int copied, long bytes, string note)
		{
			this.Linked = linked;
			this.Copied = copied;
			this.Bytes = bytes;
			this.Note = note ?? String.Empty;
		}

		public string Summary() =>
			$"The copy holds {this.FileCount} files. It linked {this.Linked} and it copied {this.Copied}.";
	}

	/// <summary>
	/// Builds one directory as a replica of another.
	///
	/// A hard link makes the replica of a 1.7 GB install cost almost no disk space and
	/// almost no time. This is what makes a staging copy usable for every deploy.
	///
	/// <b>A hard link shares the content.</b> The replica and the source are one file with
	/// two names. A write through either name changes both. Two rules follow.
	///
	/// 1. Any file that something writes gets a private copy. DeployPolicy names those.
	/// 2. Any writer that this class does not know about must call StagingFiles.MakePrivate
	///    before it writes. <b>Step 6 writes containers into the staging copy, so step 6
	///    must call it.</b>
	///
	/// This class does not use block cloning. Windows offers it on a Dev Drive only, and
	/// the application runs against a normal volume and under Wine. A hard link gives the
	/// same result there for less work.
	/// </summary>
	public static class TreeReplicator
	{
		/// <summary>
		/// Removes the target and rebuilds it from the source.
		///
		/// It links where a link is safe, and it copies the rest. It never touches the
		/// source.
		///
		/// Set linkFiles to false to copy every file and to share nothing. Use that for a
		/// deploy that hands the staging copy to another program. <b>An outside program writes
		/// where it wants, and no call here can make the right file private first.</b> A full
		/// copy costs one write of every byte of the install, and it removes the risk. See
		/// BinaryCliDeployEngine and defect 16.
		/// </summary>
		public static ReplicationReport Build(string source, string target, Action<string> log = null,
			bool linkFiles = true)
		{
			if (String.IsNullOrWhiteSpace(source)) throw new ArgumentException("The source is empty.", nameof(source));
			if (String.IsNullOrWhiteSpace(target)) throw new ArgumentException("The target is empty.", nameof(target));

			string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
			string to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));

			if (!Directory.Exists(from))
			{
				throw new DirectoryNotFoundException($"The source directory {from} does not exist.");
			}

			if (FileTree.IsSameOrInside(to, from) || FileTree.IsSameOrInside(from, to))
			{
				throw new ArgumentException(
					$"The target {to} and the source {from} overlap. A replication needs two separate directories.",
					nameof(target));
			}

			FileTree.Delete(to);
			Directory.CreateDirectory(to);

			// The probe creates and deletes a test file. Skip it when the caller wants no link,
			// because the answer would change nothing.
			bool canLink = false;
			string note = String.Empty;

			if (linkFiles)
			{
				LinkProbeResult probe = LinkSupport.ProbeBetween(from, to);
				canLink = probe.Works(LinkKind.HardLink);
				note = canLink ? String.Empty : HardLinkNote(probe, from, to);

				if (note.Length > 0) log?.Invoke(note);
			}

			IReadOnlyList<string> files = FileTree.Files(from);
			int linked = 0;
			int copied = 0;
			long bytes = 0;

			foreach (string relative in files)
			{
				string sourceFile = FileTree.Combine(from, relative);
				string targetFile = FileTree.Combine(to, relative);

				FileTree.CreateParent(targetFile);

				// A hard link to a file that the game writes would edit the source through
				// the second name. Copy those.
				bool link = canLink && !DeployPolicy.NeedsCopy(relative);

				if (link)
				{
					try
					{
						LinkSupport.Create(LinkKind.HardLink, sourceFile, targetFile);
						++linked;
					}
					catch (Exception)
					{
						// One file that rejects a link is not a reason to stop. Copy it.
						link = false;
					}
				}

				if (!link)
				{
					Copy(sourceFile, targetFile);
					++copied;
				}

				try
				{
					bytes += new FileInfo(targetFile).Length;
				}
				catch (Exception)
				{
					// A length that we cannot read changes no decision.
				}

				if ((linked + copied) % 500 == 0)
				{
					log?.Invoke($"The copy put {linked + copied} of {files.Count} files in place.");
				}
			}

			var report = new ReplicationReport(linked, copied, bytes, note);
			log?.Invoke(report.Summary());

			return report;
		}

		/// <summary>
		/// Copies one file and clears the read-only flag on the copy.
		///
		/// The game install holds read-only files. server.dll is one. A copy carries the
		/// flag across, and a later delete of the staging copy then fails.
		/// </summary>
		private static void Copy(string source, string target)
		{
			File.Copy(source, target, true);

			var copy = new FileInfo(target);
			if (copy.IsReadOnly) copy.IsReadOnly = false;
		}

		private static string HardLinkNote(LinkProbeResult probe, string from, string to)
		{
			foreach (LinkProbe entry in probe.Probes)
			{
				if (entry.Kind == LinkKind.HardLink && !entry.Works)
				{
					// Name the volume only when the two paths really sit on different ones. This
					// sentence used to appear whatever the error was. A user read it after an
					// access denial and went looking for a second drive. See
					// docs/roadmap/05-mvp-shell.md.
					string cause = FileTree.SameVolume(from, to)
						? "Both paths sit on one volume, so the cause is not a volume boundary."
						: "A hard link cannot cross a volume, and these two paths sit on different volumes.";

					return $"A hard link from {from} to {to} does not work, so the copy writes every byte. " +
						$"{entry.Error} {cause}";
				}
			}

			return String.Empty;
		}
	}
}
