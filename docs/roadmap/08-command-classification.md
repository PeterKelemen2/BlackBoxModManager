# Step 8 — Command classification

Handle the commands that sit outside the collection model.

**The problem:** conflict detection keys on `(targetFile, keyPath)`. That key describes a scalar field write. Many of the 48 commands do something else entirely, so the key does not describe them and conflict detection silently ignores them.

Our two example mods use 5 commands. Every other mod in the wild is unclassified territory.

## The vocabulary

From `Endscript/Enums/eCommandType.cs`, 48 entries:

`invalid`, `empty`, `game`, `version`, `append`, `update_collection`, `update_string`, `update_texture`, `update_incareer`, `add_collection`, `add_string`, `add_texture`, `add_incareer`, `remove_collection`, `remove_string`, `remove_texture`, `remove_incareer`, `copy_collection`, `copy_texture`, `copy_incareer`, `replace_texture`, `bind_textures`, `add_or_update_string`, `add_or_replace_texture`, `static`, `import`, `import_all`, `new`, `delete`, `watermark`, `create_file`, `create_folder`, `erase_file`, `erase_folder`, `move_file`, `generate`, `directory`, `filecount`, `capacity`, `checkbox`, `combobox`, `if`, `stop_errors`, `unlock_memory`, `speedreflect`, `unpack_stream`, `pack_stream`, `end`.

## Work

1. Read every file in `Endscript/Commands/`.
2. Classify each command into one of the categories below.
3. Record the classification in a table in this file.
4. Extend conflict detection per category.
5. Make an unclassified command produce a warning, not silence.

## Categories

| Category            | Conflict key                              | Examples                                                                  |
| ------------------- | ----------------------------------------- | ------------------------------------------------------------------------- |
| Scalar field write  | `(targetFile, keyPath)` — already handled | `update_collection`, `update_incareer`                                    |
| Existence change    | `(targetFile, collectionPath)`            | `add_*`, `remove_*`, `new`, `delete`                                      |
| Texture operation   | `(targetFile, texturePath)`               | `update_texture`, `replace_texture`, `bind_textures`                      |
| Filesystem effect   | The touched path                          | `create_file`, `erase_file`, `move_file`, `create_folder`, `erase_folder` |
| Control flow        | None. Resolved before extraction.         | `append`, `combobox`, `checkbox`, `if`, `end`                             |
| Process or metadata | None. Needs its own handling.             | `static`, `generate`, `unlock_memory`, `speedreflect`, `watermark`        |

## Pitfalls

**An existence change beats a field write.** A mod that removes a collection and a mod that edits a field inside it conflict, but their keys never match under the current scheme. Detect the case where one mod's key path is a prefix of another mod's removal target.

**Filesystem commands escape staging.** `create_file`, `erase_file`, `move_file`, and the folder commands act on paths, not on containers. A path outside the staging directory writes to the real system, and the revert logic never sees it. Sandbox these commands. Reject any resolved path outside staging, and name the command and the mod when you do.

**`if` makes scripts non-declarative.** The same script can produce different edits depending on state. A conflict list computed at import time can be wrong at deploy time. Recompute after resolution, not before.

**`unlock_memory` and `speedreflect` are not disk modding.** They target the running game. They cannot be staged, verified, or reverted the way file edits can. Decide explicitly whether to support them or to reject a mod that uses them. Do not let them pass through unnoticed.

**`stop_errors` suppresses reporting.** A script that uses it can fail silently inside the library. Our rule is that any `manager.Errors` entry fails the deploy. Confirm what this command does to that collection before you trust the rule.

**Never assume an unclassified command is conflict-free.** Silence is the failure mode this whole step exists to prevent. Treat unknown as opaque, warn, and record it for classification.

**Texture commands were once assumed impossible.** The brief originally proposed a separate binary-diff capture path for asset replacement. The vocabulary makes asset replacement expressible in `.end`, so that path was removed. Do not reintroduce it without a mod that genuinely needs it.

## Done when

Every one of the 48 commands has a recorded classification, conflict detection covers each category, and an unrecognized command produces a warning that names the file and the line.

## Results

**Step 8 is done.** All 48 verbs carry a classification. Conflict detection covers every category with a key. An unclassified verb produces a warning that names the file and the line, and the deploy stops before it writes for a verb that this application refuses.

