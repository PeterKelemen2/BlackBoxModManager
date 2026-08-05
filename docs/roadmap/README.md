# Roadmap

This directory holds the implementation plan. Work through the files in order. Each file states what to build, how to verify it, and what can go wrong.

Read `../../project_brief.md` first. The brief holds the format research and the design decisions. This roadmap holds the work.

## Sequence

| Step | File                                             | Gates                          |
| ---- | ------------------------------------------------ | ------------------------------ |
| 0    | Done. See "Completed" below.                     | —                              |
| —    | [00-test-environment.md](00-test-environment.md) | Reference. Read before step 1. |
| 1    | Done. See "Completed" below.                     | —                              |
| 2    | Done. See "Completed" below.                     | —                              |
| 3    | Done. See "Completed" below.                     | —                              |
| 4    | Done. See "Completed" below.                     | —                              |
| 5    | Done. See "Completed" below.                     | —                              |
| 6    | Done. See "Completed" below.                     | —                              |
| 7    | [07-game-profiles.md](07-game-profiles.md)       | Part done. Three games wait.   |
| 8    | Done. See "Completed" below.                     | —                              |
| 9    | Done. See "Completed" below.                     | Needs a real ASI mod sample.   |
| 10   | [10-texmod.md](10-texmod.md)                     | Nothing. Explicitly last.      |

Steps 1 to 3 prove that the foundation works. **All three pass.** Steps 4, 5, 6, 8, and 9 are done. **The success criterion of the project brief passes.**

**Step 7 is part done.** The plumbing carries any number of games, and the application manages three of the six targets. Underground 1, Carbon, and Undercover wait for a listing of a real install. Read the Results section of [07-game-profiles.md](07-game-profiles.md).

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
2. **`Save` rewrites every loaded container, not only the edited ones.** No command targets `GLOBALA.BUN`, and the file still changed. **Step 6 decided to accept it.** The verify treats a rewritten container as expected, and the revert restores the vanilla state whatever `Save` wrote.
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
2. **Hard links and copies work on both builds.** No privilege blocked a symbolic link either. **Step 5 can default to hard links.** Do not hardcode that. Call `LinkSupport.Probe` against the real target, because a hard link still fails across filesystems.

   **A symbolic link is not usable under Wine, and step 9 corrects this fact.** Wine writes a Windows symbolic link as a zero-byte file, and it appends a question mark to the Linux name. A Wine process that opens the Windows name reads the source, so the original probe passed it. `FileInfo.Length` still reports zero, and `FileHash.SameContent` compares the length first. Every deploy across two volumes therefore failed the verify with "the staging copy differs from the copy in the mod". The probe now compares the length as well as the content, so it rejects the method and the chain falls through to Copy.
3. **A Proton build ships no `winepath` program.** A wrapper that calls it falls through to system Wine and starts a wineserver of the wrong version. Convert paths with `wine winepath.exe -w`, using the same build.
4. **`IncludeNativeLibrariesForSelfExtract` does not put the DLL beside the executable.** It extracts to a temporary directory and the P/Invoke resolves from there. Test the resolution with `NativeLibrary.TryLoad`, never the file location.

### Step 4 — our layer over Endscript

**Done.** `src/BlackboxModManager.Core/Mods` holds the model. It reads text only, so every test runs on native Linux with no Wine and no game. Read [04-endscript-layer.md](04-endscript-layer.md) for the type list and the numbers.

Four facts carry forward.

1. **There are five `1 Lap` manifests, not four.** The brief and step 1 both said four. Both are corrected.
2. **A variant holds a list of option sets, not one.** A script can pause more than once, so the roadmap model needed a list.
3. **An unknown verb and an option block header parse to the same type.** Only the enclosing question separates them. `ScriptFlattener` does that and stops on a real unknown verb.
4. **An `if` command cannot resolve without loaded containers.** The flattener says so and never guesses. Step 8 owns the fix.

### Step 5 — the MVP shell

**Done.** `src/BlackboxModManager.App` holds the WPF window, and `src/BlackboxModManager.Core` grew the game, store, profile, deploy, and staging namespaces. The application detects the game, imports ASI and loose-file mods, orders them, deploys them, and reverts to vanilla. Read [05-mvp-shell.md](05-mvp-shell.md) for the type list, the workspace layout, and the findings.

Start the application with `tools/run-app.sh`. Run the platform self test with `BlackboxModManager.exe --selftest <directory>`.

Nine facts carry forward.

