using System;
using Xunit;

namespace BlackboxModManager.Tests
{
	/// <summary>
	/// A Fact that needs the permission model of Unix.
	///
	/// A test that has to make a directory refuse a write uses chmod for it. Windows ignores
	/// chmod, and the Windows way needs an edit of an access control list that a test must not
	/// make. So these tests run on the developer machine, and they report as skipped on the
	/// Windows runner.
	///
	/// <b>This leaves the denied path without cover on Windows.</b> That is the platform where
	/// the defect appeared, under C:\Program Files (x86). The gap is deliberate. Read
	/// docs/roadmap/05-mvp-shell.md.
	///
	/// A run as root ignores the mode bits, so this skips there too.
	/// </summary>
	public sealed class UnixPermissionFactAttribute : FactAttribute
	{
		public UnixPermissionFactAttribute()
		{
			if (OperatingSystem.IsWindows())
			{
				this.Skip = "This test needs the permission model of Unix. Windows ignores chmod.";

				return;
			}

			if (Environment.UserName == "root") this.Skip = "A run as root ignores the mode bits.";
		}
	}
}
