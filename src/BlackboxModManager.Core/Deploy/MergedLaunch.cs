using System;
using System.Collections.Generic;
using System.IO;
using BlackboxModManager.Core.Mods;
using Endscript.Core;
using Endscript.Enums;
using Endscript.Helpers;

namespace BlackboxModManager.Core.Deploy
{
	/// <summary>
	/// The one synthetic manifest that the whole deploy loads from, and what went into it.
	/// </summary>
	public sealed class MergedLoad
	{
		public Launch Launch { get; }

		/// <summary>
		/// The container files, in the spelling that the manifests use. This is the union of
		/// every enabled variant, with no duplicate.
		/// </summary>
		public IReadOnlyList<string> Files { get; }

		/// <summary>
		/// Which variants asked for each container file, keyed by the entry in Files.
		/// </summary>
		public IReadOnlyDictionary<string, IReadOnlyList<string>> Contributors { get; }

		/// <summary>Lines that the UI shows. An empty list is normal.</summary>
		public IReadOnlyList<string> Notes { get; }

		public MergedLoad(Launch launch, IReadOnlyList<string> files,
			IReadOnlyDictionary<string, IReadOnlyList<string>> contributors, IReadOnlyList<string> notes)
		{
			this.Launch = launch;
			this.Files = files;
			this.Contributors = contributors;
			this.Notes = notes ?? Array.Empty<string>();
		}
	}

	/// <summary>
	/// Builds the one manifest that the single pass loads from.
	///
	/// <b>Load once. Apply every enabled mod. Save once.</b> This class exists to make the
	/// first part of that rule possible. Every enabled variant contributes its container
	/// files to one union, and the deploy loads that union one time.
	/// </summary>
	public static class MergedLaunch
	{
		/// <summary>
		/// Builds the synthetic manifest for a set of enabled variants.
		///
		/// stagingDirectory becomes Directory. Every container path and every absolute link
		/// resolves against it, so it must be the staging copy and never the live install.
		/// </summary>
		public static MergedLoad Build(IReadOnlyList<EnabledVariant> variants, string stagingDirectory)
		{
			if (variants is null) throw new ArgumentNullException(nameof(variants));
			if (String.IsNullOrWhiteSpace(stagingDirectory)) throw new ArgumentException("The staging directory is empty.", nameof(stagingDirectory));

			string staging = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDirectory));

			var files = new List<string>();
			var contributors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			var notes = new List<string>();

			// The normalized key answers "is this the same container". The stored value keeps
			// the spelling, because the library needs the spelling. See SpellingOf below.
			var byKey = new Dictionary<string, string>(StringComparer.Ordinal);

			var links = new List<SubLoader>();
			var linkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (EnabledVariant variant in variants)
			{
				foreach (string file in variant.Variant.Manifest.Files ?? new List<string>())
				{
					if (String.IsNullOrWhiteSpace(file)) continue;

					string key = PathKey.Normalize(file);

					if (byKey.TryGetValue(key, out string chosen))
					{
						// Two spellings of one container cannot share one load. See the
						// note on the method below.
						if (!String.Equals(chosen, file, StringComparison.Ordinal))
						{
							throw new DeployServiceException(SpellingProblem(chosen, file, contributors, variant));
						}

						contributors[chosen].Add(variant.Label);
						continue;
					}

					byKey[key] = file;
					files.Add(file);
					contributors[file] = new List<string> { variant.Label };
				}

				foreach (SubLoader link in variant.Variant.Manifest.Links ?? new List<SubLoader>())
				{
					SubLoader resolved = Resolve(link, variant, staging, notes);

					if (resolved is null) continue;

					// Two mods of one game carry identical links. Binary writes them as
					// per-game boilerplate, so the union is short.
					if (!linkKeys.Add($"{resolved.LoadType}|{resolved.File}")) continue;

					links.Add(resolved);
				}
			}

