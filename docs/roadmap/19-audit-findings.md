# Step 19 — the audit findings

An audit read the whole of `src/BlackboxModManager.App` and `src/BlackboxModManager.Core` and found 27 defects. This file holds one part per finding. **No part of this file is implemented.**

Each part states the problem, the evidence, the fix, and the check. The evidence names a file and a method. Line numbers move, so match the quoted code and not a number.

The parts run in severity order. Part 1 can destroy the game install of a user. Parts 2 to 6 change a result that the user sees. Parts 7 to 16 cost time. Parts 17 to 27 are hygiene.

## What this step must not change

These rules come from the earlier steps. All of them still hold.

1. **A profile fully determines the deployed result.** No fix may move a profile value into the settings file, or a settings value into the profile.
2. **No code writes into the game directory.** Only `GameSwap.Swap` changes it, and only after the verify passes.
3. **One deploy runs at a time, on one background thread.** The library statics of Nikki are process-global. See defect 8 of `98-known-upstream-defects.md`.
4. **Never merge `third_party/CoreExtensions` forward to master.** See `CLAUDE.md`.
5. **The window runs under Wine with the software rasterizer.** Use no resting `Opacity`, no `DropShadowEffect`, and no live `VisualBrush`.
6. **A stored fingerprint must keep its meaning.** A change to `ProfileFingerprint.Of` that alters the output for an unchanged profile asks every user for a deploy that writes no new byte. Part 18 covers that case.

## The order to work in

Parts 1 and 2 carry the most risk. Take them first, and take them alone.

1. Part 1, the swap. Write the test before the fix.
2. Part 2, the profile dictionaries. This is one method and two call sites.
3. Parts 3, 7, 11, 12, 13, 14, and 15, the speed work. Measure each one before and after.
4. Parts 4, 5, 6, 8, 9, and 10, the remaining behavior fixes.
5. Parts 16 to 27, in any order. Each one is independent.

## The severity table

| Part | Finding | Kind | Cost of the defect |
| ---- | ------- | ---- | ------------------ |
| 1 | `GameSwap.Move` reads every `IOException` as a cross-volume move | Correctness | The live game directory is deleted |
| 2 | Three profile dictionaries lose their comparer on load | Correctness | An answer applies before a restart and not after |
| 3 | `RefreshLoaders` walks every mod directory on the UI thread | Speed | The window stalls on every click |
| 4 | The Cancel button cannot cancel an import or a revert | Correctness | A 30-minute import ignores the user |
| 5 | The archive guard and the archive extractor are two parsers | Security | A path traversal guard has a hole |
| 6 | The link probe writes into the mod store content root | Correctness | A leftover file deploys into the game |
| 7 | `StagingVerifier` reads `mod.json` once per deployed file | Speed | Thousands of file opens per deploy |
| 8 | `SevenZipTool.Extract` has no timeout and no cancellation | Correctness | A wedged import never ends |
| 9 | `Directory.SetCurrentDirectory` races the conflict check | Correctness | A background read resolves against the wrong directory |
| 10 | The mod store defaults into roaming application data | Design | Gigabytes enter a roaming profile |
| 11 | The Binary route walks the staging tree twice | Speed | Two extra full directory walks per deploy |
| 12 | The snapshot hashes one file at a time | Speed | The first deploy waits longer than it must |
| 13 | `ConflictDetector.Paths` compares every path against every path | Speed | The conflict check grows with the square |
| 14 | The mod list cannot virtualize | Speed | Every row is built, however long the list |
| 15 | The view model constructor does the startup on the UI thread | Speed | The window paints late |
| 16 | `TreeReplicator` makes two extra metadata calls per file | Speed | About 40,000 extra calls per deploy |
| 17 | `CopyLines` mis-orders a duplicate log line | Correctness | A copy of the log reads out of order |
| 18 | `AppendRoute` writes no field separator | Correctness | A later field can collide |
| 19 | `CheckBaseline` exists twice, word for word | Duplication | Two places to fix one rule |
| 20 | The developer harnesses ship in the release build | Design | About 1,000 unused lines reach the user |
| 21 | The App project has no tests | Coverage | 2,702 lines of view model are unchecked |
| 22 | `Nullable` is off in all four projects | Design | The compiler cannot find a null defect |
| 23 | The error log and the update log grow without a limit | Design | A disk fills over years |
| 24 | `LibraryGate.Enter` takes the lock outside a `try` | Correctness | A failed allocation leaks the lock |
| 25 | `SettingsStore.Update` is an unsynchronized read and write | Correctness | Two processes lose a key |
| 26 | `Safe` allocates one array per character | Speed | A small allocation storm |
| 27 | `SevenZipTool.Path` calls `File.Exists` on every read | Speed | Two disk calls where none is needed |

---

## Part 1 — the swap deletes the game directory when a file is locked

**The problem.** `GameSwap.Move` treats every `IOException` from `Directory.Move` as proof of a cross-volume move. It then copies the tree and deletes the source. A locked file also raises `IOException`. The game runs, or an antivirus holds a handle, and the recovery path deletes the live game directory of the user.