1. **Hard links work under Wine, and a two-move swap works.** The self test linked every file of the vanilla copy and of the staging copy, and it swapped the directories. A deploy of the 1.7 GB install costs almost no disk space and almost no time.
2. **A hard link shares its content with the live install.** The staging file, the vanilla file, and the live file are one file with three names. **Step 6 must call `StagingFiles.MakePrivate` for every file that its merged load names**, because `profile.Save()` writes containers in place.
3. **The workspace sits beside the game install on purpose.** A hard link cannot cross a volume, and a move across a volume copies every byte. `Settings.WorkRootOverride` moves it, and the deploy then reports the cost.
4. **A Binary mod stops a deploy with a message that names step 6.** The store classifies it and no engine in this build claims it. A silent skip would look like a deploy that worked.
5. **`GameCatalog` holds the games that a listing confirmed.** It held Underground 2 alone at the end of step 5. Step 7 added Most Wanted and ProStreet, and three targets still wait for a listing.
6. **The window asks every question in a dialog.** `Console.ReadLine` never returns on a Wine console, and a window application has no console. `IUserInteraction` holds the whole set.
7. **Never scroll a list from inside its own `CollectionChanged` handler, and always set `e.Handled`.** A synchronous `ScrollIntoView` there makes the item container generator run mid-notification, and WPF throws `An ItemsControl is inconsistent with its items source`. A handler that leaves `e.Handled` false then turns one exception into a storm of dialogs and a crash.
8. **A hardware popup paints black under Wine.** A ComboBox dropdown is a layered window that WPF composites itself, and the Direct3D path of Wine gives back no content for it. **The application sets `RenderMode.SoftwareOnly` under Wine.** Test the host with the `wine_get_version` export of `ntdll`. Wine reports `Microsoft Windows 10.0.19045`, so a version test finds nothing.

9. **A font family that Wine does not hold kills the process.** WPF reaches `Invariant.FailFast`, which no handler can catch. `FontFamily="Consolas"` on the log list ended the application on its first log line. **Name no font family in the XAML**, and let `tools/run-app.sh` link the fonts of the host into the prefix.

### Step 6 — Binary mod deployment

**Done.** Both example mods install together into one game directory, in one load, apply, and save pass. The game starts from that directory and both mods take effect. The revert restores the vanilla state exactly. Binary never ran. Read [06-binary-deployment.md](06-binary-deployment.md) for the run numbers and the findings.

Run it with `tools/run-deploy-test.sh`. Add `DEPLOY_NO_REVERT=1` to keep the result, then start `SPEED2.EXE` in the scratch copy.

**Start the game after every change to the container path.** No automated check can confirm that a race runs one lap or that the camera moved.

Six facts carry forward.

1. **The single pass works.** Two mods applied 1249 commands to one loaded profile before one `Save`. `GlobalB.lzc` grew from 5,145,778 to 8,263,472 bytes, which matches the step 1 measurement to the byte.
2. **Defect 6 was wrong and is corrected.** `AddNew` does check for a duplicate and throws. The real hazard is that `Contains` compares raw text, so two spellings of one container both pass and `Save` then writes one file twice.
3. **The library matches a container by the exact text of its name.** The merged union keeps the spelling of the manifest. Two mods that spell one container differently cannot share a load, and `MergedLaunch` says so.
4. **Pass the full path of the script to `EndScriptManager`.** The third argument becomes `Path.GetDirectoryName(launcher)`, and seventeen commands read a file relative to it. The step 1 harness passed a bare name.
5. **A link to a file that does not exist is normal.** A vanilla install holds one of the four links that every manifest names, and every loader in Nikki returns for a missing file.
6. **A combobox option is named by the quoted string in the script**, not by the file that the block appends. A stored answer holds that name.

### Step 7 — game profile support, part done

**Part done.** The application manages Underground 2, Most Wanted, and ProStreet. It detects each one, imports mods for it, deploys, and reverts. The window holds a game picker, and every mod in the store carries one game. Read [07-game-profiles.md](07-game-profiles.md) for the game table and the open work.

**Three targets have no descriptor.** This machine holds no Underground 1, no Carbon, and no Undercover install. Every value in a descriptor comes from a listing of a real install, so those three wait. `GameCatalog.Absent` names them.

Four facts carry forward.

1. **A game is supported when a descriptor exists.** Never read the membership of `GameINT` instead. `GameCatalog.All` answers "which games does this application manage". `GameCatalog.Absent` answers "which target is missing".
2. **The manifest decides the game of a Binary mod.** The window decides the game of a drop-in mod. An import that disagrees with the manifest follows the manifest and writes a note.
3. **A drop-in deploy is game-independent, and a container deploy is not proven so.** The link engine reads a descriptor and nothing else, and a test deploys and reverts on all three games. Only Underground 2 has a container proof, because we hold no Binary mod sample for another game.
4. **The `Links` boilerplate is confirmed for Underground 2 alone.** `ManifestLinkAudit` reports the differences, and a game with an empty expected set produces no report. **Silence there means "not checked" and never "clean".**

### Step 8 — command classification

**Done.** All 48 verbs of `eCommandType` carry a classification. Conflict detection covers every category that has a key. An unclassified verb warns and names the file and the line, and the deploy stops before it writes for a verb that this application refuses. Read [08-command-classification.md](08-command-classification.md) for the table, the conflict rules, and three corrected pitfalls.

The test count went from 203 to 241. `tools/run-deploy-test.sh` still passes, and the container bytes match step 6 exactly.

Five facts carry forward.

