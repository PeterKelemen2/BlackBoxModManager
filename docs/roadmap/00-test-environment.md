# Test environment

The machine-specific paths and the facts observed in them. These paths belong to one developer machine. Do not hardcode them in the application. Use them for the harness in step 1 and for the Wine work in step 3.

## Paths

| What                  | Path                                                                                                           |
| --------------------- | -------------------------------------------------------------------------------------------------------------- |
| Vanilla Underground 2 | `/mnt/Data/Games/WinePrefixes/NFSU2ModTest/drive_c/Program Files (x86)/EA GAMES/Need for Speed Underground 2/` |
| Wine prefix root      | `/mnt/Data/Games/WinePrefixes/NFSU2ModTest/`                                                                   |
| Binary 2.8.3          | `/mnt/Data/Games/Binary_v2.8.3/`                                                                               |
| Binary 2.8.3, copy 2  | `~/Downloads/Binary_v2.8.3/`                                                                                   |

The machine holds two copies of Binary 2.8.3. Their `Binary.dll` files have the same MD5 sum. The copy under `~/Downloads` is the one that the step 2 locator finds without help. The copy under `/mnt/Data` has never been run, so it holds no `userkeys` directory. The copy under `~/Downloads` has been run and does hold one. Both are useful. Use the untouched copy to test a fresh install and the used copy to test a used one.

The prefix runs GE-Proton10-34. The game launches from that prefix. Heroic manages it. The runner is `~/.config/heroic/tools/proton/GE-Proton10-34/files/bin/wine`, which reports `wine-10.0 (Staging)`. The `pfx` entry inside the prefix is a symbolic link to the prefix itself, so `WINEPREFIX` is the prefix root.

**Copy the game before every harness run.** Never point `launch.Directory` at the path above. It is the vanilla reference.

## Binary install layout — confirmed

This answers the investigation questions in [02-binary-install.md](02-binary-install.md).

**`mainkeys` sits directly beside `Binary.exe`.** The path is `<root>/mainkeys/<game>.txt`.

The six file names are lowercase and match the `GameINT` names:

```
carbon.txt   mostwanted.txt   prostreet.txt
undercover.txt   underground1.txt   underground2.txt
```

`underground2.txt` holds 78151 lines. The first line is empty. Each remaining line is one plain-text label, such as `01_WHEEL_MADCATZ`. `SaveHashList` skips lines that start with `//` or `#`.

**There is no `userkeys` directory in a fresh install.** Binary creates it on demand. An install that has been used holds `userkeys` files that mirror the `mainkeys` names but contain only a subset of the labels. This matches what `SaveHashList` does. It writes every label in `Map.BinKeys` that the `mainkeys` file does not already list. The `userkeys` files are therefore a generated overflow list, not a shipped data file.

**Consequence for us:** never expect `userkeys` to exist, and never read it as input. `CustomHashList` is an output path. Point it under our own application data directory. See defect 7.

**Read the version from `Binary.dll`, not from the directory name.** The assembly version is `2.8.3.0`. `Readme.txt` also states `v2.8.3` on its third line.

## Binary ships a 32-bit stack

**This contradicts the project brief.** The brief states that `LZCompressLib.dll` is x64-only and that `win-x64` is therefore mandatory. The truth is more specific.

Every binary in the Binary 2.8.3 install is i386:

| File                 | Architecture                    |
| -------------------- | ------------------------------- |
| `Binary.exe`         | PE32, Intel i386                |
| `Binary.dll`         | PE32, Intel i386, .NET assembly |
| `Nikki.dll`          | PE32, Intel i386, .NET assembly |
| `CoreExtensions.dll` | PE32, Intel i386, .NET assembly |
| `LZCompressLib.dll`  | PE32, Intel i386                |

The copy checked into the Nikki repository is different. It is PE32+ x86-64. The two files share a name and nothing else. Their MD5 sums differ.

| Source                                      | Architecture | MD5                                |
| ------------------------------------------- | ------------ | ---------------------------------- |
| `third_party/Nikki/Nikki/LZCompressLib.dll` | x86-64       | `be2c1e1085d776b3536f36b8e87cad0e` |
| `Binary_v2.8.3/LZCompressLib.dll`           | i386         | `47298954f5a16ce21e46e9d50034d7c4` |

**Three consequences.**

1. **We cannot source `LZCompressLib.dll` from the Binary install.** We build x64, and the shipped copy is x86. The hope recorded in the project brief is dead. We must ship the x64 copy from the Nikki repository.
2. **The license question stays open.** Because we redistribute that DLL, the question of its license is still live. It does not disappear.
3. **`win-x64` remains mandatory for us**, but for our own reason. We chose the x64 build of the DLL. An x86 build of the whole application is possible in principle, using Binary's DLL. Do not take that path without a strong reason.