`src/BlackboxModManager.Core/Mods/CommandCatalog.cs` holds the classification. It is the machine readable form of the table below, and a test compares its entry count against `eCommandType`. A library update that adds a verb fails that test at once.

Two pitfalls above were wrong. Read "Corrections to the pitfalls".

### The classification

Every row comes from the `Prepare` and `Execute` methods of the command class. The token numbers in `CommandCatalog` come from the same place.

| Verb                     | Category            | Key                                               | Support |
| ------------------------ | ------------------- | ------------------------------------------------- | ------- |
| `update_collection`      | Scalar field write  | file, manager, collection, (node, subpart), field | Yes     |
| `update_incareer`        | Scalar field write  | file, manager, career, root, collection, …, field | Yes     |
| `update_string`          | Scalar field write  | file, manager, block, key, property               | Yes     |
| `update_texture`         | Scalar field write  | file, manager, pack, key, property                | Yes     |
| `static`                 | Scalar field write  | file, manager, property                           | Yes     |
| `add_collection`         | Existence change    | file, manager, collection                         | Yes     |
| `remove_collection`      | Existence change    | file, manager, collection                         | Yes     |
| `copy_collection`        | Existence change    | file, manager, **new name**                       | Yes     |
| `add_incareer`           | Existence change    | file, manager, career, root, collection           | Yes     |
| `remove_incareer`        | Existence change    | file, manager, career, root, collection           | Yes     |
| `copy_incareer`          | Existence change    | file, manager, career, root, **new name**         | Yes     |
| `add_string`             | Existence change    | file, manager, block, key                         | Yes     |
| `remove_string`          | Existence change    | file, manager, block, key                         | Yes     |
| `add_or_update_string`   | Existence change    | file, manager, block, key                         | Yes     |
| `add_texture`            | Existence change    | file, manager, pack, name                         | Yes     |
| `remove_texture`         | Existence change    | file, manager, pack, key                          | Yes     |
| `copy_texture`           | Existence change    | file, manager, pack, **new name**                 | Yes     |
| `new`                    | Existence change    | file                                              | Warn    |
| `delete`                 | Existence change    | file                                              | Warn    |
| `import`                 | Existence change    | file, manager — opaque                            | Warn    |
| `import_all`             | Existence change    | file, manager — opaque                            | Warn    |
| `replace_texture`        | Texture operation   | file, manager, pack, key                          | Yes     |
| `add_or_replace_texture` | Texture operation   | file, manager, pack, key                          | Yes     |
| `bind_textures`          | Texture operation   | file, manager, pack — opaque                      | Warn    |
| `create_file`            | Filesystem effect   | The path it writes                                | Yes     |
| `create_folder`          | Filesystem effect   | The path it writes                                | Yes     |
| `erase_file`             | Filesystem effect   | The path it deletes                               | Yes     |
| `erase_folder`           | Filesystem effect   | The tree it deletes                               | Yes     |
| `move_file`              | Filesystem effect   | The source and the target                         | Yes     |
| `unlock_memory`          | Filesystem effect   | One memory file, or the five of `all`             | Yes     |
| `unpack_stream`          | Filesystem effect   | The two containers and the tree                   | Warn    |
| `pack_stream`            | Filesystem effect   | The two containers and the tree                   | Warn    |
| `speedreflect`           | Filesystem effect   | The copy target                                   | **No**  |
| `append`                 | Control flow        | None                                              | Yes     |
| `checkbox`               | Control flow        | None                                              | Yes     |
| `combobox`               | Control flow        | None                                              | Yes     |
| `if`                     | Control flow        | None                                              | Warn    |
| `end`                    | Control flow        | None                                              | Yes     |
| `empty`                  | Control flow        | None                                              | Yes     |
| `version`                | Process or metadata | None                                              | Yes     |
| `watermark`              | Process or metadata | None                                              | Warn    |
| `stop_errors`            | Process or metadata | None                                              | **No**  |
| `game`                   | Manifest key        | None                                              | **No**  |
| `directory`              | Manifest key        | None                                              | **No**  |
| `filecount`              | Manifest key        | None                                              | **No**  |
| `capacity`               | Manifest key        | None                                              | **No**  |
| `generate`               | Manifest key        | None                                              | **No**  |
| `invalid`                | Unclassified        | None                                              | **No**  |

