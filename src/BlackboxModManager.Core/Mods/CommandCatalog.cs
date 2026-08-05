using System;
using System.Collections.Generic;
using Endscript.Enums;

namespace BlackboxModManager.Core.Mods
{
	/// <summary>
	/// What one endscript verb does to the game.
	///
	/// The category decides the conflict key. It does not decide whether the deploy runs the
	/// command. <see cref="CommandSupport"/> decides that.
	/// </summary>
	public enum CommandCategory
	{
		/// <summary>
		/// The command writes one value into one named field. The key is the container plus
		/// the name path of the field. Two mods that write one key with two values conflict.
		/// </summary>
		ScalarFieldWrite = 0,

		/// <summary>
		/// The command adds a named thing to a container, or it removes one. The key is the
		/// container plus the name path of the thing.
		/// </summary>
		ExistenceChange,

		/// <summary>
		/// The command replaces the pixels of a texture, or of a whole texture pack. The key
		/// has the same shape as an existence change. The category is separate because the
		/// value is a file on disk and not a number.
		/// </summary>
		TextureOperation,

		/// <summary>
		/// The command reads a path or writes one. The key is the path. This category leaves
		/// the container model, so <see cref="PathSandbox"/> checks every path.
		/// </summary>
		FilesystemEffect,

		/// <summary>
		/// The command chooses which other commands run. It carries no key. The flattener
		/// resolves it, and no edit list holds it.
		/// </summary>
		ControlFlow,

		/// <summary>
		/// The command changes the state of the library or of the process. It carries no key.
		/// Each one needs its own handling.
		/// </summary>
		ProcessOrMetadata,

		/// <summary>
		/// The verb belongs to a version 4 manifest and not to a version 2 script.
		/// <c>EndDeserializer</c> reads it. <c>EndScriptParser</c> turns it into an
		/// <c>OptionalCommand</c>, which fails at execution time.
		/// </summary>
		ManifestKey,

		/// <summary>
		/// The verb has no classification. Treat it as opaque, warn, and record it.
		/// <b>Never treat this as conflict free.</b> Silence is the failure that step 8
		/// exists to prevent.
		/// </summary>
		Unclassified,
	}

	/// <summary>
	/// Whether this application runs the command.
	/// </summary>
	public enum CommandSupport
	{
		/// <summary>The deploy runs the command and the revert undoes it.</summary>
		Supported = 0,

		/// <summary>
		/// The deploy runs the command and the tool cannot compare it against another mod.
		/// The preflight reports one warning for each use.
		/// </summary>
		Warn,

		/// <summary>
		/// The deploy stops before it writes anything. The message names the mod, the file,
		/// and the line.
		/// </summary>
		Reject,
	}

	/// <summary>
	/// Where a path argument starts.
	/// </summary>
	public enum PathAnchor
	{
		/// <summary>The directory of the launcher script. This is the mod directory.</summary>
		ModDirectory = 0,

		/// <summary>The directory of the profile. This is the staging copy of the game.</summary>
		GameDirectory,

		/// <summary>
		/// A token of the command chooses the anchor. The word <c>relative</c> means the mod
		/// directory and the word <c>absolute</c> means the game directory.
		/// </summary>
		ByTypeToken,
	}

	/// <summary>
	/// One path that a command reads or writes.
	/// </summary>
	public sealed class PathArgument
	{
		/// <summary>The token that holds the path.</summary>
		public int PathToken { get; }

		/// <summary>The token that holds the anchor word, or -1 when the anchor is fixed.</summary>
		public int TypeToken { get; }

		public PathAnchor Anchor { get; }

		/// <summary>True when the command creates, deletes, or overwrites the path.</summary>
		public bool Writes { get; }

		public PathArgument(int pathToken, int typeToken, PathAnchor anchor, bool writes)
		{
			this.PathToken = pathToken;
			this.TypeToken = typeToken;
			this.Anchor = anchor;
			this.Writes = writes;
		}
	}

	/// <summary>
	/// The classification of one endscript verb.
	///
	/// Every field here comes from the source of the command in
	/// <c>third_party/Endscript/Endscript/Commands</c>. The token numbers match the
	/// <c>Prepare</c> method of that class. Token 0 is the verb.
	/// </summary>
	public sealed class CommandFacts
	{
		public eCommandType Verb { get; }

