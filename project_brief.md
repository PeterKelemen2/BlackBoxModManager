# Project Brief: Mod Manager for BlackBox-era Need for Speed titles

## Context

I want to build a mod manager for BlackBox-era Need for Speed games (Underground 2, Most Wanted, Carbon, ProStreet). Currently the modding scene for these games relies on three separate, unmanaged mod formats with no unified tooling:

1. **ASI scripts** — plugin DLLs loaded by an ASI loader. These are simple drop-in files.
2. **Texmod packages (.tpf)** — runtime texture injection via a hooking tool. Never touch disk, applied at launch time.
3. **"Binary" mods** — installed via a third-party tool called Binary (version 2.8.3), which edits game data files. This is the hardest category to manage and the primary architectural challenge of this project.

There is no Vortex/Mod Organizer 2-style manager for this scene. I want to build one.

## Platform decision

**This is a Windows-only application.** Linux users will run it inside the same Wine prefix as the game itself (same as they already run the game and Binary), so no cross-platform abstraction layer is needed — treat the target as native Windows APIs throughout, and validate behavior under Wine as a compatibility target rather than a separate platform.

## Binary mod format, as understood so far

A Binary mod folder can contain more than one `.end` file. One of them acts as a **manifest**, describing the mod as a whole, and points to another `.end` file that contains the actual edit script. Example manifest:

```
[VERSN1]

{
  "Usage": "User",
  "Game": "Underground2",
  "Directory": "",
  "Endscript": "MOD\URL.end",
  "Files": [
    "GLOBAL\GLOBALA.BUN",
    "GLOBAL\GLOBALB.LZC"
  ],
  "Links": [
    {
      "LoadType": "Attributes",
      "PathType": "Absolute",
      "File": "GLOBAL\attributes.bin"
    },
    {
      "LoadType": "FeAttrib",
      "PathType": "Absolute",
      "File": "GLOBAL\fe_attrib.bin"
    },
    {
      "LoadType": "Labels",
      "PathType": "Absolute",
      "File": "LANGUAGES\Labels_Global.bin"
    },
    {
      "LoadType": "Labels",
      "PathType": "Absolute",
      "File": "LANGUAGES\Labels.bin"
    }
  ]
}
```

Breaking this down:

- **Header** (`[VERSN1]`) followed by a **JSON body** — a versioned manifest format, presumably with other version tags possible (`VERSN2`, etc.) that may need to be handled separately if encountered.
- **`Game`** — which title the mod targets (`Underground2` here). Confirms mods are game-specific and the manifest is a reliable place to read that from, rather than guessing from folder structure or asking the user.
- **`Endscript`** — a relative path to the actual `.end` script containing the edit commands (of the `update_incareer ...` form seen previously). The manifest is a pointer/descriptor; the script referenced by `Endscript` is where the real work happens.
- **`Files`** — the list of game data files this mod touches. Includes `GLOBAL\GLOBALA.BUN` and `GLOBAL\GLOBALB.LZC`. A full file listing of a real vanilla install (`game_files.txt`) confirms these exist — `GLOBAL/GLOBALA.BUN`, `GLOBAL/GLOBALB.BUN`, and `GLOBAL/InGameCommon.lzc`/`GLOBAL/GlobalB.lzc` are all present, along with other `.BUN` files scattered across `NIS/`, `TRACKS/`, and `FRONTEND/`, and even a single `.viv` at `SDATA/sdat.viv`. So the earlier note about no `.viv`/`.bun` files being present in the install was simply incorrect — they're there, just not in the specific spot originally checked. No further reconciliation needed here.
- One more thing the file listing surfaces: `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit alongside the real files. `.bacc` looks like Binary's own backup-file convention — a copy of the original kept before editing a file in place, presumably so it can revert. This matters for our own design: our snapshot/backup step needs to either recognize and ignore Binary's `.bacc` files (they're not game content, they're Binary's own bookkeeping) or, alternatively, we could investigate whether Binary's `.bacc` files can be leveraged directly for restoring vanilla state instead of maintaining a fully separate backup mechanism of our own. Needs closer inspection of what's actually inside a `.bacc` file (verbatim original copy vs. some other format) before deciding.
- **`Links`** — a separate mechanism from `Files`/`Endscript`. Each entry has a `LoadType` (`Attributes`, `FeAttrib`, `Labels` seen so far — likely a fixed enum of known loader categories) and a `PathType` (`Absolute` seen so far — implies there may also be a `Relative` variant), pointing at loose `.bin` files (`attributes.bin`, `fe_attrib.bin`, `Labels_Global.bin`, `Labels.bin`). These look like they register additional loose data files with the game's own loader by category, separate from whatever the `Endscript` does to the `Files` list.

This is useful: it means Binary mods already ship with a machine-readable manifest describing what they touch and how, rather than needing to be inferred purely by diffing. The mod manager should **read and rely on this manifest directly** rather than inventing a parallel manifest schema from scratch — the existing `Files`/`Endscript`/`Links` structure is likely enough to build dependency tracking, conflict detection, and deployment logic on top of, once the JSON schema and `LoadType`/`PathType` enums are more fully catalogued from a wider sample of real mods.

There is also an `example_mods` folder alongside this plan document, containing two real, complete Binary mods. These should be inspected directly (manifest, `.end` script, and any other files each mod folder contains) as primary source material for the schema/vocabulary survey below, rather than working only from the single manifest snippet and single script line seen so far.

## Tech stack (decided)

- **UI**: WPF, MVVM pattern. Use CommunityToolkit.Mvvm for low-ceremony MVVM (not full Prism unless a need emerges).
- **Win32 interop**: Direct P/Invoke — `CreateHardLinkW`, `CreateSymbolicLinkW`, registry access via `Microsoft.Win32.Registry` for game install path discovery.
- **Installer automation**: FlaUI, using the **win32 (message-based) backend**, not UIA — Wine's UI Automation surface is much less complete than its legacy Win32 messaging support, so win32 backend is the safer bet for driving the Binary installer under Wine, for whatever mod behavior can't be handled by reading manifests and scripts directly.
- **Hashing**: `System.IO.Hashing` (XxHash) for fast internal diffing, or `Blake3.NET` if community-standard checksums matter. Avoid relying on file size + mtime for identity checks — mtimes get reset on extraction and aren't reliable; hash actual content.
- **Archive handling (mod packages, i.e. the zips/rars mods are distributed in)**: `System.IO.Compression` for zip, `SharpCompress` for rar/7z.
- **Manifest parsing**: `System.Text.Json` can parse the `.end` manifest's JSON body directly once the `[VERSN1]` header line is stripped/handled separately.
- **Game container format parsing**: format(s) of `.LZC` and `.BUN` still need to be understood at the byte level for anything not expressible via `.end` script commands (e.g. actual asset replacement). Don't assume prior BlackBox VIV/BUN tooling applies without verifying against real files from this game's install.
- **`.end` script handling**: needs its own parser/interpreter for the command-line-style script referenced by `Endscript` (line-oriented commands like `update_incareer <file> <path...> <value>`).
- **Packaging**: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` — self-contained specifically so Linux users don't need to `winetricks dotnet` into their existing game prefix.

