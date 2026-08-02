# Step 2 — Binary install discovery

The application requires an existing Binary 2.8.3 install. It does not bundle Binary. This step finds that install, validates it, and wires its data files into the libraries.

**Why this exists:** the libraries need per-game hash lists at run time. Those files ship with Binary, not with the MIT libraries. Reading them from the install of the user removes any need to redistribute them.

## Investigate first

The design below assumes a layout that nobody has verified. Answer these against a real 2.8.3 install before you write code.

1. Where do `mainkeys` and `userkeys` sit relative to `Binary.exe`?
2. What is the exact file name per game? The profile classes expect one file per game.
3. How do you read the version? Check the file version resource of `Binary.exe` first. Check for a version file second.
4. Does the install ship `LZCompressLib.dll`? A yes removes the last redistribution question. See step 4 of the open questions in the brief.
5. What does a `.bacc` file hold? These sit beside the game containers, not in the Binary install. Grep the upstream source for `bacc` to answer this cheaply.

Record the answers in this file. Do not leave them as assumptions.

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

The exact file names come from the investigation above. One pair of statics exists per game class. The properties are `static`, so they are process-global.

## Pitfalls

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
