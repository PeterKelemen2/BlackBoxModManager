using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlackboxModManager.Core.Files;

namespace BlackboxModManager.Core.Store
{
	/// <summary>
	/// The metadata file of one mod. It sits beside the content, and it serializes to JSON.
	/// Keep it a plain data holder.
	/// </summary>
	public sealed class ModManifest
	{
		/// <summary>The shape of the file. Raise this when a change needs a migration.</summary>
		public int Version { get; set; } = 1;

		/// <summary>The directory name of the mod inside the store. It never changes.</summary>
		public string Id { get; set; }

		/// <summary>The name that the UI shows. The user can change this.</summary>
		public string Name { get; set; }

		public ModKind Kind { get; set; }

		/// <summary>
		/// The GameINT name that the mod belongs to. This is null when the mod names no
		/// game. Only a Binary manifest names one.
		/// </summary>
		public string Game { get; set; }

		/// <summary>The file or directory that the import read.</summary>
		public string Source { get; set; }

		public DateTimeOffset Imported { get; set; }

		public int FileCount { get; set; }

		public long TotalBytes { get; set; }

		public List<string> Notes { get; set; } = new List<string>();
	}

	/// <summary>
	/// One mod in the store, with the paths that go with it.
	/// </summary>
	public sealed class InstalledMod
	{
		public ModManifest Manifest { get; }

		/// <summary>The directory of the mod inside the store.</summary>
		public string Root { get; }

		public string Id => this.Manifest.Id;

		public string Name => this.Manifest.Name;

		public ModKind Kind => this.Manifest.Kind;

		/// <summary>
		/// The directory that holds the files of the mod. A deploy reads from here, and it
		/// keeps the relative path of every file.
		/// </summary>
		public string ContentRoot => Path.Combine(this.Root, ModStore.ContentFolder);

		internal InstalledMod(ModManifest manifest, string root)
		{
			this.Manifest = manifest;
			this.Root = root;
		}

		public override string ToString() => $"{this.Name} ({this.Kind})";
	}

	/// <summary>
	/// The managed mod store.
	///
	/// Every mod lives in its own directory outside every game directory. A game reinstall
	/// therefore deletes no mod. The layout of one mod is two entries.
	///
	/// 1. mod.json, the metadata.
	/// 2. content, the files of the mod at their game-relative paths.
	/// </summary>
	public sealed class ModStore
	{
		public const string ContentFolder = "content";
		public const string ManifestFile = "mod.json";

		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			Converters = { new JsonStringEnumConverter() },
		};

		public string Root { get; }

		public ModStore() : this(AppPaths.ModsDirectory) { }

		public ModStore(string root)
		{
			if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("The root is empty.", nameof(root));

			this.Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
		}

		/// <summary>
		/// Returns every mod in the store, by name. A directory with no readable metadata is
		/// not a mod, and this method skips it.
		/// </summary>
		public IReadOnlyList<InstalledMod> List()
		{
			var found = new List<InstalledMod>();

			if (!Directory.Exists(this.Root)) return found;

			foreach (string directory in Directory.EnumerateDirectories(this.Root))
			{
				InstalledMod mod = ReadDirectory(directory);
				if (mod != null) found.Add(mod);
			}

			found.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			return found;
		}

		public InstalledMod Find(string id)
		{
			if (String.IsNullOrWhiteSpace(id)) return null;

			return ReadDirectory(Path.Combine(this.Root, id));
		}

		/// <summary>
		/// Writes the metadata of one mod. Call this after a change to the name.
		/// </summary>
		public void Save(InstalledMod mod)
		{
			if (mod is null) throw new ArgumentNullException(nameof(mod));

			Directory.CreateDirectory(mod.Root);

			string path = Path.Combine(mod.Root, ManifestFile);
			string temporary = path + ".tmp";

			File.WriteAllText(temporary, JsonSerializer.Serialize(mod.Manifest, Options));
			File.Move(temporary, path, true);
		}

		/// <summary>
		/// Removes one mod and its files from the store. It does nothing when the mod is
		/// absent. This never touches a game directory.
		/// </summary>
		public void Remove(string id)
		{
			if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("The identifier is empty.", nameof(id));

			string directory = Path.Combine(this.Root, id);

			// Guard against an identifier that walks out of the store.
			if (!FileTree.IsSameOrInside(directory, this.Root) || String.Equals(
				Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), this.Root, StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException($"The identifier \"{id}\" does not name a mod in the store.", nameof(id));
			}

			FileTree.Delete(directory);
		}

		/// <summary>
		/// Adds a directory to the store under a free identifier, and writes the metadata.
		/// ModImporter calls this. It moves the source directory, so the source must sit on
		/// the volume of the store.
		/// </summary>
		internal InstalledMod Adopt(string contentDirectory, ModManifest manifest)
		{
			Directory.CreateDirectory(this.Root);

			manifest.Id = this.FreeId(manifest.Name);

			string root = Path.Combine(this.Root, manifest.Id);
			Directory.CreateDirectory(root);
			Directory.Move(contentDirectory, Path.Combine(root, ContentFolder));

			var mod = new InstalledMod(manifest, root);
			this.Save(mod);

			return mod;
		}

		private InstalledMod ReadDirectory(string directory)
		{
			string path = Path.Combine(directory, ManifestFile);

			try
			{
				if (!File.Exists(path)) return null;

				ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(path), Options);

				if (manifest is null) return null;

				// The directory name is the identifier. A copied directory then reads under
				// its new name, and no two mods claim one identifier.
				manifest.Id = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));

				if (String.IsNullOrWhiteSpace(manifest.Name)) manifest.Name = manifest.Id;

				return new InstalledMod(manifest, Path.GetFullPath(directory));
			}
			catch (Exception)
			{
				// A damaged metadata file hides one mod. It must not stop the list.
				return null;
			}
		}

		/// <summary>
		/// Turns a name into a directory name that no mod uses yet.
		/// </summary>
		private string FreeId(string name)
		{
			string slug = Slug(name);
			string candidate = slug;

			for (int suffix = 2; Directory.Exists(Path.Combine(this.Root, candidate)); ++suffix)
			{
				candidate = $"{slug}-{suffix}";
			}

			return candidate;
		}

		/// <summary>
		/// Turns a mod name into a safe directory name.
		///
		/// A mod name comes from an archive name, and it can hold any character. The
		/// identifier goes into a path, so it keeps letters, digits, and three separators.
		/// </summary>
		public static string Slug(string name)
		{
			var text = new StringBuilder();

			foreach (char c in name ?? String.Empty)
			{
				if (Char.IsLetterOrDigit(c)) text.Append(Char.ToLowerInvariant(c));
				else if ((c == '_' || c == '.') && text.Length > 0) text.Append(c);
				else if (text.Length > 0 && text[^1] != '-') text.Append('-');
			}

			string slug = text.ToString().Trim('-', '.');

			// A long name makes a path that Windows rejects. Keep the head, which carries
			// the part that a user recognizes.
			if (slug.Length > 64) slug = slug.Substring(0, 64).TrimEnd('-', '.');

			return slug.Length == 0 ? "mod" : slug;
		}
	}
}