## Core architecture

### Mod type taxonomy — separate deployment backends behind a common interface

Don't unify these into one code path. Build a `ModPackage` abstraction with the following implementations:

| Type                           | Behavior                                                                                                            | Deployment strategy                                                                                                                             |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| ASI/DLL plugins                | Drop-in files                                                                                                       | Direct link (hardlink→symlink→copy fallback), no capture step                                                                                   |
| Loose files                    | Drop-in override                                                                                                    | Direct link, no capture step                                                                                                                    |
| Texmod (.tpf)                  | Runtime hook, never touches disk                                                                                    | No file management — just launcher wrapper tracking which package to inject                                                                     |
| Binary mod — manifest-driven   | Root `.end` manifest (`VERSN1` + JSON) pointing to an `Endscript`, declaring target `Files` and loader `Links`      | Parse the manifest and script directly; apply via our own interpreter or track declared file/link targets for conflict detection and deployment |
| Binary mod — asset replacement | Wholesale replacement of textures/models/other binary assets inside a container, not expressible as `.end` commands | Needs a capture pipeline (below) — likely can't be read purely from the manifest                                                                |

The split between the last two rows may narrow once more real mods have been inspected — it's possible asset replacement is also represented somewhere in the manifest/script combination (e.g. via a command that points at a loose replacement file) rather than needing a separate binary diff path. Don't assume either way until a wider sample of manifests and scripts has been surveyed.

### Fallback capture pipeline (for whatever isn't expressible via manifest + script)

For any mod behavior that can't be captured by reading the manifest and script alone:

1. **Vanilla baseline**: On first setup, snapshot the pointed-at game install — content hash (not size+date) for every file, plus full byte copies of the identified container format(s) since raw bytes are needed later, not just hashes.
2. **Staging install**: A working copy of vanilla the manager resets before each mod capture. Use hardlinks or block-cloning (ReFS) where possible to avoid duplicating gigabytes per reset; fall back to full copy otherwise.
3. **Capture a mod**: Reset staging to vanilla → run the Binary installer (via FlaUI automation) against staging → diff staging vs. vanilla by content hash → the delta (added/modified/removed files) becomes the mod's payload, stored in the manager's own mod library. The real game folder is never touched during this process.
4. **Deploy**: Compute a merged file tree from all enabled mods (in load order) layered over vanilla, then link that tree into the real game folder (hardlink→symlink→copy fallback). Keep vanilla file backups so disabling/uninstalling a mod is a clean revert.

### Conflict resolution

- **For manifest/script-driven mods**: cross-reference each mod's `Files` list and each script's command target paths (file + hierarchical key path) to detect direct collisions between enabled mods before deployment. Also cross-reference `Links` entries by `LoadType` + `File`, since two mods registering conflicting loose files under the same `LoadType` is its own kind of conflict distinct from a `Files`/script collision.
- **For container-level asset edits** (once the `.LZC`/`.BUN` byte format is understood): if two mods modify the same container file outside of what the manifest/script declares, naive whole-file "last mod wins" can silently destroy one mod's changes. Unpack both vanilla and modded versions of any changed container during capture, diff at the internal asset/entry level rather than whole-file level, and composite entries from all enabled mods into a merged container at deploy time (later load-order mod wins per-entry conflict). For any container structure not yet understood, fall back to whole-file overwrite with a conflict warning in the UI — acceptable for v1.