This also explains why `CoreExtensions` at the pinned commit carried `PlatformTarget x86`. Upstream targeted 32-bit on purpose. Our change to x64 is correct for our build, and it is a real divergence from upstream, not a cleanup.

**Binary itself needs the .NET Core 3.1 Desktop runtime.** Its `runtimeconfig.json` names `Microsoft.WindowsDesktop.App` version `3.1.0`. This matters only if we ever run `Binary.exe` as a fallback engine. Our own application is self-contained and does not need it.

## Game install facts

The install is at the path in the table above. `SPEED2.EXE` is the executable.

**The container file names do not match the manifest case.** This is a real problem on a case-sensitive filesystem.

| Manifest declares    | On disk              | Match  |
| -------------------- | -------------------- | ------ |
| `GLOBAL\GLOBALA.BUN` | `GLOBAL/GLOBALA.BUN` | Yes    |
| `GLOBAL\GLOBALB.LZC` | `GLOBAL/GlobalB.lzc` | **No** |

`Launch.CheckFiles()` calls `File.Exists(Path.Combine(Directory, file))`. Under Wine the file layer resolves the case, so this works. Under a native Linux .NET run it fails, on both the separator and the case.

**Consequence:** the step 1 harness must run under Wine, or it must resolve paths case-insensitively itself. Do not conclude that the container code is broken when `CheckFiles` throws `FileNotFoundException` for a file you can see in the directory listing.

**The install holds read-only files.** `server.dll` is mode 444. A copy of the install carries the flag across, and a later delete of that copy fails with `UnauthorizedAccessException`. Clear the read-only flag on every file that we copy. Clear it again over the whole tree before we delete a staging copy.

**Nikki writes `GlobalB.lzc` without whole-file compression, and the game accepts it.** The vanilla file is 5,145,778 bytes. After one harness run it is 8,263,472 bytes. `DatabaseSaver.WriteFromBuffer` decompresses the source and writes the assembled blocks straight out. Nikki still compresses single blocks through `Interop.Compress`. Binary behaves the same way. The step 1 acceptance run confirmed that the game loads the result.

**A scratch copy runs as a game.** Start `SPEED2.EXE` in the scratch directory under the Proton prefix. The game runs from any path. A copy over the install is not needed to test a change.

**A vanilla install holds no `.bacc` files.** The directory listing confirms this. Binary creates them when it first edits a container. The brief recorded them from a used install. Our snapshot step must still ignore them, because a user can point us at an install that Binary has already touched.

## Wine console facts

**`Console.ReadLine` never returns on a Wine console.** The console echoes the typed line and the read never completes. A minimal .NET program shows the same behavior, so this belongs to Wine and not to our code. **Never build an interactive prompt on `Console.ReadLine`.** The UI of step 5 must ask its questions in a dialog.

Console output works. `Console.WriteLine` and `Console.Error.WriteLine` both reach the terminal.

**A self-contained `win-x64` publish of .NET 10 runs under system Wine 11.13 and under GE-Proton10-34.** A fresh prefix needs no configuration. Both builds produce byte-identical containers. See [03-wine-verification.md](03-wine-verification.md).

**A Proton build ships no `winepath` program.** Its `files/bin` holds `wine`, `wine64`, `wineserver`, and `msidb`, and nothing else. Convert a path with `wine winepath.exe -w`, using the same build. A call to the `winepath` program falls through to system Wine and starts a wineserver of the wrong version in the prefix.

**Never mix Wine builds in one prefix.** The symptom is `wine client error:0: version mismatch 956/864`. It names neither the cause nor the prefix. Stop the wineserver, then use one build.

## Open items this environment can still answer

1. What does a `.bacc` file contain? Run Binary once against a scratch copy and inspect the result. A grep already proved that no MIT library reads or writes one. The string sits in `Binary.dll`.

### Answered

**Does the x64 `LZCompressLib.dll` P/Invoke work inside the GE-Proton prefix?** Yes. It works on GE-Proton10-34 and on system Wine 11.13. Both builds write the same bytes. The prefix uses GE-Proton10-34, not the 10-32 that an earlier note named.

**Does `userkeys` appear after one Binary run, and does its content match what `SaveHashList` would write?** Yes to both. The `userkeys/underground2.txt` of the used install holds 1018 labels. One deploy through our own code wrote the same 1018 labels to `customkeys/underground2.txt`. The two match exactly. The used install also holds five empty `userkeys` files, one per game that Binary never edited.