1. **`CommandCatalog` is the one place that names a verb.** It holds the category, the token numbers, and the support level of all 48. `EditKeyExtractor` reads the token numbers out of it and keeps no verb list. A test compares the catalog count against the enum, so a library update that adds a verb fails at once.
2. **The preflight reports and the gate blocks.** `ConflictPreflight` never stops a deploy. `CommandGate` runs inside `ContainerDeployEngine` before it loads anything, so a caller that skips the preflight cannot skip the rule. Keep both.
3. **`absolute` in a filesystem command means the game directory and not the root of the filesystem.** A well behaved script therefore stays inside staging on its own. Two forms escape. A `..` segment climbs out, and `Path.Combine` drops the anchor when the second path is rooted. `PathSandbox` tests for both, and it converts the separator first because the scripts write a backslash.
4. **We refuse `stop_errors` and `speedreflect`.** `stop_errors true` makes the library drop every later error of that script, which defeats our deploy rule. `speedreflect` copies a GPL-3.0 file that we do not ship. See defect 11.
5. **`unlock_memory` is disk modding after all.** It writes a short header over a memory file of the game directory. Staging covers it and the revert restores it, so it needs no special handling. The step 8 pitfall said the opposite and is corrected.

### Step 9 — ASI configuration and the loader

**Done.** The window shows the options of an ASI mod grouped by section, a question mark marker holds the comment of each key, and a deploy writes the answers into the staging copy. Two mods that both ship `dinput8.dll` produce one prompt, one deployed file, and a log line that names the winner and the losers. Read [09-asi-configuration.md](09-asi-configuration.md) for the format rules, the awkward cases, and the type list.

The test count went from 241 to 306. `tools/run-deploy-test.sh` still passes, and the container bytes match step 6 exactly.

**The reader handles two real mods, and the deploy path is tested against synthetic ones.** The Widescreen Fix and the Extra Options mod both parse with no warning, they round trip byte for byte, and each resolves its plugin. Every deploy test builds its mod by hand, because this repository ships no ASI mod. **No ASI mod has reached a running game yet.** Read the "What is open" section of the step file.

Nine facts carry forward.

1. **The raw line is the truth for an `.ini` file.** The reader keeps every line with its terminator and records the character span of the value. The writer replaces those characters and nothing else, so the comment, the alignment, and the blank lines survive. A user sees one difference per changed value. Never rebuild a line from the model.
2. **Three comment markers are real.** The Widescreen Fix writes `;` and Extra Options writes `//`. No plugin declares which one it reads, so the reader accepts `;`, `#`, and `//`, and it keeps the marker of each line. `IniLine.CommentMarker` is a string because `//` is two characters wide.
3. **A comment is not a schema.** The editor comes from the value alone. A comment such as `(1 = Cropped | 2 = Stretched)` reads like a list of choices, and a drop-down built from one would lock the user out of a legal value. Every row also carries a `Text` toggle, because `FPSLimit = -1` and `ImproveGamepadSupport = 0` both defeat the guess.
4. **The profile holds the differences and not the file.** An answer whose value matches the mod leaves the profile, so the deployed file matches the mod store again. `DeployPolicy.WritableExtensions` must keep `.ini`, or a write would reach the mod store through a hard link.
5. **An edited file cannot be verified against the mod store.** `DeployedFile.Edited` says that the deploy changed the content on purpose, and `StagingVerifier` then checks existence and a length above zero. Any future engine that rewrites a placed file must set that flag.
6. **Never pick an ASI loader automatically.** A proxy DLL forwards to the real system library, and a version that forwards wrongly breaks sound or input rather than a plugin. Version numbers on these files are often absent or wrong, so the dialog shows what the file holds, ranks nothing, and preselects nothing but the current answer. `LoaderPreflight` stops a deploy that has no answer, and the window asks.
7. **A symbolic link is not usable under Wine, and a probe that only reads the content misses that.** Wine writes one as a zero-byte file with a question mark on the Linux name. `FileHash.SameContent` compares the length first, so every ASI deploy across two volumes failed the verify. The probe now compares the length too, and the chain falls to Copy. Correction of step 3, fact 2.
8. **The window holds a `Folders` button.** It lists the game install, the workspace, the staging copy, the vanilla copy, the mod store, and the logs, with `Open` and `Copy path` on each row. A deploy that the verify stopped leaves its result in the staging copy, and that is the only place the failure is readable. `Copy path` always works, because a Wine prefix has no guaranteed file manager.
9. **Every error dialog holds a `Copy error` button.** `Views/MessageWindow.xaml` replaced the message box, because a message box gives the user no way to copy the text. The dispatcher handler uses the same window and falls back to a message box, because a render failure can break a new WPF window. Run `BlackboxModManager.exe --dialogtest` to open the dialog on the run platform.

## Reference files

- [00-test-environment.md](00-test-environment.md) — the developer machine paths, plus the confirmed Binary install layout and the game install facts. Read it before step 1. It answers several questions that steps 2 and 3 raise.
- [98-known-upstream-defects.md](98-known-upstream-defects.md) — defects in the MIT libraries that we work around rather than fix.
- [99-api-notes.md](99-api-notes.md) — verified signatures and call order for Nikki and Endscript. Read this before you write library code. The project brief describes some of these APIs incorrectly.