## Known risks / open questions to research first

1. **Catalogue the manifest schema more fully**, using both `example_mods` and any further mods provided. Determine the full set of `LoadType` values, whether `PathType` has values beyond `Absolute` (e.g. `Relative`), whether `VERSN1` is the only header version in circulation, and what `Usage` (`"User"` seen here) and `Directory` (empty here) are for.
2. **Understand the `.bacc` backup-file convention.** `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` were found sitting next to their originals in the vanilla install listing, suggesting Binary keeps its own pre-edit backups. Inspect a `.bacc` file directly (ideally from `example_mods` if either mod's installation produced one, or by installing a mod to a scratch copy of the game) to confirm whether it's a verbatim original copy, and decide whether our own capture pipeline can lean on this mechanism or needs to stay fully independent of it.
3. **Survey the `.end` script command vocabulary** referenced via `Endscript`, using the scripts inside `example_mods` as the primary source, plus any further scripts provided. Catalogue distinct command names (the `update_` prefix suggests a family — likely also `add_`/`remove_` or similar), and determine whether all Binary mod behavior can be expressed this way or whether some mods still require Binary's compiled installer for things not expressible in script form.
4. **Identify the `.LZC`/`.BUN`/`.viv` byte-level container format(s)** for whatever isn't handled by manifest/script parsing alone — needed for the asset-replacement deployment path and for container-level conflict merging. Note the vanilla install's file listing shows at least one `.viv` file (`SDATA/sdat.viv`) alongside `.BUN` and `.lzc`/`.LZC` files, so more than one container format may be in play across the install, not just one.
5. **Does Binary 2.8.3 have any silent/CLI install flags**, for whatever residual mod behavior can't be handled by directly parsing manifests and scripts?
6. **Does FlaUI (win32 backend) reliably drive the Binary 2.8.3 installer under Wine**, for whatever residual GUI-dependent cases remain? Test under both vanilla Wine and Proton if targeting Steam installs.
7. **Symlink permissions under Wine**: Windows normally requires `SeCreateSymbolicLinkPrivilege` (admin or Developer Mode) for symlink creation; Wine's ntdll enforcement of this varies by build. Confirm hardlink and symlink behavior on the actual Wine/Proton builds being targeted.

## Suggested roadmap

1. **Research spike** — manifest schema survey, `.end` script command vocabulary survey, reconciling the `.BUN` finding, container format identification for whatever remains, Binary silent-install check, FlaUI-under-Wine viability for residual GUI-dependent cases.
2. **MVP** — ASI/DLL + loose-file mods only. Build MVVM scaffolding, profiles, load order UI, and the link-deploy engine (hardlink→symlink→copy fallback). Smoke-test under Wine immediately, not at the end.
3. **Texmod integration** — package tracking + launcher wrapper.
4. **Manifest + `.end` script parsing** — read `Files`/`Endscript`/`Links` from manifests, parse and apply script-driven edits, build conflict detection on declared file targets, script command target paths, and `Links` entries.
5. **Fallback capture pipeline** — for whatever mod behavior isn't expressible via manifest/script (staging install + diff, container-format-dependent).
6. **Container-level asset merge** — entry-level diffing/compositing for `.LZC`/`.BUN` (or whatever the confirmed format turns out to be), once understood.
7. **Game profile support** — path/registry/executable differences across Underground 2, Most Wanted, Carbon, and ProStreet.

## What I want from you right now

Given the manifest format and container format are still being pieced together, please start with the research/discovery work rather than writing application code:

1. Start by inspecting the two real mods in `example_mods` directly — read each mod's manifest `.end` file and its referenced `Endscript` file in full, and note anything else present in the mod folders that hasn't come up yet.
2. Help catalogue the manifest JSON schema more fully from what's in `example_mods` plus anything else I provide — build up the known set of `LoadType`/`PathType`/`Usage` values and flag anything that doesn't fit the pattern seen so far.
3. Help build out a working understanding of the `.end` script command vocabulary from the scripts in `example_mods`, starting from the one known example (`update_incareer GLOBAL\GLOBALB.LZC GCareers Main GCareerRaces S5_URL_5 Stages STAGE1 NumberOfLaps 1`).
4. Help investigate the `.bacc` backup-file convention noted above, if either example mod's install process produces one, or if I provide a `.bacc` file separately.
5. Once we have a clearer picture, propose a `ModPackage` interface / class hierarchy design (C#) covering the deployment backends above, and a manifest-reading layer that maps the existing `.end` manifest JSON schema onto our internal model — but treat this as provisional until the format research lands.

Let's proceed research-first, starting with `example_mods`. I'll provide additional real manifests, `.end` scripts, and any other game files as needed.
