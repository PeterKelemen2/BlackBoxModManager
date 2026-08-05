using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// How the deploy engine puts one file in place.
	/// </summary>
	public enum LinkKind
	{
		/// <summary>Cheapest. One file, two names, same filesystem only.</summary>
		HardLink = 0,

		/// <summary>
		/// Cheap. Windows needs a privilege for this, and Wine builds differ.
		///
		/// <b>The probe rejects this under Wine.</b> Wine writes a Windows symbolic link as a
		/// zero-byte file and keeps the link in its own metadata, so the length of the target
		/// never matches the length of the source. See <c>Try</c>.
		/// </summary>
		SymbolicLink,

		/// <summary>Always works. Costs disk space and time.</summary>
		Copy,
	}

	/// <summary>
	/// The result of one probe. Works is false when the method failed. Error then names why.
	/// </summary>
	public sealed class LinkProbe
	{
		public LinkKind Kind { get; }

		public bool Works { get; }

		public string Error { get; }

		internal LinkProbe(LinkKind kind, bool works, string error)
		{
			this.Kind = kind;
			this.Works = works;
			this.Error = error ?? String.Empty;
		}

		public override string ToString() => this.Works ? $"{this.Kind}: works" : $"{this.Kind}: {this.Error}";
	}

	public sealed class LinkProbeResult
	{
		public string Directory { get; }

		public IReadOnlyList<LinkProbe> Probes { get; }

		internal LinkProbeResult(string directory, IReadOnlyList<LinkProbe> probes)
		{
			this.Directory = directory;
			this.Probes = probes;
		}

		public bool Works(LinkKind kind)
		{
			foreach (LinkProbe probe in this.Probes)
			{
				if (probe.Kind == kind) return probe.Works;
			}

			return false;
		}

		/// <summary>
		/// The cheapest method that works on this filesystem. Copy always works, so this
		/// always gives an answer.
		/// </summary>
		public LinkKind Best
		{
			get
			{
				if (this.Works(LinkKind.HardLink)) return LinkKind.HardLink;
				if (this.Works(LinkKind.SymbolicLink)) return LinkKind.SymbolicLink;
				return LinkKind.Copy;
			}
		}
	}

	/// <summary>
	/// Tests which link methods a directory supports.
	///
	/// Do not guess this from the platform name. Windows needs a privilege for a symbolic
	/// link, Wine builds differ, and a hard link fails across filesystems. Probe the real
	/// target directory and take the answer from the probe.
	/// </summary>
	public static class LinkSupport
	{
		private const string Content = "blackbox link probe";

		/// <summary>
		/// Creates a temporary file in the directory and tries each method against it.
		/// Removes everything that it made. Never throws for a method that fails.
		/// </summary>
		public static LinkProbeResult Probe(string directory)
		{
			if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("The directory is empty.", nameof(directory));

			string work = Path.Combine(directory, $".blackbox-probe-{Guid.NewGuid():N}");
			var probes = new List<LinkProbe>(3);

			try
			{
				Directory.CreateDirectory(work);

				string source = Path.Combine(work, "source.bin");
				File.WriteAllText(source, Content);

				probes.Add(Try(LinkKind.HardLink, source, Path.Combine(work, "hard.bin")));
				probes.Add(Try(LinkKind.SymbolicLink, source, Path.Combine(work, "soft.bin")));
				probes.Add(Try(LinkKind.Copy, source, Path.Combine(work, "copy.bin")));
			}
			catch (Exception ex)
			{
				// The directory itself is not writable. Report every method as failed.
				probes.Clear();

				foreach (LinkKind kind in new[] { LinkKind.HardLink, LinkKind.SymbolicLink, LinkKind.Copy })
				{
					probes.Add(new LinkProbe(kind, false, $"The probe directory is not usable. {ex.Message}"));
				}
			}
			finally
			{
				Remove(work);
			}

			return new LinkProbeResult(directory, probes);
		}

		/// <summary>
		/// Tests each method from a source directory into a target directory.
		///
		/// Probe returns the answer for one directory. A deploy reads from the mod store and
		/// writes into the staging copy, and those two can sit on different volumes. A hard
		/// link then fails while the same-directory probe reports success. Ask this question
		/// with the two real directories.
		/// </summary>
		public static LinkProbeResult ProbeBetween(string sourceDirectory, string targetDirectory)
		{
			if (String.IsNullOrWhiteSpace(sourceDirectory)) throw new ArgumentException("The source directory is empty.", nameof(sourceDirectory));
			if (String.IsNullOrWhiteSpace(targetDirectory)) throw new ArgumentException("The target directory is empty.", nameof(targetDirectory));

			string sourceWork = Path.Combine(sourceDirectory, $".blackbox-probe-{Guid.NewGuid():N}");
			string targetWork = Path.Combine(targetDirectory, $".blackbox-probe-{Guid.NewGuid():N}");
			var probes = new List<LinkProbe>(3);

			try
			{
				Directory.CreateDirectory(sourceWork);
				Directory.CreateDirectory(targetWork);

				string source = Path.Combine(sourceWork, "source.bin");
				File.WriteAllText(source, Content);

				probes.Add(Try(LinkKind.HardLink, source, Path.Combine(targetWork, "hard.bin")));
				probes.Add(Try(LinkKind.SymbolicLink, source, Path.Combine(targetWork, "soft.bin")));
				probes.Add(Try(LinkKind.Copy, source, Path.Combine(targetWork, "copy.bin")));
			}
			catch (Exception ex)
			{
				// One of the two directories is not writable. Report every method as failed.
				probes.Clear();

				foreach (LinkKind kind in new[] { LinkKind.HardLink, LinkKind.SymbolicLink, LinkKind.Copy })
				{
					probes.Add(new LinkProbe(kind, false, $"The probe directory is not usable. {ex.Message}"));
				}
			}
			finally
			{
				Remove(sourceWork);
				Remove(targetWork);
			}

			return new LinkProbeResult(targetDirectory, probes);
		}

		/// <summary>
		/// Puts source in place at target with the named method. Throws when the method
		/// fails. Probe first, then call this with a method that the probe accepted.
		/// </summary>
		public static void Create(LinkKind kind, string source, string target)
		{
			switch (kind)
			{
				case LinkKind.HardLink:
					CreateHardLink(source, target);
					break;

				case LinkKind.SymbolicLink:
					File.CreateSymbolicLink(target, source);
					break;

				case LinkKind.Copy:
					File.Copy(source, target, true);
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(kind));
			}
		}

		private static LinkProbe Try(LinkKind kind, string source, string target)
		{
			try
			{
				Create(kind, source, target);

				// Creation is not proof. Read the content back through the new name. Wine can
				// make an entry that does not resolve.
				if (!File.Exists(target))
				{
					return new LinkProbe(kind, false, "The method reported success and the target does not exist.");
				}

				if (File.ReadAllText(target) != Content)
				{
					return new LinkProbe(kind, false, "The target does not hold the content of the source.");
				}

				// Content is not proof either.
				//
				// <b>Wine writes a Windows symbolic link as a zero-byte file.</b> The Linux
				// name gets a question mark appended, and Wine keeps the link itself in its own
				// metadata. A Wine process that opens the Windows name reads the source, so the
				// content test above passes. FileInfo.Length still reports zero.
				//
				// That breaks this application in two ways. FileHash.SameContent compares the
				// length first, so the verify reports every linked file as changed and no
				// deploy ever reaches the swap. Nothing outside Wine can read the file either,
				// and the name is illegal on real Windows.
				//
				// A deployed file must look the same as the file that it came from. Reject any
				// method that does not manage that, and let the chain fall through to Copy.
				long expected = new FileInfo(source).Length;
				long actual = new FileInfo(target).Length;

				if (actual != expected)
				{
					return new LinkProbe(kind, false,
						$"The source holds {expected} bytes and the target reports {actual}. " +
						"The content reads back and the length does not, so a verify cannot trust it. " +
						"Wine writes a symbolic link this way.");
				}

				return new LinkProbe(kind, true, null);
			}
			catch (Exception ex)
			{
				return new LinkProbe(kind, false, $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void CreateHardLink(string source, string target)
		{
			if (OperatingSystem.IsWindows())
			{
				// The base class library has no hard link method. Both Windows and Wine
				// carry CreateHardLinkW in kernel32.
				if (!CreateHardLinkW(target, source, IntPtr.Zero))
				{
					throw new IOException($"CreateHardLinkW failed with error {Marshal.GetLastWin32Error()}.");
				}

				return;
			}

			if (link(source, target) != 0)
			{
				throw new IOException($"link failed with errno {Marshal.GetLastWin32Error()}.");
			}
		}

		private static void Remove(string directory)
		{
			try
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
			catch (Exception)
			{
				// A probe that cannot clean up must not fail the run. The name carries a
				// GUID, so the leftover collides with nothing.
			}
		}

		[DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool CreateHardLinkW(string target, string source, IntPtr securityAttributes);

		[DllImport("libc", SetLastError = true)]
		private static extern int link(string source, string target);
	}
}
