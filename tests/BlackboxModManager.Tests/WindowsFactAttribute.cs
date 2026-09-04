using System.Runtime.InteropServices;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// A Fact that needs Windows. The test reports as skipped on every other platform.
	///
	/// Most tests of this project run on native Linux, because they read paths and hashes
	/// and never container data. A file lock is the exception. Windows refuses a rename of
	/// a directory that holds an open file, and Linux completes the same rename. A test of
	/// that refusal has no meaning on Linux.
	///
	/// xUnit 2 has no Assert.Skip. The runner reads the decision from the Skip property of
	/// the attribute. See <see cref="ExampleModsFactAttribute"/> for the same pattern.
	/// </summary>
	public sealed class WindowsFactAttribute : FactAttribute
	{
		public WindowsFactAttribute()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				this.Skip = "This test needs Windows. Only Windows locks a file against a rename.";
			}
		}
	}
}
