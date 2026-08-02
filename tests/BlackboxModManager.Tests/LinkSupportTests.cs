using System;
using System.IO;
using BlackboxModManager.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the link probe of step 3. The probe answers a question that no platform name
	/// can answer, so these tests check the shape of the answer and the cleanup, not which
	/// method wins. The real matrix lives in docs/roadmap/03-wine-verification.md.
	/// </summary>
	public class LinkSupportTests : IDisposable
	{
		private readonly string _root;

		public LinkSupportTests()
		{
			this._root = Path.Combine(Path.GetTempPath(), $"link-test-{Guid.NewGuid():N}");
			Directory.CreateDirectory(this._root);
		}

		public void Dispose()
		{
			if (Directory.Exists(this._root)) Directory.Delete(this._root, true);
		}

		[Fact]
		public void TheProbeReportsAllThreeMethods()
		{
			LinkProbeResult result = LinkSupport.Probe(this._root);

			Assert.Equal(3, result.Probes.Count);
			Assert.Contains(result.Probes, p => p.Kind == LinkKind.HardLink);
			Assert.Contains(result.Probes, p => p.Kind == LinkKind.SymbolicLink);
			Assert.Contains(result.Probes, p => p.Kind == LinkKind.Copy);
		}

		[Fact]
		public void CopyIsTheFloorAndAlwaysWorks()
		{
			Assert.True(LinkSupport.Probe(this._root).Works(LinkKind.Copy));
		}

		[Fact]
		public void TheProbeRemovesEverythingThatItMade()
		{
			LinkSupport.Probe(this._root);

			Assert.Empty(Directory.GetFileSystemEntries(this._root));
		}

		[Fact]
		public void BestNamesAMethodThatWorks()
		{
			LinkProbeResult result = LinkSupport.Probe(this._root);

			Assert.True(result.Works(result.Best));
		}

		[Fact]
		public void AFailedMethodCarriesAReason()
		{
			LinkProbeResult result = LinkSupport.Probe(this._root);

			foreach (LinkProbe probe in result.Probes)
			{
				if (!probe.Works) Assert.NotEmpty(probe.Error);
			}
		}

		[Fact]
		public void ADirectoryThatDoesNotExistReportsEveryMethodAsFailed()
		{
			// The probe must never throw for a bad target. It reports instead.
			LinkProbeResult result = LinkSupport.Probe(Path.Combine(this._root, "absent", "deeper", "\0bad"));

			Assert.Equal(3, result.Probes.Count);
			Assert.All(result.Probes, p => Assert.False(p.Works));
			Assert.Equal(LinkKind.Copy, result.Best);
		}

		[Fact]
		public void AnEmptyDirectoryArgumentIsARejectedArgument()
		{
			Assert.Throws<ArgumentException>(() => LinkSupport.Probe(" "));
		}

		[Fact]
		public void CreateCopyPutsTheContentInPlace()
		{
			string source = Path.Combine(this._root, "source.bin");
			string target = Path.Combine(this._root, "target.bin");
			File.WriteAllText(source, "payload");

			LinkSupport.Create(LinkKind.Copy, source, target);

			Assert.Equal("payload", File.ReadAllText(target));
		}
	}
}
