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
