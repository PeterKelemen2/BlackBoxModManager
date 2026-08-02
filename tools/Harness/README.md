# Harness

The step 1 console harness. It applies one `VERSN1` manifest to a scratch copy of Underground 2.

**Throw this away after step 3.** It exists to answer one question: do the libraries work. Do not grow it into the application. See [docs/roadmap/01-console-harness.md](../../docs/roadmap/01-console-harness.md).

## Run it

The harness needs a Binary 2.8.3 install. Point it at one once. The path goes into the settings file and every later run reads it from there.

```sh
tools/run-harness.sh --show-binary
tools/run-harness.sh --set-binary 'C:\users\you\Downloads\Binary_v2.8.3'
```

`--show-binary` reports what the locator found. Give one of those paths to `--set-binary`. The path is a Windows path, because the harness runs under Wine.

Then apply a mod.

```sh
tools/run-harness.sh "example_mods/NFSU2 - 1 Lap URL And Other Races v2.0/1 Lap URL Races.end"
tools/run-harness.sh "example_mods/3822ca-NFSUG2 - Camera MOD MW to U2 ver.1.0/Install.end" --choice 0
```

The script publishes a self-contained `win-x64` build and starts it under Wine. It converts the manifest path with `winepath`. Every other argument goes to the harness without a change.

The harness exits 0 only when the load errors, the script errors, and the save errors are all empty.

## Why Wine

Two reasons. Both are hard blocks, not preferences.

1. `LZCompressLib.dll` is a native Windows x64 library. Nikki calls it through P/Invoke to read and write the containers.
2. The manifests declare `GLOBAL\GLOBALB.LZC`. The file on disk is `GLOBAL/GlobalB.lzc`. Wine resolves the separator and the case. Native .NET on a case-sensitive filesystem does not, and `CheckFiles` throws `FileNotFoundException` for a file you can see.

## Options

| Option                 | Purpose                                                           |
| ---------------------- | ----------------------------------------------------------------- |
| `--manifest <path>`    | The manifest to apply. Required.                                  |
| `--choice <n[,n...]>`  | One answer for each option pause, in order.                       |
| `--game <dir>`         | The vanilla install to copy. The harness reads it only.           |
| `--scratch <dir>`      | The scratch copy. The harness deletes this on every run.          |
| `--binary <dir>`       | The Binary install for this run only.                             |
| `--main-keys <file>`   | Overrides the `mainkeys` list that the install gives.             |
| `--custom-keys <file>` | Overrides the hash list output path.                              |
| `--skip-copy`          | Keeps the scratch copy from the last run. Use this to iterate.    |
| `--count-only`         | Parses the script, reports the counts, and stops. Writes no file. |

Three more commands manage the Binary install. Each one does its work and stops. None of them takes a manifest.

| Command               | Purpose                                                       |
| --------------------- | ------------------------------------------------------------- |
| `--show-binary`       | Reports the install, the candidates, and the resolved paths.  |
| `--set-binary <dir>`  | Validates a directory and stores it in the settings file.     |
| `--forget-binary`     | Removes the stored directory.                                 |
| `--probe <dir>`       | Tests links, letter case, and the backslash separator.        |

The `--game` and `--scratch` defaults hold the paths of one developer machine. They come from [00-test-environment.md](../../docs/roadmap/00-test-environment.md). They are Wine drive `Z` paths. The Binary path is no longer a default. Step 2 replaced it with discovery.

The harness never writes to `--game`. It copies that directory to `--scratch` and edits the copy.

## There is no prompt

`Console.ReadLine` never returns on a Wine console. The harness therefore asks nothing. Run `--set-binary` once. The real first-run question belongs to the UI of step 5, as a dialog.

## What the harness writes

Everything except the scratch copy goes under `%APPDATA%\BlackBoxModManager\`.

| Path                    | Holds                                                        |
| ----------------------- | ------------------------------------------------------------ |
| `settings.json`         | The stored Binary install directory.                         |
| `customkeys\<game>.txt` | The `CustomHashList` output of `Save`. See defect 7.         |
| `logs\MainLog.txt`      | The log that Nikki writes to the working directory. Defect 9. |

The harness never writes into the Binary install.

## Environment overrides for the wrapper

| Variable             | Default                                                     |
| -------------------- | ------------------------------------------------------------ |
| `HARNESS_RUNNER`     | `wine`. Give the path to another wine to test that build.   |
| `HARNESS_WINEPREFIX` | `~/.local/share/blackbox-harness-wine`                      |
| `HARNESS_OUT`        | `artifacts/harness`                                         |
| `HARNESS_SINGLEFILE` | `0`. Set `1` for a single-file publish.                     |
| `HARNESS_QUIET`      | `1`. Set `0` to keep the graphics driver noise of Wine.     |
| `WINEDEBUG`          | `-all`                                                      |

## Running under Proton

Point `HARNESS_RUNNER` at the wine of the Proton build and `HARNESS_WINEPREFIX` at the game prefix.

```sh
HARNESS_RUNNER=~/.config/heroic/tools/proton/GE-Proton10-34/files/bin/wine \
HARNESS_WINEPREFIX=/mnt/Data/Games/WinePrefixes/NFSU2ModTest \
HARNESS_SINGLEFILE=1 \
tools/run-harness.sh "example_mods/NFSU2 - 1 Lap URL And Other Races v2.0/1 Lap URL Races.end" \
	--binary 'Z:\home\peti\Downloads\Binary_v2.8.3'
```

**Never mix Wine builds in one prefix.** A wineserver of the wrong version makes every later call fail with `version mismatch`. The wrapper puts the directory of the runner first on `PATH` to keep `wine` and `wineserver` together. Stop a stale server with the matching `wineserver -k`.

The settings file lives inside the prefix, under `%APPDATA%`. Each prefix therefore needs its own `--set-binary`. Use `--binary` instead to avoid storing anything.
