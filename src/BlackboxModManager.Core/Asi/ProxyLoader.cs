using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BlackboxModManager.Core.Asi
{
	/// <summary>
	/// The file names that act as an ASI loader.
	///
	/// The game loads one of these because the name matches a system library that the game
	/// imports. The loader then reads every <c>.asi</c> file of the <c>scripts</c> directory.
	///
	/// <b>This is a list and not one string.</b> Some mods use <c>dsound.dll</c> or
	/// <c>vorbisFile.dll</c> instead. The list starts with <c>dinput8.dll</c> alone, because
	/// that is the name that our samples use. Add a name here when a real mod uses it.
	/// </summary>
	public static class ProxyNames
	{
		public const string DirectInput = "dinput8.dll";

		public static IReadOnlySet<string> Default { get; } =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DirectInput };

		/// <summary>
		/// The names that a mod could use and that this application does not manage yet. The
		/// window shows these in a note when a mod supplies one, so that a user who hits the
		/// case can report it.
		/// </summary>
		public static IReadOnlySet<string> Known { get; } =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				DirectInput, "dsound.dll", "vorbisFile.dll", "d3d9.dll", "winmm.dll", "version.dll",
			};

		public static bool IsProxy(string fileName, IReadOnlySet<string> names = null)
		{
			if (String.IsNullOrEmpty(fileName)) return false;

			return (names ?? Default).Contains(Path.GetFileName(fileName));
		}
	}

	/// <summary>
	/// Where the version of a loader came from.
	/// </summary>
	public enum ProxyIdentitySource
	{
		/// <summary>The file carries a version resource.</summary>
		VersionResource = 0,

		/// <summary>The file holds a build marker in its bytes.</summary>
		BuildMarker,

		/// <summary>Neither answered. The hash is the only thing that tells the files apart.</summary>
		HashOnly,

		/// <summary>This application could not read the file.</summary>
		Unreadable,
	}

	/// <summary>
	/// What one loader file says about itself.
	///
	/// <b>A missing version is normal and it is not an error.</b> Many builds of the Ultimate
	/// ASI Loader carry no version resource, and a mod author who renames a file changes no
	/// resource inside it. The dialog shows what the file holds. It never hides a candidate,
	/// and it never ranks one above another.
	/// </summary>
	public sealed class ProxyIdentity
	{
		public ProxyIdentitySource Source { get; }

		/// <summary>The version, or an empty string.</summary>
		public string Version { get; }

		/// <summary>The product name, or an empty string.</summary>
		public string Product { get; }

		/// <summary>The company, or an empty string.</summary>
		public string Company { get; }

		/// <summary>The first eight characters of the SHA-256 of the file, in lower case.</summary>
		public string ShortHash { get; }

		/// <summary>The full SHA-256, for the test that two candidates are one file.</summary>
		public string Hash { get; }

		/// <summary>The reason that this application could not read the file, or an empty string.</summary>
		public string Problem { get; }

		public ProxyIdentity(ProxyIdentitySource source, string version, string product, string company,
			string hash, string problem = null)
		{
			this.Source = source;
			this.Version = version ?? String.Empty;
			this.Product = product ?? String.Empty;
			this.Company = company ?? String.Empty;
			this.Hash = hash ?? String.Empty;
			this.ShortHash = this.Hash.Length >= 8 ? this.Hash.Substring(0, 8) : this.Hash;
			this.Problem = problem ?? String.Empty;
		}

		/// <summary>The word that the version column shows.</summary>
		public string VersionText => this.Version.Length > 0 ? this.Version : "unknown";

		/// <summary>One line for a dialog row.</summary>
		public string Describe()
		{
			var parts = new List<string> { $"version {this.VersionText}" };

			if (this.Product.Length > 0) parts.Add(this.Product);
			if (this.Company.Length > 0) parts.Add(this.Company);
			if (this.ShortHash.Length > 0) parts.Add($"hash {this.ShortHash}");
			if (this.Problem.Length > 0) parts.Add(this.Problem);

			return String.Join(", ", parts);
		}

		public override string ToString() => this.Describe();
	}

	/// <summary>
	/// Reads what a loader file says about itself.
	///
	/// Three sources answer, in this order. It stops at the first one that gives a version.
	///
	/// 1. The version resource. <c>FileVersionInfo.GetVersionInfo</c> reads it.
	/// 2. A build marker in the bytes. An Ultimate ASI Loader build holds a text marker.
	/// 3. The SHA-256 of the file, shortened to eight characters. This always answers.
	///
	/// <b>The hash runs for every file whatever the first two sources say.</b> Two candidates
	/// with one hash are one file, and the dialog needs to be able to say so.
	/// </summary>
	public static class ProxyIdentityReader
	{
		/// <summary>
		/// The text markers that name a known loader build. The reader scans the bytes for
		/// each one and reports the first match.
		///
		/// Read a marker as evidence and never as a version number. A build that holds
		/// <c>Ultimate ASI Loader</c> and no version resource is still a build of unknown age.
		/// </summary>
		public static IReadOnlyList<string> BuildMarkers { get; } = new[]
		{
			"Ultimate ASI Loader", "ThirteenAG", "Ultimate-ASI-Loader", "ASI Loader",
		};

		/// <summary>
		/// How many bytes the marker scan reads. A loader is a few hundred kilobytes, and this
		/// caps the work for a file that is not one.
		/// </summary>
		public const int ScanLimit = 4 * 1024 * 1024;

		public static ProxyIdentity Read(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("The path is empty.", nameof(path));

			string hash;

			try
			{
				hash = Sha256(path);
			}
			catch (Exception ex)
			{
				// A truncated download and a file that another process holds both reach here.
				// Report the candidate and keep it in the list.
				return new ProxyIdentity(ProxyIdentitySource.Unreadable, null, null, null, null,
					$"This application could not read the file. {ex.Message}");
			}

			(string version, string product, string company) = ReadResource(path);

			if (version.Length > 0)
			{
				return new ProxyIdentity(ProxyIdentitySource.VersionResource, version, product, company, hash);
			}

			string marker = ReadMarker(path);

			if (marker != null)
			{
				return new ProxyIdentity(ProxyIdentitySource.BuildMarker, null, marker, company, hash);
			}

			return new ProxyIdentity(ProxyIdentitySource.HashOnly, null, product, company, hash);
		}

		/// <summary>
		/// Reads the version resource. <c>FileVersionInfo</c> throws on a file that it cannot
		/// read, and it returns empty fields for a file that carries no resource. Both are
		/// normal.
		/// </summary>
		private static (string Version, string Product, string Company) ReadResource(string path)
		{
			try
			{
				FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);

				// FileVersion is the build number and ProductVersion is the release number.
				// Prefer the one that says more, and fall back to the other.
				string version = Pick(info.ProductVersion, info.FileVersion);

				return (version, Text(info.ProductName), Text(info.CompanyName));
			}
			catch (Exception)
			{
				return (String.Empty, String.Empty, String.Empty);
			}
		}

		/// <summary>
		/// Finds the first build marker inside the file. It reads the file as bytes and
		/// compares against the ASCII form and the UTF-16 form of each marker, because a
		/// resource string in a DLL is UTF-16.
		/// </summary>
		private static string ReadMarker(string path)
		{
			byte[] bytes;

			try
			{
				using FileStream stream = File.OpenRead(path);

				int length = (int)Math.Min(stream.Length, ScanLimit);
				bytes = new byte[length];

				stream.ReadExactly(bytes, 0, length);
			}
			catch (Exception)
			{
				return null;
			}

			foreach (string marker in BuildMarkers)
			{
				if (Contains(bytes, Encoding.ASCII.GetBytes(marker))) return marker;
				if (Contains(bytes, Encoding.Unicode.GetBytes(marker))) return marker;
			}

			return null;
		}

		private static bool Contains(byte[] haystack, byte[] needle)
		{
			if (needle.Length == 0 || haystack.Length < needle.Length) return false;

			for (int i = 0; i <= haystack.Length - needle.Length; ++i)
			{
				int j = 0;

				while (j < needle.Length && haystack[i + j] == needle[j]) ++j;

				if (j == needle.Length) return true;
			}

			return false;
		}

		private static string Sha256(string path)
		{
			using FileStream stream = File.OpenRead(path);
			using var sha = SHA256.Create();

			return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
		}

		private static string Pick(string first, string second)
		{
			string one = Text(first);

			return one.Length > 0 ? one : Text(second);
		}

		private static string Text(string value) => value?.Trim() ?? String.Empty;
	}
}