**The evidence.** `src/BlackboxModManager.Core/Staging/GameSwap.cs`, the private `Move` method.

```csharp
try
{
    Directory.Move(from, to);
    return;
}
catch (IOException)
{
    // A move across a volume fails here. Fall through to the copy.
}

TreeReplicator.Build(from, to);
FileTree.Delete(from);
```

`Swap` calls `Move(live, aside)` first, where `live` is `workspace.Install.Root`. That call is the dangerous one.

**The fix.**

1. Test for the cross-volume case before the move. Compare `Path.GetPathRoot` of the source against `Path.GetPathRoot` of the target.
2. Call `Directory.Move` only when the two roots match. Let any exception out of that call.
3. Call the copy path only when the two roots differ.
4. Keep `FileTree.Delete(from)` inside the copy path alone.
5. Do not delete the source when the copy throws. Report the directory that holds the content, in the way that the outer `catch` of `Swap` already does.

**One rule, one place.** `GameWorkspace.SharesVolumeWithGame` asks the same question with the same method. A volume that Windows mounts into a folder gives a wrong answer in both. Put the rule in one method and call it from both.

**The check.** Write a test that holds a file open in the source directory and calls `Swap`. The source directory must still exist after the call, and the message must name the lock.

---

## Part 2 — three profile dictionaries lose their comparer on load

**The problem.** `ProfileEntry.IniSettings`, `ModSelections.Variants`, and `Profile.LoaderChoices` each declare `StringComparer.OrdinalIgnoreCase`. `System.Text.Json` does not fill the declared instance. It builds a new `Dictionary` with the default ordinal comparer and assigns it. So a key matches without letter case before a save, and with letter case after a load. An ini answer and a loader choice both go missing after a restart.

**The evidence.**

| Place | The declaration |
| --- | --- |
| `Profiles/Profile.cs`, `ProfileEntry.IniSettings` | `new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)` |
| `Profiles/Profile.cs`, `Profile.LoaderChoices` | `new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)` |
| `Mods/ModSelections.cs`, `ModSelections.Variants` | `new Dictionary<string, VariantSelection>(StringComparer.OrdinalIgnoreCase)` |

`ProfileEntry.EnsureIni` builds the inner map with `OrdinalIgnoreCase` too, so the inner map has the same defect.

The application already knows this trap and repairs it in two other places.

| Place | What it repairs |
| --- | --- |
| `Settings.Normalize` | `GameDirectories` and `ActiveProfiles` |
| `SnapshotReader.Load` | `VanillaSnapshot.Files` |

`ProfileStore.Read` repairs only the null case of `Selections`. `ProfileStore.Clone` repairs nothing, so a background conflict check and the live profile can disagree about one key.

**The fix.**

1. Add a `Normalize` method to `Profile`. Follow the shape of `Settings.Normalize`.
2. Rebuild `LoaderChoices` with `StringComparer.OrdinalIgnoreCase` inside it.
3. Rebuild `entry.Selections.Variants` for every entry with the same comparer.
4. Rebuild `entry.IniSettings` and every inner map with the same comparer.
5. Call `Normalize` at the end of `ProfileStore.Read`, beside the loop that fills `Selections`.
6. Call `Normalize` on the result of `ProfileStore.Clone`.

**The check.** Write a test that saves a profile with the key `Scripts/Fix.ini`, reads it back, and looks the answer up as `scripts/fix.ini`. The lookup must succeed. Repeat the test for a loader choice and for a variant name.

---

## Part 3 — the loader scan walks every mod directory on the UI thread

**The problem.** `MainViewModel.RefreshLoaders` runs a recursive directory walk of every enabled mod, on the window thread, on every click of a checkbox.

**The evidence.** `ViewModels/MainViewModel.cs`, `RefreshLoaders`.

```csharp
ProxyPlan plan = new DeployService(this._store).PlanLoaders(this._profile);
```

The call chain reads the disk twice.

1. `DeployService.PlanLoaders` calls `this._store.Find(id)` for each enabled mod. Each call opens and parses one `mod.json`.
2. `ProxyScanner.Scan` calls `FileTree.Files(mod.ContentRoot)` for each mod. That walk is recursive and it reads every file entry of the mod.

Six call sites reach `RefreshLoaders`, and two of them are hot. `OnModToggled` fires on every checkbox. `ResyncOrder` fires on every drag and on every press of `Move up` or `Move down`.

`RefreshConflicts` had the same shape and moved to a background thread. `RefreshLoaders` did not.

**A second defect in the same method.** `RefreshLoaders` builds `new DeployService(this._store)` and passes no Binary install and no work root override. Every other caller uses `this.Service()`, which passes both.

**The fix.**