			var launch = new Launch
			{
				Game = GameOf(variants),
				Directory = staging,

				// The library never reads UsageID. We set the field because the manifest
				// declares what the run is, and a reader of the file expects it.
				Usage = nameof(eUsage.Modder),

				// Every variant brings its own script. The deploy runs each one against the
				// profile that this manifest loads, so this field names none of them.
				Endscript = String.Empty,
				Files = new List<string>(files),
				Links = links,

				// CheckEndscript would resolve against this. The deploy never calls it,
				// because Endscript is empty on purpose.
				ThisDir = staging,
			};

			var readOnly = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, List<string>> entry in contributors) readOnly[entry.Key] = entry.Value;

			return new MergedLoad(launch, files, readOnly, notes);
		}

		/// <summary>
		/// Turns one link of one variant into a link that the merged manifest can carry.
		///
		/// LoadLinks resolves a Relative link against ThisDir and an Absolute link against
		/// Directory. One synthetic manifest has one ThisDir, and the variants have several,
		/// so a relative link of one mod cannot resolve through it.
		///
		/// The way out is a rooted path. Path.Combine returns the second argument unchanged
		/// when that argument is rooted, so a link that already holds a full path resolves
		/// to itself whatever the base is. Resolve every link here and store the full path.
		/// </summary>
		private static SubLoader Resolve(SubLoader link, EnabledVariant variant, string staging,
			List<string> notes)
		{
			if (link is null || String.IsNullOrWhiteSpace(link.File)) return null;

			string baseDirectory = link.PType == ePathType.Relative
				? variant.Variant.Manifest.ThisDir
				: staging;

			if (String.IsNullOrWhiteSpace(baseDirectory))
			{
				notes.Add($"The variant \"{variant.Label}\" carries the link {link.File} of type " +
					$"{link.LoadType}, and its base directory is empty. The deploy leaves that link out.");

				return null;
			}

			string full = ModPath.Resolve(baseDirectory, link.File);

			if (!File.Exists(full))
			{
				// A missing link file is normal, not an error. Binary writes the same four
				// links into every manifest of one game, and a vanilla Underground 2 install
				// holds only LANGUAGES\Labels.bin of them. Every loader in Nikki returns for
				// a file that does not exist, so the library would skip this link anyway.
				// Leave it out, and say so once.
				string note = $"The game holds no {link.File}, so the deploy skips that {link.LoadType} link. " +
					"A manifest names the same links for every mod of one game, and an install holds only some.";

				// Every variant of every mod names the same links. Say it once.
				if (!notes.Contains(note)) notes.Add(note);

				return null;
			}

			return new SubLoader
			{
				LoadType = link.LoadType,
				PathType = nameof(ePathType.Absolute),
				File = full,
			};
		}

		/// <summary>
		/// Explains why two spellings of one container stop a deploy.
		///
		/// CollectionMap keys every collection by the container file name exactly as the
		/// manifest wrote it, and a command looks its target up by the string that the
		/// script wrote. Neither side normalizes the separator or the letter case. One load
		/// therefore carries one spelling, and a mod that writes the other spelling fails
		/// its lookup with "Collection named X does not exist".
		///
		/// Loading both spellings is worse. BaseProfile.Contains compares the raw strings,
		/// so it would accept both and build two containers for one file. Save then writes
		/// that file twice and the edits of the first mod disappear with no error.
		/// </summary>
		private static string SpellingProblem(string chosen, string other,
			Dictionary<string, List<string>> contributors, EnabledVariant variant)
		{
			string owners = contributors.TryGetValue(chosen, out List<string> list)
				? String.Join(", ", list)
				: "an earlier mod";

			return $"Two mods name one container in two ways. {owners} writes \"{chosen}\" and " +
				$"\"{variant.Label}\" writes \"{other}\". The container editor matches that name as plain " +
				"text, so one load cannot serve both spellings. Deploy these mods one at a time.";
		}

		private static string GameOf(IReadOnlyList<EnabledVariant> variants)
		{
			return variants.Count > 0 ? variants[0].Variant.Game.ToString() : Nikki.Core.GameINT.None.ToString();
		}
	}
}
