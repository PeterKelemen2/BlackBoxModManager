using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Asi
{
	/// <summary>
	/// How sure this application is that one <c>.ini</c> file configures one plugin.
	/// </summary>
	public enum AsiMatchKind
	{
		/// <summary>No plugin name matches. The file may belong to something else.</summary>
		None = 0,

		/// <summary>
		/// The file name and the plugin name are the same. The Widescreen Fix ships
		/// <c>NFSUnderground2.WidescreenFix.asi</c> and <c>NFSUnderground2.WidescreenFix.ini</c>.
		/// </summary>
		Exact,

		/// <summary>
		/// The file name is the plugin name plus a word such as <c>Settings</c>. Extra Options
		/// ships <c>NFSU2ExtraOptions.asi</c> and <c>NFSU2ExtraOptionsSettings.ini</c>.
		///
		/// This is a strong guess and not a fact. The window says "probably".
		/// </summary>
		NameWithSuffix,
	}

	/// <summary>
	/// One <c>.ini</c> file of a mod, parsed, with the plugin that owns it.
	/// </summary>
	public sealed class AsiSettingsFile
	{
		/// <summary>The path inside the game directory, with a forward slash separator.</summary>
		public string RelativePath { get; }

		/// <summary>
		/// The plugin that this file configures, or null when no plugin name matches.
		///
		/// <b>Not every <c>.ini</c> beside a plugin is the settings of that plugin.</b> The
		/// window shows an unmatched file under its own heading, and it never claims that the
		/// file belongs to a plugin.
		/// </summary>
		public string PluginPath { get; }

		/// <summary>How the plugin was matched. Read this before you word a message.</summary>
		public AsiMatchKind MatchKind { get; }

		public IniDocument Document { get; }

		/// <summary>The reason that this file could not be read, or an empty string.</summary>
		public string Problem { get; }

		public bool HasPlugin => !String.IsNullOrEmpty(this.PluginPath);

		public bool IsReadable => this.Document != null;

		/// <summary>The file name with no directory. The window shows this.</summary>
		public string Name => Path.GetFileName(this.RelativePath);

		public AsiSettingsFile(string relativePath, string pluginPath, IniDocument document,
			string problem = null, AsiMatchKind matchKind = AsiMatchKind.None)
		{
			this.RelativePath = relativePath ?? String.Empty;
			this.PluginPath = pluginPath;
			this.Document = document;
			this.Problem = problem ?? String.Empty;
			this.MatchKind = matchKind;
		}

		/// <summary>
		/// One line that names the plugin, or that names none. The wording follows
		/// <see cref="MatchKind"/>, so this application never claims more than it knows.
		/// </summary>
		public string Owner()
		{
			return this.MatchKind switch
			{
				AsiMatchKind.Exact => $"The settings of {Path.GetFileName(this.PluginPath)}.",
				AsiMatchKind.NameWithSuffix =>
					$"Probably the settings of {Path.GetFileName(this.PluginPath)}. " +
					"The two names differ by one word.",
				_ => "This application found no plugin with a matching name. The file may belong to " +
					"something else.",
			};
		}

		public override string ToString() =>
			this.HasPlugin ? $"{this.RelativePath} for {this.PluginPath}" : $"{this.RelativePath}, no plugin";
	}

	/// <summary>
	/// What one mod holds for an ASI loader.
	/// </summary>
	public sealed class AsiLayout
	{
		/// <summary>Every <c>.asi</c> plugin, by its path inside the game directory.</summary>
		public IReadOnlyList<string> Plugins { get; }

		/// <summary>
		/// Every <c>.ini</c> file of the mod. A file that this application could not read is
		/// in this list too, with its reason.
		/// </summary>
		public IReadOnlyList<AsiSettingsFile> Settings { get; }

		/// <summary>Every proxy loader that this mod supplies, by path.</summary>
		public IReadOnlyList<string> ProxyFiles { get; }

		public AsiLayout(IReadOnlyList<string> plugins, IReadOnlyList<AsiSettingsFile> settings,
			IReadOnlyList<string> proxyFiles)
		{
			this.Plugins = plugins ?? Array.Empty<string>();
			this.Settings = settings ?? Array.Empty<AsiSettingsFile>();
			this.ProxyFiles = proxyFiles ?? Array.Empty<string>();
		}

		/// <summary>The settings files that carry at least one option.</summary>
		public IEnumerable<AsiSettingsFile> Configurable
		{
			get
			{
				foreach (AsiSettingsFile file in this.Settings)
				{
					if (file.IsReadable && HasEntry(file)) yield return file;
				}
			}
		}

		public AsiSettingsFile Find(string relativePath)
		{
			foreach (AsiSettingsFile file in this.Settings)
			{
				if (PathKey.Same(file.RelativePath, relativePath)) return file;
			}

			return null;
		}

		private static bool HasEntry(AsiSettingsFile file)
		{
			foreach (IniEntry entry in file.Document.Entries) return true;

			return false;
		}

		public override string ToString() =>
			$"{this.Plugins.Count} plugins, {this.Settings.Count} settings files, {this.ProxyFiles.Count} loaders";
	}

	/// <summary>
	/// Reads the ASI layout of one mod directory.
	///
	/// The match rule has two steps, and it reports which one answered.
	///
	/// 1. An <c>.ini</c> whose name is the name of an <c>.asi</c> configures that plugin. The
	///    Widescreen Fix ships <c>scripts/NFSUnderground2.WidescreenFix.asi</c> and
	///    <c>scripts/NFSUnderground2.WidescreenFix.ini</c>.
	/// 2. An <c>.ini</c> whose name is the name of an <c>.asi</c> plus one word of
	///    <see cref="SettingsSuffixes"/> probably configures that plugin. Extra Options ships
	///    <c>scripts/NFSU2ExtraOptions.asi</c> and <c>scripts/NFSU2ExtraOptionsSettings.ini</c>.
	///
	/// Both steps compare the file name with no extension and they ignore the directory. A mod
	/// that puts the settings elsewhere still matches, because the name is the stronger signal.
	///
	/// <b>Nothing else matches.</b> Not every <c>.ini</c> beside a plugin is the settings of
	/// that plugin, and a guess from the directory alone would claim an owner for a file that
	/// belongs to something else.
	/// </summary>
	public static class AsiLayoutReader
	{
		public const string SettingsExtension = ".ini";

		/// <summary>
		/// The words that a mod appends to the plugin name to name its settings file.
		///
		/// Add a word here only after a real mod uses it. Every entry widens the guess, and a
		/// wrong guess puts the options of one plugin under the name of another.
		/// </summary>
		public static IReadOnlyList<string> SettingsSuffixes { get; } = new[]
		{
			"Settings", "Config", "Configuration", "Options", "Ini",
		};

		public static AsiLayout Read(string contentRoot, IReadOnlySet<string> proxyNames = null)
		{
			if (String.IsNullOrWhiteSpace(contentRoot))
			{
				throw new ArgumentException("The content root is empty.", nameof(contentRoot));
			}

			IReadOnlySet<string> proxies = proxyNames ?? ProxyNames.Default;

			var plugins = new List<string>();
			var settings = new List<AsiSettingsFile>();
			var proxyFiles = new List<string>();
			var candidates = new List<string>();

			foreach (string relative in FileTree.Files(contentRoot))
			{
				string extension = Path.GetExtension(relative);

				if (String.Equals(extension, ModClassifier.AsiExtension, StringComparison.OrdinalIgnoreCase))
				{
					plugins.Add(relative);
					continue;
				}

				if (String.Equals(extension, SettingsExtension, StringComparison.OrdinalIgnoreCase))
				{
					candidates.Add(relative);
					continue;
				}

				if (proxies.Contains(Path.GetFileName(relative))) proxyFiles.Add(relative);
			}

			foreach (string relative in candidates)
			{
				settings.Add(ReadSettings(contentRoot, relative, plugins));
			}

			return new AsiLayout(plugins, settings, proxyFiles);
		}

		private static AsiSettingsFile ReadSettings(string contentRoot, string relative,
			IReadOnlyList<string> plugins)
		{
			(string plugin, AsiMatchKind kind) = MatchPlugin(relative, plugins);
			string full = FileTree.Combine(contentRoot, relative);

			try
			{
				return new AsiSettingsFile(relative, plugin, IniReader.Read(full), null, kind);
			}
			catch (Exception ex)
			{
				// A file that we cannot read is not an error of the deploy. The link engine
				// still puts it in place, and the window says that it holds no editor for it.
				return new AsiSettingsFile(relative, plugin, null,
					$"This application could not read the file. {ex.Message}", kind);
			}
		}

		/// <summary>
		/// The plugin whose name matches this settings file, and how it matched.
		///
		/// An exact match wins over a suffix match, whatever order the plugins come in. So a
		/// mod that ships both <c>Plugin.ini</c> and <c>PluginSettings.ini</c> gives the exact
		/// name to the plugin and reports the other one as a guess.
		/// </summary>
		private static (string Plugin, AsiMatchKind Kind) MatchPlugin(string relative,
			IReadOnlyList<string> plugins)
		{
			string name = Path.GetFileNameWithoutExtension(relative);
			string guess = null;

			foreach (string plugin in plugins)
			{
				string stem = Path.GetFileNameWithoutExtension(plugin);

				if (String.Equals(stem, name, StringComparison.OrdinalIgnoreCase))
				{
					return (plugin, AsiMatchKind.Exact);
				}

				if (guess is null && HasSuffix(name, stem)) guess = plugin;
			}

			return guess is null ? (null, AsiMatchKind.None) : (guess, AsiMatchKind.NameWithSuffix);
		}

		/// <summary>
		/// True when the file name is the plugin name plus one of the known words.
		/// </summary>
		private static bool HasSuffix(string name, string stem)
		{
			if (stem.Length == 0 || name.Length <= stem.Length) return false;

			if (!name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) return false;

			// The rest must be exactly one known word. A separator before it is optional,
			// because a mod writes either NameSettings or Name.Settings.
			string rest = name.Substring(stem.Length).TrimStart('.', '_', '-', ' ');

			foreach (string suffix in SettingsSuffixes)
			{
				if (String.Equals(rest, suffix, StringComparison.OrdinalIgnoreCase)) return true;
			}

			return false;
		}
	}
}
