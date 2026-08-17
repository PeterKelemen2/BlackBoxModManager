# Known upstream defects

Defects in the MIT libraries. We work around each one. We do not fix them, because a fix changes behavior that we cannot test yet.

Review this list when a symptom does not match the code you wrote.

## 1. `ASMBuilder` emits the wrong immediate length for 8-bit registers

**Where:** `CoreExtensions/CoreExtensions/Native/ASMBuilder.cs`, the eight methods `MovToAL`, `MovToCL`, `MovToDL`, `MovToBL`, `MovToAH`, `MovToCH`, `MovToDH`, `MovToBH`.

**What happens:** each method takes a `byte` and calls `BitConverter.GetBytes(value)`. The result is 2 bytes. The opcodes are `MOV_TO_AL = 0xB0` through `MOV_TO_BH = 0xB7`. The x86 encoding `B0+r ib` takes a one-byte immediate. The methods therefore emit one byte too many and produce invalid machine code.

**What we did:** we cast to `(short)` to resolve a compile error, not to fix the defect. `Half` gained an implicit conversion from `byte` in current .NET, which made the call ambiguous between the `Half` and `short` overloads. Both overloads return 2 bytes, so the cast changes nothing about the length.

**Why we did not fix it:** `ASMBuilder` has no reference anywhere in Nikki or in Endscript. It is dead code on our path. A correct fix replaces `AddRange(GetBytes(...))` with `Add(value)`, which changes emitted machine code. Nobody should make that change without a test.

**Act on this only if** we ever use `ASMBuilder`. That would mean memory patching, which belongs to `SpeedReflect`, not to on-disk modding.

## 2. `Launch.Deserialize` does not set `ThisDir`

**Where:** `Endscript/Endscript/Core/Launch.cs`.

**What happens:** `ThisDir` carries `[JsonIgnore]`, so deserialization leaves it null. `CheckEndscript` and `LoadLinks` both call `Path.Combine(this.ThisDir, ...)`. A null value throws `ArgumentNullException`.

**Work around it:** set `launch.ThisDir = Path.GetDirectoryName(manifestPath)` immediately after every `Deserialize` call. Never call `Deserialize` without that line.

## 3. `Launch.Deserialize` breaks on a byte order mark

**Where:** `Endscript/Endscript/Core/Launch.cs`.

**What happens:** the method calls `File.ReadAllText`, then tests `settings.StartsWith("[VERSN1]")`, then slices `settings[8..]`. `File.ReadAllText` strips a UTF-8 byte order mark by default, so this usually works. A file with an unusual encoding or with leading whitespace fails the `StartsWith` test. The thrown error is `InvalidVersionException(1)`, which reads as "this is not a VERSN1 file" and hides the real cause.

**Work around it:** when `InvalidVersionException` surfaces, report the first 16 bytes of the file in the message. That turns a misleading error into a readable one.

## 4. `Launch.Deserialize` replaces backslashes without discrimination

**Where:** `Endscript/Endscript/Core/Launch.cs`.

**What happens:** the method runs `settings.Replace(@"\", @"\\")` across the whole body. This is what makes the non-standard dialect parse. A manifest that used a correct JSON escape, such as `\"` or `\n`, would corrupt.

**Work around it:** nothing, for now. No observed manifest uses a real escape. Keep a round-trip test over `example_mods` so a change here cannot pass unnoticed. See [01-console-harness.md](01-console-harness.md).

## 5. `ProcessScript` reports an out-of-range `Choice` as an unrelated error

**Where:** `Endscript/Endscript/Core/EndScriptManager.cs`, inside `ProcessScript`.

**What happens:** the method runs `var option = select.Options[select.Choice]`. An out-of-range `Choice` throws `IndexOutOfRangeException`. The surrounding catch converts that into `new Exception("Unable to find end to a selectable statement")`. The message names the wrong problem, and it names no file and no line.

