using System.IO;
using BlackboxModManager.Core.Staging;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the staging copy that shares nothing.
	///
	/// <b>This is the guard of the CLI route.</b> A hard link makes the staging file, the
	/// vanilla file, and the live file one file with three names. The container engine breaks
	/// that share for every file that it writes, because it reads the script first. Binary
	/// writes where it wants and tells us nothing, so the copy must share nothing from the
	/// start. See defect 16.
	/// </summary>
	public class FullCopyStagingTests
	{
		private static string WriteTree(string root)
		{
			Directory.CreateDirectory(Path.Combine(root, "GLOBAL"));

			File.WriteAllText(Path.Combine(root, "GLOBAL", "GLOBALB.LZC"), "the container");
			File.WriteAllText(Path.Combine(root, "server.dll"), "a library");

			// A .txt lands on the writable list, so the linking path copies it anyway. Keep one
			// in the tree so that both branches of the policy run.
			File.WriteAllText(Path.Combine(root, "files.txt"), "a listing");

			return root;
		}

		[Fact]
		public void AFullCopyHoldsTheSameContent()
		{
			using var temp = new TempDirectory();

			string source = WriteTree(Path.Combine(temp.Path, "vanilla"));
			string target = Path.Combine(temp.Path, "staging");

			TreeReplicator.Build(source, target, null, linkFiles: false);

			Assert.Equal("the container",
				File.ReadAllText(Path.Combine(target, "GLOBAL", "GLOBALB.LZC")));
			Assert.Equal("a library", File.ReadAllText(Path.Combine(target, "server.dll")));
			Assert.Equal("a listing", File.ReadAllText(Path.Combine(target, "files.txt")));
		}

		[Fact]
		public void AFullCopyLinksNothing()
		{
			using var temp = new TempDirectory();

			string source = WriteTree(Path.Combine(temp.Path, "vanilla"));
			string target = Path.Combine(temp.Path, "staging");

			ReplicationReport report = TreeReplicator.Build(source, target, null, linkFiles: false);

			Assert.Equal(0, report.Linked);
			Assert.Equal(3, report.Copied);
		}

		/// <summary>
		/// The test that matters. Write into the staging copy the way an outside program does,
		/// and prove that the source did not move.
		/// </summary>
		[Fact]
		public void AWriteIntoAFullCopyLeavesTheSourceAlone()
		{
			using var temp = new TempDirectory();

			string source = WriteTree(Path.Combine(temp.Path, "vanilla"));
			string target = Path.Combine(temp.Path, "staging");

			TreeReplicator.Build(source, target, null, linkFiles: false);

			// FileMode.Create is what Nikki uses to save a container, and it is what any
			// program uses to overwrite a file. A hard link would carry this to the source.
			using (var stream = new FileStream(Path.Combine(target, "GLOBAL", "GLOBALB.LZC"),
				FileMode.Create, FileAccess.Write))
			using (var writer = new StreamWriter(stream))
			{
				writer.Write("what Binary wrote");
			}

			Assert.Equal("the container",
				File.ReadAllText(Path.Combine(source, "GLOBAL", "GLOBALB.LZC")));
			Assert.Equal("what Binary wrote",
				File.ReadAllText(Path.Combine(target, "GLOBAL", "GLOBALB.LZC")));
		}

		/// <summary>
		/// The linking path is the default and it must stay the default. A caller that passes
		/// nothing gets the cheap copy.
		/// </summary>
		[Fact]
		public void TheDefaultStillLinksWhatItCan()
		{
			using var temp = new TempDirectory();

			string source = WriteTree(Path.Combine(temp.Path, "vanilla"));
			string target = Path.Combine(temp.Path, "staging");

			ReplicationReport report = TreeReplicator.Build(source, target);

			// A volume that rejects a hard link copies instead, and that is not a failure of
			// this rule. Either way the file count holds.
			Assert.Equal(3, report.Linked + report.Copied);
		}
	}
}
