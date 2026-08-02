# Project Brief: Mod Manager for BlackBox-era Need for Speed titles

## Context

I want to build a mod manager for BlackBox-era Need for Speed games (Underground 2, Most Wanted, Carbon, ProStreet). Currently the modding scene for these games relies on three separate, unmanaged mod formats with no unified tooling:

1. **ASI scripts** — plugin DLLs loaded by an ASI loader. These are simple drop-in files.
2. **"Binary" mods** — installed via a third-party tool called Binary (version 2.8.3), which edits game data files. This is the hardest category to manage and the primary architectural challenge of this project.
3. **Texmod packages (.tpf)** — runtime texture injection via a hooking tool. Never touch disk, applied at launch time. **Not in scope for the initial implementation** — deliberately deferred to a later phase. It's recorded here so the architecture leaves room for it, but nothing in the MVP or the Binary work should block on it, and no `.tpf` code needs to be written until the rest is working.

There is no Vortex/Mod Organizer 2-style manager for this scene. I want to build one.

## Platform decision

**This is a Windows-only application.** Linux users will run it inside the same Wine prefix as the game itself (same as they already run the game and Binary), so no cross-platform abstraction layer is needed — treat the target as native Windows APIs throughout, and validate behavior under Wine as a compatibility target rather than a separate platform.

## Binary mod format (confirmed against `example_mods`)

Both example mods have been read in full. The format is **two distinct file types that share the `.end` extension**, distinguished by their header line:

| Header     | File type    | Body                                                                  |
| ---------- | ------------ | --------------------------------------------------------------------- |
| `[VERSN1]` | **Manifest** | A JSON object (`Usage`/`Game`/`Directory`/`Endscript`/`Files`/`Links`) |
| `[VERSN2]` | **Script**   | Line-oriented commands (`update_*`, `combobox`, `append`, `end`)       |

So `VERSN1` and `VERSN2` are **not two versions of the same schema** — the version tag selects the parser. A reader must dispatch on the header, and must not assume `.end` extension implies manifest. Any other `VERSNn` value encountered should be a hard parse error naming the file, not a silent fallback.

A mod folder contains one or more `VERSN1` manifests at its root, each pointing via `Endscript` at a `VERSN2` script (typically in a subfolder). Example manifest:

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