**Work around it:** validate `Choice` against `Options.Length` before you resume. Throw your own error that names the mod, the script, and the option set. Never pass an unvalidated stored selection straight into `Choice`.

## 6. `BaseProfile.Load` adds a container per call, and its duplicate check compares raw text

**Where:** `Endscript/Endscript/Profiles/BaseProfile.cs`, inside `Load`, `AddNew`, and `Contains`.

**Corrected in step 6.** An earlier version of this entry said that `AddNew` has no duplicate check. It has one. The real defect is narrower and it needs the same fix.

**What happens:** `Load` calls `this.AddNew(launch.Files[i])` for every entry. `AddNew` calls `Contains(filename)` first and throws `DatabaseExistenceException` for a duplicate. `Contains` compares with `String.Equals(..., OrdinalIgnoreCase)` on the raw string, so it normalizes neither the separator nor the path.

**Two failure modes follow.**

1. Two manifests that write the container the same way stop the load with `DatabaseExistenceException`. That is loud, and the message names the file.
2. Two manifests that write one container two ways, such as `GLOBAL\GLOBALB.LZC` against `GLOBAL/GlobalB.lzc`, both pass the check. The profile then holds two `SynchronizedDatabase` objects for one file. `Save` writes that file twice from two different in-memory states, the last write wins, and the edits of the first mod disappear with no error.

**This directly threatens the single-pass design.** The brief requires one load, then all mods applied, then one save. A loop that calls `profile.Load(launch)` once per mod hits failure mode 1 for every shared container.

**Work around it:** call `Load` once. Build one synthetic `Launch` whose `Files` is the union of every enabled mod's `Files`, deduplicated on a key that normalizes the separator and the letter case. `MergedLaunch` does this. See [06-binary-deployment.md](06-binary-deployment.md).

**One spelling per container, and only one.** The union keeps the spelling of the manifest, because `CollectionMap` matches a container by that exact text. Two enabled mods that spell one container differently cannot share a load at all, and `MergedLaunch` reports that instead of loading both. See [99-api-notes.md](99-api-notes.md).

## 7. `SaveHashList` writes to disk during `Save`

**Where:** `Endscript/Endscript/Profiles/Underground2Profile.cs` and every sibling profile class.

**What happens:** `BaseProfile.Save()` calls `SaveHashList()` as its last step. That method calls `Directory.CreateDirectory(Path.GetDirectoryName(CustomHashList))` and then opens `CustomHashList` with `FileMode.Create`. It creates a directory and overwrites a file.

**Two failure modes.** A null `CustomHashList` throws inside `Path.GetDirectoryName`. A `CustomHashList` that points into the Binary install of the user makes us write into a directory we do not own.

**Work around it:** always set `CustomHashList` to a path under our own application data directory. Never point it at the Binary install. See [02-binary-install.md](02-binary-install.md).

## 8. The hash list statics make the libraries single-threaded

**Where:** every profile class, plus `Map.ReloadBinKeys()` in Nikki.

**What happens:** `MainHashList` and `CustomHashList` are `static`. `LoadHashList` calls `Map.ReloadBinKeys()`, which resets global state. Two profiles loaded at the same time overwrite each other.

**Work around it:** serialize all library access behind one lock. Never load two profiles at once. Never run a deploy for two games in parallel. If the UI needs to stay responsive, run the whole deploy on one background thread, not on several.

## 9. Nikki writes log files into the working directory

**Where:** every `DatabaseLoader` and `DatabaseSaver` in Nikki, plus `Logger` in `CoreExtensions`.

**What happens:** each loader and saver constructs `new Logger("MainLog.txt", ...)`. `Logger` opens that name with no directory, so the file lands in the current working directory of the process. The harness run left a `MainLog.txt` in the repository root. Binary ships one beside `Binary.exe` for the same reason.

**Work around it:** set the working directory of the process before any container work. Put it under our own application data directory. Never leave it at the directory the user started us from.

## 10. `DatabaseSaver.WriteFromStream` writes a temporary file beside the executable