1. Give `RefreshLoaders` the shape of `RefreshConflictsAsync`. Keep a run counter, and let only the newest run write the collection.
2. Read the mods and clone the profile on the window thread. Run `PlanLoaders` inside `Task.Run`.
3. Write `Loaders`, `LoaderHeader`, and `LoaderNeedsAnswer` after the wait.
4. Replace `new DeployService(this._store)` with `this.Service()`.
5. Keep `AskForLoaders` synchronous. It runs before a deploy, where a wait costs nothing.

**The check.** Toggle a mod in a profile that enables six mods with large content directories. The window must accept the next click at once.

---

## Part 4 — the Cancel button cannot cancel an import or a revert

**The problem.** `RunTaskAsync` builds a `CancellationTokenSource` for every run, so `CanCancel` reports true for every run. Two operations never read the token. The user presses Cancel, reads "The operation stops at its next safe point", and the operation runs to the end.

**The evidence.**

| Place | The defect |
| --- | --- |
| `MainViewModel.ImportAsync` | Calls the `Action<Action<string>>` overload of `RunAsync`, which drops the token |
| `MainViewModel.RevertAsync` | Calls the same overload |
| `ModImporter.Import` | Takes no `CancellationToken` parameter |
| `DeployService.Revert` | Takes no `CancellationToken` parameter |

`ArchiveExtractor.Extract`, `SevenZipTool.Extract`, and `ModImporter.CopyTree` take no token either.

**The fix.**

1. Add a `CancellationToken cancellation = default` parameter to `ModImporter.Import`, `ArchiveExtractor.Extract`, `SevenZipTool.Extract`, `ModImporter.CopyTree`, and `DeployService.Revert`.
2. Call `cancellation.ThrowIfCancellationRequested()` once per file in each extraction loop and in each copy loop.
3. Pass the token from `SevenZipTool.Extract` to the child process. End the process on a cancel, in the way that `ProcessRunner.Wait` does.
4. Change `ImportAsync` and `RevertAsync` to the `Action<Action<string>, CancellationToken>` overload of `RunAsync`.
5. Leave the scratch cleanup as it is. `ModImporter.Import` already deletes the scratch directory in its `finally` block.

**A canceled revert must leave the game directory unchanged.** The revert builds the replacement first and only then swaps. Put the cancel checks in the build. Put none between the two moves of the swap.

**The check.** Start an import of a large archive. Press Cancel. The log must report `CANCELED`, and the mod store must hold no new mod.

---

## Part 5 — the archive guard and the archive extractor are two parsers

**The problem.** `ArchiveExtractor.ReadListing` validates every entry name with SharpCompress. `SevenZipTool.Extract` then writes the files with `7z.exe x`. The guard never sees what 7-Zip decides to write. Any disagreement between the two readers is a hole in the check.

**The evidence.** `Store/ArchiveExtractor.cs`, `ExtractOther`.

```csharp
int total = ReadListing(archivePath, target);

return SevenZipTool.Exists
    ? SevenZipTool.Extract(archivePath, target, total, progress)
    : ExtractWithLibrary(archivePath, target, total, progress);
```

The doc comment of `ReadListing` calls itself "the guard of the whole path". It is the guard for `ExtractWithLibrary` alone.

Three kinds of disagreement matter.

1. **Name encoding.** The two readers can decode a non-ASCII entry name differently.
2. **A symbolic link entry.** A 7z archive and a rar archive can both carry one. SharpCompress reports it as a normal entry. 7-Zip writes a real link, and a later entry can then write through it to a place outside the target.
3. **A hard link entry.** The same reasoning applies.

**The fix.** Take the belt and the braces. Both steps are cheap.

1. Add the switches that make `7z.exe` store a link as a plain file. Confirm the exact switch names against the 7-Zip copy that this repository ships, and record the answer in `99-api-notes.md`.
2. Refuse an entry in `ReadListing` whose `IsDirectory` is false and whose attributes name a link. Report the archive path and the entry name.
3. Walk the target directory after the extraction. Refuse the import when the walk finds a reparse point.
4. Keep the `SafePath` test. It stays correct for `ExtractWithLibrary`.

**The check.** Build a 7z archive that holds a symbolic link to `..`. The import must fail with a message that names the entry.

---

## Part 6 — the link probe writes into the mod store content root

**The problem.** `LinkDeployEngine.Deploy` probes the link methods once per mod. The probe creates a directory inside the content directory of that mod. The cleanup is best effort and it swallows every failure. A directory that stays behind then enters `FileTree.Files(mod.ContentRoot)` on the next deploy, and the deploy ships it into the game.

**The evidence.** `Deploy/LinkDeployEngine.cs`, inside the mod loop.

```csharp
LinkProbeResult probe = LinkSupport.ProbeBetween(mod.ContentRoot, context.StagingDirectory);
```

`LinkSupport.ProbeBetween` builds `Path.Combine(sourceDirectory, $".blackbox-probe-{Guid.NewGuid():N}")` and removes it in a `finally` block that catches every exception.

`MainViewModel.ReportStoreVolume` calls `LinkSupport.ProbeBetween(this._store.Root, staging)` too. That call writes into the store root and not into a content directory, so it is safer. It is still a write into the library of the user.