		public CommandCategory Category { get; }

		public CommandSupport Support { get; }

		/// <summary>The token that names the container, or -1 when the command names none.</summary>
		public int FileToken { get; }

		/// <summary>
		/// The tokens that form the name path of the key, in order. An empty array with a
		/// file token means that the key is the container itself.
		/// </summary>
		public IReadOnlyList<int> KeyTokens { get; }

		/// <summary>The tokens that the report shows as the value, in order.</summary>
		public IReadOnlyList<int> ValueTokens { get; }

		/// <summary>
		/// True when the key path sits between the container token and the last token. The
		/// four <c>update_</c> verbs and <c>static</c> work this way. Their token count
		/// varies, so no fixed list of token numbers describes them.
		/// </summary>
		public bool UsesMiddleSpan { get; }

		/// <summary>True when the command deletes the thing that the key names.</summary>
		public bool Removes { get; }

		/// <summary>
		/// True when the key names a container that the command changes, and the names inside
		/// stay unknown until the deploy runs. An import reads its collection names out of a
		/// binary file. A texture bind reads them out of a directory listing.
		/// </summary>
		public bool Opaque { get; }

		public IReadOnlyList<PathArgument> Paths { get; }

		/// <summary>The smallest token count that the library accepts.</summary>
		public int MinTokens { get; }

		/// <summary>The largest token count that the library accepts, or -1 for no limit.</summary>
		public int MaxTokens { get; }

		/// <summary>Why this verb needs care. The preflight shows this for a warn or a reject.</summary>
		public string Note { get; }

		public CommandFacts(eCommandType verb, CommandCategory category, CommandSupport support,
			int minTokens, int maxTokens, int fileToken = -1, int[] keyTokens = null,
			int[] valueTokens = null, bool usesMiddleSpan = false, bool removes = false,
			bool opaque = false, PathArgument[] paths = null, string note = null)
		{
			this.Verb = verb;
			this.Category = category;
			this.Support = support;
			this.MinTokens = minTokens;
			this.MaxTokens = maxTokens;
			this.FileToken = fileToken;
			this.KeyTokens = keyTokens ?? Array.Empty<int>();
			this.ValueTokens = valueTokens ?? Array.Empty<int>();
			this.UsesMiddleSpan = usesMiddleSpan;
			this.Removes = removes;
			this.Opaque = opaque;
			this.Paths = paths ?? Array.Empty<PathArgument>();
			this.Note = note ?? String.Empty;
		}

		/// <summary>True when this verb produces a conflict key.</summary>
		public bool HasKey => this.FileToken >= 0;
	}

	/// <summary>
	/// The classification of every verb of <c>eCommandType</c>.
	///
	/// The enum holds 48 entries and this catalog holds 48 entries. A test compares the two
	/// counts, so a library update that adds a verb fails that test at once.
	///
	/// <b>Read the command source before you change an entry here.</b> The token numbers are
	/// the argument order of the <c>Prepare</c> method, and the project brief describes some
	/// of them wrongly. See <c>docs/roadmap/99-api-notes.md</c>.
	/// </summary>
	public static class CommandCatalog
	{
		private static readonly Dictionary<eCommandType, CommandFacts> Table = Build();

		/// <summary>Every classified verb.</summary>
		public static IReadOnlyDictionary<eCommandType, CommandFacts> All => Table;

		/// <summary>
		/// Returns the classification of a verb. An unknown verb returns an unclassified
		/// record rather than null, so no caller can skip it by accident.
		/// </summary>
		public static CommandFacts Lookup(eCommandType verb)
		{
			if (Table.TryGetValue(verb, out CommandFacts facts)) return facts;

			return new CommandFacts(verb, CommandCategory.Unclassified, CommandSupport.Warn, 1, -1,
				note: "This verb has no classification in this application. " +
					"Read the command source and add one.");
		}

		/// <summary>The verbs of one category.</summary>
		public static IReadOnlyList<eCommandType> Of(CommandCategory category)
		{
			var list = new List<eCommandType>();

			foreach (KeyValuePair<eCommandType, CommandFacts> entry in Table)
			{
				if (entry.Value.Category == category) list.Add(entry.Key);
			}

			list.Sort();

			return list;
		}

