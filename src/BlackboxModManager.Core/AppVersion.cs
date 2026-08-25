using System.Reflection;

namespace BlackboxModManager.Core
{
	/// <summary>
	/// The version of this build, for a person to read.
	///
	/// src/Directory.Build.props sets the version, and the release workflow overrides it from
	/// the git tag. Both assemblies of this application read that same file, so both carry
	/// the same string.
	///
	/// <b>This value is for display only. Never compare it against a release feed.</b> An
	/// update check reads <c>UpdateManager.CurrentVersion</c>, which Velopack takes from the
	/// metadata of the installed package. A build that runs out of a publish directory
	/// carries a version here and no package at all.
	/// </summary>
	public static class AppVersion
	{
		/// <summary>The value that a missing attribute reads as.</summary>
		public const string Unknown = "unknown";

		/// <summary>
		/// The informational version, for example <c>0.1.0</c> or <c>0.1.0-alpha.1</c>.
		///
		/// This reads the attribute of the assembly that holds this class, and never the
		/// entry assembly. A test run starts testhost.exe, so the entry assembly there is
		/// testhost and the version of testhost is not the version of this application.
		///
		/// src/Directory.Build.props turns
		/// <c>IncludeSourceRevisionInInformationalVersion</c> off, so this string carries no
		/// plus sign and no commit hash. Nothing here has to split the value.
		/// </summary>
		public static string Display { get; } = Read();

		private static string Read()
		{
			var attribute = typeof(AppVersion).Assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

			string version = attribute?.InformationalVersion;

			return string.IsNullOrWhiteSpace(version) ? Unknown : version;
		}
	}
}