**The fix.**

1. Probe from the store root and never from a content directory. One probe answers for every mod on that volume.
2. Move the probe out of the mod loop in `LinkDeployEngine.Deploy`. Run it once before the loop.
3. Make `FileTree.Files` skip a directory whose name starts with `.blackbox-`. That covers the leftovers that already sit on a user machine.
4. Keep the per-mod message. Read the probe result once, and report it for the first mod that does not get a hard link.

**The check.** Deploy a profile with four mods. The mod store must hold no `.blackbox-probe-` directory after the deploy, and the log must report the link method once.

---

## Part 7 — the verify reads the metadata of a mod once per file

**The problem.** `StagingVerifier.Verify` calls `store.Find(file.ModId)` inside the loop over the deployed files. Each call opens one `mod.json`, reads it, and deserializes it. A mod with 2,000 files costs 2,000 file opens for one answer.

**The evidence.** `Staging/StagingVerifier.cs`, pass 2.

```csharp
foreach (DeployedFile file in deployed.Values)
{
    ...
    InstalledMod mod = store.Find(file.ModId);
```

**The fix.**

1. Build a `Dictionary<string, InstalledMod>` with `StringComparer.OrdinalIgnoreCase` before the loop.
2. Fill it with one `store.Find` call per distinct `ModId`.
3. Read the dictionary inside the loop.
4. Keep the "left the store during the deploy" message. A mod that the dictionary does not hold still gets it.

**A second place with the same shape.** `DeployService.ResolveEnabled` and `DeployService.PlanLoaders` each call `_store.Find(id)` for the same set of identifiers, in the same deploy. One read serves both.

**The check.** Deploy a mod with more than 1,000 files. Compare the verify span in the timing table before and after.

---

## Part 8 — the 7-Zip child process has no timeout and no cancellation

**The problem.** `SevenZipTool.Extract` starts `7z.exe` and reads its output to the end. It passes no timeout and no cancellation token. A wedged child process holds the import open forever.

`ProcessRunner` exists for this. It closes the input, reads the output on another thread, honors a timeout, and ends the process on a cancel. `SevenZipTool` repeats a part of that work and leaves out the rest.

**The evidence.** `Store/SevenZipTool.cs`, `Extract`.

```csharp
while ((line = process.StandardOutput.ReadLine()) != null)
...
process.WaitForExit();
```

**The fix.** Two routes exist. Take route A.

**Route A.** Extend `IProcessRunner` with a callback for each output line. Then rewrite `SevenZipTool.Extract` to call `ProcessRunner`. This gives the timeout, the cancellation, and the input close in one place.

**Route B.** Add a timeout and a token to `SevenZipTool.Extract` by hand. This repeats the logic of `ProcessRunner` a second time. Take this route only if route A proves too large.

Whichever route you take, set the timeout from the size of the archive and never from a constant. A 4 GB archive is legitimate.

**The check.** Import a large archive and press Cancel. The `7z.exe` process must end, and the log must report `CANCELED`.

---

## Part 9 — the container pass changes the working directory of the whole process

**The problem.** `ContainerDeployEngine.RunPasses` calls `Directory.SetCurrentDirectory(AppPaths.LogDirectory)`. That is a process-global change. The comment says that it works around defect 9, and the reasoning is sound. The risk is that another thread reads a file at the same moment.

`LibraryGate` does not cover that other thread. `MainViewModel.RefreshConflicts` starts a background check and waits for nothing.

```csharp
_ = this.RefreshConflictsAsync();
```

`IsBusy` blocks the commands. It does not block a check that already started. So a conflict check can run while a deploy holds the gate and changes the working directory under it.

**The evidence.** `Deploy/ContainerDeployEngine.cs`, `RunPasses`, and `ViewModels/MainViewModel.cs`, `RefreshConflicts`.

**The fix.** Take both halves.

1. Confirm that every read of the conflict path uses an absolute path. Audit `ScriptReader`, `ScriptFlattener`, and `ScriptAppendGraph` for a bare file name. `ModPath.Resolve` already returns an absolute path, so the audit may find nothing. Record the answer in the Results section below either way.
2. Make `DeployAsync` wait for a conflict check that still runs. Keep the `Task` of `RefreshConflictsAsync` in a field, and await it at the top of `DeployAsync`.

**Do not remove the `SetCurrentDirectory` call.** Nikki writes `MainLog.txt` into the working directory and takes no path. See defect 9.

**The check.** Start a conflict check on a large profile, then press Deploy at once. The deploy must produce the same report as a deploy with no check in flight.

---

## Part 10 — the mod store defaults into the roaming profile

**The problem.** `AppPaths.Root` reads `Environment.SpecialFolder.ApplicationData`. That is the roaming application data directory. `ModsDirectory` sits under it, so the default mod store of a user sits in the roaming profile. On a machine that a domain manages, the roaming profile copies to a server at every logon. A mod library of several gigabytes then travels with it.

