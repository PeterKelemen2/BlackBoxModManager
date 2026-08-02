# Roadmap

This directory holds the implementation plan. Work through the files in order. Each file states what to build, how to verify it, and what can go wrong.

Read `../../project_brief.md` first. The brief holds the format research and the design decisions. This roadmap holds the work.

## Sequence

| Step | File                                                         | Gates                                  |
| ---- | ------------------------------------------------------------ | -------------------------------------- |
| 0    | Done. See "Completed" below.                                 | —                                      |
| —    | [00-test-environment.md](00-test-environment.md)             | Reference. Read before step 1.         |
| 1    | [01-console-harness.md](01-console-harness.md)               | Everything. Do this first.             |
| 2    | [02-binary-install.md](02-binary-install.md)                 | Any container work on a clean machine. |
| 3    | [03-wine-verification.md](03-wine-verification.md)           | The Linux target.                      |
| 4    | [04-endscript-layer.md](04-endscript-layer.md)               | All Binary mod features.               |
| 5    | [05-mvp-shell.md](05-mvp-shell.md)                           | The UI.                                |
| 6    | [06-binary-deployment.md](06-binary-deployment.md)           | The success criterion.                 |
| 7    | [07-game-profiles.md](07-game-profiles.md)                   | Games other than Underground 2.        |
| 8    | [08-command-classification.md](08-command-classification.md) | Mods outside the two examples.         |
| 9    | [09-texmod.md](09-texmod.md)                                 | Nothing. Explicitly last.              |

Steps 1 to 3 prove that the foundation works. Do not start step 4 until all three pass. Steps 2 and 3 can run in parallel with each other. Both need step 1 to finish first.

## Completed

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

## Reference files

- [00-test-environment.md](00-test-environment.md) — the developer machine paths, plus the confirmed Binary install layout and the game install facts. Read it before step 1. It answers several questions that steps 2 and 3 raise.
- [98-known-upstream-defects.md](98-known-upstream-defects.md) — defects in the MIT libraries that we work around rather than fix.
- [99-api-notes.md](99-api-notes.md) — verified signatures and call order for Nikki and Endscript. Read this before you write library code. The project brief describes some of these APIs incorrectly.
