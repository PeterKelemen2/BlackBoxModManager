# Test environment

The machine-specific paths and the facts observed in them. These paths belong to one developer machine. Do not hardcode them in the application. Use them for the harness in step 1 and for the Wine work in step 3.

## Paths

| What                  | Path                                                                                                           |
| --------------------- | -------------------------------------------------------------------------------------------------------------- |
| Vanilla Underground 2 | `/mnt/Data/Games/WinePrefixes/NFSU2ModTest/drive_c/Program Files (x86)/EA GAMES/Need for Speed Underground 2/` |
| Wine prefix root      | `/mnt/Data/Games/WinePrefixes/NFSU2ModTest/`                                                                   |
| Binary 2.8.3          | `/mnt/Data/Games/Binary_v2.8.3/`                                                                               |

The prefix runs GE-Proton-10-34. The game launches from that prefix.

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

**A vanilla install holds no `.bacc` files.** The directory listing confirms this. Binary creates them when it first edits a container. The brief recorded them from a used install. Our snapshot step must still ignore them, because a user can point us at an install that Binary has already touched.

## Open items this environment can still answer

1. What does a `.bacc` file contain? Run Binary once against a scratch copy and inspect the result.
2. Does `userkeys` appear after one Binary run, and does its content match what `SaveHashList` would write?
3. Does the x64 `LZCompressLib.dll` P/Invoke work inside the GE-Proton-10-32 prefix? This is the step 3 question.