**The evidence.** `AppPaths.cs`.

```csharp
public static string Root { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
```

Eight directories hang off `Root`. Six of them hold bulk data or scratch space and belong in the local directory.

| Directory | Belongs in | Why |
| --- | --- | --- |
| `settings.json` | Roaming | Small, and it belongs to the user |
| `profiles` | Roaming | Same reason |
| `mods` | Local | It holds gigabytes |
| `import` | Local | Scratch space |
| `binary-cli` | Local | Scratch space |
| `logs` | Local | Machine detail |
| `customkeys` | Local | Machine detail |
| `snapshots` | Local | One per install of this machine |

**The fix.**

1. Add `LocalRoot` to `AppPaths`, built from `Environment.SpecialFolder.LocalApplicationData`.
2. Point `ModsDirectory`, `ImportDirectory`, `BinaryCliDirectory`, `LogDirectory`, `CustomKeysDirectory`, and `SnapshotDirectory` at `LocalRoot`.
3. Leave `SettingsFile` and `ProfilesDirectory` on `Root`.
4. Write a migration. At start, move an existing `mods` directory from `Root` to `LocalRoot` when the settings hold no `ModStoreOverride`. Report the move in the log.
5. Raise `Settings.Version` to 3 and record the migration in the doc comment of that field.

**The migration must never delete a mod.** Move the directory. Stop with a message when the move fails, and let the user move it by hand.

**The check.** Start a build with an old layout on disk. The log must name the new store path, and every mod must still appear in the list.

---

## Part 11 — the Binary route walks the whole staging tree twice

**The problem.** `BinaryCliDeployEngine.Deploy` calls `Differences(context)` once before the runs and once inside `BuildObservedWrites`. Each call runs `SnapshotReader.Compare` over the whole staging directory.

**The evidence.** `Deploy/BinaryCliDeployEngine.cs`.

```csharp
IReadOnlySet<string> before = Differences(context);
...
writes.AddRange(BuildObservedWrites(context, before, containers, writes, variants));
```

`BuildObservedWrites` calls `Differences(context)` again in its own loop header.

**The fix.**

1. Compute the second difference set once, in `Deploy`, after the last run.
2. Pass both sets into `BuildObservedWrites` as parameters.
3. Leave the first call where it is. The route needs the state from before the runs.

Two walks stay. That is the smallest correct number for this route.

**The check.** Read the timing table of a CLI deploy. The two spans that walk the tree must drop to one.

---

## Part 12 — the snapshot hashes one file at a time

**The problem.** `SnapshotReader.Create` reads and hashes every file of a 1.7 GB install on one thread. This is the path that a user waits on before the first deploy of an install. `SnapshotReader.Compare` has the same shape when `hashContent` is true, which is what the full verify asks for.

**The evidence.** `Staging/VanillaSnapshot.cs`, `Create` and `Compare`. Both hold a plain `foreach` over `FileTree.Files`.

**The fix.**

1. Replace the loop of `Create` with `Parallel.ForEach` over the file list.
2. Collect the entries into a `ConcurrentDictionary` and copy them into the snapshot after the loop.
3. Keep the progress line. Count with `Interlocked.Increment` and report every 500 files.
4. Do the same in `Compare` for the hash branch. Collect the differences into a `ConcurrentBag` and sort them after the loop.
5. Set `MaxDegreeOfParallelism` to `Environment.ProcessorCount`. A hash of a file is bound by the disk, so a higher number buys nothing.

**The application already sets `ServerGarbageCollection`.** The App project turns it on for the parallel container work, so parallel work here fits the shape that exists.

**The check.** Time the first deploy against a real install before and after. Record both numbers in the Results section below.

---

## Part 13 — the conflict check compares every path against every path

**The problem.** `ConflictDetector.Paths` runs a Cartesian product. For each pair of variants, it compares every path effect of the left against every path effect of the right, with a linear `SamePath` test on each comparison. The cost grows with the square of the path count.

**The evidence.** `Mods/ConflictDetector.cs`, `Paths`.

```csharp
foreach ((ResolvedEdit Edit, PathEffect Path) first in left.Paths)
{
    foreach ((ResolvedEdit Edit, PathEffect Path) second in right.Paths)
```

`MainViewModel.RefreshConflictsAsync` records that one Most Wanted profile with two Binary mods needs about 900 ms. One real mod holds 97 `move_file` commands.

**The fix.**

1. Give `PathEffect` a normalized key. Use `Resolved` when it is not null, and `PathKey.Normalize(Written)` when it is null. Join the key with the `Anchor` value.
2. Index `Summary.Paths` by that key in a `Dictionary<string, List<(ResolvedEdit, PathEffect)>>`.
3. Rewrite `Paths` to walk the smaller index and to look each key up in the other index.
4. Keep the `Writes` test. A read against a read is still no conflict.

`Coverage` and `Opaque` have the same shape and use `EditKey.Covers`, which is a prefix test and not an equality test. **Do not index those two the same way.** Measure them first, and only then decide.

