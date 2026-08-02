using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Endscript.Commands;
using Endscript.Core;
using Nikki.Core;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// Reads a mod folder into a ModPackage.
	///
	/// This never installs anything and it never touches a game directory. It reads text.
	/// Every failure lands on the variant that caused it, so one bad manifest does not hide
	/// the good ones beside it.
	/// </summary>
	public static class ModPackageReader
	{
		public const string Version1 = "[VERSN1]";

		/// <summary>Matches any version header, so that we can name an unknown one.</summary>
		private static readonly Regex VersionHeader = new Regex(@"^\[VERSN(\d+)\]", RegexOptions.Compiled);

		/// <summary>
		/// Reads one mod folder. The folder name becomes the package name.
		/// </summary>
		public static ModPackage Read(string root)
		{
			if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("The root is empty.", nameof(root));

			string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

			if (!Directory.Exists(full))
			{
				return new ModPackage(full, Path.GetFileName(full), Array.Empty<ModVariant>(),
					new[] { $"The directory {full} does not exist." });
			}

			var variants = new List<ModVariant>();
			var problems = new List<string>();

			foreach (string path in FindManifests(full, problems))
			{
				variants.Add(ReadVariant(path));
			}

			variants.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

			if (variants.Count == 0)
			{
				problems.Add($"The directory {full} holds no {Version1} manifest.");
			}

			return new ModPackage(full, Path.GetFileName(full), variants, problems);
		}

		/// <summary>
		/// Finds every manifest under the root.
		///
		/// It tests the first line, not the extension. A VERSN1 manifest and a VERSN2
		/// script both use ".end", so an extension filter cannot tell them apart.
		/// </summary>
		private static IEnumerable<string> FindManifests(string root, List<string> problems)
		{
			var found = new List<string>();

			foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
			{
				string header = ReadHeader(path);

				if (header is null) continue;

				if (header == Version1)
				{
					found.Add(path);
					continue;
				}

				Match match = VersionHeader.Match(header);

				if (!match.Success) continue;

				// VERSN2 is a script and VERSN3 is a description menu. Both are expected
				// beside a manifest. Any other number is a file that we cannot read.
				string number = match.Groups[1].Value;

				if (number != "2" && number != "3")
				{
					problems.Add($"The file {Path.GetRelativePath(root, path)} carries an unknown header [VERSN{number}].");
				}
			}

			found.Sort(StringComparer.Ordinal);
			return found;
		}

		/// <summary>
		/// Tests one file for the manifest header. The mod classifier calls this.
		/// </summary>
		public static bool IsManifest(string path) => ReadHeader(path) == Version1;

		/// <summary>
		/// Returns the version header of a file, such as [VERSN1]. It returns null when the
		/// first line carries no header.
		///
		/// Read the first line, not the extension. A VERSN1 manifest and a VERSN2 script
		/// both use ".end", so an extension filter cannot tell them apart.
		/// </summary>
		public static string ReadHeader(string path)
		{
			try
			{
				using var reader = new StreamReader(path);
				string first = reader.ReadLine();

				if (first is null) return null;

				first = first.Trim();
				return first.StartsWith("[VERSN", StringComparison.Ordinal) ? first : null;
			}
			catch (Exception)
			{
				// A file that we cannot open is not a manifest. A real manifest that we
				// cannot open surfaces later, when the user tries to install it.
				return null;
			}
		}

		private static ModVariant ReadVariant(string manifestPath)
		{
			string name = Path.GetFileNameWithoutExtension(manifestPath);
			Launch launch;

			try
			{
				Launch.Deserialize(manifestPath, out launch);

				// Defect 2: ThisDir carries [JsonIgnore], so Deserialize leaves it null.
				// Every relative path in the manifest resolves through it.
				launch.ThisDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
			}
			catch (Exception ex)
			{
				return new ModVariant(name, manifestPath, ModVariantState.BadManifest,
					DescribeManifestFailure(manifestPath, ex), null, GameINT.None, null);
			}

			// Reject an unsupported game here. Do not install it hopefully and fail deep
			// inside the container code.
			if (launch.GameID == GameINT.None)
			{
				return new ModVariant(name, manifestPath, ModVariantState.UnsupportedGame,
					$"The manifest names the game \"{launch.Game}\", which this tool does not support.",
					launch, GameINT.None, null);
			}

			try
			{
				IReadOnlyList<ModOptionSet> options = ReadOptions(launch);
				return new ModVariant(name, manifestPath, ModVariantState.Ok, null, launch, launch.GameID, options);
			}
			catch (Exception ex)
			{
				return new ModVariant(name, manifestPath, ModVariantState.BadScript,
					ex.Message, launch, launch.GameID, null);
			}
		}

		/// <summary>
		/// Parses the script of a variant and collects the questions that it asks.
		/// </summary>
		private static IReadOnlyList<ModOptionSet> ReadOptions(Launch launch)
		{
			string scriptPath = ModPath.Resolve(launch.ThisDir, launch.Endscript);

			if (!File.Exists(scriptPath))
			{
				throw new FileNotFoundException($"The script {scriptPath} does not exist.", scriptPath);
			}

			// Find an append loop before the library parser recurses into it.
			ScriptAppendGraph.Walk(scriptPath);

			BaseCommand[] commands = ScriptReader.Parse(scriptPath);

			return Extract(commands);
		}

		/// <summary>
		/// Collects one option set per selectable command, in script order.
		///
		/// IfStatementCommand also carries ISelectable and it never pauses. ProcessScript
		/// evaluates it inline against the loaded containers. Filter on the concrete types,
		/// so that an if statement never reaches the user as a question.
		/// </summary>
		public static IReadOnlyList<ModOptionSet> Extract(BaseCommand[] commands)
		{
			var sets = new List<ModOptionSet>();

			if (commands is null) return sets;

			foreach (BaseCommand command in commands)
			{
				switch (command)
				{
					case ComboboxCommand combobox:
						sets.Add(Build(sets.Count, ModOptionKind.Combobox, combobox.Description,
							Names(combobox.Options), command));
						break;

					case CheckboxCommand checkbox:
						// The two names are fixed. The script blocks must use them.
						sets.Add(Build(sets.Count, ModOptionKind.Checkbox, checkbox.Description,
							Names(checkbox.Options), command));
						break;
				}
			}

			return sets;
		}

		private static ModOptionSet Build(int ordinal, ModOptionKind kind, string description,
			IReadOnlyList<string> names, BaseCommand command)
		{
			var options = new List<ModOption>(names.Count);

			for (int i = 0; i < names.Count; ++i) options.Add(new ModOption(names[i], i));

			return new ModOptionSet(ordinal, kind, description, options, command.Filename, command.Index);
		}

		private static IReadOnlyList<string> Names(Endscript.Helpers.OptionState[] states)
		{
			var names = new List<string>(states.Length);

			foreach (Endscript.Helpers.OptionState state in states) names.Add(state.Name);

			return names;
		}

		private static string DescribeManifestFailure(string path, Exception ex)
		{
			if (ex is Endscript.Exceptions.InvalidVersionException)
			{
				// Defect 3: the message reads as "this is not a VERSN1 file" and hides the
				// real cause, which is usually an encoding or a leading space.
				return $"The manifest does not start with {Version1}. {FirstBytes(path)}";
			}

			return $"The manifest did not read. {ex.Message}";
		}

		private static string FirstBytes(string path)
		{
			try
			{
				byte[] head = File.ReadAllBytes(path);
				int count = Math.Min(16, head.Length);
				var text = new List<string>(count);

				for (int i = 0; i < count; ++i) text.Add(head[i].ToString("X2"));

				return $"The first {count} bytes are: {String.Join(" ", text)}.";
			}
			catch (Exception)
			{
				return "The file could not be read a second time.";
			}
		}
	}
}
