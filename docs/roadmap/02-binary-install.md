# Step 2 — Binary install discovery

The application requires an existing Binary 2.8.3 install. It does not bundle Binary. This step finds that install, validates it, and wires its data files into the libraries.

**Why this exists:** the libraries need per-game hash lists at run time. Those files ship with Binary, not with the MIT libraries. Reading them from the install of the user removes any need to redistribute them.

## Layout — confirmed

A real 2.8.3 install answered the layout questions. See [00-test-environment.md](00-test-environment.md) for the evidence.

- `mainkeys` sits directly beside `Binary.exe`. The path is `<root>/mainkeys/<game>.txt`.
- The file names are lowercase `GameINT` names: `underground1.txt`, `underground2.txt`, `mostwanted.txt`, `carbon.txt`, `prostreet.txt`, `undercover.txt`.
- Read the version from the `Binary.dll` assembly version, which reads `2.8.3.0`.
- A fresh install has **no** `userkeys` directory. Binary creates it on demand. Never require it and never read it.
- The install ships `LZCompressLib.dll`, but it is the **32-bit** build. We build x64, so we cannot use it. See the pitfalls below.

**`.bacc` belongs to the Binary application, not to the libraries.** A grep over all three MIT repositories finds no hit. The string `.bacc` sits in `Binary.dll`, so the GUI of Binary creates those files. Nikki and Endscript never read or write one. The consequence for us: our snapshot step must ignore `.bacc` files, and we cannot reuse the mechanism through the libraries. What a `.bacc` file holds is still unknown, and only a Binary run against a scratch copy can answer that.

## Work

1. Add a settings store. Use a JSON file under `%APPDATA%\BlackBoxModManager\`. Do not use the registry for our own settings.
2. Add a first-run prompt that asks for the Binary install directory.
3. Write a validator. It returns a typed result, not a boolean. The user needs to know which check failed.
4. Try to find the install automatically before you prompt. Check the registry uninstall keys. Check the common install directories. Treat a hit as a suggestion that the user confirms, never as a silent answer.
5. Write the resolver that maps a `GameINT` to a `mainkeys` path under the stored root.
6. Set `MainHashList` and `CustomHashList` on the correct profile class before any `Load` call.

## The validator

Check these in order. Stop at the first failure and name it.

1. The directory exists.
2. `Binary.exe` exists inside it.
3. The version is 2.8.3. A different version is a warning, not a hard stop. Our expectations come from that release, so say so and continue.
4. A `mainkeys` file exists for each supported game.

## Wiring the statics

```csharp
Underground2Profile.MainHashList   = Path.Combine(binaryRoot, "mainkeys", "underground2.txt");
Underground2Profile.CustomHashList = Path.Combine(ourAppData, "customkeys", "underground2.txt");
```

One pair of statics exists per game class. The properties are `static`, so they are process-global.

## Pitfalls

**Do not take `LZCompressLib.dll` from the Binary install.** The shipped copy is 32-bit. Our build is x64. The two files share a name and differ in every other way, including their MD5 sums. Ship the x64 copy from the Nikki repository instead. This keeps the license question for that DLL open. See [00-test-environment.md](00-test-environment.md).

**Do not require a `userkeys` directory.** A fresh install has none. Treating its absence as a validation failure rejects a perfectly good install.

**`userkeys` is output, not input.** `SaveHashList` generates those files. It writes every label that the `mainkeys` file does not already list. Never read them as a data source.

**`CustomHashList` must point at a path we own.** `BaseProfile.Save()` calls `SaveHashList()`, which creates the parent directory and overwrites the file. Pointing it into the Binary install makes us write into a directory that belongs to another application. Point it under `%APPDATA%\BlackBoxModManager\`. See defect 7.

**A null `CustomHashList` throws inside `Save`, not inside your setup code.** `Path.GetDirectoryName(null)` fails at the end of a long operation, after the containers already wrote. Validate both statics before you start a deploy.

**The statics are global, so profile switching leaks.** Setting them for Underground 2 and then loading a Most Wanted profile leaves the wrong paths in place on the wrong class. Set the pair for the target game immediately before every `Load`.

**Never run two deploys at once.** `LoadHashList` calls `Map.ReloadBinKeys()`, which resets global state in Nikki. Serialize all library access behind one lock. See defect 8.

**Do not treat a found path as confirmed.** A registry hit can point at an uninstalled or moved directory. Always run the full validator against whatever you found.

**Re-validate on every launch, not only on first run.** The user can move or delete the install between sessions. A stale stored path must produce a clear message, not a crash inside the libraries.

**Block, do not degrade.** When the install is missing, disable the Binary mod features and say why. ASI and loose-file mods do not need Binary and must keep working. Never fall back to a guessed path.

**Reading the version from a file name is unreliable.** Users rename directories. Read the version resource from the executable.

## Done when

The application finds or accepts a Binary install, validates it, stores the path, and drives the step 1 harness with no hardcoded paths left.

## Results

Step 2 is done. `src/BlackboxModManager.Core` holds the first application code. The harness drives it, and no Binary path is hardcoded anywhere.

| Type | Holds |
| ---- | ----- |
| `AppPaths` | The directories that we own. Every path that we write to resolves through here. |
| `Settings`, `SettingsStore` | The JSON settings file. A damaged file gives fresh settings and never throws. |
| `BinaryInstallValidator` | The four checks, in order. Returns `BinaryInstallStatus`. |
| `BinaryInstallStatus`, `BinaryInstall` | The typed result. Only a passing status can produce a `BinaryInstall`. |
| `BinaryInstallLocator` | The registry scan and the common directory scan. Suggestions only. |
| `BinaryInstallService` | Joins the three. Resolves override, then settings, then candidates. |
| `HashListPaths` | Maps a `GameINT` to its two hash list paths. |
| `ProfileHashLists` | Sets the static pair on the profile class of one game. |
| `LibraryGate` | One lock for all library access. See defect 8. |

`tests/BlackboxModManager.Tests` covers all of it against a fake install in a temporary directory. No test reads the real install. 36 tests pass.

### Three findings

**Console.ReadLine never returns on a Wine console.** A minimal test program shows the same behavior, so this is Wine and not our code. The console echoes the typed line and the read never completes. **There is therefore no first-run prompt in the harness.** Work item 2 cannot be met in the only environment that the harness runs in. The `--set-binary` command answers the question instead, and the error text names the exact command to run. **The real first-run question belongs to step 5, as a dialog.** Do not build it on `Console.ReadLine`.

**Automatic discovery works on this machine.** The common directory scan found `C:\users\peti\Downloads\Binary_v2.8.3` with no help. The registry scan found nothing, which matches expectation, because Binary ships as a directory that the user unpacks. Keep the registry scan. It costs little.

**Read the version from `Binary.dll`, not from `Binary.exe`.** `Binary.exe` is only the .NET host. Its version resource reads as empty under a Unix run of .NET, so a read of the host gives a false negative. `Binary.dll` gives `2.8.3.0` on both Wine and native Linux. The validator reads the assembly first and falls back to the host.

### The open `userkeys` question is answered

`SaveHashList` writes the same content that Binary writes. The `userkeys/underground2.txt` of a used Binary install holds 1018 labels. Our `customkeys/underground2.txt` holds the same 1018 labels after one deploy. The two files match exactly. This confirms three things at once. `userkeys` is generated output. Our redirect to `%APPDATA%` loses nothing. Never read those files as input.

A used install also holds five empty `userkeys` files, one per game that Binary never edited. A reader that treats `userkeys` as a data source would therefore get nothing at all for those games.