**Where:** `Nikki/Support.<Game>/Framework/DatabaseSaver.cs`.

**What happens:** the method takes the directory from `Process.GetCurrentProcess().MainModule.FileName` and writes the new container there, then moves it over the target. `Invoke` selects this path for a container that is not compressed and is larger than 64 MB. An installation directory that we cannot write to therefore fails the save.

**Act on this only if** we hit a container over 64 MB. `GLOBALB.LZC` is compressed, so Underground 2 takes the buffer path instead. Later games may not.

## 11. `stop_errors true` drops every later failure of a script

**Where:** `Endscript/Core/EndScriptManager.cs`, `ExecuteSingle`, and `Endscript/Commands/StopErrorsCommand.cs`.

**What happens:** `ExecuteSingle` catches every exception of a command. It adds an `EndError` only when `_stop_errors` is false. So one `stop_errors true` line makes the manager drop every later failure of that script with no trace. `manager.Errors` then reports nothing and the script looks like a script that worked.

**Why it matters to us:** our whole deploy rule is that one entry in `manager.Errors` fails the deploy. This command defeats that rule. A broken mod that looks installed is the worst result this project can produce.

**Work around it:** refuse the command. `CommandCatalog` marks it `Reject`, and `CommandGate` stops the deploy before it writes. See step 8.

**The scope is one variant.** `ContainerDeployEngine.Apply` builds one `EndScriptManager` per variant, so the flag never reaches the next mod.

## 12. `CheckboxCommand` and `ComboboxCommand` default `LastCommand` to zero

**Where:** `Endscript/Commands/CheckboxCommand.cs` and `ComboboxCommand.cs`.

**What happens:** both declare `public int LastCommand { get; set; }` with no initializer, so the value starts at 0. `IfStatementCommand` declares the same property with `= -1`. `ProcessScript` and our own walk both test for -1 to find a statement with no closing `end`.

**Why it does not bite today:** `CommandChase` sets `LastCommand` for every statement that has an `end`, and it throws for a statement that does not. So the default never survives a successful chase.

**Act on this only if** you write code that reads `LastCommand` without a chase first. Test for a value that is not greater than the index of the statement, and never for -1 alone.

## 13. `ProcessScript` throws when an `if` branch has no block

**Where:** `Endscript/Core/EndScriptManager.cs`, `ProcessScript`.

**What happens:** the method reads `Options[Choice].Start` and throws `Missing optional command '<name>'` when that value is -1. An `if` command always offers `do` and `else`. A script that writes a `do` block and no `else` block therefore ends the deploy whenever the condition is false.

**Work around it:** the flattener of step 8 walks every branch that exists and warns when a branch has no block. The warning names the file and the line before the deploy starts.

## 14. SharpCompress decodes a solid 7z once for each entry

**Where:** `SharpCompress` 1.0.0, the 7z path. `Core/Store/ArchiveExtractor.ExtractOther` calls it.

**What happens:** a solid 7z holds one compressed stream for a whole group of files. To read one file, a reader must decode the stream from the start of the group. SharpCompress does that decode again for every entry, so the cost of an import grows with the square of the entry count.

**The measurement.** The archive `NFSMWUHUD11302024a.7z` holds 1205 entries and 1.12 GB behind 98 MB. The time to decode one entry, with no disk write:

| Entry index | Time    |
| ----------- | ------- |
| 0           | 13 ms   |
| 50          | 92 ms   |
| 200         | 472 ms  |
| 500         | 722 ms  |
| 900         | 6740 ms |

The whole archive takes more than 30 minutes. `7z.exe` writes the same 1205 files in **3.9 seconds**, so the format is not the problem.

**`ExtractAllEntries` does not help.** That method returns the reader that SharpCompress builds for a solid archive. It reached entry 700 of 1205 after 532 seconds, which is the same curve.

