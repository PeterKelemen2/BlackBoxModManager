# Step 5 — MVP shell

Build the application around the simplest mod types. ASI plugins and loose files are drop-in files with no capture step and no container work.

**Why these first:** they exercise profiles, load order, staging, deployment, and revert without touching Nikki. A working manager for simple mods is useful on its own, and step 6 reuses all of its plumbing.

## Work

### 5.1 Scaffolding

1. Create the WPF application targeting `net10.0-windows`. The libraries stay on plain `net10.0`.
2. Add CommunityToolkit.Mvvm. Do not add Prism unless a need appears.
3. Set `InvariantCulture` in the entry point, before anything else runs.
4. Set `<PlatformTarget>x64</PlatformTarget>`.

### 5.2 Game detection

1. Find installs through the registry with `Microsoft.Win32.Registry`.
2. Let the user browse for a path when detection fails.
3. Validate a candidate directory by checking for known game files.
4. Store the confirmed path per game.

### 5.3 Mod import

1. Extract archives. Use `System.IO.Compression` for zip. Use `SharpCompress` for rar and 7z.
2. Import into a managed mod store outside the game directory.
3. Classify each mod by type. Detect `.asi` files. Detect `VERSN1` manifests. Treat the rest as loose files.

### 5.4 Profiles and load order

1. A profile holds the enabled mod set, the load order, and every option selection.
2. A profile must fully determine the deployed result, with no prompting.
3. Support several profiles per game.

### 5.5 Deploy engine

1. Implement a link deployer with three strategies, tried in order: hardlink, symlink, copy.
2. Use `CreateHardLinkW` and `CreateSymbolicLinkW` through direct P/Invoke.
3. Record which strategy succeeded, so the UI can explain a slow deploy.
4. Deploy in load order. A later mod overrides an earlier one.

### 5.6 Staging and revert

1. Snapshot the vanilla state before the first deploy. Hash the content. Do not use size and modification time.
2. Build the staging copy. Use hardlinks or block cloning where available. Fall back to a full copy.
3. Apply to staging. Verify. Then swap into the game folder.
4. Keep enough state to revert to vanilla cleanly.

## Pitfalls

**Never write to the live install.** Apply to staging, verify, then swap. This rule holds for every mod type, including the simple ones in this step.

**Do not identify files by size and modification time.** Archive extraction resets timestamps. Hash the content. Use `System.IO.Hashing` with XxHash for internal diffing.

**Ignore `.bacc` files during snapshot.** `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit beside the real files in a real install. They are the backup bookkeeping of Binary, not game content. Treating them as content corrupts the vanilla baseline.

**Hardlinks cannot cross volumes.** A mod store on a different drive from the game silently falls through to copy. Report the strategy so a user can understand the disk use.

**Hardlinks share content.** Editing a deployed file edits the file in the mod store too. This matters for loose files that a mod expects to stay pristine. Copy where an edit is possible.

**Symlinks need a privilege on Windows.** `SeCreateSymbolicLinkPrivilege` means administrator rights or Developer Mode. The fallback chain exists for this reason. Use the Wine results from step 3 for the Linux target.

**Test under Wine continuously, not at the end.** Every deploy strategy in this step has a different Wine story.

**Set the culture before any parsing.** A comma-decimal locale corrupts float values. Binary itself forces `en-US` on its main thread. Do not inherit the right behavior by luck.

**Keep the deploy engine behind an interface.** Step 6 adds a container-based deployment that shares staging, backup, and revert but not the link strategy.

## Done when

The application detects a game, imports ASI and loose-file mods, orders them, deploys them through the link engine, and reverts to vanilla cleanly. All of it verified under Wine.

## Results

Step 5 is done. `src/BlackboxModManager.App` holds the window. `src/BlackboxModManager.Core` grew four namespaces. The application detects the game, imports both drop-in kinds, orders them, deploys them, and reverts. A self test drives the whole path under Wine and every check passes.

### The types

| Namespace                   | Holds                                                                            |
| --------------------------- | -------------------------------------------------------------------------------- |
| `Core.Games`                | `GameCatalog`, `GameDefinition`, `GameInstall*`. Detection and validation. 5.2.   |
| `Core.Store`                | `ModStore`, `ModImporter`, `ModClassifier`, `ArchiveExtractor`. Import. 5.3.      |
| `Core.Profiles`             | `Profile`, `ProfileEntry`, `ProfileStore`. The enabled set and the order. 5.4.    |
| `Core.Deploy`               | `IDeployEngine`, `LinkDeployEngine`, `DeployService`, `DeployPolicy`. 5.5.        |
| `Core.Staging`              | `GameWorkspace`, `SnapshotReader`, `TreeReplicator`, `GameSwap`, `StagingFiles`.  |
| `Core.Files`                | `FileHash` with XxHash128, `FileTree`. Content identity and tree work.            |
| `App.ViewModels`            | `MainViewModel`, `ModRowViewModel`. CommunityToolkit.Mvvm.                        |

`tests/BlackboxModManager.Tests` holds 163 tests. The new ones build a game directory of text files, so they run on native Linux with no Wine and no game.

Run the application with `tools/run-app.sh`. Run the self test with `BlackboxModManager.exe --selftest <directory>`.

### The workspace layout

The workspace sits beside the game install and carries the name of the game directory plus `.blackbox`.

```
Need for Speed Underground 2/            the live install. Only the swap changes it.
Need for Speed Underground 2.blackbox/
    vanilla/        the pristine state. A revert restores this.
    staging/        the copy that a deploy writes to.
    previous/       the live directory that a swap set aside. Empty between runs.
    vanilla.json    the content hash of every vanilla file.
    state.json      the profile that the game directory holds.
