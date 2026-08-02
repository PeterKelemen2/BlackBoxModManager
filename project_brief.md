# Project Brief: Mod Manager for BlackBox-era Need for Speed titles

## Context

This project builds a mod manager for the BlackBox-era Need for Speed games. The target titles are Underground 2, Most Wanted, Carbon, and ProStreet. The modding scene for these games uses three separate mod formats. No unified tool manages them.

1. **ASI scripts** — plugin DLLs that an ASI loader reads. These are drop-in files.
2. **Binary mods** — a third-party tool called [Binary](https://github.com/SpeedReflect/Binary) (version 2.8.3) installs these. The tool edits game data files. This category is the most difficult to manage. It is the main architectural problem of this project.
3. **Texmod packages (.tpf)** — a hooking tool injects textures at run time. These files never touch the disk. The tool applies them at launch time. **This category is out of scope for the first implementation.** The brief records it so that the architecture keeps room for it. No part of the MVP or the Binary work depends on it. Do not write `.tpf` code until the rest works.

No Vortex-style or Mod Organizer 2-style manager exists for this scene. This project builds one.

## Platform decision

**This is a Windows-only application.** Linux users run it in the same Wine prefix as the game. They already run the game and Binary this way. Therefore the project needs no cross-platform abstraction layer. Use native Windows APIs throughout. Test behavior under Wine as a compatibility target, not as a separate platform.

## Prerequisite: an existing Binary 2.8.3 install

**The application requires an existing Binary 2.8.3 install on the machine. It does not bundle Binary.** The user installs Binary themselves. Our application asks for the path to that install and stores it.

This is a decision, not a limitation to remove later. It settles the largest distribution problem in the project. Binary ships data files that the MIT libraries need at run time but do not contain. The `mainkeys/<game>.txt` and `userkeys/<game>.txt` hash lists are the main example. We read those files from the install of the user. We never redistribute them. Therefore we never need permission to redistribute them.

What this means for the implementation:

1. On first run, ask the user for the Binary install directory.
2. Validate the directory. Check that `Binary.exe` exists. Check that `mainkeys/` holds a file for each supported game.
3. Read the version and confirm that it is 2.8.3. Warn on any other version, because our hash-list expectations come from that release.
4. Store the path in application settings.
5. At profile load, point the profile statics such as `Underground2Profile.MainHashList` at the files under that path.
6. If the path is missing or invalid at any later run, block Binary mod features and tell the user. Do not fail silently and do not guess a path.

We still consume Binary only as a data source and as a read-only reference. We do not link its code, because it is GPL-3.0. See the upstream section below. A path to an install that the user made is not distribution, so the GPL does not reach our application.

Two consequences worth noting:

- The Wine story gets simpler. Linux users already have Binary in the game prefix, so the file that we need is already there.
- This does **not** solve the `LZCompressLib.dll` redistribution question. A real 2.8.3 install does ship that DLL, but the shipped copy is 32-bit. Every binary in the Binary distribution is i386, including `Binary.exe` and its own `Nikki.dll`. We build x64, so we must ship the x64 copy from the Nikki repository. See `docs/roadmap/00-test-environment.md`.

## Binary mod format (confirmed against `example_mods`)

Both example mods have been read in full. The format has **two different file types that share the `.end` extension**. The header line identifies the type.

| Header     | File type    | Body                                                                   |
| ---------- | ------------ | ---------------------------------------------------------------------- |
| `[VERSN1]` | **Manifest** | A JSON object (`Usage`/`Game`/`Directory`/`Endscript`/`Files`/`Links`) |
| `[VERSN2]` | **Script**   | Line-oriented commands (`update_*`, `combobox`, `append`, `end`)       |

`VERSN1` and `VERSN2` are **not two versions of one schema**. The version tag selects the parser. A reader must dispatch on the header. A reader must not assume that the `.end` extension means manifest. Any other `VERSNn` value must cause a hard parse error that names the file. Do not fall back without a message.

A mod folder holds one or more `VERSN1` manifests at its root. Each manifest points through `Endscript` at a `VERSN2` script. The script is usually in a subfolder. Example manifest:

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

The parts of the manifest:

- **Header** (`[VERSN1]`) and then a **JSON body**. The JSON is **not valid JSON**. Path values hold raw backslashes without escapes, such as `"MOD\URL.end"` and `"GLOBAL\GLOBALA.BUN"`. `System.Text.Json` rejects `\U`, `\G`, and `\a` as invalid escape sequences. The reader must process the body first. Change each single backslash to `\\` before deserialization, or use a tolerant reader. This problem is real, not theoretical.
- **`Game`** — the title that the mod targets. This example targets `Underground2`. Mods are game-specific. The manifest is a reliable source for the game name. Do not guess it from the folder structure and do not ask the user. Treat the field as a closed enum that maps to our game profiles. If the value is unknown, mark the mod as unsupported and do not install it.
- **`Usage`** — the value is `"User"` in all four manifests that we inspected. The enum is `Invalid`/`User`/`Modder`. Mods that authors distribute always use `User`. `Modder` is the automation mode. `Modder` mode needs a value in `Directory`.
- **`Directory`** — the value is an empty string in all four manifests that we inspected. **The upstream source confirms that this field holds the game install directory.** Distributed `User`-mode mods leave it empty because Binary asks the user to browse for the directory. In `Modder` mode the field must hold a path, and the path must exist on the disk. `Files` paths resolve against this directory. `Links` entries with `PathType: Absolute` also resolve against it. This is the field that _we_ fill in. We point it at our staging copy. This keeps the live install untouched.
- **`Endscript`** — a Windows-style relative path to the `VERSN2` script. It uses backslashes. It resolves against the folder of the manifest. Normalize it to the host separator before use.
- **`Files`** — the list of game data files that this mod touches. **This list is a superset of what the script edits.** All five `1 Lap *` manifests and the `Install.end` file of the camera mod declare `GLOBAL\GLOBALA.BUN`, `GLOBAL\GLOBALB.LZC`, or both. But every `update_*` command across all 1484 script lines targets only `GLOBAL\GLOBALB.LZC`. Read `Files` as the set of containers that Binary must open together to resolve the script. Do not read it as the set of files that change. The consequence for us: **conflict detection must use the command targets of the script, not `Files`.** `Files` alone would report a false conflict between any two mods that both load `GLOBALA.BUN`. `Files` is still worth capture as the set that needs a backup. A full file listing of a real vanilla install (`game_files.txt`) confirms that these files exist. The listing holds `GLOBAL/GLOBALA.BUN`, `GLOBAL/GLOBALB.BUN`, `GLOBAL/InGameCommon.lzc`, and `GLOBAL/GlobalB.lzc`. It also holds other `.BUN` files in `NIS/`, `TRACKS/`, and `FRONTEND/`, and one `.viv` file at `SDATA/sdat.viv`. An earlier note said that the install held no `.viv` or `.bun` files. That note was wrong. The files are there, but not in the place that we first checked.
- The file listing shows one more thing. `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit beside the real files. `.bacc` looks like the backup-file convention of Binary. Binary appears to copy the original before it edits a file in place, so that it can revert. This matters for our design. Our snapshot step must ignore `.bacc` files, because they are bookkeeping and not game content. Alternatively, we can examine whether the `.bacc` files of Binary restore the vanilla state for us. Then we would not need a fully separate backup mechanism. First inspect what a `.bacc` file holds. It may be a verbatim copy of the original, or another format.
- **`Links`** — a mechanism separate from `Files` and `Endscript`. Each entry holds a `LoadType` and a `PathType`. We have seen the load types `Attributes`, `FeAttrib`, and `Labels`. These look like a fixed enum of loader categories. We have seen only the path type `Absolute`. A `Relative` variant probably also exists. The entries point at loose `.bin` files: `attributes.bin`, `fe_attrib.bin`, `Labels_Global.bin`, and `Labels.bin`. These entries appear to register extra loose data files with the game loader by category. This is separate from what the `Endscript` does to the `Files` list.

`Links` is **the same in all four manifests that we inspected**. The four entries and their order match: `Attributes`/`GLOBAL\attributes.bin`, `FeAttrib`/`GLOBAL\fe_attrib.bin`, `Labels`/`LANGUAGES\Labels_Global.bin`, and `Labels`/`LANGUAGES\Labels.bin`. The two mods are unrelated and have different authors. This strongly suggests that `Links` is **boilerplate that the mod-authoring mode of Binary writes**. It describes the standard loose-file set for Underground 2. It is not a per-mod declaration. The design consequence: **do not base conflict detection on `Links`.** That approach would flag every pair of U2 mods as a conflict. Parse and store `Links`. Compare it against the expected per-game boilerplate. Report it only when a mod differs from that boilerplate.

This is still useful. Binary mods ship with a machine-readable manifest, so we do not need to infer anything by diff. The mod manager must **read this manifest directly** instead of an invented parallel schema.

### `VERSN2` script grammar (confirmed)

The script is line-oriented. The parser ignores blank lines. Whitespace separates the tokens, but a double-quoted string is one token and can hold spaces. A plain `Split(' ')` is wrong and breaks `combobox`. Write a quote-aware tokenizer.

These commands appear across the 1484 script lines in `example_mods`:

**1. Edit commands — `update_collection` and `update_incareer`**

```
update_collection GLOBAL\GLOBALB.LZC CarTypeInfos PEUGOT PlayerCamera PLAYER_CAMERA_FAR CameraAngle 0
update_incareer   GLOBAL\GLOBALB.LZC GCareers Main GCareerRaces S5_URL_5 Stages STAGE1 NumberOfLaps 1
```

Counts: `update_collection` 1194 times in the camera mod, `update_incareer` 290 times in the 1 Lap mod. Every `update_collection` has exactly 7 arguments. Every `update_incareer` has exactly 9. **Do not hardcode these counts.** Both commands fit one general shape:

```
<verb> <targetFile> <keyPath...> <value>
```

`targetFile` is the first argument. `value` is the last token. The tokens between them form a hierarchical key path of variable length. Parse to this shape. The `update_` prefix implies that sibling verbs such as `add_` and `remove_` exist. An unknown verb must fail loudly with the file name and the line number. Do not skip it. A skipped edit produces an install that is wrong in a way that is hard to see.

The script does not type the `value` token. Observed values include integers such as `0` and `1`. They also include positive and negative floats with full round-trip precision, such as `-0.19500002`, `2.746582`, and `1.016`. **Parse floats with `InvariantCulture`. Keep the original text of the literal for output.** A default `ToString()` would corrupt `-0.19500002`. Type resolution belongs to the container layer, not to the script parser. Carry each value as a string plus a parsed hint.

**2. `combobox` — install options that the user selects.** See the section below.

**3. `append "<relative path>"`** — this command splices in another `.end` script. The path resolves against the folder of the _containing script_. The appended files carry their own `[VERSN2]` header line. The interpreter must accept and skip a header in appended content. Implement this recursively. Use a visited-set for cycle detection and a depth cap. We have not seen nesting deeper than one level, but deeper nesting is possible.

**4. `end`** — this command terminates the script. It is the last line of `script.end` in the camera mod. It is absent from the pure-edit scripts `MOD/*.end`, `Main/[0]_*.end`, and `Main/[1]_*.end`, which run to the end of the file. Treat `end` as optional but honored. Stop at `end` or at the end of the file, whichever comes first.

**Not observed at all:** no command touches a `.BUN`, and no command replaces an asset, a texture, or a file. No conditional and no variable appears. Everything in both example mods is a scalar field write into `GLOBALB.LZC`. This is a narrow but solid base for the first working version.

## Install options that the user selects — two different mechanisms

Both example mods give the user a choice at install time. They do this in **two completely different ways**. The project must support both. Different parts of the codebase support them.

### Mechanism A — `combobox` inside the script (the camera mod)

`Main/script.end` has only nine lines and is entirely a menu:

```
[VERSN2]
combobox "Install Camera Mod [NFSMW TO U2]" "Restore original camera settings"  "Choose option you needeed"

"Install Camera Mod [NFSMW TO U2]"
append "[1]_Camera_MOD_NFSMW_TO_U2.end"

"Restore original camera settings"
append "[0]_Restore_Camera_Settings.end"

end
```

The two large files `Main/[0]_*.end` and `Main/[1]_*.end` hold 744 and 450 lines. They are the option bodies. No manifest references them directly. Only the `append` command in the selected branch references them.

The grammar:

- `combobox <string>...` — a list of quoted strings. The **last** string is the prompt that the user sees, here `"Choose option you needeed"`. The strings before it are the option labels.
- The upstream source confirms this rule. `ComboboxCommand.Prepare` takes `splits[1 .. ^2]` as the options and `splits[^1]` as the description. It needs at least 4 tokens. The rule "the last quoted string is the caption" is correct. An earlier draft proposed a block-header cross-check heuristic. That heuristic is unnecessary. Drop it.
- A **block** runs from its header line to the next block header, the `end` command, or the end of the file. Block bodies hold ordinary commands. Here each body holds one `append`. The interpreter must allow any command sequence. Do not special-case `append`.
- The real file has two spaces between the second option and the caption. Whitespace between tokens is not significant. This is another reason to drive the tokenizer by quotes and not by position.

**Our approach: we resolve the choice in our own UI. Binary never asks.** The option labels and the caption are plain text. We read them out of the script at import time. Therefore:

1. On mod import, parse the `Endscript`. If it holds a `combobox`, extract the caption and the labels. Record them on the mod as a declared **option set**.
2. Show the option set as a native WPF control in our install flow or in a per-mod settings pane. The choice stays visible, the user can edit it later, and the profile stores it. This improves on the one-shot modal of Binary.
3. Store the chosen label with the install state of the mod. A later change of the selection is only a re-deploy with a different branch. This is the purpose of the "Restore original camera settings" option. Our UI gets a real toggle where Binary had only a re-run.
4. At deploy time, the interpreter takes the resolved selection as an input. It walks only the selected block and flattens the `append` commands into one linear command list. **The output of this stage is a flat list of `<verb> <file> <keyPath...> <value>` edits.** It holds no `combobox`, no `append`, and no open question for the user. Everything downstream sees only that flat list. Conflict detection, deployment, and the apply engine never need to know that options existed.
5. The system must also resolve options without a user. Headless re-deploy and profile switching need this. If no selection is stored, use the **first** option label and log the assumption. Do not block on a prompt.

### Mechanism B — several sibling manifests (the 1 Lap mod)

The 1 Lap mod has **no `combobox`**. Instead it ships five `VERSN1` manifests side by side at its root. Each manifest points at a different `Endscript`.

| Manifest                  | `Endscript`       | Script lines |
| ------------------------- | ----------------- | ------------ |
| `1 Lap ALL Races.end`     | `MOD\ALL.end`     | 147          |
| `1 Lap URL Races.end`     | `MOD\URL.end`     | 53           |
| `1 Lap CIRCUIT Races.end` | `MOD\CIRCUIT.end` | 51           |
| `1 Lap STREET Races.end`  | `MOD\STREET.end`  | 37           |
| `1 Lap SUV Races.end`     | `MOD\SUV.end`     | 12           |

Apart from `Endscript`, the five manifests are byte-identical. The readme tells the user to open Binary and pick one of the five files. It also says _"You can use 1 or multiple variants of this mod"_. These variants are therefore **not mutually exclusive**, unlike a `combobox`. `ALL` is the union of the other four. A user who selects `ALL` and another variant creates a redundant but harmless overlap, because the same field gets the same value twice. Our conflict detector must treat same-value collisions as benign and must not warn about them.

**Our approach:** a mod folder maps to **one `ModPackage` with N discoverable variants**. It does not map to N unrelated mods. Discovery scans the mod root for `VERSN1` files. Show the variants as a multi-select checkbox list. This contrasts with the single-select `combobox`. The internal model therefore needs both concepts:

```
ModPackage
  ├── Variants     : IReadOnlyList<BinaryVariant>   // from sibling VERSN1 manifests — multi-select
  └── each Variant ├── Manifest (VERSN1)
                   └── OptionSet? (from combobox)   // single-select, may be null
```

The profile holds the selection state for both. A profile therefore determines the resolved edit list with no prompt. A mod can use both mechanisms at the same time. It can have several manifests, and each script can hold a `combobox`. The model above handles this case. Do not collapse the two concepts into one list.

## Upstream code: Binary is open source, and its libraries are MIT

**This section supersedes most of the reverse-engineering plan below.** The source of Binary is public at `github.com/SpeedReflect/Binary`. Binary is a thin WinForms shell over two libraries that do the real work. **The libraries and the application have different licenses.**

| Repo                                                                        | License        | Size   | What it is                                                                  |
| --------------------------------------------------------------------------- | -------------- | ------ | --------------------------------------------------------------------------- |
| [`SpeedReflect/Nikki`](https://github.com/SpeedReflect/Nikki)               | **MIT**        | ~7 MB  | Reads and writes the game containers (`.BIN`, `.BUN`, `.LZC`). The format.  |
| [`SpeedReflect/Endscript`](https://github.com/SpeedReflect/Endscript)       | **MIT**        | 225 KB | `.end` manifest and script parsing, command model, execution against Nikki  |
| [`MaxHwoy/CoreExtensions`](https://github.com/MaxHwoy/CoreExtensions)       | **MIT**        | —      | Utility library. The only dependency of Nikki.                              |
| [`SpeedReflect/Binary`](https://github.com/SpeedReflect/Binary)             | **GPL-3.0** ⚠️ | 1.5 MB | WinForms GUI and a CLI entry point. The application shell only.             |
| [`MaxHwoy/ILWrapper`](https://github.com/MaxHwoy/ILWrapper)                 | MIT            | —      | DevIL image wrapper. Only the GUI of Binary uses it. **We do not need it.** |
| [`SpeedReflect/SpeedReflect`](https://github.com/SpeedReflect/SpeedReflect) | MIT            | —      | C++ memory-patching extension for the game. Unrelated to on-disk modding.   |

One author wrote all of them: MaxHwoy, `max.hwoy@gmail.com`. The last change was in November 2021. There are no other contributors. The provenance is clean and one person can answer questions.

### How we consume the three MIT libraries

None of the three are on NuGet. There is no package to install. The only way to get them is the git repository. Therefore:

1. Fork `Nikki`, `Endscript`, and `CoreExtensions` to our own account.
2. Add each fork as a git submodule under `third_party/`.
3. Retarget each `.csproj` from `netcoreapp3.1` to a current .NET version.
4. Add a `<ProjectReference>` from our application to each `.csproj`.
5. Copy `LZCompressLib.dll` to the output directory beside the executable.

Step 1 is what makes step 3 possible. We cannot retarget a repository that we do not control. A vendored copy in-tree is an acceptable alternative to a submodule. A direct submodule of the upstream repository is not, because we must change the target framework.

### The licensing answer

- **Nikki, Endscript, and CoreExtensions: use them directly.** They are MIT (`Copyright (c) 2020 MaxHwoy`). MIT is permissive and has no copyleft. A closed project or a differently-licensed project can use them. The only obligation is to keep the copyright notice and the license text. Ship a `THIRD-PARTY-NOTICES` file that lists all three. The license does not require a fork. Our retarget to a current .NET version does require one. See the consumption steps above.
- **Binary itself is GPL-3.0. Do not copy code from it.** This is the one real constraint. A link to GPL-3.0 code would force us to release our whole application under GPL-3.0. The libraries are MIT and hold everything that we need. **There is no reason to touch the source of Binary.** Treat that repo as read-only documentation. Two specific traps:
  - Do not take helpers out of `Binary/`, even attractive ones such as `Editor.FixLaunchDirectory`, which the CLI path calls. Reimplement from the behavior. Do not copy and paste.
  - Do not add `Binary.csproj` as a project reference to reuse its CLI. A call to `Binary.exe` as a separate process is acceptable, because the GPL covers distribution and linking, not a call to an unmodified program that the user already installed. But this is a fallback that we probably do not need.
- **Choose our own license and record it in the repo before publication.** MIT keeps us aligned with the libraries that we depend on.
- **One open item:** `Nikki/Nikki/LZCompressLib.dll` is a **closed-source native x64 PE DLL that the repo checks in**. It is 116 KB and its build path is `C:\Users\Max\source\repos\LZCompressLib`. It has no public source repo and no separate license file. Nikki calls its `BlockCompress` and `BlockDecompress` entry points through P/Invoke from `Nikki/Utils/Interop.cs`. **Container compression needs this DLL.** It is not optional. It sits inside an MIT repo by the same author, so the MIT grant most likely covers it. But the blob itself carries no license statement. The risk is low. This matters only if we ship our own copy of the DLL. The Binary install that we already require may hold it, which would remove the question. See the prerequisite section above.

### What this eliminates

The sections below described reverse-engineering work. That work is now a code-reading exercise.

- **The `.LZC` and `.BUN` container format** needs no reverse-engineering. Nikki implements it. Nikki supports **Underground 1, Underground 2, Most Wanted, Carbon, ProStreet, and Undercover**. This is a superset of our four targets. Each game has its own `Support.<Game>/` tree with `Attributes`, `Class`, `Parts`, and `Framework` types.
- **`.end` parsing** needs no new code. `Endscript` provides `EndScriptParser`, `EndScriptManager`, a `BaseCommand` model, and `Launch`. `Launch` is the exact `VERSN1` DTO. Its `Serialize` method reproduces the backslash dialect through `settings.Replace(@"\\", @"\")`. This confirms the dialect quirk and gives us the writer at no cost.
- **Guesses about enums and vocabulary** are no longer needed. The source answers them. See the corrections below.

## Corrections and confirmations from the upstream source

A read of `Endscript` and `Nikki` confirms most of the `example_mods` survey and corrects several details. Where this section disagrees with anything above, **this section wins**. It comes from the implementation, not from inference.

- **The full command vocabulary has 48 entries**, not 5. From `Endscript/Enums/eCommandType.cs`:
  `invalid`, `empty`, `game`, `version`, `append`, `update_collection`, `update_string`, `update_texture`, `update_incareer`, `add_collection`, `add_string`, `add_texture`, `add_incareer`, `remove_collection`, `remove_string`, `remove_texture`, `remove_incareer`, `copy_collection`, `copy_texture`, `copy_incareer`, `replace_texture`, `bind_textures`, `add_or_update_string`, `add_or_replace_texture`, `static`, `import`, `import_all`, `new`, `delete`, `watermark`, `create_file`, `create_folder`, `erase_file`, `erase_folder`, `move_file`, `generate`, `directory`, `filecount`, `capacity`, `checkbox`, `combobox`, `if`, `stop_errors`, `unlock_memory`, `speedreflect`, `unpack_stream`, `pack_stream`, `end`.
  The predicted `add_` and `remove_` families exist. Note also **`if`**, which adds conditionals, so scripts are not purely declarative. Note the **texture commands**. Asset replacement is expressible in `.end` after all, so we can drop the speculative "asset replacement" taxonomy row. Note the **file and folder commands**. Note **`checkbox`**, a second interactive command beside `combobox`.
- **`checkbox` is a second option mechanism** that we had not seen. It is a yes/no toggle. The CLI of Binary prompts `Select one [yes, no]` and maps the answer to `Choice` 1 or 0. Our option UI must handle three shapes, not two: sibling-manifest variants (multi-select), `combobox` (single-select from N), and `checkbox` (boolean).
- **The `combobox` grammar matches the inference exactly.** `ComboboxCommand.Prepare` needs at least 4 tokens. It takes `splits[1 .. ^2]` as the options and `splits[^1]` as the description. The trailing-token-is-caption rule is correct. Drop the block-header cross-check heuristic that the section above proposed.
- **The tokenizer is `SmartSplitString` in `CoreExtensions/Text/RegX.cs`.** It is a quote-toggling scanner that splits only on `' '`. It does **not** treat tabs as separators. It emits quoted segments as tokens without the quotes. If we write our own tokenizer, it must match this behavior exactly. Use of `CoreExtensions` removes the question.
- **`ePathType.Absolute` does not mean "absolute path".** From `Launch.LoadLinks()`: `Relative` resolves against the folder of the _manifest_, and `Absolute` resolves against the _game install_ directory. Both are relative paths. `Absolute` means "rooted at the game directory". A reversal of this rule would break every `Links` entry, because every observed entry is `Absolute`.
- **`Directory` is the game install directory.** The field is empty in our example mods because `User` mode asks the user to browse for the directory. In `Modder` mode the field must hold a path, and the path must exist. The earlier note said to fail loudly when the field is not empty. That note is wrong. **A filled field is normal for the automation path, and we fill it.**
- **`eUsage`** = `Invalid`/`User`/`Modder`. **`eLoaderType`** = `Invalid`/`BinKeys`/`VltKeys`/`Attributes`/`FeAttrib`/`Labels`, so two loader types exist beyond the three that we had seen. **`GameINT`** = `None`/`Carbon`/`MostWanted`/`Underground2`/`Underground1`/`Prostreet`/`Undercover`.
- **The semantics of `Files` are confirmed.** `Launch.CheckFiles()` only verifies that each entry exists under `Directory`. It is a load-and-verify set, exactly as deduced. It is not an edit list. The conflict-detection correction above stands.
- **`Links` needs per-game hash lists.** Binary ships `mainkeys/<game>.txt` and `userkeys/<game>.txt` files. It wires them into the profile classes through static properties such as `Underground2Profile.MainHashList`. These are string and hash dictionaries that the libraries need at run time. They come from the distribution of Binary, not from the MIT libraries. This is a real dependency that the library source alone does not show. **We read these files from the Binary install of the user.** See the prerequisite section above.

## Applying the edits: use the libraries in-process

The earlier plan had this backwards. Nikki and Endscript are MIT and do the container work. **The native path that we called long-term is available now and must be the primary design.** No external process, no GUI automation, no FlaUI.

### Primary path: reference Nikki and Endscript directly

The pipeline mirrors what `Binary/CLI.cs` does. Read that file as documentation. Do not copy it.

1. Call `Launch.Deserialize(manifestPath, out var launch)` to get the `VERSN1` model. Set `launch.ThisDir` to the folder of the manifest.
2. Point `launch.Directory` at our **staging copy** of the game. Set `launch.Usage` to `Modder`.
3. Call `BaseProfile.NewProfile(launch.GameID, launch.Directory)` and then `profile.Load(launch)`. This returns a `string[]` of non-fatal exceptions. **Show these. Do not discard them.**
4. Call `new EndScriptParser(path).Read()` to get `BaseCommand[]`. The parser exposes `CurrentFile`, `CurrentIndex`, and `CurrentLine`. Use them for precise error reports.
5. Call `new EndScriptManager(profile, commands, path)`, then `CommandChase()`, then loop over `ProcessScript()`. **This loop is the option hook.** It returns `false` and parks on `manager.CurrentCommand` when it reaches a `ComboboxCommand` or a `CheckboxCommand`. It waits for a value in `.Choice` before it continues. The CLI of Binary answers these from `Console.ReadLine()`. **We answer them from the stored selections of the profile. This is where our WPF option UI plugs in.** No synthesized script is needed.
6. Check `manager.Errors`. A script can apply and still produce errors. Treat any error as a failed deploy.
7. Call `profile.Save()` to write the containers. This also returns exceptions to show.

Step 5 is a pause with the shape of a callback. **Therefore we do not need to generate `.end` files to control option selection.** The earlier plan synthesized a manifest and a script so that Binary would never show a combobox. That plan is no longer necessary for this purpose. Keep script generation only where it helps. It helps for a merge of several mods into one ordered apply pass. It also helps as an artifact that a user can inspect and log. The load-order semantics do not change. The `update_*` command of the later mod runs last, and the last write wins.

### Fallback: the CLI of Binary (confirmed to exist)

`Binary/Program.cs` has a real non-interactive entry point, so the open question has an answer:

```
Binary.exe <user|modder> <VERSN1-manifest-path> <VERSN2-script-path>
```

It calls `AllocConsole()`, then `CLI.LoadProfile(args[1])`, then `CLI.ImportEndscript(args[2])`, then `CLI.Save()`. A read of the code shows these problems:

- **The program parses `args[0]` and then never uses it.** It is dead code. The enforced mode comes from the `Usage` field of the manifest. `LoadProfile` **throws unless the value is `Modder`**. Our `User`-mode example manifests would fail as they are. CLI use needs a synthesized `Modder` manifest with a value in `Directory`.
- `combobox` and `checkbox` prompt on **stdin**. A script that holds them blocks, unless we resolve them first or pipe the answers in.
- **Failure reporting is poor.** The program writes parse errors and apply errors with `Console.WriteLine` and then returns. It sets no non-zero exit code. Errors also go to `EndError.log` and `MainLog.txt` in the _working directory_. Use of the CLI therefore means that we scrape stdout and log files instead of a check of the exit code. This is a strong argument for the in-process path, which gives us `string[]` exception lists and `manager.Errors` directly.

Keep the seam anyway, so that the choice does not carry weight:

```csharp
interface IEndscriptApplyEngine {
    Task<ApplyResult> ApplyAsync(ApplyRequest request, CancellationToken ct);
}
// InProcessApplyEngine (primary, via Nikki+Endscript) | BinaryCliApplyEngine (fallback)
```

**FlaUI is now unnecessary. Drop it from the stack.** For either engine: **apply against a staging copy, verify, then swap into the real game folder.** Never write to the live install of the user.

### Integration constraints to plan for

- **The target framework of all three libraries is `netcoreapp3.1`.** Support for it ended long ago. Our fork must retarget to a current .NET version, such as net8 or net9. This work is mostly mechanical. `AllowUnsafeBlocks` is already on in Nikki and Endscript and must stay on.
- **The `LZCompressLib.dll` in the Nikki repository is native x64.** Therefore we must build `win-x64`, which we already planned. Note that the copy shipped with Binary is a different, 32-bit build of the same name. Do not mix them. The DLL must sit next to the executable. **`PublishSingleFile=true` therefore needs `IncludeNativeLibrariesForSelfExtract=true`**, or the P/Invoke fails at run time. The current packaging line in the stack section is not sufficient. Wine handles this well, because Wine runs PE binaries natively. This supports the Windows-only decision.
- **Binary uses WinForms and we use WPF.** This matters only because we cannot reuse the UI of Binary. We did not plan to reuse it.
- **Culture handling in Nikki:** Binary forces `en-US` on the main thread before it does anything. Our scripts hold float literals, so **set `InvariantCulture` explicitly** in our entry point. Do not inherit it by luck.

## Tech stack (decided)

- **UI**: WPF with the MVVM pattern. Use CommunityToolkit.Mvvm for low-ceremony MVVM. Do not use full Prism unless a need appears.
- **Win32 interop**: direct P/Invoke to `CreateHardLinkW` and `CreateSymbolicLinkW`. Use `Microsoft.Win32.Registry` to find the game install path.
- **Endscript and container work**: `SpeedReflect/Nikki`, `SpeedReflect/Endscript`, and `MaxHwoy/CoreExtensions`. All are MIT. Reference them in-process as forked submodules that we retarget to a current .NET version. Do **not** reference `SpeedReflect/Binary`, which is GPL-3.0. See the upstream section above.
- **Installer automation**: dropped. FlaUI is not needed. The MIT libraries expose the option-selection pause in-process, and Binary has a real CLI as a fallback. No scenario needs a GUI driver.
- **Hashing**: `System.IO.Hashing` (XxHash) for fast internal diffs. Use `Blake3.NET` if community-standard checksums matter. Do not identify files by size and mtime. Extraction resets mtimes, so they are unreliable. Hash the content.
- **Archive handling** for the zip and rar files that hold mods: `System.IO.Compression` for zip, and `SharpCompress` for rar and 7z.
- **Manifest parsing**: use `Endscript.Core.Launch` with `Deserialize` and `Serialize`. Do not hand-roll this. The class already handles the `[VERSN1]` header and the non-standard backslash dialect on both read and write. Keep a round-trip test over `example_mods` that reads, writes, and compares bytes. This stops a future framework retarget from a silent change to the output dialect.
- **Game container format parsing**: **Nikki solves this.** No byte-level reverse-engineering is needed. The per-game support trees cover all four target titles plus Underground 1 and Undercover. This needs `LZCompressLib.dll` (native x64) beside the executable.
- **`.end` script handling**: use `EndScriptParser`, `EndScriptManager`, and the `BaseCommand` model from `Endscript`. Use the `SmartSplitString` tokenizer from `CoreExtensions`. Do not write our own. Our code adds the layer _above_: variant discovery, option persistence, answers to the `ProcessScript()` option pauses from stored selections, conflict detection over resolved command targets, and multi-mod ordering. A merged-script emitter is still worth building as an inspectable deploy artifact, but it is not on the critical path.
- **Hash lists**: the `mainkeys/<game>.txt` string and hash dictionaries are needed at run time. They ship with **the distribution of Binary**, not with the MIT libraries. **Point the profile statics at the Binary 2.8.3 install of the user.** We never redistribute these files. See the prerequisite section above.
- **Packaging**: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`. Self-contained means that Linux users do not need `winetricks dotnet` in their existing game prefix. `IncludeNativeLibrariesForSelfExtract` is required because the code P/Invokes `LZCompressLib.dll` by name, and the call fails at run time if the file is not extracted next to the host. `win-x64` is mandatory once we use the x64 `LZCompressLib.dll` from the Nikki repository, which is our decision. Binary itself is a 32-bit application, so an x86 build is possible in principle. Do not take that path without a strong reason. Ship `THIRD-PARTY-NOTICES` with the three MIT license texts.

## Core architecture

### Mod type taxonomy — separate deployment backends behind one interface

Do not unify these into one code path. Build a `ModPackage` abstraction with these implementations:

| Type                          | Behavior                                                                                                                                                               | Deployment strategy                                                                                                                                                                                                                                                                                   |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ASI/DLL plugins               | Drop-in files                                                                                                                                                          | Direct link (hardlink, then symlink, then copy), no capture step                                                                                                                                                                                                                                      |
| Loose files                   | Drop-in override                                                                                                                                                       | Direct link, no capture step                                                                                                                                                                                                                                                                          |
| Texmod (.tpf) — **deferred**  | Run-time hook, never touches the disk                                                                                                                                  | Out of scope for now. Later: no file management. Only a launcher wrapper that tracks which package to inject.                                                                                                                                                                                         |
| Binary mod — endscript-driven | One or more `VERSN1` manifests, each with a `VERSN2` script. Options come from sibling manifests (multi-select), `combobox` (single-select), and `checkbox` (boolean). | Discover variants, load through `Launch` and `BaseProfile`, parse through `EndScriptParser`, drive `EndScriptManager.ProcessScript()` and answer option pauses from stored selections, then call `profile.Save()` against staging. **This is the main path. It covers both example mods completely.** |

The speculative "asset replacement" row is **removed**. The command vocabulary holds `update_texture`, `replace_texture`, `add_or_replace_texture`, `bind_textures`, `import`, `import_all`, `pack_stream`, and `unpack_stream`. Asset replacement is therefore expressible in `.end` and flows through the same endscript-driven backend. No known mod behavior needs a separate binary-diff path.

### Fallback capture pipeline — probably unnecessary, keep only as an escape hatch

Nikki handles the containers, and the command vocabulary covers textures, streams, and file operations. **No known mod behavior needs capture by diff.** Do not build this. If a mod appears that the libraries cannot express, the pipeline would take this shape:

1. **Vanilla baseline**: content-hash every file, not size and date. Also keep full byte copies of the container files.
2. **Staging install**: a working copy that we can reset. Use hardlinks or block cloning where they are available, and a full copy otherwise.
3. **Capture**: reset staging, apply the mod, then diff by content hash. The delta becomes the payload of the mod.
4. **Deploy**: merge the file trees in load order over vanilla, link them into the game folder, and keep backups for a clean revert.

Steps 1, 2, and 4 are worth building anyway. **The main path also needs staging and backup/revert.** Only step 3, the diff-based capture, is speculative.

### Conflict resolution

- **For script-driven mods**, the conflict key is **`(targetFile, keyPath)` from the resolved flat command list**. An example key is `(GLOBAL\GLOBALB.LZC, [CarTypeInfos, PEUGOT, PlayerCamera, PLAYER_CAMERA_FAR, CameraAngle])`. Two enabled mods that write **different** values to the same key have a real conflict. Two mods that write the **same** value to the same key are benign, and the tool must not report them. This case happens legitimately, for example when the `ALL` variant of 1 Lap overlaps `URL`. Compare file paths and key segments case-insensitively. Normalize the separators first.
- **Do not** base conflict detection on the `Files` list of the manifest, which is a load-set superset and not an edit list. **Do not** base it on `Links`, which is identical per-game boilerplate. Both were the original plan, and both would produce mass false positives. See the manifest breakdown above.
- The resolution UI needs a per-conflict "which mod wins" control and a global load order. The order in which we apply the scripts of the mods in one profile load realizes the decision. The last write wins.
- **Container-level merging is no longer a problem.** All enabled mods run their scripts against one loaded `BaseProfile` before one `Save()`. Edits therefore composite at the collection and entry level on their own. There is never a whole-file overwrite where one mod wins. This removes what was the hardest planned subsystem. One rule keeps it that way: **apply all enabled mods in a single load, apply, and save pass.** One pass per mod would bring the problem back.

## Known risks and open questions

**The `example_mods` survey and the upstream source read closed these questions.** They need no more research: `VERSN1` and `VERSN2` are different file types. The command vocabulary has 48 entries. The `eUsage`, `eLoaderType`, `ePathType`, and `GameINT` enums are known. The grammar and semantics of `combobox` and `checkbox` are known. `Directory` is the game install directory. `Files` is a load-and-verify set, not an edit list. `Links` is per-game loader boilerplate. Nikki implements the `.LZC` and `.BUN` container format for all six BlackBox titles. The exact behavior of the tokenizer is known. **Binary has a CLI. See the fallback section.** **The source of the `mainkeys/<game>.txt` hash lists is settled: we read them from the Binary 2.8.3 install of the user and never redistribute them.**

**Still open, in rough priority order:**

1. **Retarget the three libraries from `netcoreapp3.1` to a current .NET version.** We expect mechanical work. This is the first real integration task, and everything else waits on it. Verify that the `unsafe` code and the P/Invoke still behave. Verify that container round-trips stay byte-identical before and after.
2. **Behavioral verification under Wine.** Test both the managed libraries and the `LZCompressLib.dll` P/Invoke. Do this early. It is the one remaining assumption that a code read cannot answer.
3. **License confirmation for `LZCompressLib.dll`.** It is a closed-source native blob inside an MIT repo, with no separate license text and no public source. The MIT grant of Nikki almost certainly covers it. We must ship our own x64 copy, because the copy in the Binary distribution is 32-bit. The question is therefore live. One email to MaxHwoy settles it.
4. **What a `.bacc` file holds.** This is the last unanswered question about the Binary install. A vanilla game directory holds none, so Binary creates them on first edit. Grep the upstream source for `bacc`, or run Binary once against a scratch copy. The rest of the install layout is confirmed in `docs/roadmap/00-test-environment.md`.
5. **Symlink permissions under Wine.** Windows normally needs `SeCreateSymbolicLinkPrivilege`, which means admin rights or Developer Mode. Enforcement in the ntdll of Wine varies by build. Confirm hardlink and symlink behavior on the Wine and Proton builds that we target. This affects the ASI and loose-file MVP.
6. **Which of the 48 commands need first-class UI and conflict handling.** Our two example mods use 5. Commands such as `if`, `static`, `generate`, `create_file`, `erase_file`, `unlock_memory`, and `speedreflect` have side effects outside the collection model. Conflict detection keyed on `(targetFile, keyPath)` does not cover them. Read `Endscript/Commands/` and classify each command by what it touches. Treat unclassified commands as opaque and warn. Do not assume that they are conflict-free.
7. **The `.bacc` backup-file convention.** `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit next to their originals in the install listing. They are probably the pre-edit backups of Binary. A grep for `bacc` in the upstream source now answers this cheaply. No byte inspection is needed. Decide whether to reuse the mechanism or to keep our backups fully independent. Either way, our snapshot step must not treat `.bacc` files as game content.
8. **A wider manifest and script sample** for Most Wanted, Carbon, and ProStreet. This would validate the per-game `Links` boilerplate assumption and exercise commands that the U2 mods do not use. This is lower priority now, because the enums come from the source and not from inference.

## Suggested roadmap

Format research is **done**. The upstream libraries **solve the container problem**. The remaining order of work:

1. **Fork and retarget the three MIT libraries** (`Nikki`, `Endscript`, `CoreExtensions`) to a current .NET version. Get them to build and package with the native DLL. Prove the result end to end with a throwaway console harness. The harness applies one `example_mods` manifest to a scratch copy of the game and produces a working `GLOBALB.LZC`. **This is the single highest-value first step. It de-risks the whole project in one move.** Verify under Wine here, not later.
2. **Build the Binary install discovery and validation** (open question 3). Ask for the path, validate it, store it, and wire the hash lists into the profile statics. Also write the license notices. This work is small, but it gates anything that we distribute and everything that touches a container.
3. **Build our layer over Endscript**: variant discovery from sibling `VERSN1` files, an option model for `combobox` and `checkbox` with persisted selections, non-interactive answers to the `ProcessScript()` pauses, and resolved-command extraction for conflict detection. All of this is testable against `example_mods` with no UI.
4. **Build the MVP shell** for ASI/DLL and loose-file mods. This covers MVVM scaffolding, game detection, profiles, the load order UI, the link-deploy engine (hardlink, then symlink, then copy), and staging with backup and revert. Smoke-test under Wine continuously.
5. **Add Binary mod deployment.** Wire step 3 into the UI: variant multi-select, option controls, the conflict list, a single load, apply-all, and save pass against staging, an atomic swap, and revert.
6. **Add game profile support** for the path, registry, and executable differences across Underground 2, Most Wanted, Carbon, and ProStreet. Nikki already covers all four plus UG1 and Undercover, so this is our own plumbing only.
7. **Harden command classification** (open question 6). Handle commands outside the collection model properly.
8. **Add Texmod and `.tpf` support.** This step is explicitly last and explicitly optional. No earlier step may depend on it.

## Success criterion for the first Binary-capable build

Both mods in `example_mods` install correctly, together, from our UI, and Binary never runs:

- The tool imports the camera mod. Its `combobox` appears in our UI as a two-option single-select with the caption `"Choose option you needeed"`. The stored selection answers the option pause of `EndScriptManager.ProcessScript()` with no prompt. The run executes 450 or 744 `update_collection` commands.
- The tool imports the 1 Lap mod as one mod with five multi-select variants. A user enables `URL` and `CIRCUIT`, which executes 53 plus 51 `update_incareer` commands. The tool reports the `ALL` overlap case as benign.
- With both mods enabled, the tool reports no false conflict, because the key paths are disjoint: `CarTypeInfos` against `GCareers`. The tool applies both in **one** load, apply-all, and save pass against staging. `manager.Errors` is empty. The tool swaps the result into the game folder atomically and reverts to vanilla cleanly afterward.
- The game launches and both mods are visibly in effect. The camera behavior changes and career races run one lap.