**What we did:** the application ships 7-Zip and starts `7z.exe` for a 7z and a rar. SharpCompress still reads the listing of every archive, because that read costs milliseconds and it carries the safety guard of the entry names. It also still unpacks the files when `7z.exe` is not beside the application. See [13-import-progress.md](13-import-progress.md), Parts C and D.

**Act on this** if anybody removes 7-Zip from the build. The import then works and takes half an hour for an archive of this shape. No setting of SharpCompress avoids that. A fix needs a decoder that reads each solid group one time.

## 15. `VersionCommand` reads a static that the library never sets

**Where:** `Endscript/Endscript/Version.cs` and `Endscript/Commands/VersionCommand.cs`.

**What happens:** `Version.Value` is a static property with no default. Nothing in Endscript assigns it. `VersionCommand.Prepare` then runs `Version.Value.CompareTo(this._version)` with no null test. A script that holds a `version` line ends the parse with a `NullReferenceException`. The text of that error is "Object reference not set to an instance of an object", which names neither the static nor the cause.

**This hits real mods and not edge cases.** Binary writes the `version` line into the launcher script that it exports, so most published mods carry one. The mod `NFSMWRV-1024x-Advanced` states `version 2.8.3` on line 3 of `RecompiledVinylsMain.end`. The Mod tab reported that line as a parse failure and showed no variant and no question.

**Work around it:** the host sets the static. `EndscriptVersion.Ensure` assigns `BinaryInstallStatus.ExpectedVersion`, which is `2.8.3.0`. `ScriptReader.Parse` calls it before it builds the parser.

**The value is a constant and it does not follow the Binary install of the user.** The number states what our engine runs, and our engine is the Endscript library. A user who holds a newer Binary still gets the command set of this library. A script that asks for more then gets the message of the library, which names both numbers and is correct.

**Do not add a second parse path.** `ScriptReader.Parse` is the only place that builds an `EndScriptParser`. Any new caller must call `Ensure` first.

## 16. A container that `new` creates is in no manifest, and the save reaches the vanilla copy

**Where:** `Endscript/Commands/NewCommand.cs`, `Endscript/Profiles/BaseProfile.New` and `Delete`, and `Nikki/Support.<Game>/Framework/DatabaseSaver.WriteFromBuffer`.

**What happens:** three facts combine into one data loss.

1. `new [type] [file]` adds a container to the loaded profile at run time. `delete [file]` calls `SaveOneSDB` and writes that container to disk. Neither file is in the `Files` list of any manifest, so `MergedLaunch` never sees it.
2. `DatabaseSaver.WriteFromBuffer` opens the target with `File.Open(path, FileMode.Create)`. That call truncates the existing file. It does not replace the directory entry.
3. `TreeReplicator` builds the staging copy with hard links. A staging container, the vanilla container, and the live container are one file with several names.

A write with `FileMode.Create` keeps the share, so the new content reaches every name. **The mod rewrites the vanilla baseline and the game of the user, and the revert then restores modded files.**

**We hit this.** The mod `NFSMWRV-1024x-Advanced` runs `new negate "CARS\<car>\VINYLS.BIN"` and `delete "CARS\<car>\VINYLS.BIN"` for 46 cars. Its manifest names one container, `GLOBAL\GLOBALB.LZC`. One interrupted deploy rewrote 8 files in the vanilla copy of a real install before it stopped.

**Work around it:** make every container private, not only the containers of the merged load. `CommandGate.Check` returns the target of every command as `GateResult.Containers`, and `ContainerDeployEngine.Prepare` calls `StagingFiles.MakePrivate` for each one that exists. A container that does not exist yet needs no call, because a new file shares nothing.

**A container is not the only thing that a script writes.** The first fix covered the commands that carry an edit key. It missed every command of category `FilesystemEffect`, because those carry no key. `unlock_memory` writes a header over five memory files of the game. `move_file` and `copy_file` write a target that no manifest names.

The same real install proves it. Four files carry the time of the deploy that failed:

