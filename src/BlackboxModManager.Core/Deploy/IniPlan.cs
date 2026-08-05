using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Asi;
using BlackboxModManager.Core.Files;
using BlackboxModManager.Core.Mods;
using BlackboxModManager.Core.Profiles;
using BlackboxModManager.Core.Store;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// The settings answers of one mod, ready to apply to the staging copy.
	///
	/// <b>The file in the mod store never changes.</b> The link engine places the file, then
	/// this class rewrites the copy in the staging directory. <c>DeployPolicy</c> holds
	/// <c>.ini</c> in its writable set, so that copy is a private file and not a hard link.
	/// <b>Do not remove <c>.ini</c> from that set.</b> A write through a hard link would edit
	/// the mod store of the user and the vanilla copy.
	/// </summary>
	public sealed class IniPlan
	{
		/// <summary>
		/// The answers of one settings file, keyed by the path of the file. The comparison
		/// folds the separator and the letter case, so the key matches the path that the file
		/// walk produced.
		/// </summary>
		private readonly Dictionary<string, Dictionary<IniKey, string>> _answers;

		private readonly string _modId;

		private IniPlan(string modId, Dictionary<string, Dictionary<IniKey, string>> answers)
		{
			this._modId = modId;
			this._answers = answers;
		}

		/// <summary>True when this mod carries no answer, which is the normal case.</summary>
		public bool IsEmpty => this._answers.Count == 0;

		/// <summary>
		/// Reads the answers of one mod out of the profile.
		///
		/// A profile that names a file which the mod no longer holds produces a log line and no
		/// error. A mod update that renames a settings file reaches that case.
		/// </summary>
		public static IniPlan Build(Profile profile, InstalledMod mod, Action<string> log = null)
		{
			if (profile is null) throw new ArgumentNullException(nameof(profile));
			if (mod is null) throw new ArgumentNullException(nameof(mod));

			var answers = new Dictionary<string, Dictionary<IniKey, string>>(StringComparer.Ordinal);
			ProfileEntry entry = profile.Find(mod.Id);

			if (entry is null || entry.IniSettings.Count == 0) return new IniPlan(mod.Id, answers);

			foreach (KeyValuePair<string, Dictionary<string, string>> file in entry.IniSettings)
			{
				if (file.Value is null || file.Value.Count == 0) continue;

				var values = new Dictionary<IniKey, string>();

				foreach (KeyValuePair<string, string> option in file.Value)
				{
					values[IniKey.Parse(option.Key)] = option.Value;
				}

				answers[PathKey.Normalize(file.Key)] = values;
			}

			return new IniPlan(mod.Id, answers);
		}

		/// <summary>
		/// Rewrites one placed file when the profile holds answers for it. It returns null when
		/// the profile holds none, which means that the file stays as the mod shipped it.
		/// </summary>
		public SettingsWrite Apply(string relativePath, string targetPath, Action<string> log = null)
		{
			if (this.IsEmpty) return null;

			if (!this._answers.TryGetValue(PathKey.Normalize(relativePath), out Dictionary<IniKey, string> values))
			{
				return null;
			}

			Action<string> write = log ?? (line => { });

			IniDocument document;

			try
			{
				document = IniReader.Read(targetPath);
			}
			catch (Exception ex)
			{
				throw new DeployException(
					$"The settings file {relativePath} of the mod \"{this._modId}\" holds answers in the " +
					$"profile and this application could not read the file. {ex.Message}",
					relativePath, this._modId, ex);
			}

			IniWriteResult result = IniWriter.Apply(document, values);

			// The game writes to some settings files and the user edits others outside this
			// application. The staging copy is the live file, and a deploy overwrites it.
			foreach (string warning in document.Warnings) write($"  {relativePath}: {warning}");

			try
			{
				File.WriteAllText(targetPath, result.Text);
			}
			catch (Exception ex)
			{
				throw new DeployException(
					$"The settings file {relativePath} of the mod \"{this._modId}\" did not take its " +
					$"answers. {ex.Message}", relativePath, this._modId, ex);
			}

			var changed = new List<string>(result.Changed.Count);
			var skipped = new List<string>(result.Skipped.Count);

			foreach (IniKey key in result.Changed) changed.Add(key.ToString());
			foreach (IniKey key in result.Skipped) skipped.Add(key.ToString());

			var record = new SettingsWrite(relativePath, this._modId, changed, skipped);

			write($"  {record}");

			foreach (string key in skipped)
			{
				write($"  {relativePath}: the profile answers \"{key}\" and the file holds no such option. " +
					"A mod update can rename an option. Open the settings panel and set it again.");
			}

			return record;
		}
	}
}
