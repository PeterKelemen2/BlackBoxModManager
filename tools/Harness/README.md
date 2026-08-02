# Harness

The step 1 console harness. It applies one `VERSN1` manifest to a scratch copy of Underground 2.

**Throw this away after step 3.** It exists to answer one question: do the libraries work. Do not grow it into the application. See [docs/roadmap/01-console-harness.md](../../docs/roadmap/01-console-harness.md).

## Run it

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
| `--main-keys <file>`   | The `mainkeys` list of Binary for Underground 2.                  |
| `--custom-keys <file>` | The hash list output path. Never point this into Binary.          |
| `--skip-copy`          | Keeps the scratch copy from the last run. Use this to iterate.    |
| `--count-only`         | Parses the script, reports the counts, and stops. Writes no file. |

The defaults hold the paths of one developer machine. They come from [00-test-environment.md](../../docs/roadmap/00-test-environment.md). They are Wine drive `Z` paths. Step 2 replaces the Binary path with discovery.

The harness never writes to `--game`. It copies that directory to `--scratch` and edits the copy.

## Environment overrides for the wrapper

| Variable             | Default                                |
| -------------------- | -------------------------------------- |
| `HARNESS_OUT`        | `artifacts/harness`                    |
| `HARNESS_WINEPREFIX` | `~/.local/share/blackbox-harness-wine` |
| `WINEDEBUG`          | `-all`                                 |
