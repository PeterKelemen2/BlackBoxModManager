using System;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Sets the caller version that the Endscript parser tests a <c>version</c> line against.
	///
	/// <c>Endscript.Version.Value</c> is a static property with no default, and the library
	/// never assigns it. <c>VersionCommand.Prepare</c> reads it with no null test, so a script
	/// that states a version ends the parse with a <c>NullReferenceException</c>. The host
	/// must set the value. See defect 15.
	/// </summary>
	public static class EndscriptVersion
	{
		/// <summary>
		/// The endscript level that this application runs.
		///
		/// The Endscript library carries the command set of Binary 2.8.3, so that is the
		/// version we state. A script that asks for more gets a message that names the two
		/// numbers, which is the answer of the library and it is correct.
		/// </summary>
		public static readonly Version Supported = BinaryInstallStatus.ExpectedVersion;

		/// <summary>
		/// Assigns the caller version one time. Call this before any parse.
		/// </summary>
		public static void Ensure()
		{
			Endscript.Version.Value ??= Supported;
		}
	}
}