```
GLOBAL/FrontEndMemoryFile.bin    18:24
GLOBAL/InGameMemoryFile.bin      18:24
GLOBAL/PermanentMemoryFile.bin   18:24
GLOBAL/GlobalMemoryFile.bin      18:24
```

`CommandGate.Check` now also returns `GateResult.WritePaths`, which holds the resolved path of every write of every filesystem command. `EditKeyExtractor` already expands the word `all` of `unlock_memory` into the five names, so the list is complete. `Prepare` makes each of those private too.

**The manifest list is never the whole list.** Any future code that writes into the staging copy must take its file list from the commands and not from the manifest.

**The verify read the same short list, and failed a clean deploy.** `ContainerDeployEngine` reported only the containers of the merged load to `StagingVerifier`. It did not know about the containers that only a script names. The verify then failed every one of them as "no mod supplied it," on a deploy that changed nothing else. `ContainerReportBuilder` now reports every container that `GateResult.Containers` names, not only the manifest ones. See fact 11 of [06-binary-deployment.md](06-binary-deployment.md).

**The verify missed the files that are no containers, and it failed a clean deploy again.** `GateResult.WritePaths` covered them for `MakePrivate` and nothing carried them into the report. `unlock_memory all` therefore stopped the first full deploy of `NFSMWRV-1024x-Advanced` with three problems, and the first one read "The game file GLOBAL/GLOBALMEMORYFILE.BIN in the staging copy differs from the vanilla state, and no mod supplied it."

`ContainerReportBuilder.BuildScriptWrites` now turns every entry of `WritePaths` into a `ScriptWrite`, and `GateResult.WritePathContributors` names the variant behind each one. `StagingVerifier` leaves those paths out of the drift check.

**A `ScriptWrite` carries no check of its own, and it must not.** The static walk enters both branches of every `if`, and this mod guards 97 `move_file` commands with one. So a path in that list is a path that a script may write, and not one that it did. An absent file is normal.

**How to repair an install that this damaged.** The vanilla copy holds the modded content, so every later deploy reads modded input and reports errors that name no cause.

1. Close the application.
2. Delete the workspace directory `<game dir>.blackbox`.
3. Restore the game install from its installer, or reinstall the game.
4. Start the application and deploy one time. That records a new baseline.

`BaselineVerifier` now compares the vanilla copy against `vanilla.json` before every deploy and stops when the two disagree. That catches the next hole. It cannot catch a baseline that was already wrong when the application recorded it.

## 17. Nikki forces a full garbage collection inside its hot loops

**Where:** `CoreExtensions/Management/ForcedX.cs`, plus the callers in `Nikki/Core/Manager.cs`, `Nikki/Support.<Game>/Class/TPKBlock.cs`, both database framework classes, and `Endscript/Core/CollectionMap.cs`.

**What happens:** `ForcedX.GCCollect` runs `GC.Collect`, then `GC.WaitForPendingFinalizers`, then `GC.Collect` again. That is two blocking collections of every generation. Four call sites run it inside a loop.

1. `Manager<T>.Capacity` calls it on every growth. The `Add` methods grow with `this.Capacity += this.Extender`, which is a fixed step and not a doubling. A manager that takes N collections therefore grows N divided by `Extender` times, and each growth copies the whole array and collects.
2. `Manager<T>.Add` calls `Contains(cname)` first, and `Contains` is a linear scan with a string compare. Adding N collections costs N squared compares.
3. `CollectionMap.LoadMapFromProfile(true)` rebuilds the whole path map and then collects. `NewCommand` and `DeleteCommand` both call it.
4. `DatabaseSaver.Invoke` and `DatabaseLoader` collect once for each container.

**What we did:** the `Capacity` setter no longer collects. `Manager<T>.Grow` doubles the array instead of adding a fixed step. `CollectionMap.LoadMapFromProfile` ignores its `gccollect` flag. Every `ForcedX.GCCollect` call in Nikki and in Endscript is gone. `ContainerByteStabilityTests` proves that the containers still hold the same bytes.