The category list of the work plan needed two additions.

**Manifest key.** Five verbs belong to a version 4 manifest and not to a version 2 script. `EndDeserializer` reads them. `EndScriptParser` has no class for them, so a script line that holds one parses to an `OptionalCommand`, and the flattener names the word and stops.

**Opaque.** Four verbs carry a key on a container and name nothing inside it. `import`, `import_all`, and `bind_textures` read their names out of a binary file or out of a directory listing at deploy time. The tool reports the pair of mods and says that it cannot compare them.

Three rows moved from the category that the work plan guessed.

- `static` writes a property of the manager. That is a scalar field write with a shorter key, and not process state.
- `update_texture` writes one property of a texture. It does not replace the pixels. `replace_texture` does that.
- `unlock_memory` writes a short header over a file of the game directory. That is a filesystem effect.

### Corrections to the pitfalls

**`unlock_memory` is disk modding.** The pitfall said that it targets the running game. It does not. `MemoryUnlock.FastUnlock` opens a file with `FileMode.Create` and writes 16 bytes. `LongUnlock` writes a longer block. Both take a path under the profile directory, which is our staging copy. The staging copy covers the write and the revert restores the file, so the command needs no special handling. It is supported.

**`speedreflect` is disk modding too, and we still refuse it.** The command copies `SpeedReflect.asi` out of the directory of the running executable into the game directory. The copy is stageable. The source is not. SpeedReflect is GPL-3.0 and this application ships MIT libraries only, so the file never sits beside our executable and the command always fails. We refuse it up front with a message that says why.

**`stop_errors` is worse than the pitfall says, and the scope is smaller.** `EndScriptManager.ExecuteSingle` catches every exception of a command and adds an `EndError` only when `_stop_errors` is false. So `stop_errors true` drops every later failure of that script with no trace. Our rule is that one `Errors` entry fails the deploy, and this command defeats it. The flag lives on the manager instance, and `ContainerDeployEngine.Apply` builds one manager per variant, so the effect stops at the end of that variant. We refuse the command anyway, because a broken mod that looks installed is the worst result this project can produce.

**`if` does not need a recomputation after resolution.** The pitfall asked for that, and a cheaper answer covers the same failure. `ProcessScript` calls `IfStatementCommand.Execute` inline and never pauses, so the deploy resolves an `if` on its own with no help from us. Only the preflight has the problem. The preflight now walks **both** branches and marks every edit inside as conditional. A conflict against a conditional edit reports `Possible` and not `Certain`. The old code threw and dropped the whole variant out of the check, which is the silence that this step exists to remove.

### The conflict rules

`ConflictDetector` reports five kinds.

| Kind         | Rule                                                            | Does load order settle it? |
| ------------ | --------------------------------------------------------------- | -------------------------- |
| `FieldValue` | Two mods write one key with two values.                         | Yes. The last write wins.  |
| `Existence`  | Two mods name one thing, and one of them adds it or removes it. | No. One command fails.     |
| `Coverage`   | One mod removes a thing and another mod edits inside it.        | No. One command fails.     |
| `Filesystem` | Two mods name one path and at least one writes.                 | Partly.                    |
| `Opaque`     | An opaque command shares a container with another command.      | Unknown.                   |

**An existence conflict is a certain deploy failure.** `Manager.Add` calls `CreationCheck` and throws on a duplicate name. `Manager.Remove` throws when the name is absent. So two mods that add one collection, or two that remove one, break the second command. Read `ConflictEntry.LoadOrderDecides` before you show a winner in the UI.

**`Coverage` is the prefix rule.** `EditKey.Covers` tests whether one key path starts the other one. A `delete` key holds no segment, so it covers every key on that container.

**One fixed defect in our own key.** `EditKey` joined its parts with an empty string, so `("CAR", "A")` and `("CARA")` produced one normalized key. Equality was wrong before the prefix rule made it visible. The join now uses a separator.

### The path sandbox