**The check.** Time `CheckConflicts` on a profile with two large Binary mods before and after. Record both numbers.

---

## Part 14 — the mod list builds every row

**The problem.** The mod list is an `ItemsControl` inside a `ScrollViewer`. An `ItemsControl` uses a plain `StackPanel` as its items panel, and a `ScrollViewer` around it sets `CanContentScroll` to false. Neither condition allows virtualization. So the window builds one container for every mod, however long the list is.

**The evidence.** `MainWindow.xaml`, around the `ModList` element.

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
  ...
  <ItemsControl x:Name="ModList"
```

The Mod options tab has the same shape and nests three `ItemsControl` elements. `MainViewModel.LoadSettings` builds every control of every ini option on each change of the selection.

**The fix.**

1. Set the items panel of `ModList` to a `VirtualizingStackPanel`.
2. Set `VirtualizingPanel.IsVirtualizing` to true and `VirtualizingPanel.VirtualizationMode` to `Recycling` on the `ItemsControl`.
3. Set `ScrollViewer.CanContentScroll` to true on the `ScrollViewer`.

**Read step 17, Part E, before you touch this.** The three drag handlers sit on `ModPanel` and read `RowBorder` by name. Recycling reuses a container and changes its `DataContext`, so confirm that `FindRowElement`, `IsControlSurface`, and `DraggedIndex` still find the right row during a drag.

**The check.** Import 200 mods. The list must scroll without a stall, and a drag must still reorder the right row.

---

## Part 15 — the view model constructor does the startup on the window thread

**The problem.** `MainWindow` builds `MainViewModel` in its own constructor. That constructor reads the settings, opens the store, lists every mod with a JSON parse each, resolves the game install, reads the workspace state, lists the profiles, and then runs `RefreshMods`, which runs the conflict check and the loader scan. All of it runs before the window paints.

**The evidence.** `ViewModels/MainViewModel.cs`, the constructor. The tail of it reads:

```csharp
this.RefreshGame();
this.RefreshBinary();
this.RefreshProfiles();
```

**The fix.**

1. Keep the settings load and the log header in the constructor. Both are cheap, and the window needs them.
2. Move `RefreshGame`, `RefreshBinary`, and `RefreshProfiles` into a public `StartAsync` method.
3. Run the disk work of that method inside `Task.Run`, and write the properties after the wait.
4. Call `StartAsync` from `MainWindow.OnLoaded`, beside the `CheckForUpdatesAtStartAsync` call.
5. Set `Status` to a line that names the work while it runs.

**Part 3 must land first.** `RefreshProfiles` reaches `RefreshLoaders`, and that method is the slowest part of the chain.

**One more reason to move this code.** `RefreshHeroLook` reads `Application.Current.Resources["SurfaceBase"]` from the constructor. Part 21 needs a view model that a test can build with no window, and that read blocks it.

**The check.** Start the application with 100 mods in the store. The window must paint within one second.

---

## Part 16 — the tree copy makes two extra metadata calls per file

**The problem.** `TreeReplicator.Build` builds a `FileInfo` for every target file only to add its length to a counter. `Copy` builds a second `FileInfo` for the read-only flag. Across about 20,000 files that is about 40,000 avoidable metadata calls per deploy.

**The evidence.** `Staging/TreeReplicator.cs`.

```csharp
try
{
    bytes += new FileInfo(targetFile).Length;
}
```

**The fix.**

1. Read the length from the source file. A hard link and a copy both give a target of that same length.
2. Return the length from `Copy`, which already builds one `FileInfo`.
3. Delete the second `FileInfo`.

**The counter reaches no decision.** `ReplicationReport.Bytes` feeds a log line and nothing else. Drop the counter if the read stays awkward.

**The check.** Time a staging build of a real install before and after.

---

## Part 17 — a copy of the log mis-orders a duplicate line

**The problem.** `MainWindow.CopyLines` sorts the selected lines by `list.Items.IndexOf(item)`. The items are plain strings and the log repeats a string. `IndexOf` returns the first match, so every copy of a repeated line takes the index of the first one.

**The evidence.** `MainWindow.xaml.cs`, `CopyLines`.

```csharp
lines.Add((list.Items.IndexOf(item), item?.ToString() ?? String.Empty));
```

`MainViewModel.Write` adds lines such as `Ready.` many times in one session.

**The fix.**

1. Walk `list.Items` by index, and test each index against `list.SelectedItems.Contains`.
2. Build the output inside that walk. The order is then the order of the list.
3. Delete the tuple list and the sort.

**The check.** Write the same line twice into the log, select both with the Control key, and copy. The clipboard must hold two lines.

---

## Part 18 — the fingerprint writes no field separator for the route

**The problem.** `ProfileFingerprint.AppendRoute` joins the label and the value with no separator. Every other field in that file uses the unit separator, `U+001F`.

**The evidence.** `Profiles/ProfileFingerprint.cs`, `AppendRoute`.

```csharp
text.Append("  route").Append(route.ToString()).Append('\n');
```

Compare it against `AppendVariants` in the same file, which writes the label, then `U+001F`, then the value.

**The fix.** **Do not change the output for a profile that exists today.** A change to the string changes every stored fingerprint, and the window then asks every user for a deploy that writes no new byte.

1. Leave the line as it is.
2. Add a comment that names the missing separator and says why it stays.
3. Add the separator only in a release that also raises `WorkspaceState.Version` and reads an older version as "unknown".

**The check.** Read the fingerprint of a profile before and after. The two must match.

---

## Part 19 — the baseline check exists twice

**The problem.** `ContainerDeployEngine.CheckBaseline` and `BinaryCliDeployEngine.CheckBaseline` hold the same body, word for word. One log line differs.

**The evidence.** Both methods build a `paths` list from `merged.Files`, `gate.Containers`, and `gate.WritePaths`. Both call `BaselineVerifier.CheckFiles`. Both throw `BaselineVerifier.Describe(drift)`.

**The fix.**

1. Move the method to `BaselineVerifier` as a public static method.
2. Give it the context, the merged load, the gate, and the log line as parameters.
3. Call it from both engines.
4. Delete both private copies.

**The check.** The tests in `BaselineDriftTests.cs` must still pass.

---

## Part 20 — the developer harnesses ship in the release build

**The problem.** Five developer surfaces compile into the shipped executable.

| File | Lines |
| --- | --- |
| `SelfTest.cs` | 224 |
| `DeployTest.cs` | 257 |
| `OneModDeployTest.cs` | 245 |
| `Views/FontTestWindow.xaml` and its code-behind | 123 |
| `Views/ThemeTestWindow.xaml` and its code-behind | 317 |

`Program.Main` reads six command line switches that reach them. No `#if DEBUG` guards any of it, and the project holds no separate configuration.