**The forced collections were not the cost.** This entry first blamed them for a deploy that took about 100 seconds for each car. A measurement disproved that. Removing every call changed the time of one load and save of six real containers from 49,891 ms to 49,480 ms. The linear `Contains` in `Add` is real and it is also not the cost.

**Where the time goes.** See defect 20. One save of `CARS\911GT2\VINYLS.BIN` took 46,198 ms, and 45,159 ms of that was the native compressor. The container holds 322 textures and 281 MB of decompressed texture data, and Nikki compresses all of it on every save.

**Keep the changes.** They cost nothing, they remove two blocking collections from paths that run thousands of times, and they make the heap behavior of a large deploy sane. Do not expect them to change a wall clock number on their own.

## 18. `delete` removes a container from the profile, and the next mod cannot find it

**Where:** `Endscript/Profiles/BaseProfile.Delete` and `Endscript/Commands/DeleteCommand.cs`.

**What happens:** `delete [file]` saves the container to disk and then calls `RemoveAt`. The container leaves the profile. A later command that names the same container fails, and `ImportCommand`, `AddCollectionCommand`, `CopyCollectionCommand`, `RemoveCollectionCommand` and `StaticCommand` all report the same text: `File <name> was never loaded`.

**We hit this.** The mod `nfsmwuhud11302024a` runs `delete GLOBAL\GLOBALB.LZC` on line 249 of `assets/userstart.end`. It sits before `NFSMWRV-1024x-Advanced` in the load order. The vinyls mod then runs `import override GLOBAL\GLOBALB.LZC DBModelParts "CarParts\VINYL.bin"` on line 14 of `Menu\Install.end`, and the deploy reported 391 errors that started with that line.

**This is our design meeting the library.** The engine loaded one profile for every enabled mod and saved one time. That rule cannot survive `delete`, because one mod can unload a container that another mod needs.

**What we did:** the engine runs one load, one script and one save for each variant, in load order. Each pass builds a new `BaseProfile` from the manifest of that one variant. The next pass reads what the last pass wrote. This is what Binary 2.8.3 does, and every published mod is written for it. See [06-binary-deployment.md](06-binary-deployment.md).

## 19. `Map.BinKeys` takes writes from parallel tasks and it is a plain dictionary

**Where:** `Nikki/Core/Map.cs`, `Nikki/Utils/Hashing.cs` and `Endscript/Profiles/BaseProfile.cs`.

**What happens:** `Map.BinKeys` and `Map.VltKeys` were `Dictionary<uint, string>`. `Hashing.BinHash` adds an entry on every call, and `Hashing.VltHash` writes through the indexer. `BaseProfile.Load` and `BaseProfile.Save` read and write containers with `Task.Run` and `Task.WaitAll`, and parsing one container hashes thousands of strings.

A `Dictionary` gives no guarantee under a write from two threads. The result is a lost entry, a corrupt bucket chain, or a read that never returns. `TryAdd` does not change that.

**We saw no failure from this.** It is a race, so it shows up as a rare wrong hash name or a hang, and neither names a cause.

**What we did:** both maps are now `ConcurrentDictionary<uint, string>`. Every caller uses `TryGetValue`, `TryAdd`, the indexer, `Values` or `Clear`, and all of those exist on the concurrent type.

**One behavior changed.** `SaveHashList` writes the custom hash list from `Map.BinKeys.Values`, and a concurrent dictionary does not promise the insert order. The file is our own, it lives under our application data, and it is read as a set of names. The order does not matter. No container byte depends on it.

## 20. Nikki compresses every texture on every save, and one lock serializes all of it

**Where:** `Nikki/Support.Shared/Class/TPKBlock.GetCompressedFullData` and `Nikki/Utils/Interop.cs`.

