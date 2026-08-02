# Roadmap

This directory holds the implementation plan. Work through the files in order. Each file states what to build, how to verify it, and what can go wrong.

Read `../../project_brief.md` first. The brief holds the format research and the design decisions. This roadmap holds the work.

## Sequence

| Step | File                                                         | Gates                           |
| ---- | ------------------------------------------------------------ | ------------------------------- |
| 0    | Done. See "Completed" below.                                 | —                               |
| —    | [00-test-environment.md](00-test-environment.md)             | Reference. Read before step 1.  |
| 1    | Done. See "Completed" below.                                 | —                               |
| 2    | Done. See "Completed" below.                                 | —                               |
| 3    | Done. See "Completed" below.                                 | —                               |
| 4    | Done. See "Completed" below.                                 | —                               |
| 5    | [05-mvp-shell.md](05-mvp-shell.md)                           | The UI.                         |
| 6    | [06-binary-deployment.md](06-binary-deployment.md)           | The success criterion.          |
| 7    | [07-game-profiles.md](07-game-profiles.md)                   | Games other than Underground 2. |
| 8    | [08-command-classification.md](08-command-classification.md) | Mods outside the two examples.  |
| 9    | [09-texmod.md](09-texmod.md)                                 | Nothing. Explicitly last.       |

Steps 1 to 3 prove that the foundation works. **All three pass.** Step 4 is done. Step 5 is open.

## Completed

### Step 0 — the library forks

The three MIT libraries are forked, retargeted, and building.

- `third_party/CoreExtensions` — branch `net10-retarget`, based on commit `1e1e687`
- `third_party/Nikki` — branch `net10-retarget`
- `third_party/Endscript` — branch `net10-retarget`

All three target `net10.0` and build with 0 errors. `LZCompressLib.dll` reaches the output directory through the existing `CopyToOutputDirectory` rule in `Nikki.csproj`.

Four facts from that work carry into everything below. Read them before you touch the submodules.

1. **`CoreExtensions` must stay on commit `1e1e687`.** Its master branch is version 1.2.2 and deletes `ReadNullTermUTF8` and `WriteNullTermUTF8`. Nikki calls both. A merge to master breaks the build with 46 errors. The branch is 2 commits behind master on purpose. Never merge it forward.
2. **The `ProjectReference` paths point at flat siblings.** Upstream wires the libraries through nested submodules under `Modules/`, which clone empty. Our forks reference `..\..\<Repo>\<Repo>\<Repo>.csproj` instead. The forks therefore build only inside this `third_party/` layout.
3. **`CoreExtensions` at that pin was `PlatformTarget x86`.** We changed it to x64. An x86-marked assembly cannot load into the x64 process that `LZCompressLib.dll` forces.
4. **Eight call sites in `CoreExtensions/Native/ASMBuilder.cs` needed a cast.** `Half` gained an implicit conversion from `byte`, so `BitConverter.GetBytes(value)` became ambiguous. We cast to `(short)`. See [98-known-upstream-defects.md](98-known-upstream-defects.md) — the cast preserves an upstream defect on purpose.

### Step 1 — the console harness

**All four checks of [01-console-harness.md](01-console-harness.md) pass.** The libraries work end to end. The game reads the container that Nikki wrote, and the mod takes effect.

- `tools/Harness` applies one manifest to a scratch copy of the game.
- `tools/run-harness.sh` publishes the harness for `win-x64` and starts it under Wine.
- `tests/Endscript.Tests` holds the permanent manifest round trip test.
- `BlackboxModManager.slnx` ties the five projects together.

Read [tools/Harness/README.md](../../tools/Harness/README.md) to run it.

Three facts carry forward.

1. **A self-contained `win-x64` build of .NET 10 runs under system Wine 11.13.** A fresh prefix needs no configuration. The `LZCompressLib.dll` P/Invoke works. Step 3 still has to confirm the same inside the GE-Proton prefix.
2. **`Save` rewrites every loaded container, not only the edited ones.** No command targets `GLOBALA.BUN`, and the file still changed. Step 6 must decide what to do about that.
3. **The game accepts a `GlobalB.lzc` that carries no whole-file compression.** Nikki writes it that way, and the file grows by 60 percent. This matches what Binary does.

**Throw the harness away after step 3.** Do not grow it into the application.

### Step 2 — Binary install discovery

**Done.** `src/BlackboxModManager.Core` holds the first application code. The harness drives it, and no Binary path is hardcoded anywhere. Read [02-binary-install.md](02-binary-install.md) for the type list and the findings.

Three facts carry forward.

1. **`Console.ReadLine` never returns on a Wine console.** A minimal test program behaves the same way, so this is Wine. The harness therefore has no first-run prompt. **The step 5 UI must ask its questions in a dialog.**
2. **Automatic discovery works.** The common directory scan finds an unpacked Binary install with no help from the user. The registry scan finds nothing, which is expected.
3. **`userkeys` is generated output, and our redirect matches it.** One deploy wrote the same 1018 labels that a Binary run wrote. See [00-test-environment.md](00-test-environment.md).

### Step 3 — Wine verification

**Done.** The game launched from the scratch directory inside the GE-Proton10-34 prefix, from a single-file `win-x64` publish, and the URL career races ran one lap. Read [03-wine-verification.md](03-wine-verification.md) for the run matrix and the probe results.

Four facts carry forward.

1. **The container does not depend on the Wine build or on the publish shape.** Wine 11.13 and GE-Proton10-34, multi-file and single-file, all wrote the same bytes. The `LZCompressLib.dll` P/Invoke works on both. This closes the largest open risk in the project.
2. **Hard links, symbolic links, and copies all work on both builds.** No privilege blocked a symbolic link. **Step 5 can default to hard links.** Do not hardcode that. Call `LinkSupport.Probe` against the real target, because a hard link still fails across filesystems.
3. **A Proton build ships no `winepath` program.** A wrapper that calls it falls through to system Wine and starts a wineserver of the wrong version. Convert paths with `wine winepath.exe -w`, using the same build.
4. **`IncludeNativeLibrariesForSelfExtract` does not put the DLL beside the executable.** It extracts to a temporary directory and the P/Invoke resolves from there. Test the resolution with `NativeLibrary.TryLoad`, never the file location.

### Step 4 — our layer over Endscript

**Done.** `src/BlackboxModManager.Core/Mods` holds the model. It reads text only, so every test runs on native Linux with no Wine and no game. Read [04-endscript-layer.md](04-endscript-layer.md) for the type list and the numbers.

Four facts carry forward.

1. **There are five `1 Lap` manifests, not four.** The brief and step 1 both said four. Both are corrected.
2. **A variant holds a list of option sets, not one.** A script can pause more than once, so the roadmap model needed a list.
3. **An unknown verb and an option block header parse to the same type.** Only the enclosing question separates them. `ScriptFlattener` does that and stops on a real unknown verb.
4. **An `if` command cannot resolve without loaded containers.** The flattener says so and never guesses. Step 8 owns the fix.

## Reference files

- [00-test-environment.md](00-test-environment.md) — the developer machine paths, plus the confirmed Binary install layout and the game install facts. Read it before step 1. It answers several questions that steps 2 and 3 raise.
- [98-known-upstream-defects.md](98-known-upstream-defects.md) — defects in the MIT libraries that we work around rather than fix.
- [99-api-notes.md](99-api-notes.md) — verified signatures and call order for Nikki and Endscript. Read this before you write library code. The project brief describes some of these APIs incorrectly.
