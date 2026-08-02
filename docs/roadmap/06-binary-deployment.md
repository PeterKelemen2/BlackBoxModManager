# Step 6 — Binary mod deployment

Wire step 4 into the UI from step 5. This step delivers the success criterion in the project brief.

## The single-pass rule

**Load once. Apply every enabled mod. Save once.**

This is the most important design constraint in the project. It is also the easiest one to break.

Because all enabled mods run against one loaded `BaseProfile` before one `Save()`, their edits composite at the collection and entry level. There is never a whole-file overwrite where one mod wins. Container-level merging, which the brief once called the hardest planned subsystem, does not exist as a problem.

One pass per mod reintroduces it, and worse. See the first pitfall below.

## Work

### 6.1 The merged load

1. Collect every enabled variant for the target game.
2. Build the union of every variant's `Files`. Deduplicate case-insensitively, with separators normalized.
3. Build one synthetic `Launch` for the load. Set `Files` to that union. Set `Directory` to the staging copy. Set `Usage` to `Modder`.
4. Set `MainHashList` and `CustomHashList` on the target game's profile class.
5. Call `BaseProfile.NewProfile` once. Call `profile.Load` once with the synthetic manifest.
6. Surface every string the `Load` call returns.

### 6.2 Applying each mod

1. For each enabled variant, in load order, parse its script with `EndScriptParser`.
2. Create an `EndScriptManager` against the **same** profile instance.
3. Call `CommandChase()`, then loop `ProcessScript()`, answering pauses from stored selections.
4. Check `manager.Errors` after each mod. Any entry fails the whole deploy.

### 6.3 Saving and swapping

1. Call `profile.Save()` once, after every mod has applied.
2. Surface every string it returns.
3. Verify the staging result.
4. Swap into the game folder atomically.
5. Keep the revert path from step 5 working.

### 6.4 UI

1. Show variants as a multi-select checkbox list.
2. Show a combobox option set as a single-select control, with its description as the caption.
3. Show a checkbox option set as a boolean toggle.
4. Show the conflict list, with a per-conflict winner and a global load order.
5. Persist every selection in the profile.

## Pitfalls

**Call `StagingFiles.MakePrivate` for every file of the merged `Files` union, before the load.** Step 5 builds the staging copy with hard links, so a staging file, the vanilla file, and the live file are one file with three names. `profile.Save()` writes a container in place, and that write would reach the vanilla baseline and the live install of the user. `MakePrivate` replaces the linked name with a private copy and breaks the share. The link engine of step 5 never needs the call, because it deletes the target and then creates a new name. **The container engine does need it.** See the results section of [05-mvp-shell.md](05-mvp-shell.md).

**Never call `profile.Load(launch)` once per mod.** `Load` calls `AddNew` for every entry in `Files` with no duplicate check. Two mods that both declare `GLOBALB.LZC` produce two container objects for one file. `Save` then writes that file twice from two different in-memory states, and the edits of the first mod vanish with no error reported. Build the merged `Files` union and load once. See defect 6.

**A `Load` that reports nothing may have loaded nothing.** `Load` returns an empty array immediately when `Files` is empty. Verify the profile holds the containers you expect.

**`CheckFiles` throws on the first missing file.** The union includes containers that no script edits, such as `GLOBALA.BUN`. All of them must exist under the staging directory.

**Run the whole deploy on one thread.** The hash list statics are process-global and `LoadHashList` calls `Map.ReloadBinKeys()`. Two concurrent deploys corrupt each other. Keep the UI responsive with one background thread, not several. See defect 8.

**`Save` writes `CustomHashList` as its last step.** Point that path under our own application data. Never point it into the Binary install. A null value throws after the containers already wrote. See defect 7.

**Errors can coexist with success.** `ProcessScript` can return `true` while `manager.Errors` holds entries. Check the collection explicitly. Treat any entry as a failed deploy.

**`ProcessScript` throws rather than returning a code.** Wrap every call. Report `parser.CurrentFile`, `parser.CurrentLine`, and `parser.CurrentIndex` on a parse failure.

**Load order is the conflict resolution.** The order in which mods apply within the single pass decides the winner. The last write wins. Do not build a separate resolution mechanism.

**Report same-value collisions as benign.** Enabling the `1 Lap` `ALL` variant alongside `URL` writes the same field the same value twice. Reporting that as a conflict trains users to ignore the conflict list.

**Verify the swap, then verify the game.** A container that saves without error can still fail to load. The success criterion requires a running game.

## Done when

The success criterion in the project brief passes. Both example mods install together from the UI, with Binary never launched, in one load-apply-save pass, with a clean revert afterward and both mods visibly in effect in the running game.