		private static Dictionary<eCommandType, CommandFacts> Build()
		{
			var table = new Dictionary<eCommandType, CommandFacts>();

			void Add(CommandFacts facts) => table.Add(facts.Verb, facts);

			// ---------------------------------------------------------------------------
			// Scalar field write. The key is the container plus the name path, and the last
			// token is the value.
			//
			// Do not read a token count from this group. update_collection accepts 6 or 8
			// and update_incareer accepts 8 or 10. The middle span rule covers every form.
			// ---------------------------------------------------------------------------

			Add(new CommandFacts(eCommandType.update_collection, CommandCategory.ScalarFieldWrite,
				CommandSupport.Supported, 6, 8, fileToken: 1, usesMiddleSpan: true));

			Add(new CommandFacts(eCommandType.update_incareer, CommandCategory.ScalarFieldWrite,
				CommandSupport.Supported, 8, 10, fileToken: 1, usesMiddleSpan: true));

			Add(new CommandFacts(eCommandType.update_string, CommandCategory.ScalarFieldWrite,
				CommandSupport.Supported, 7, 7, fileToken: 1, usesMiddleSpan: true));

			Add(new CommandFacts(eCommandType.update_texture, CommandCategory.ScalarFieldWrite,
				CommandSupport.Supported, 7, 7, fileToken: 1, usesMiddleSpan: true,
				note: "This verb writes one property of a texture. It does not replace the pixels. " +
					"replace_texture does that."));

			// static writes a property of the manager and not of a collection. The shape is
			// the same, so the middle span rule gives the key (file, manager, property).
			Add(new CommandFacts(eCommandType.@static, CommandCategory.ScalarFieldWrite,
				CommandSupport.Supported, 5, 5, fileToken: 1, usesMiddleSpan: true,
				note: "The key names the manager and not a collection. A static property covers " +
					"every collection of that manager, so it can change what another mod reads."));

			// ---------------------------------------------------------------------------
			// Existence change. The key names the thing that appears or disappears.
			// ---------------------------------------------------------------------------

			Add(new CommandFacts(eCommandType.add_collection, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 4, 4, fileToken: 1, keyTokens: new[] { 2, 3 }));

			Add(new CommandFacts(eCommandType.remove_collection, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 4, 4, fileToken: 1, keyTokens: new[] { 2, 3 }, removes: true));

			// copy_collection [file] [manager] [from] [to]. The key is the new name.
			Add(new CommandFacts(eCommandType.copy_collection, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 5, 5, fileToken: 1, keyTokens: new[] { 2, 4 },
				valueTokens: new[] { 3 },
				note: "The command reads the source collection. A mod that removes the source " +
					"before this command runs makes this command fail."));

			Add(new CommandFacts(eCommandType.add_incareer, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 6, 6, fileToken: 1, keyTokens: new[] { 2, 3, 4, 5 }));

			Add(new CommandFacts(eCommandType.remove_incareer, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 6, 6, fileToken: 1, keyTokens: new[] { 2, 3, 4, 5 }, removes: true));

			// copy_incareer [file] [manager] [gcareer] [root] [from] [to].
			Add(new CommandFacts(eCommandType.copy_incareer, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 7, 7, fileToken: 1, keyTokens: new[] { 2, 3, 4, 6 },
				valueTokens: new[] { 5 }));

			// add_string [file] [manager] [strblock] [key] [label] [text]. The record key is
			// token 4. The label and the text are the value.
			Add(new CommandFacts(eCommandType.add_string, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 7, 7, fileToken: 1, keyTokens: new[] { 2, 3, 4 },
				valueTokens: new[] { 5, 6 }));

			Add(new CommandFacts(eCommandType.remove_string, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 5, 5, fileToken: 1, keyTokens: new[] { 2, 3, 4 }, removes: true));

			Add(new CommandFacts(eCommandType.add_or_update_string, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 7, 7, fileToken: 1, keyTokens: new[] { 2, 3, 4 },
				valueTokens: new[] { 5, 6 },
				note: "The command adds the record, and it writes the text when the record already " +
					"exists. Two mods that name one record with two texts conflict."));

			// new [type] [filename] creates a container. The key is the container itself, so
			// it is a prefix of every key that names that container.
			Add(new CommandFacts(eCommandType.@new, CommandCategory.ExistenceChange,
				CommandSupport.Warn, 3, 3, fileToken: 2,
				note: "The command creates a container and reloads the collection map. " +
					"MergedLaunch builds the load list before the deploy, so a container that " +
					"this command creates is not in that list."));

			// delete [filename] removes a container from the profile.
			Add(new CommandFacts(eCommandType.delete, CommandCategory.ExistenceChange,
				CommandSupport.Warn, 2, 2, fileToken: 1, removes: true,
				note: "The command removes a whole container from the load. Every edit that " +
					"another mod makes to that container then fails."));

			// import [type] [filename] [manager] [path]. The names inside the file stay
			// unknown until the deploy reads it.
			Add(new CommandFacts(eCommandType.import, CommandCategory.ExistenceChange,
				CommandSupport.Warn, 5, 5, fileToken: 2, keyTokens: new[] { 3 }, opaque: true,
				paths: new[] { new PathArgument(4, -1, PathAnchor.ModDirectory, false) },
				note: "The command reads its collection names out of a binary file. The tool " +
					"cannot name what it changes until the deploy runs."));

			Add(new CommandFacts(eCommandType.import_all, CommandCategory.ExistenceChange,
				CommandSupport.Warn, 5, 5, fileToken: 2, keyTokens: new[] { 3 }, opaque: true,
				paths: new[] { new PathArgument(4, -1, PathAnchor.ModDirectory, false) },
				note: "The command reads every file of a directory. The tool cannot name what it " +
					"changes until the deploy runs."));

			// ---------------------------------------------------------------------------
			// Existence change on a texture. The roadmap groups add and remove here, and it
			// groups a pixel replacement under Texture operation. The key shape is the same.
			// ---------------------------------------------------------------------------

			Add(new CommandFacts(eCommandType.add_texture, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 6, 6, fileToken: 1, keyTokens: new[] { 2, 3, 4 },
				valueTokens: new[] { 5 },
				paths: new[] { new PathArgument(5, -1, PathAnchor.ModDirectory, false) }));

			Add(new CommandFacts(eCommandType.remove_texture, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 5, 5, fileToken: 1, keyTokens: new[] { 2, 3, 4 }, removes: true));

			// copy_texture [file] [manager] [tpk] [from] [to].
			Add(new CommandFacts(eCommandType.copy_texture, CommandCategory.ExistenceChange,
				CommandSupport.Supported, 6, 6, fileToken: 1, keyTokens: new[] { 2, 3, 5 },
				valueTokens: new[] { 4 }));

			// ---------------------------------------------------------------------------
			// Texture operation. The value is a file on disk.
			// ---------------------------------------------------------------------------

			Add(new CommandFacts(eCommandType.replace_texture, CommandCategory.TextureOperation,
				CommandSupport.Supported, 6, 6, fileToken: 1, keyTokens: new[] { 2, 3, 4 },
				valueTokens: new[] { 5 },
				paths: new[] { new PathArgument(5, -1, PathAnchor.ModDirectory, false) }));

			// add_or_replace_texture [file] [manager] [tpk] [key] [cname] [path].
			Add(new CommandFacts(eCommandType.add_or_replace_texture, CommandCategory.TextureOperation,
				CommandSupport.Supported, 7, 7, fileToken: 1, keyTokens: new[] { 2, 3, 4 },
				valueTokens: new[] { 6 },
				paths: new[] { new PathArgument(6, -1, PathAnchor.ModDirectory, false) },
				note: "The command adds the texture, and it replaces the pixels when the texture " +
					"already exists."));

			// bind_textures [type] [file] [manager] [tpk] [path]. The key is the whole pack.
			Add(new CommandFacts(eCommandType.bind_textures, CommandCategory.TextureOperation,
				CommandSupport.Warn, 6, 6, fileToken: 2, keyTokens: new[] { 3, 4 }, opaque: true,
				valueTokens: new[] { 5 },
				paths: new[] { new PathArgument(5, -1, PathAnchor.ModDirectory, false) },
				note: "The command reads a directory listing and changes one texture for each " +
					"file. The tool cannot name those textures until the deploy runs. The key " +
					"names the whole texture pack."));

			// ---------------------------------------------------------------------------
			// Filesystem effect. The key is the path. PathSandbox checks every one of these.
			// ---------------------------------------------------------------------------

			// create_file [creationtype] [pathtype] [path].
			Add(new CommandFacts(eCommandType.create_file, CommandCategory.FilesystemEffect,
				CommandSupport.Supported, 4, 4,
				paths: new[] { new PathArgument(3, 2, PathAnchor.ByTypeToken, true) }));

			// create_folder [pathtype] [path].
			Add(new CommandFacts(eCommandType.create_folder, CommandCategory.FilesystemEffect,
				CommandSupport.Supported, 3, 3,
				paths: new[] { new PathArgument(2, 1, PathAnchor.ByTypeToken, true) }));

			Add(new CommandFacts(eCommandType.erase_file, CommandCategory.FilesystemEffect,
				CommandSupport.Supported, 3, 3,
				paths: new[] { new PathArgument(2, 1, PathAnchor.ByTypeToken, true) }));

			// erase_folder deletes the tree under the path.
			Add(new CommandFacts(eCommandType.erase_folder, CommandCategory.FilesystemEffect,
				CommandSupport.Supported, 3, 3,
				paths: new[] { new PathArgument(2, 1, PathAnchor.ByTypeToken, true) },
				note: "The command deletes the whole tree under the path."));

			// move_file [movetype] [fromtype] [totype] [from] [to]. The library copies and it
			// does not delete the source, so only the target takes a write.
			Add(new CommandFacts(eCommandType.move_file, CommandCategory.FilesystemEffect,
				CommandSupport.Supported, 6, 6,
				paths: new[]
				{
					new PathArgument(4, 2, PathAnchor.ByTypeToken, false),
					new PathArgument(5, 3, PathAnchor.ByTypeToken, true),
				},
				note: "The command copies the file. It does not delete the source, so the name " +
					"of the verb is wrong."));

			// unlock_memory [all/file] rewrites a memory file of the game directory.
			Add(new CommandFacts(eCommandType.unlock_memory, CommandCategory.FilesystemEffect,
				CommandSupport.Supported, 2, 2,
				paths: new[] { new PathArgument(1, -1, PathAnchor.GameDirectory, true) },
				note: "The command writes a short header over a memory file of the game. It is a " +
					"disk edit. The word 'all' names the five memory files of the profile."));

			// unpack_stream [lxry] [streamlxry] [dest] writes a tree under the game directory.
			Add(new CommandFacts(eCommandType.unpack_stream, CommandCategory.FilesystemEffect,
				CommandSupport.Warn, 4, 4,
				paths: new[]
				{
					new PathArgument(1, -1, PathAnchor.GameDirectory, false),
					new PathArgument(2, -1, PathAnchor.GameDirectory, false),
					new PathArgument(3, -1, PathAnchor.GameDirectory, true),
				},
				note: "The command writes one directory for each section of the stream. The tool " +
					"cannot name those directories until the deploy runs."));

			// pack_stream [lxry] [streamlxry] [source] rewrites the two stream containers.
			Add(new CommandFacts(eCommandType.pack_stream, CommandCategory.FilesystemEffect,
				CommandSupport.Warn, 4, 4,
				paths: new[]
				{
					new PathArgument(1, -1, PathAnchor.GameDirectory, true),
					new PathArgument(2, -1, PathAnchor.GameDirectory, true),
					new PathArgument(3, -1, PathAnchor.GameDirectory, false),
				},
				note: "The command rewrites two containers outside the single load and save pass. " +
					"StagingFiles.MakePrivate never sees them, so a hard link would carry the " +
					"write into the game install."));

			// speedreflect [auto/dir] copies SpeedReflect.asi beside our executable.
			Add(new CommandFacts(eCommandType.speedreflect, CommandCategory.FilesystemEffect,
				CommandSupport.Reject, 2, 2,
				paths: new[] { new PathArgument(1, -1, PathAnchor.GameDirectory, true) },
				note: "The command copies SpeedReflect.asi out of the directory of the running " +
					"executable. SpeedReflect is GPL-3.0 and this application does not ship it, " +
					"so the command always fails."));

			// ---------------------------------------------------------------------------
			// Control flow. The flattener resolves these and no edit list holds them.
			// ---------------------------------------------------------------------------

			// The parser splices the appended file inline and drops the command, so no
			// command list ever holds one.
			Add(new CommandFacts(eCommandType.append, CommandCategory.ControlFlow,
				CommandSupport.Supported, 2, 2,
				note: "EndScriptParser reads the named file and adds its commands in place. " +
					"ScriptAppendGraph walks the same graph first, because the parser keeps no " +
					"cycle guard."));

			Add(new CommandFacts(eCommandType.checkbox, CommandCategory.ControlFlow,
				CommandSupport.Supported, 2, 2,
				note: "The options are always 'disabled' and 'enabled', in that order."));

			Add(new CommandFacts(eCommandType.combobox, CommandCategory.ControlFlow,
				CommandSupport.Supported, 4, -1,
				note: "The quoted option names come before the description. A stored answer holds " +
					"an option name and not the name of an appended file."));

			Add(new CommandFacts(eCommandType.@if, CommandCategory.ControlFlow,
				CommandSupport.Warn, 2, -1,
				note: "The command reads the loaded containers. A static walk cannot know the " +
					"branch, so the flattener walks both and marks every edit inside as " +
					"conditional."));

			Add(new CommandFacts(eCommandType.end, CommandCategory.ControlFlow,
				CommandSupport.Supported, 1, 1));

			// ---------------------------------------------------------------------------
			// Process or metadata. No key. Each one needs its own handling.
			// ---------------------------------------------------------------------------

			Add(new CommandFacts(eCommandType.version, CommandCategory.ProcessOrMetadata,
				CommandSupport.Supported, 2, 2,
				note: "Prepare compares the version and throws. The parse fails, and no error " +
					"reaches the error list of the manager."));

			Add(new CommandFacts(eCommandType.watermark, CommandCategory.ProcessOrMetadata,
				CommandSupport.Warn, 2, 2,
				note: "The command writes a static field of SynchronizedDatabase. The value is " +
					"process wide, so the last mod of the pass names every container that the " +
					"save writes."));

			Add(new CommandFacts(eCommandType.stop_errors, CommandCategory.ProcessOrMetadata,
				CommandSupport.Reject, 2, 2,
				note: "The command tells the manager to drop every later error of this script. " +
					"Our rule is that one error entry fails the deploy. This command defeats " +
					"that rule, and a broken mod then looks like a mod that installed."));

			// ---------------------------------------------------------------------------
			// Manifest keys. EndDeserializer reads these out of a version 4 manifest.
			// EndScriptParser has no class for them, so a version 2 script that holds one
			// parses to an OptionalCommand and fails at execution time.
			// ---------------------------------------------------------------------------

			const string ManifestOnly =
				"This verb belongs to a version 4 manifest. A version 2 script that holds it " +
				"parses to an unknown verb and the deploy stops.";

			Add(new CommandFacts(eCommandType.game, CommandCategory.ManifestKey,
				CommandSupport.Reject, 2, 2, note: ManifestOnly));

			Add(new CommandFacts(eCommandType.directory, CommandCategory.ManifestKey,
				CommandSupport.Reject, 2, 2, note: ManifestOnly));

			Add(new CommandFacts(eCommandType.filecount, CommandCategory.ManifestKey,
				CommandSupport.Reject, 2, 2, note: ManifestOnly));

			Add(new CommandFacts(eCommandType.capacity, CommandCategory.ManifestKey,
				CommandSupport.Reject, 2, -1, note: ManifestOnly));

			Add(new CommandFacts(eCommandType.generate, CommandCategory.ManifestKey,
				CommandSupport.Reject, 1, 1, note: ManifestOnly));

			// ---------------------------------------------------------------------------
			// Not a command. The parser never builds one of these from a script line.
			// ---------------------------------------------------------------------------

			Add(new CommandFacts(eCommandType.invalid, CommandCategory.Unclassified,
				CommandSupport.Reject, 1, -1,
				note: "The parser gives this type to every word that it does not know. " +
					"OptionalCommand carries the word, and the flattener names it and stops."));

			Add(new CommandFacts(eCommandType.empty, CommandCategory.ControlFlow,
				CommandSupport.Supported, 1, -1,
				note: "ExecuteSingleCommand returns this type for a blank line or a comment. " +
					"No command list ever holds one."));

			return table;
		}
	}
}