**What happens:** a save of a texture container assembles every TPK block again from scratch. `GetCompressedFullData` walks every texture, reads `texture.Data`, and calls `Interop.Compress(array, LZCompressionType.BEST)`. `BEST` makes the native library try every codec and keep the smallest result. Nikki keeps no record of which textures changed, so an edit to one texture recompresses all of them.

`Interop` then held one static lock around every native call, so none of that work could use a second core.

**The measurement.** One load and one save of six real Most Wanted containers, with no edit:

| Container | Save time | Compressed |
| --- | --- | --- |
| `GLOBAL\GLOBALB.LZC` | 152 ms | none |
| `GLOBAL\GLOBALA.BUN` | 4 ms | none |
| `CARS\TEXTURES.BIN` | 3,902 ms | 489 calls, 32 MB |
| `CARS\911GT2\VINYLS.BIN` | 46,198 ms | 322 calls, 281 MB |
| `FRONTEND\FRONTB.LZC` | 55 ms | none |
| `GLOBAL\INGAMEA.BUN` | 44 ms | none |

**Read the input of that table.** The 9.3 MB `CARS\911GT2\VINYLS.BIN` was not a vanilla file. It was the output of an earlier run of the vinyls mod, which the damaged baseline had recorded as vanilla. See defect 16. A vanilla container of that car is 1,750,208 bytes.

That is why the failed deploy took 35 minutes. Each car loaded a container that already held the textures of a past run, so every save recompressed all of them again. A deploy against a clean install starts from the small container and costs far less.

The same six containers from a clean install take 20,448 ms before this change and 8,757 ms after it.

**The lock is not needed.** `BlockCompress` and `BlockDecompress` take the input buffer and the output buffer from the caller and keep no state. A test compressed 35 blocks of real texture data on one thread, then on four threads with the lock gone, and every output byte matched. The same test took 429 ms with the lock and 219 ms without it.

**What we did:** `Interop` holds no lock. `GetCompressedFullData` computes the running data offset of every texture in one cheap pass, because `Texture.DataLength` gives the length with no decompression. It then compresses every texture with `Parallel.For` and writes the results in texture order.

One save of `CARS\911GT2\VINYLS.BIN` fell from 46,198 ms to 15,471 ms on four cores. The six containers together fell from 51,267 ms to 17,654 ms. Every output byte stayed the same.

**`BEST` was still the larger part of the cost, and defect 21 removed it.** Read that entry next.

**What is still open.** The real fix is to keep the compressed blocks that the loader read and write them back for a texture that nothing changed. That does not work as written, because the header of each texture holds the total data length before it. Adding one texture shifts that number for every later texture, so their bytes change too. A fix needs the header to move out of the compressed blob, and that changes the container format.

**That fix would not help a vinyl mod anyway.** `add_texture` adds 47 textures to each car container, and `SortTexturesByType` then shifts the data offset of most of the textures that were already there. Almost every cached block would be stale.

## 21. `BEST` runs JDLZ for every texture on every save, and JDLZ is 60 times slower than HUFF

**Where:** `Nikki/Support.Shared/Class/TPKBlock.GetCompressedFullData` and `GetCompressedByParts`, plus the same two methods in `Support.Undercover/Class/TPKBlock.cs`.

**What happens:** every one of those call sites passed `LZCompressionType.BEST`. The native library then runs each codec that it holds and keeps the smallest result. Nikki keeps no record of which codec the loader read for a texture, and no record of which textures an edit changed, so every save recompressed every texture with every codec.

**The measurement.** One 4,194,432-byte texture of the mod `NFSMWRV-1024x-Advanced`, through `BlockCompress` directly:

| Codec | Time   | Output    | Rate       |
| ----- | ------ | --------- | ---------- |
| RAWW  | 35 ms  | 4,194,448 | 114 MB/s   |
| JDLZ  | 635 ms | 224,534   | 6.3 MB/s   |
| HUFF  | 9 ms   | 169,584   | 405 MB/s   |
| COMP  | throws | —         | —          |
| RFPK  | throws | —         | —          |
| BEST  | 646 ms | 169,584   | 6.2 MB/s   |