```

**The default place matters.** A hard link cannot cross a volume, and a directory move across a volume copies every byte. Both the staging build and the swap are cheap only on the volume of the game. `Settings.WorkRootOverride` moves the workspace, and the deploy then reports the cost.

### Facts that carry forward

1. **Hard links work under Wine, and the swap works.** The self test ran on system Wine and linked every file of the vanilla copy and the staging copy. Two `Directory.Move` calls swapped the directories. **A deploy of the real 1.7 GB install therefore costs almost no disk space and almost no time.**

2. **A hard link shares the content, and the chain reaches the live install.** The staging file, the vanilla file, and the live file are one file with three names. A write in place through any of them changes all three. Two rules follow, and **step 6 depends on both**.
   - `DeployPolicy.NeedsCopy` names the extensions that something writes. Those get a private copy. The link engine and `TreeReplicator` both obey it.
   - Any other writer must call `StagingFiles.MakePrivate` first. The link engine never needs it, because it deletes the target and then creates a new name. **`profile.Save()` writes containers in place, so step 6 must call `MakePrivate` for every file that the merged load names.**

3. **A wrapper walk must stop at a name that the game knows.** An archive wraps its content in one directory, and the import drops that level. A plain "one directory and no file" rule descends into `scripts/` too, and the plugin then lands in the game root. `ModClassifier.GameRelativeDirectories` holds the stop list, read from the install listing.

4. **The verify checks the last writer of a path, not every writer.** Two mods can supply one file. The earlier one lost, so its content is not in the staging copy, and a check against it fails for the wrong reason.

5. **`GameCatalog` holds one game.** Underground 2 is the only install that we listed file by file, so it is the only entry. Add an entry only after a listing of a real install confirms the executable name and the markers. **Step 7 added Most Wanted and ProStreet under that rule.** Underground 1, Carbon, and Undercover still wait for a listing.

6. **A Binary mod stops a deploy with a message.** The store classifies it, the profile can hold it, and no engine in this build claims it. `DeployService` names step 6 and changes nothing. A silent skip would look like a successful deploy that did nothing.

### A font family that Wine does not hold kills the process

**Name no font family in the XAML.** WPF resolves a family through its own enumeration of `drive_c/windows/Fonts`. It does not read the fontconfig of the host. A family that it cannot resolve reaches `MS.Internal.Invariant.FailFast`, and that call ends the process at once. There is no dialog, no exception, and no way to catch it. The `DispatcherUnhandledException` handler of the application never runs.

The log list carried `FontFamily="Consolas"`. Wine holds no Consolas. The window opened, and the first log line killed the application. The stack named `TypefaceMap.MapUnresolvedCharacters` and nothing about the action that the user took, so the failure looked like a broken import.

Two changes came out of it.

1. The log list names no family. It uses the family that the rest of the window uses, which is the only one that we know resolves.
2. `tools/run-app.sh` links the font files of the host into the prefix. A fresh prefix holds an empty `Fonts` directory, and one family with no fallback is a thin base for a UI. With 372 font files linked, a mod name in Cyrillic renders and the window survives.

**Do not add a font family without a run under Wine.** The cost of a wrong one is an unrecoverable crash, and the stack blames the wrong code.

### Never scroll a list from inside its own CollectionChanged handler

The log list scrolled to the last line from its `CollectionChanged` handler. `ScrollIntoView` lays the list out at once, so the item container generator ran while the collection change was still in flight. The generator counted one event fewer than the list held, and WPF threw `An ItemsControl is inconsistent with its items source`. The message named an accumulated count of 4 against an actual count of 5.

**The failure is a race.** It appeared for a user after five log lines, and the same build with the same archive did not reproduce it in a headless run. Do not treat an operation that works once as proof.

`OnLogChanged` now posts one scroll at `DispatcherPriority.Background` and coalesces a burst into a single call. The scroll also catches its own exceptions, because a scroll is cosmetic and must never fail an operation.

### A dispatcher handler must set Handled

`DispatcherUnhandledException` showed a message box and left `e.Handled` false. That was worse than no handler at all. The process still ended, and the message box pumped messages while it waited, so the failing render ran again and raised the same exception again. The user got a storm of dialogs and then a crash.

The handler now sets `e.Handled` first, appends the exception to `logs/error.log`, and shows one dialog at a time. A run with an exception every 700 milliseconds now keeps the window open. Every disk operation already reports its own failure through `MainViewModel.RunAsync`, so an exception that reaches this handler is a defect in the window, and the log file is the record of it.

### One deviation from the work list

**The symbolic link goes through `File.CreateSymbolicLink`, not through a `CreateSymbolicLinkW` P/Invoke.** Work item 5.5.2 asks for direct P/Invoke for both. The hard link needs it, because the base class library has no hard link method. The symbolic link does not need it. The base class library calls `CreateSymbolicLinkW` on Windows, and the same code then works on native Linux, where the tests run. Step 3 verified the method under both Wine builds. This is the step 3 code, unchanged.
