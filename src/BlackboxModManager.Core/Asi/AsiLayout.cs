using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Asi
{
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

		public IniDocument Document { get; }

		/// <summary>The reason that this file could not be read, or an empty string.</summary>
		public string Problem { get; }

		public bool HasPlugin => !String.IsNullOrEmpty(this.PluginPath);

		public bool IsReadable => this.Document != null;

		/// <summary>The file name with no directory. The window shows this.</summary>
		public string Name => Path.GetFileName(this.RelativePath);

		public AsiSettingsFile(string relativePath, string pluginPath, IniDocument document, string problem = null)
		{
			this.RelativePath = relativePath ?? String.Empty;
			this.PluginPath = pluginPath;
			this.Document = document;
			this.Problem = problem ?? String.Empty;
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
	/// The match rule is one line long. An <c>.ini</c> whose name matches the name of an
	/// <c>.asi</c> configures that plugin. Everything else is an unmatched file.
	///
	/// The rule compares the file name with no extension and it ignores the directory. The
	/// Widescreen Fix ships <c>scripts/NFSUnderground2.WidescreenFix.asi</c> and
	/// <c>scripts/NFSUnderground2.WidescreenFix.ini</c>, so the names match and the
	/// directories do too. A mod that puts the settings elsewhere still matches, because the
	/// name is the stronger signal.
	/// </summary>
	public static class AsiLayoutReader
	{
		public const string SettingsExtension = ".ini";

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
			string plugin = MatchPlugin(relative, plugins);
			string full = FileTree.Combine(contentRoot, relative);

			try
			{
				return new AsiSettingsFile(relative, plugin, IniReader.Read(full));
			}
			catch (Exception ex)
			{
				// A file that we cannot read is not an error of the deploy. The link engine
				// still puts it in place, and the window says that it holds no editor for it.
				return new AsiSettingsFile(relative, plugin, null,
					$"This application could not read the file. {ex.Message}");
			}
		}

		/// <summary>
		/// The plugin whose name matches this settings file, or null.
		/// </summary>
		private static string MatchPlugin(string relative, IReadOnlyList<string> plugins)
		{
			string name = Path.GetFileNameWithoutExtension(relative);

			foreach (string plugin in plugins)
			{
				if (String.Equals(Path.GetFileNameWithoutExtension(plugin), name,
					StringComparison.OrdinalIgnoreCase))
				{
					return plugin;
				}
			}

			return null;
		}
	}
}
