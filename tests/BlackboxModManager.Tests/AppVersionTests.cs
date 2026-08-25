using BlackboxModManager.Core;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// Covers the version that the application shows. These tests run under testhost, so they
	/// also prove that AppVersion reads its own assembly and not the entry assembly.
	/// </summary>
	public class AppVersionTests
	{
		/// <summary>
		/// The build carries a version.
		///
		/// A test run starts testhost.exe. An implementation that read
		/// <c>Assembly.GetEntryAssembly()</c> would return the version of testhost here, and
		/// the value would look right and mean nothing.
		/// </summary>
		[Fact]
		public void TheBuildReportsAVersion()
		{
			Assert.False(string.IsNullOrWhiteSpace(AppVersion.Display));
			Assert.NotEqual(AppVersion.Unknown, AppVersion.Display);
		}

		/// <summary>
		/// The version carries no commit hash.
		///
		/// src/Directory.Build.props turns IncludeSourceRevisionInInformationalVersion off.
		/// Without that switch the SDK appends a plus sign and the commit hash, and every
		/// reader of this string would have to split it. This test fails if somebody removes
		/// the switch.
		/// </summary>
		[Fact]
		public void TheVersionHoldsNoCommitHash()
		{
			Assert.DoesNotContain("+", AppVersion.Display);
		}
	}
}