**The fix.** Two routes exist. Decide which one you want, and record the decision in the Results section below.

**Route A.** Wrap each file and each switch in `#if DEBUG`. This keeps the files in the App project, next to the code that they exercise. A Release build then drops them.

**Route B.** Move the three test entry points into `tools/Harness`, which is a console project with a reference to Core already. The two probe windows cannot move, because they need WPF. Guard those two with `#if DEBUG`.

**The two probe windows answer a real question.** Step 10 records that a font family which the machine does not hold reaches `MS.Internal.Invariant.FailFast`. That kills the process with no dialog and no catchable exception. Do not delete them.

**The check.** Build in Release. `BlackboxModManager.exe` must not respond to `--fonttest`.

---

## Part 21 — the App project has no tests

**The problem.** `BlackboxModManager.Tests` references `BlackboxModManager.Core` alone. `MainViewModel` holds 2,702 lines and owns three rules that no test covers.

1. The settings merge rule of `SaveSettings` and `ReloadSettings`. A defect there already lost a game install path once.
2. The pending-changes comparison of `RefreshPending`, which reads `ProfileFingerprint`.
3. The loader contest flow of `AskForLoaders` and `ChooseLoader`.

**The fix.**

1. Change the test project to `net10.0-windows` and set `UseWPF`. Then add a project reference to the App project.
2. Write a fake `IUserInteraction`. The interface exists for this, and `MainWindow` is its only implementation today.
3. Cover the three rules above first.
4. Point the test at a temporary application data root. `AppPaths.Root` is a static get-only property, so add a way to override it for a test.

**A test must build the view model with no window.** The constructor reads `Application.Current.Resources["SurfaceBase"]` inside `RefreshHeroLook`. Part 15 moves that call, so land Part 15 first.

**The check.** `dotnet test` runs green on the Windows CI runner.

---

## Part 22 — nullable reference types are off

**The problem.** All four projects set `<Nullable>disable</Nullable>`. The code guards against null by hand everywhere, and the compiler helps with none of it.

**The evidence.** `BlackboxModManager.Core.csproj`, `BlackboxModManager.App.csproj`, `BlackboxModManager.Tests.csproj`, and `tools/Harness/Harness.csproj`.

**The fix.** Do this one project at a time. Never do it in the same change as another part of this file.

1. Start with `BlackboxModManager.Core`. It holds the rules and it has the most tests.
2. Set `<Nullable>enable</Nullable>` and read the warning list before you change any code.
3. Annotate the return of a method that returns null on purpose. `ModStore.Find`, `ProfileStore.Find`, `SnapshotReader.Load`, and `InstalledMod.Game` are four of them.
4. Add no null check that the code does not need. The goal is to describe what the code already does.
5. Leave the three `third_party` forks alone.

**The build must stay free of `TreatWarningsAsErrors`.** The CI file records why. The forks emit about 21 warnings.

**The check.** `dotnet build` produces no new warning in the project that you changed.

---

## Part 23 — two log files grow without a limit

**The problem.** `App.WriteErrorLog` and `UpdateLog.Log` both call `File.AppendAllText`, and neither one caps the file.

