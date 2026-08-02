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