- **Header** (`[VERSN1]`) followed by a **JSON body**. Note the JSON is **not valid JSON**: path values contain raw unescaped backslashes (`"MOD\URL.end"`, `"GLOBAL\GLOBALA.BUN"`). `System.Text.Json` will reject `\U`, `\G`, `\a` etc. as invalid escape sequences. The reader must pre-process the body — escape lone backslashes to `\\` before deserializing — or use a tolerant reader. This is a concrete, must-handle detail, not a theoretical concern.
- **`Game`** — which title the mod targets (`Underground2` here). Confirms mods are game-specific and the manifest is a reliable place to read that from, rather than guessing from folder structure or asking the user. Treat as a closed enum mapped to our game profiles; unknown value → mod flagged unsupported rather than installed hopefully.
- **`Usage`** — `"User"` in all four manifests inspected. Enum is `Invalid`/`User`/`Modder`. Distributed mods are always `User`; `Modder` is the automation-oriented mode that requires `Directory` to be filled in.
- **`Directory`** — empty string in all four manifests inspected. **Confirmed from upstream source: this is the game install directory.** It is empty in distributed `User`-mode mods because Binary prompts the user to browse for it; in `Modder` mode it must be present and must exist on disk. `Files` paths resolve against it, as do `Links` entries with `PathType: Absolute`. This is the field *we* populate — pointing it at our staging copy is how we keep the live install untouched.
- **`Endscript`** — a Windows-style relative path (backslash-separated) to the `VERSN2` script, resolved relative to the manifest's own folder. Must be normalized to the host separator before use.
- **`Files`** — the list of game data files this mod touches. **Confirmed to be a superset of what the script actually edits**: all four `1 Lap *` manifests and the camera mod's `Install.end` declare `GLOBAL\GLOBALA.BUN` and/or `GLOBAL\GLOBALB.LZC`, but every single `update_*` command across all 1484 script lines targets only `GLOBAL\GLOBALB.LZC`. So `Files` is best read as "the set of containers Binary must open/load together to resolve this script", not "the set of files that get modified". Consequence for us: **conflict detection must key off the script's actual command targets, not `Files`** — using `Files` alone would report a false conflict between any two mods that both merely load `GLOBALA.BUN`. `Files` is still worth capturing, as the set that needs backing up. Includes `GLOBAL\GLOBALA.BUN` and `GLOBAL\GLOBALB.LZC`. A full file listing of a real vanilla install (`game_files.txt`) confirms these exist — `GLOBAL/GLOBALA.BUN`, `GLOBAL/GLOBALB.BUN`, and `GLOBAL/InGameCommon.lzc`/`GLOBAL/GlobalB.lzc` are all present, along with other `.BUN` files scattered across `NIS/`, `TRACKS/`, and `FRONTEND/`, and even a single `.viv` at `SDATA/sdat.viv`. So the earlier note about no `.viv`/`.bun` files being present in the install was simply incorrect — they're there, just not in the specific spot originally checked. No further reconciliation needed here.
- One more thing the file listing surfaces: `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit alongside the real files. `.bacc` looks like Binary's own backup-file convention — a copy of the original kept before editing a file in place, presumably so it can revert. This matters for our own design: our snapshot/backup step needs to either recognize and ignore Binary's `.bacc` files (they're not game content, they're Binary's own bookkeeping) or, alternatively, we could investigate whether Binary's `.bacc` files can be leveraged directly for restoring vanilla state instead of maintaining a fully separate backup mechanism of our own. Needs closer inspection of what's actually inside a `.bacc` file (verbatim original copy vs. some other format) before deciding.
- **`Links`** — a separate mechanism from `Files`/`Endscript`. Each entry has a `LoadType` (`Attributes`, `FeAttrib`, `Labels` seen so far — likely a fixed enum of known loader categories) and a `PathType` (`Absolute` seen so far — implies there may also be a `Relative` variant), pointing at loose `.bin` files (`attributes.bin`, `fe_attrib.bin`, `Labels_Global.bin`, `Labels.bin`). These look like they register additional loose data files with the game's own loader by category, separate from whatever the `Endscript` does to the `Files` list.

`Links` is **identical across all four manifests inspected** (same four entries, same order: `Attributes`/`GLOBAL\attributes.bin`, `FeAttrib`/`GLOBAL\fe_attrib.bin`, `Labels`/`LANGUAGES\Labels_Global.bin`, `Labels`/`LANGUAGES\Labels.bin`), despite the two mods being unrelated and by different authors. That strongly suggests `Links` is **boilerplate emitted by Binary's own mod-authoring/export mode** describing the standard loose-file set to register for Underground 2, not a per-mod declaration. Design consequence: **do not build conflict detection on `Links`** as originally planned — it would flag every pair of U2 mods as conflicting. Parse and store it, compare it against the expected per-game boilerplate, and only surface it as interesting if a mod deviates from that boilerplate.

This is still useful: Binary mods ship with a machine-readable manifest, so nothing needs to be inferred by diffing. The mod manager should **read and rely on this manifest directly** rather than inventing a parallel schema.

### `VERSN2` script grammar (confirmed)

The script is line-oriented; blank lines are ignored. Tokens are whitespace-separated **except** that double-quoted strings are single tokens and may contain spaces — a naive `Split(' ')` is wrong and will break `combobox`. Write a quote-aware tokenizer.

Commands observed across 1484 script lines in `example_mods`:

**1. Edit commands — `update_collection` and `update_incareer`**

```
update_collection GLOBAL\GLOBALB.LZC CarTypeInfos PEUGOT PlayerCamera PLAYER_CAMERA_FAR CameraAngle 0
update_incareer   GLOBAL\GLOBALB.LZC GCareers Main GCareerRaces S5_URL_5 Stages STAGE1 NumberOfLaps 1
```

Counts: `update_collection` ×1194 (camera mod), `update_incareer` ×290 (1 Lap mod). Every occurrence of `update_collection` has exactly 7 arguments and every `update_incareer` exactly 9 — but **do not hardcode those arities.** Both fit one general shape:

```
<verb> <targetFile> <keyPath...> <value>
```

where `targetFile` is the first argument, `value` is the last token, and everything between is a variable-length hierarchical key path. Parse to that shape. The `update_` prefix implies sibling verbs (`add_`/`remove_`/etc.) exist in the wild; an unrecognized verb must fail loudly with file and line number rather than being skipped, since silently ignoring an edit produces a subtly wrong install.

`value` is untyped in the script — observed values include integers (`0`, `1`), and floats both positive and negative with full round-trip precision (`-0.19500002`, `2.746582`, `1.016`). **Parse floats with `InvariantCulture` and preserve the original literal text verbatim** for re-emission; reformatting `-0.19500002` through a default `ToString()` would corrupt the value. Type resolution belongs to the container layer, not the script parser — carry values as strings plus a parsed hint.

**2. `combobox` — user-selectable install options.** See the dedicated section below.

**3. `append "<relative path>"`** — splices in another `.end` script, resolved relative to the *containing script's* folder. The appended files carry their own `[VERSN2]` header line, so the interpreter must tolerate and skip a header in appended content. Implement recursively with a visited-set for cycle detection and a depth cap; nesting deeper than one level hasn't been observed but isn't excluded.

**4. `end`** — terminates the script. Present as the final line of the camera mod's `script.end`; absent from the pure-edit scripts (`MOD/*.end`, `Main/[0]_*.end`, `Main/[1]_*.end`), which simply run to EOF. So treat `end` as optional-but-honored: stop interpreting at `end` or EOF, whichever comes first.

**Not observed at all:** any command touching a `.BUN`, any asset/texture/file-replacement command, any conditional or variable. Everything in both example mods is a scalar field write into `GLOBALB.LZC`. That's a narrow but very solid base to build the first working version on.

## User-selectable install options — two distinct mechanisms

Both example mods offer the user a choice at install time, and they do it in **two completely different ways**. Both must be supported, and they are supported by *different* parts of the codebase.

### Mechanism A — `combobox` inside the script (the camera mod)

`Main/script.end` is only nine lines, and is entirely a menu:

```
[VERSN2]
combobox "Install Camera Mod [NFSMW TO U2]" "Restore original camera settings"  "Choose option you needeed"

"Install Camera Mod [NFSMW TO U2]"
append "[1]_Camera_MOD_NFSMW_TO_U2.end"

"Restore original camera settings"
append "[0]_Restore_Camera_Settings.end"

end
```

The two bulky `Main/[0]_*.end` / `Main/[1]_*.end` files (744 and 450 lines) are the option bodies; neither is referenced by any manifest directly, only via `append` from within the selected branch.

Inferred grammar:

- `combobox <string>...` — a list of quoted strings. The **last** one is the prompt/caption shown to the user (`"Choose option you needeed"`); the preceding ones are the selectable option labels.
- ~~A block-header cross-check heuristic was proposed here.~~ **Unnecessary — confirmed against `ComboboxCommand.Prepare` in the upstream source:** options are `splits[1 .. ^2]` and the description is `splits[^1]`, with a minimum of 4 tokens. The simple "last quoted string is the caption" rule is exactly right.
- A **block** runs from its header line until the next block header, the `end` command, or EOF. Block bodies contain ordinary commands — here just a single `append` each, but the interpreter should allow any command sequence, not special-case `append`.
- Note the double space between the second option and the caption in the real file — whitespace between tokens is not significant, another reason the tokenizer must be quote-driven rather than position-driven.

**How we handle it: resolve the choice ourselves, in our own UI. Never let Binary ask.** The option labels and caption are plain text we can read out of the script at import time, so:

1. On mod import, parse the `Endscript`. If it contains a `combobox`, extract `(caption, [labels])` and record them on the mod as a declared **option set**.
2. Present that as a native WPF control in our own install/configure flow (or a per-mod settings pane), so the choice is visible, re-editable later, and persisted in the profile — a strict improvement over Binary's one-shot modal.
3. Persist the chosen label with the mod's install state. Changing the selection later is just a re-deploy with a different branch resolved; that's exactly what "Restore original camera settings" is for, and it means our UI gets a real toggle where Binary only had a re-run.
4. At deploy time, the interpreter takes the resolved selection as an input and walks only the selected block, flattening its `append`s into a single linear command list. **The output of this stage is a flat, fully-resolved list of `<verb> <file> <keyPath...> <value>` edits with no `combobox`, no `append`, and no remaining user questions.** Everything downstream — conflict detection, deployment, the apply engine — sees only that flat list and never needs to know options existed.
5. Options must be resolvable non-interactively too (headless re-deploy, profile switching): if no selection is stored, default to the **first** option label and log the assumption, rather than blocking on a prompt.

### Mechanism B — multiple sibling manifests (the 1 Lap mod)

The 1 Lap mod has **no `combobox`**. Instead it ships five `VERSN1` manifests side by side at its root, each pointing at a different `Endscript`:

| Manifest                  | `Endscript`      | Script lines |
| ------------------------- | ---------------- | ------------ |
| `1 Lap ALL Races.end`     | `MOD\ALL.end`     | 147          |
| `1 Lap URL Races.end`     | `MOD\URL.end`     | 53           |
| `1 Lap CIRCUIT Races.end` | `MOD\CIRCUIT.end` | 51           |
| `1 Lap STREET Races.end`  | `MOD\STREET.end`  | 37           |
| `1 Lap SUV Races.end`     | `MOD\SUV.end`     | 12           |

Apart from `Endscript`, the five manifests are byte-identical. The readme tells the user to open Binary and pick one of the five files — and explicitly says *"You can use 1 or multiple variants of this mod"*, so these are **not mutually exclusive**, unlike a `combobox`. (`ALL` is the union of the other four, so selecting `ALL` alongside another variant is a redundant-but-harmless overlap — the same field written to the same value twice. Our conflict detector should recognize same-value collisions as benign and not nag.)

**How we handle it:** a mod folder maps to **one `ModPackage` with N discoverable variants**, not N unrelated mods. Discovery = scan the mod root for `VERSN1` files. Present them as a multi-select (checkbox) list, in contrast to the `combobox` single-select. So the internal model needs both:

```
ModPackage
  ├── Variants     : IReadOnlyList<BinaryVariant>   // from sibling VERSN1 manifests — multi-select
  └── each Variant ├── Manifest (VERSN1)
                   └── OptionSet? (from combobox)   // single-select, may be null
```

Selection state for both lives in the profile, so a profile fully determines the resolved edit list with no prompting. A mod can in principle exercise both mechanisms at once (several manifests, each whose script has a `combobox`) — the model above handles that; don't collapse the two concepts into one list.

## Upstream code: Binary is open source, and its libraries are MIT

**This section supersedes most of the reverse-engineering plan below.** Binary's source is public at `github.com/SpeedReflect/Binary`, and it is a thin WinForms shell over two libraries that do all the real work. Critically, **the libraries and the application have different licenses**:

| Repo                        | License        | Size  | What it is                                                                    |
| --------------------------- | -------------- | ----- | ----------------------------------------------------------------------------- |
| `SpeedReflect/Nikki`        | **MIT**        | ~7 MB | Reads/writes the actual game containers (`.BIN`, `.BUN`, `.LZC`). The format. |
| `SpeedReflect/Endscript`    | **MIT**        | 225 KB | `.end` manifest + script parsing, command model, and execution against Nikki |
| `MaxHwoy/CoreExtensions`    | **MIT**        | —     | Utility library; Nikki's only dependency                                       |
| `SpeedReflect/Binary`       | **GPL-3.0** ⚠️ | 1.5 MB | WinForms GUI + a CLI entry point. The application shell only.                |
| `MaxHwoy/ILWrapper`         | MIT            | —     | DevIL image wrapper — used only by Binary's GUI, **not** needed by us         |
| `SpeedReflect/SpeedReflect` | MIT            | —     | C++ memory-patching extension for the game; unrelated to modding on disk      |

All are by a single author (MaxHwoy, `max.hwoy@gmail.com`), last touched Nov 2021, no other contributors — clean provenance, one person to contact if needed. None are published on NuGet, so consume them as git submodules or a vendored fork.

### The licensing answer

- **Nikki, Endscript, CoreExtensions: yes, use directly.** MIT (`Copyright (c) 2020 MaxHwoy`) — permissive, no copyleft, usable in a closed or differently-licensed project. The only obligation is retaining the copyright notice and license text; ship a `THIRD-PARTY-NOTICES` file listing all three. Forking is fine and advisable (pins the version, lets us retarget the framework), but forking is not required for use.
- **Binary itself: GPL-3.0 — do not copy code from it.** This is the one real constraint. Linking GPL-3.0 code into our application would require releasing our whole application under GPL-3.0. Since the libraries are MIT and contain everything we need, **there is no reason to touch Binary's source at all** — treat that repo as read-only documentation. Two specific traps:
  - Don't lift helpers out of `Binary/` even though they're tempting (e.g. `Editor.FixLaunchDirectory`, which the CLI path calls). Reimplement from behavior, not by copy-paste.
  - Don't add `Binary.csproj` as a project reference "just to reuse its CLI". *Invoking* `Binary.exe` as a separate process is fine — GPL covers distribution and linking, not calling an unmodified program the user already installed — but that's a fallback we probably won't need.
- **Decide our own license explicitly** and record it in the repo before publishing. MIT keeps us aligned with the libraries we depend on.
- **One genuine open item:** `Nikki/Nikki/LZCompressLib.dll` is a **checked-in closed-source native x64 PE DLL** (116 KB, build path `C:\Users\Max\source\repos\LZCompressLib`) with no public source repo and no separate license file. Nikki P/Invokes its `BlockCompress`/`BlockDecompress` entry points from `Nikki/Utils/Interop.cs` — i.e. **it is required for container compression**, not optional. It sits inside an MIT-licensed repo by the same author, so it is most likely covered by that MIT grant, but strictly the blob carries no license statement of its own. Low risk, worth one email to MaxHwoy to confirm, and worth noting before we redistribute it.

### What this eliminates

Everything below that was framed as reverse-engineering is now a code-reading exercise instead:

- **`.LZC`/`.BUN` container format** — no longer needs reverse-engineering. Nikki implements it, including per-game support for **Underground1, Underground2, MostWanted, Carbon, Prostreet, and Undercover** (a superset of our four targets), each with its own `Support.<Game>/` tree of `Attributes`, `Class`, `Parts`, and `Framework` types.
- **`.end` parsing** — no longer needs writing from scratch. `Endscript` has `EndScriptParser`, `EndScriptManager`, a `BaseCommand` model, and `Launch` (the exact `VERSN1` DTO, including a `Serialize` that reproduces the backslash-unescaped JSON dialect via `settings.Replace(@"\\", @"\")` — confirming that quirk was real and giving us the writer for free).
- **Guessing at enums and vocabulary** — all now readable as source. See the corrections below.

## Corrections and confirmations from the upstream source

Reading `Endscript` and `Nikki` confirms most of the `example_mods` survey and corrects several details. Where this section disagrees with anything above, **this section wins** — it's from the implementation, not inference.

- **Full command vocabulary is 48 entries**, not 5. `Endscript/Enums/eCommandType.cs`:
  `invalid`, `empty`, `game`, `version`, `append`, `update_collection`, `update_string`, `update_texture`, `update_incareer`, `add_collection`, `add_string`, `add_texture`, `add_incareer`, `remove_collection`, `remove_string`, `remove_texture`, `remove_incareer`, `copy_collection`, `copy_texture`, `copy_incareer`, `replace_texture`, `bind_textures`, `add_or_update_string`, `add_or_replace_texture`, `static`, `import`, `import_all`, `new`, `delete`, `watermark`, `create_file`, `create_folder`, `erase_file`, `erase_folder`, `move_file`, `generate`, `directory`, `filecount`, `capacity`, `checkbox`, `combobox`, `if`, `stop_errors`, `unlock_memory`, `speedreflect`, `unpack_stream`, `pack_stream`, `end`.
  The predicted `add_`/`remove_` families exist. Note also **`if`** (conditionals — scripts are not purely declarative), **texture commands** (so asset replacement *is* expressible in `.end` after all — the speculative "asset replacement" taxonomy row can likely be dropped), **file/folder manipulation** commands, and **`checkbox`** — a second interactive command alongside `combobox`.
- **`checkbox` is a second option mechanism** we hadn't seen: a yes/no toggle (Binary's CLI prompts `Select one [yes, no]`, mapping to `Choice` 1/0). Our option UI must handle three shapes, not two: sibling-manifest variants (multi-select), `combobox` (single-select from N), and `checkbox` (boolean).
- **`combobox` grammar confirmed exactly as inferred.** `ComboboxCommand.Prepare` requires ≥4 tokens, takes `splits[1 .. ^2]` as options and `splits[^1]` as the description. So the trailing-token-is-caption rule is correct and the block-header cross-check heuristic proposed above is unnecessary — drop it.
- **The tokenizer is `SmartSplitString` in `CoreExtensions/Text/RegX.cs`** — a quote-toggling scanner that splits on `' '` only. Note it does **not** treat tabs as separators, and it emits quoted segments as tokens without the quotes. Matching this behavior exactly matters if we write our own; using `CoreExtensions` avoids the question.
- **`ePathType.Absolute` does not mean "absolute path".** From `Launch.LoadLinks()`: `Relative` resolves against the *manifest's* folder, `Absolute` resolves against the *game install* directory. Both are relative paths; `Absolute` means "rooted at the game dir". Getting this backwards would break every `Links` entry, since all observed entries are `Absolute`.
- **`Directory` is the game install directory** — the field that's empty in all our example mods because `User` mode asks the user to browse for it. In `Modder` mode it must be filled in and must exist. So the earlier "fail loudly if non-empty" note is wrong: **non-empty is the normal case for the automation path, and it's the field we populate.**
- **`eUsage`** = `Invalid`/`User`/`Modder`. **`eLoaderType`** = `Invalid`/`BinKeys`/`VltKeys`/`Attributes`/`FeAttrib`/`Labels` — so two loader types beyond the three we'd seen. **`GameINT`** = `None`/`Carbon`/`MostWanted`/`Underground2`/`Underground1`/`Prostreet`/`Undercover`.
- **`Files` semantics confirmed**: `Launch.CheckFiles()` only verifies each entry exists under `Directory`. It is a load/verify set, exactly as deduced — not an edit list. The conflict-detection correction above stands.
- **`Links` requires per-game hash lists**: Binary ships `mainkeys/<game>.txt` and `userkeys/<game>.txt` files and wires them into the profile classes via static properties (`Underground2Profile.MainHashList` etc.). These are string/hash dictionaries the libraries need at runtime. **We must ship or generate the equivalent `mainkeys` data** — it comes from Binary's distribution, not from the MIT libraries, so check its licensing separately. This is a real dependency that isn't obvious from the library source alone.

## Applying the edits: use the libraries in-process

The earlier plan had this backwards. Since Nikki and Endscript are MIT and do the container work, **the "long-term" native path is available immediately and should be the primary design.** No external process, no GUI automation, no FlaUI.

### Primary path: reference Nikki + Endscript directly

The pipeline, mirroring what `Binary/CLI.cs` does (read it as documentation, don't copy it):

1. `Launch.Deserialize(manifestPath, out var launch)` → the `VERSN1` model. Set `launch.ThisDir` to the manifest's folder.
2. Point `launch.Directory` at our **staging copy** of the game, and set `launch.Usage` to `Modder`.
3. `BaseProfile.NewProfile(launch.GameID, launch.Directory)` then `profile.Load(launch)` → returns a `string[]` of non-fatal exceptions. **Surface these; don't discard them.**
4. `new EndScriptParser(path).Read()` → `BaseCommand[]`. The parser exposes `CurrentFile`, `CurrentIndex`, `CurrentLine` for precise error reporting — use them.
5. `new EndScriptManager(profile, commands, path)`, then `CommandChase()`, then loop `ProcessScript()`. **This loop is the option hook:** it returns `false` and parks on `manager.CurrentCommand` whenever it hits a `ComboboxCommand` or `CheckboxCommand`, waiting for `.Choice` to be set before continuing. Binary's CLI answers these from `Console.ReadLine()`; **we answer them from the profile's stored selections — that is exactly where our WPF option UI plugs in**, with no synthesized-script trickery required.
6. Check `manager.Errors` — a script can be "applied" *and* have errors. Treat any error as a failed deploy.
7. `profile.Save()` → writes the containers. Also returns exceptions to surface.

Because step 5 is a callback-shaped pause, **we don't need to generate `.end` files to control option selection** — the earlier "synthesize a manifest+script so Binary never shows a combobox" plan is no longer necessary for that purpose. Keep script generation only where it's genuinely useful: merging multiple mods' edits into one ordered apply pass, and producing an inspectable/loggable artifact of what a deploy did. Load-order semantics are unchanged — later mod's `update_*` runs last, last write wins.

### Fallback: Binary's CLI (now confirmed to exist)

`Binary/Program.cs` has a real non-interactive entry point, so the open question is answered:

```
Binary.exe <user|modder> <VERSN1-manifest-path> <VERSN2-script-path>
```

It calls `AllocConsole()`, then `CLI.LoadProfile(args[1])` → `CLI.ImportEndscript(args[2])` → `CLI.Save()`. Gotchas found by reading it:

- **`args[0]` is parsed and then never used** — dead code. The mode actually enforced is the manifest's own `Usage` field, and `LoadProfile` **throws unless it is `Modder`**. Our `User`-mode example manifests would be rejected as-is; CLI use requires a synthesized `Modder` manifest with `Directory` filled in.
- `combobox`/`checkbox` prompt on **stdin**, so a script containing them blocks unless we either pre-resolve them or pipe answers in.
- **Failure reporting is poor**: parse errors and apply errors are `Console.WriteLine`-d and the method `return`s — no non-zero exit code. Errors also land in `EndError.log`/`MainLog.txt` in the *working directory*. So driving the CLI means scraping stdout and log files rather than checking an exit code — a strong argument for the in-process path, where we get `string[]` exception lists and `manager.Errors` directly.

Keep the seam anyway, so the choice isn't load-bearing:

```csharp
interface IEndscriptApplyEngine {
    Task<ApplyResult> ApplyAsync(ApplyRequest request, CancellationToken ct);
}
// InProcessApplyEngine (primary, via Nikki+Endscript) | BinaryCliApplyEngine (fallback)
```

**FlaUI is now unnecessary — drop it from the stack.** Regardless of engine: **apply against a staging copy, verify, then swap into the real game folder.** Never write to the user's live install directly.

### Integration constraints to plan for

- **Target framework is `netcoreapp3.1`** across all three libraries — long out of support. Our fork should retarget to current .NET (net8/net9). Expect this to be mostly mechanical; `AllowUnsafeBlocks` is already enabled in Nikki and Endscript and must stay.
- **`LZCompressLib.dll` is native x64.** Consequences: we must build `win-x64` (already planned); the DLL must sit next to the executable, so **`PublishSingleFile=true` needs `IncludeNativeLibrariesForSelfExtract=true`** or the P/Invoke will fail at runtime — the current packaging line in the stack section is insufficient as written. Under Wine this is unproblematic (Wine runs PE binaries natively), and it's a reason the Windows-only decision holds up well.
- **Binary is WinForms, we are WPF.** Only relevant in that we cannot reuse any of Binary's UI, which we weren't going to anyway.
- Nikki's culture handling: Binary forces `en-US` on the main thread before doing anything. Given the float literals in scripts, **set `InvariantCulture` explicitly** in our own entry point rather than inheriting it by luck.

## Tech stack (decided)

- **UI**: WPF, MVVM pattern. Use CommunityToolkit.Mvvm for low-ceremony MVVM (not full Prism unless a need emerges).
- **Win32 interop**: Direct P/Invoke — `CreateHardLinkW`, `CreateSymbolicLinkW`, registry access via `Microsoft.Win32.Registry` for game install path discovery.
- **Endscript/container work**: `SpeedReflect/Nikki` + `SpeedReflect/Endscript` + `MaxHwoy/CoreExtensions`, all MIT, referenced in-process as forked submodules retargeted to current .NET. Do **not** reference `SpeedReflect/Binary` (GPL-3.0). See the upstream section above.
- **Installer automation**: ~~FlaUI~~ **not needed — dropped.** The MIT libraries expose the option-selection pause point directly in-process, and Binary has a real CLI as a fallback. There is no scenario left that requires driving a GUI.
- **Hashing**: `System.IO.Hashing` (XxHash) for fast internal diffing, or `Blake3.NET` if community-standard checksums matter. Avoid relying on file size + mtime for identity checks — mtimes get reset on extraction and aren't reliable; hash actual content.
- **Archive handling (mod packages, i.e. the zips/rars mods are distributed in)**: `System.IO.Compression` for zip, `SharpCompress` for rar/7z.
- **Manifest parsing**: use `Endscript.Core.Launch` (`Deserialize`/`Serialize`) rather than hand-rolling it — it already handles the `[VERSN1]` header and the non-standard backslash-unescaped JSON dialect on both read and write. Keep a round-trip test over `example_mods` (read → write → byte-compare) so a future framework retarget can't silently change the output dialect.
- **Game container format parsing**: **solved by Nikki** — no byte-level reverse-engineering needed. Per-game support trees cover all four target titles plus Underground 1 and Undercover. Requires `LZCompressLib.dll` (native x64) alongside the executable.
- **`.end` script handling**: use `Endscript`'s `EndScriptParser`/`EndScriptManager`/`BaseCommand` model and `CoreExtensions`' `SmartSplitString` tokenizer instead of writing our own. Our code contributes the layer *above*: variant discovery, option persistence, answering the `ProcessScript()` option pauses from stored selections, conflict detection over resolved command targets, and multi-mod ordering. A merged-script emitter is still worth having as an inspectable deploy artifact, but it's no longer on the critical path.
- **Hash lists**: `mainkeys/<game>.txt` string/hash dictionaries are needed at runtime and ship with **Binary's distribution**, not with the MIT libraries. Confirm their licensing before redistributing; otherwise point the profile statics at a user-supplied Binary install.
- **Packaging**: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` — self-contained so Linux users don't need to `winetricks dotnet` into their existing game prefix, and `IncludeNativeLibrariesForSelfExtract` because `LZCompressLib.dll` is P/Invoked by name and will fail at runtime if it isn't extracted next to the host. `win-x64` is mandatory, not a preference: that DLL is x64-only. Ship `THIRD-PARTY-NOTICES` with the three MIT license texts.

## Core architecture

### Mod type taxonomy — separate deployment backends behind a common interface

Don't unify these into one code path. Build a `ModPackage` abstraction with the following implementations:

| Type                           | Behavior                                                                                                            | Deployment strategy                                                                                                                             |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| ASI/DLL plugins                | Drop-in files                                                                                                       | Direct link (hardlink→symlink→copy fallback), no capture step                                                                                   |
| Loose files                    | Drop-in override                                                                                                    | Direct link, no capture step                                                                                                                    |
| Texmod (.tpf) — **deferred**   | Runtime hook, never touches disk                                                                                    | Out of scope for now. Eventually: no file management — just a launcher wrapper tracking which package to inject                                 |
| Binary mod — endscript-driven   | One or more `VERSN1` manifests, each pointing at a `VERSN2` script; options via sibling manifests (multi-select), `combobox` (single-select), and `checkbox` (boolean) | Discover variants → load via `Launch` + `BaseProfile` → parse via `EndScriptParser` → drive `EndScriptManager.ProcessScript()`, answering option pauses from stored selections → `profile.Save()` against staging. **This is the main path and covers both example mods completely.** |

The speculative "asset replacement" row has been **removed**: the command vocabulary includes `update_texture`, `replace_texture`, `add_or_replace_texture`, `bind_textures`, `import`/`import_all`, and `pack_stream`/`unpack_stream`, so asset replacement is expressible in `.end` and flows through the same endscript-driven backend. There is no known mod behavior requiring a separate binary-diff path.

### Fallback capture pipeline — probably unnecessary, keep only as an escape hatch

With Nikki handling containers and the command vocabulary covering textures, streams, and file operations, there is currently **no identified mod behavior that requires capture-by-diffing**. Do not build this. If a mod ever turns up that the libraries can't express, the shape it would take:

1. **Vanilla baseline**: content-hash every file (not size+date), plus full byte copies of container files.
2. **Staging install**: a resettable working copy; hardlinks or block-cloning where available, full copy otherwise.
3. **Capture**: reset staging → apply → diff by content hash → the delta becomes the mod's payload.
4. **Deploy**: merge file trees in load order over vanilla, link into the game folder, keep backups for clean revert.

Steps 1, 2, and 4 are worth building regardless — **staging and backup/revert are needed by the main path too.** It's only step 3 (diff-based capture) that is speculative.

### Conflict resolution

- **For script-driven mods**: the conflict key is **`(targetFile, keyPath)` from the resolved, flattened command list** — e.g. `(GLOBAL\GLOBALB.LZC, [CarTypeInfos, PEUGOT, PlayerCamera, PLAYER_CAMERA_FAR, CameraAngle])`. Two enabled mods writing the same key with **different** values is a real conflict; writing the same key with the **same** value is benign and must not be reported (this happens legitimately, e.g. 1 Lap's `ALL` variant overlapping `URL`). Case-insensitive comparison on both file paths and key segments, with separators normalized first.
- **Do not** build conflict detection on the manifest's `Files` list (it's a load-set superset, not an edit list) or on `Links` (it's identical per-game boilerplate). Both were the original plan; both would produce mass false positives. See the manifest breakdown above.
- Resolution UI: per-conflict "which mod wins" plus a global load order; the resolved decision is realized simply by the order mods' scripts are applied within one profile load (last write wins).
- **Container-level merging is a non-issue now.** Because all enabled mods' scripts run against a single loaded `BaseProfile` before one `Save()`, edits composite at the collection/entry level automatically — there is never a whole-file "last mod wins" overwrite to guard against. This removes what was previously the hardest planned subsystem. The one thing to preserve: **apply all enabled mods in a single load→apply→save pass**, not one pass per mod, or we reintroduce the problem.

## Known risks / open questions

**Closed by the `example_mods` survey plus the upstream source read.** No longer needs research: `VERSN1`/`VERSN2` are different file types; the full 48-entry command vocabulary; `eUsage`/`eLoaderType`/`ePathType`/`GameINT` enums; `combobox` and `checkbox` grammar and semantics; `Directory` = game install dir; `Files` = load/verify set, not an edit list; `Links` = per-game loader boilerplate; the `.LZC`/`.BUN` container format (Nikki implements it for all six BlackBox titles); the tokenizer's exact behavior; **and whether Binary has a CLI (it does — see the fallback section).**

**Still open, in rough priority order:**

1. **`mainkeys/<game>.txt` hash-list licensing and sourcing.** Required at runtime, ships with Binary's distribution rather than the MIT libraries. Either confirm we may redistribute them, generate equivalents, or require the user to point at an existing Binary install. This is now the most likely thing to block a redistributable build.
2. **`LZCompressLib.dll` license confirmation.** Closed-source native blob inside an MIT repo, no separate license text, no public source. Almost certainly covered by Nikki's MIT grant; worth one email to MaxHwoy since we'd be redistributing it.
3. **Retargeting the three libraries from `netcoreapp3.1` to current .NET.** Expected to be mechanical, but it's the first real integration task and it gates everything else. Verify `unsafe` code and the P/Invoke still behave, and that container round-trips are byte-identical before and after.
4. **Behavioral verification under Wine.** Both the managed libraries and the native `LZCompressLib.dll` P/Invoke. Do this early — it's the one remaining assumption with no code-reading answer.
5. **Symlink permissions under Wine**: Windows normally requires `SeCreateSymbolicLinkPrivilege` (admin or Developer Mode); Wine's ntdll enforcement varies by build. Confirm hardlink and symlink behavior on the actual Wine/Proton builds targeted. Relevant to the ASI/loose-file MVP.
6. **Which of the 48 commands actually need first-class UI/conflict handling.** Our two example mods use 5. Commands like `if`, `static`, `generate`, `create_file`/`erase_file`, and `unlock_memory`/`speedreflect` have side effects outside the collection model, so conflict detection keyed on `(targetFile, keyPath)` won't cover them. Read `Endscript/Commands/` and classify each command by what it touches; treat unclassified commands as opaque and warn rather than silently assuming they're conflict-free.
7. **`.bacc` backup-file convention.** `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit next to their originals in the install listing — presumably Binary's own pre-edit backups. Now cheap to answer by grepping the upstream source for `bacc` rather than by inspecting bytes. Decide whether to reuse the mechanism or keep our backups fully independent. Either way, our snapshot step must not treat `.bacc` files as game content.
8. **Wider manifest/script sample**, for Most Wanted, Carbon, and ProStreet — to validate the per-game `Links` boilerplate assumption and exercise commands the U2 mods don't. Lower priority now that the enums come from source rather than inference.

## Suggested roadmap

Format research is **done**, and the container problem is **solved upstream**. Remaining order of work:

1. **Fork and retarget the three MIT libraries** (`Nikki`, `Endscript`, `CoreExtensions`) to current .NET; get them building and packaged with the native DLL. Prove it end to end with a throwaway console harness that applies one `example_mods` manifest to a scratch copy of the game and produces a working `GLOBALB.LZC`. **This is the single highest-value first step — it de-risks the whole project in one move.** Verify under Wine here, not later.
2. **Sort out the hash lists and license notices** (open questions 1–2) — small, but they gate anything distributable.
3. **Our layer over Endscript**: variant discovery from sibling `VERSN1` files, option model (`combobox`/`checkbox`) with persisted selections, answering `ProcessScript()` pauses non-interactively, resolved-command extraction for conflict detection. Testable against `example_mods` without any UI.
4. **MVP shell** — ASI/DLL + loose-file mods. MVVM scaffolding, game detection, profiles, load order UI, link-deploy engine (hardlink→symlink→copy fallback), staging + backup/revert. Smoke-test under Wine continuously.
5. **Binary mod deployment** — wire step 3 into the UI: variant multi-select, option controls, conflict list, single load→apply-all→save pass against staging, atomic swap, revert.
6. **Game profile support** — path/registry/executable differences across Underground 2, Most Wanted, Carbon, and ProStreet. Nikki already covers all four (plus UG1 and Undercover), so this is our own plumbing only.
7. **Command classification hardening** (open question 6) — proper handling for commands outside the collection model.
8. **Texmod / `.tpf` support** — explicitly last, explicitly optional. Nothing earlier may depend on it.

## Success criterion for the first Binary-capable build

Both mods in `example_mods` install correctly, together, from our UI, with Binary never launched:

- The camera mod is imported, its `combobox` surfaces in our UI as a two-option single-select with the caption `"Choose option you needeed"`, and the stored selection answers `EndScriptManager.ProcessScript()`'s option pause without any prompt (450 or 744 `update_collection` commands executed).
- The 1 Lap mod is imported as one mod with five multi-select variants; enabling `URL` + `CIRCUIT` executes 53 + 51 `update_incareer` commands, with the `ALL`-overlap case reported as benign rather than as a conflict.
- Both enabled at once → no false conflict reported (disjoint key paths: `CarTypeInfos` vs. `GCareers`), applied in **one** load→apply-all→save pass against staging, `manager.Errors` empty, atomic swap into the game folder, and a clean revert to vanilla afterward.
- The game launches and both mods are observably in effect — camera behavior changed and career races run one lap.