**The evidence.** `App.xaml.cs`, `WriteErrorLog`, and `UpdateLog.cs`, `Log`. Both write into `AppPaths.LogDirectory`.

`MainViewModel.Write` caps the in-memory log at 2,000 lines. The two files have no such cap.

**The fix.**

1. Read the length of the file before each append.
2. Rename the file to `error.log.1` when the length passes 1 MB, and start a new file. Keep one old file and no more.
3. Put the rule in one place. Both callers then use it.

**Neither writer may throw.** Both swallow every exception today, and that rule stays. A log is a convenience.

**The check.** Write 2 MB into the error log. The directory must hold `error.log` and `error.log.1`, and nothing else.

---

## Part 24 — the library gate takes the lock outside a `try`

**The problem.** `LibraryGate.Enter` calls `Monitor.Enter` and then allocates a `Scope` before it returns. An exception between the two leaks the lock, and every later deploy then blocks forever.

**The evidence.** `LibraryGate.cs`.

```csharp
public static IDisposable Enter()
{
    Monitor.Enter(Sync);
    ++_depth;
    return new Scope();
}
```

**The fix.**

1. Allocate the `Scope` first.
2. Call `Monitor.Enter(Sync, ref taken)` with a `bool taken` local.
3. Increment `_depth` only when `taken` is true.
4. Release the lock in a `catch` when the increment or the return fails.

The window for this failure is small. The cost of the failure is an application that hangs with no message, so fix it.

**The check.** The deploy tests must still pass.

---

## Part 25 — the settings write is a read and a write with no lock

**The problem.** `SettingsStore.Update` reads the file, applies a change, and writes the file. Nothing serializes two of those. Two processes that write at the same time lose a key. That is the failure that the doc comment of the method was written to prevent inside one process.

**The evidence.** `Settings.cs`, `Update`. The temporary file name is fixed at `path + ".tmp"`, so two writers collide on that name too.

Two processes are realistic. Velopack starts the application again with a hook argument, and a user can start a second copy.

**The fix.**

1. Take a named mutex around the read and the write. Name it after `AppPaths.Root`, so two different data roots do not block each other.
2. Give the temporary file a unique name. Use a GUID, in the way that `ModImporter` names its scratch directory.
3. Keep the `File.Move` with `overwrite: true`. That part is correct already.

**A hook process must never block on a user.** Give the mutex a short timeout. Skip the write and report it in `update.log` when the wait ends.

**The check.** Start two processes that each call `Update` 100 times with a different key. Both keys must survive.

---

## Part 26 — a helper allocates one array per character

**The problem.** `BinaryCliDeployEngine.Safe` calls `Path.GetInvalidFileNameChars()` inside the character loop. That method returns a new array on each call.

**The evidence.** `Deploy/BinaryCliDeployEngine.cs`, `Safe`.

```csharp
text.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), letter) >= 0 ? '-' : letter);
```

**The fix.**

1. Read `Path.GetInvalidFileNameChars()` once into a `static readonly SearchValues<char>` field.
2. Test with `SearchValues.Contains`.

**Three methods clean a name and no two agree.** `ModStore.Slug`, `ProfileStore.FileName`, and `Safe` each keep a different character set. Give one thing one name. Consider one shared helper with a parameter for the allowed set.

**The check.** The tests in `BinaryCliEngineTests.cs` must still pass.

---

## Part 27 — the 7-Zip path check runs twice per test

**The problem.** `SevenZipTool.Path` calls `File.Exists` on every read, and `SevenZipTool.Exists` reads `Path`. So each test of `Exists` costs two disk calls, and `ExtractOther` then reads `Path` again.

**The evidence.** `Store/SevenZipTool.cs`.

```csharp
public static string Path
{
    get
    {
        string full = System.IO.Path.Combine(AppContext.BaseDirectory, DirectoryName, ExecutableName);

        return File.Exists(full) ? full : null;
    }
}
```

**The fix.**

1. Make `Path` a `static readonly` field with a lazy initializer.
2. Keep `Exists` as a read of that field.

**The file does not appear or go away while the application runs.** It ships beside the executable. A cached answer is correct.

**The check.** The import tests must still pass.

---

## Results

Fill this table in as each part lands. Record a measurement for every speed part, before and after.

| Part | State | Note |
| ---- | ----- | ---- |
| 1 | Not started | |
| 2 | Not started | |
| 3 | Not started | |
| 4 | Not started | |
| 5 | Not started | |
| 6 | Not started | |
| 7 | Not started | |
| 8 | Not started | |
| 9 | Not started | |
| 10 | Not started | |
| 11 | Not started | |
| 12 | Not started | |
| 13 | Not started | |
| 14 | Not started | |
| 15 | Not started | |
| 16 | Not started | |
| 17 | Not started | |
| 18 | Not started | |
| 19 | Not started | |
| 20 | Not started | |
| 21 | Not started | |
| 22 | Not started | |
| 23 | Not started | |
| 24 | Not started | |
| 25 | Not started | |
| 26 | Not started | |
| 27 | Not started | |