`PathSandbox` builds the path that the library will really touch, then tests it against two roots. A write outside the staging copy stops the deploy. `CommandGate` runs the same test inside `ContainerDeployEngine` before it loads anything, so a caller that skips the preflight cannot skip the rule.

Three facts decide the test.

1. **`absolute` does not mean the root of the filesystem.** `EnumConverter.StringToPathType` reads `relative` and `absolute`. `relative` anchors at the directory of the launcher script, which is the mod directory. `absolute` anchors at `map.Profile.Directory`, which is the staging copy. So a well behaved script stays inside on its own.
2. **`Path.Combine` drops the root when the second path is rooted.** The commands call `Path.Combine` and nothing else. A path such as `C:\Windows\System32\x` therefore names its own place and never reaches the anchor. The sandbox reports that before it resolves anything.
3. **A `..` segment climbs out.** The sandbox converts the separator first. The scripts come from Windows and write a backslash. A native Linux run keeps a backslash inside a file name, so a test that skips the conversion misses an escape that the same script makes under Wine.

### The types

| Type                                  | Holds                                                            |
| ------------------------------------- | ---------------------------------------------------------------- |
| `Mods/CommandCatalog.cs`              | The classification of all 48 verbs, with token numbers.          |
| `Mods/PathSandbox.cs`                 | `PathEffect`, `SandboxRoots`, and the escape test.               |
| `Mods/EditKey.cs`                     | The key, the prefix test, `ResolvedEdit`, `ScriptWarning`.       |
| `Mods/ConflictDetector.cs`            | `ConflictKind`, `ConflictCertainty`, and the five rules.         |
| `Mods/ScriptFlattener.cs`             | The recursive walk that covers both branches of an `if`.         |
| `Deploy/CommandGate.cs`               | The rule that stops a deploy, beside the code that writes.       |
| `Deploy/ConflictPreflight.cs`         | `Warnings`, `Rejections`, `Escapes`, `Approximate`, `CanDeploy`. |
| `tests/CommandClassificationTests.cs` | 35 tests. They read text only, so they need no game and no Wine. |

### The run

`dotnet test` reports 241 passing tests, up from 203.

`tools/run-deploy-test.sh` still passes end to end. The gate logged `It refused nothing and it found 0 commands that the conflict check cannot compare`, and `GlobalB.lzc` grew from 5,145,778 to 8,263,472 bytes. That matches step 1 and step 6 to the byte, so the classification work changed no output.

### Facts that carry forward

1. **The catalog is the one place that names a verb.** `EditKeyExtractor` reads the token numbers out of it and has no verb list of its own. Add a verb to the catalog and the extractor, the warnings, and the sandbox cover it with no other change.
2. **A missing sandbox root is not a pass.** `PathSandbox.Describe` with no root returns a `PathEffect` that carries no resolved path and no violation. `ConflictPreflight` therefore takes the staging directory, and `DeployService.CheckConflicts` passes it even though the directory does not exist yet. The test compares paths and reads no file.
3. **The preflight and the gate are not the same thing.** The preflight reports and never blocks. The gate blocks and lives inside the engine. Keep both.
4. **A question inside an `if` block cannot line up with the deploy.** `ProcessScript` counts only the questions that it reaches, and the walk counts the questions of both branches. The flattener warns and marks the script approximate. No real mod does this yet.
5. **An `if` with no `else` block ends the deploy when the check picks that branch.** `ProcessScript` jumps to `Options[Choice].Start` and throws when that value is -1. The flattener warns for the case.

### What is open

**No real mod exercises the new categories.** Every test of the new work builds its script by hand. The two example mods use 5 verbs, and the run above proves that the classification changed nothing for them. A mod that uses `bind_textures`, `import`, or a filesystem command would prove the rest.

**`pack_stream` writes outside the single load and save pass.** The command rewrites two containers itself. `StagingFiles.MakePrivate` never sees those files, so a hard link would carry the write into the game install. The command is supported with a warning today. **A mod that uses it needs `MakePrivate` for both containers first.** Do that work when a sample exists.

**A conditional edit collapses to one value per key.** A variant that writes one key in both branches of an `if` keeps the last value in the comparison index. A mod that writes the other value would then read as a conflict against the wrong branch. The certainty flag says `Possible`, so the report is honest, and the value it shows can be the wrong one of the two.