Three facts follow. **JDLZ is the whole cost of `BEST`.** **`COMP` and `RFPK` throw an SEH exception, so `BEST` never had five codecs to choose from.** **The x86 library that Binary 2.8.3 ships gives the same times and the same bytes**, so the x64 rebuild is not slow and there is nothing to fix in the native code.

**What we did:** `Texture.SourceCompression` holds the codec that the loader read from the block of that texture, and every save writes the block again with it. `MagicHeader.SourceCompression` does the same for a container that stores textures in parts. A texture that `add_texture` creates has no block to read a codec from, so it takes the default, and the default is HUFF.

**One load and one save of the six real Most Wanted containers, with no edit:**

| | Save time | Compressor thread time |
| --- | --- | --- |
| `BEST` | 7,128 ms | 30,849 ms |
| the codec of each texture | 1,094 ms | 2,585 ms |

**Every output byte matched.** `ContainerByteStabilityTests` needs no new baseline. A vanilla container keeps the codec that it already held, so a load and a save with no edit writes the same file that `BEST` wrote.

**Why the default is HUFF and not `BEST`.** One car of the vinyl mod, which adds 47 textures of 4 MB to the 2.8 MB vanilla container of the 350Z:

| Default for a new texture | Save time | Container |
| --- | --- | --- |
| HUFF | 1,102 ms | 26.9 MB |
| JDLZ | 12,816 ms | 10.3 MB |
| `BEST` | 13,920 ms | 9.8 MB |

Across the 45 cars that is about 50 seconds against about 10 minutes, and about 1.2 GB against about 440 MB. **The project chose speed.** A vanilla car vinyl container holds 252 textures and 244 of them are HUFF, so HUFF is also what the game already reads for this kind of data.

`GetCompressedByParts` now also computes the running offsets in one pass and compresses with `Parallel.For`, in the same shape as `GetCompressedFullData`. No Most Wanted container of a real install takes that path, so the change has no test. Every container of the six reads as `CompressedFullData`.

**Never pass `COMP` or `RFPK`.** Both throw, in the x64 build and in the x86 build.

## 22. `BaseProfile.Delete` rewrites both hash lists on every call

**Where:** `Endscript/Profiles/BaseProfile.cs`, inside `Delete`.

**What happens:** `Delete` called `SaveHashList()` after it saved the container. `SaveHashList` reads the whole main hash list of the Binary install into a `HashSet`, and then it rewrites the custom hash list from `Map.BinKeys.Values`. `BinKeys` grows with every container that the pass loads, so each rewrite is larger than the one before it.

`NFSMWRV-1024x-Advanced` runs 60 `delete` commands, so one deploy paid for 60 of those pairs.

**What we did:** `Delete` no longer calls it. `Save()` calls `SaveHashList` as its last step, so the list still reaches disk when the pass ends. The custom hash list lives under our own application data directory and nothing outside our process reads it during a run.

## 23. `CollectionMap.LoadMapFromProfile` rebuilds the whole map for one new container

**Where:** `Endscript/Core/CollectionMap.cs`, plus `Endscript/Commands/NewCommand.cs` and `DeleteCommand.cs`.

**What happens:** `LoadMapFromProfile` clears the dictionary and then walks every collection of every manager of every loaded container. `FastEstimateCapacity` walks the same tree a second time to size the dictionary. `NewCommand.Execute` and `DeleteCommand.Execute` both called it after every single command.

`NFSMWRV-1024x-Advanced` runs 46 `new` commands and 46 `delete` commands, so it rebuilt the map 92 times over a profile that grows to tens of thousands of collections.

**What we did:** `CollectionMap.AddDatabase` adds the keys of one container and `RemoveDatabase` drops them. `NewCommand` calls the first and `DeleteCommand` calls the second, before the profile drops the container. `LoadMapFromProfile` stays for the constructor.
