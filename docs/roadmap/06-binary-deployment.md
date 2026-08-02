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

## Results

**Step 6 is done and the success criterion of the project brief passes in full.** Both example mods install together into one game directory, in one load, apply, and save pass. The game starts from that directory and both mods take effect. The revert restores the vanilla state exactly. Binary never ran.

Read the run with `tools/run-deploy-test.sh`. It copies the game, applies both mods, verifies, and reverts.

### The numbers from the run

| Fact             | Value                                                       |
| ---------------- | ----------------------------------------------------------- |
| Vanilla snapshot | 1561 files, 1729 MB                                         |
| Staging build    | 1472 files linked, 89 copied                                |
| Merged load      | 2 containers, `GLOBAL\GLOBALB.LZC` and `GLOBAL\GLOBALA.BUN` |
| Camera mod       | 1198 commands, 1 question answered                          |
| 1 Lap URL mod    | 51 commands, 0 questions                                    |
| Conflict check   | 2 variants, 501 field edits, 0 conflicts                    |
| `Load` errors    | none                                                        |
| `Save` errors    | none                                                        |
| `manager.Errors` | empty for both mods                                         |
| Full verify      | 1563 files, 0 problems                                      |
| `GlobalB.lzc`    | 5,145,778 bytes vanilla, 8,263,472 bytes after the deploy   |
| Revert           | 0 differences against the snapshot                          |

The container growth matches the step 1 measurement to the byte. Nikki writes `GlobalB.lzc` with no whole-file compression, and the game accepts it.

### The types

| Type                    | Holds                                                                   |
| ----------------------- | ----------------------------------------------------------------------- |
| `ContainerDeployEngine` | The single pass. Load once, apply every variant, save once. 6.1 to 6.3. |
| `MergedLaunch`          | The one synthetic manifest, with the union of files and links. 6.1.     |
| `VariantReader`         | The enabled variants of every Binary mod, in load order.                |
| `ConflictPreflight`     | Same key and a different value, with the load order winner. 6.4.        |
| `VariantRowViewModel`   | A variant checkbox, and a control per question. 6.4.                    |

`tests/BlackboxModManager.Tests` holds 178 tests. The new ones read the real example mods and build no container, so they run on native Linux.

### The game confirms the result

**A person ran the game and both mods took effect.** The game started from the scratch copy at `/mnt/Data/Games/DeployTest`, and the camera change and the one-lap races were both visible.

Keep doing this after a change to the container path. No automated check can replace it. A container that saves with no error can still fail to load, and only the game answers that. Run the test with `DEPLOY_NO_REVERT=1`, then start `SPEED2.EXE` in the scratch copy.

### Facts that carry forward

1. **The single pass works, and the load order decides every collision.** Two mods applied 1249 commands to one loaded profile before one `Save`. No mod overwrote the container of the other. Container merging never became a problem that somebody had to solve.

2. **`AddNew` does check for duplicates.** Defect 6 said it does not. It calls `Contains` and throws `DatabaseExistenceException`. The real hazard is narrower: `Contains` compares raw text, so two spellings of one container both pass and the profile then holds two objects for one file. Defect 6 is corrected.

3. **The library matches a container by the exact text of its name.** `CollectionMap` keys every collection on `sdb.Filename` and `GetCollection` does one dictionary lookup. The merged union therefore keeps the spelling of the manifest, and two mods that spell one container differently cannot share a load. `MergedLaunch` reports that instead of loading both.

4. **Pass the full path of the script to `EndScriptManager`.** The third argument becomes `Path.GetDirectoryName(launcher)` inside `CollectionMap`, and seventeen commands read a file relative to it. The step 1 harness passed a bare file name, which gives an empty directory. Neither example mod reads a file, so nothing broke and nothing proved the point.

5. **A link to a file that does not exist is normal.** A vanilla install holds one of the four links that every manifest names. Every loader in Nikki returns for a missing file. Report it once as a note, never as an error. A first attempt treated it as an error and stopped the deploy.

6. **The verify needs a third category.** A rewritten container differs from the vanilla snapshot on purpose and matches no file in the mod store. It cannot be compared against either, so the check on it is existence and a length above zero.

7. **A combobox option is named by the quoted string, not by the file it appends.** The camera mod offers `Install Camera Mod [NFSMW TO U2]` and `Restore original camera settings`. The blocks that those options append are `[1]_Camera_MOD_NFSMW_TO_U2.end` and `[0]_Restore_Camera_Settings.end`. A stored answer holds the option name, so it holds the first form.

8. **A profile records the variants of a Binary mod separately from the mod.** `ProfileEntry.Enabled` switches the mod on, and `VariantSelection.Enabled` switches one variant on. A variant that the user switched off keeps its answers, so a later switch back changes nothing else.
