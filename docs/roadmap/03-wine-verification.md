# Step 3 — Wine verification

Confirm that the harness from step 1 runs under Wine. This is the one remaining assumption that no amount of code reading can answer.

**Do this early.** A failure here changes the packaging plan and possibly the platform decision. A failure found at step 6 wastes all the UI work.

## What is actually at risk

Three things, in descending order of risk.

1. **The `LZCompressLib.dll` P/Invoke.** This is a native x64 PE DLL with no source. Nikki calls `BlockCompress` and `BlockDecompress` from `Nikki/Utils/Interop.cs`. Container compression needs it. Wine runs PE binaries natively, so this should work. Nothing proves it until it runs.
2. **Symlink and hardlink creation.** Windows normally needs `SeCreateSymbolicLinkPrivilege`, which means administrator rights or Developer Mode. Wine enforces this differently across builds. This gates the deploy engine in step 5, not the container work.
3. **File path case sensitivity.** This is no longer a risk. It is a confirmed problem. The manifests declare `GLOBAL\GLOBALB.LZC`. The file on disk is `GLOBAL/GlobalB.lzc`. Wine resolves the case for us. A native Linux .NET run does not, and `CheckFiles` throws for a file you can see in the listing. See [00-test-environment.md](00-test-environment.md).

The test prefix is GE-Proton10-34. Record results against that build. Heroic manages it. The runner is `~/.config/heroic/tools/proton/GE-Proton10-34/files/bin/wine`, which reports `wine-10.0 (Staging)`. The `pfx` entry inside the prefix is a symbolic link to the prefix itself, so `WINEPREFIX` is the prefix root.

## Work

1. Publish the step 1 harness as `win-x64`, self-contained, single file. Use `-p:IncludeNativeLibrariesForSelfExtract=true`.
2. Copy the published harness into the Wine prefix that holds the game.
3. Run every step 1 verification check under Wine.
4. Compare the written `GLOBALB.LZC` byte for byte against the file that the same harness produced on Windows or on a plain Linux .NET run. A difference here is a real defect, not a platform quirk.
5. Test hardlink creation. Test symlink creation. Test the copy fallback. Record which ones work on your target Wine and Proton builds.
6. Launch the game inside the prefix and confirm the mods took effect.

## Pitfalls

**`IncludeNativeLibrariesForSelfExtract=true` is mandatory, not optional.** Without it, single-file publishing embeds `LZCompressLib.dll` in a way that the P/Invoke cannot resolve. The code loads that DLL by name, so it must exist as a real file next to the host at run time. The failure appears as a `DllNotFoundException` deep inside a container operation.

**`win-x64` is mandatory.** `LZCompressLib.dll` is x64-only. Confirm this with `file` if you doubt it. An `AnyCPU` or x86 build fails at the first compression call.

**Self-contained publishing is the point.** Linux users must not need `winetricks dotnet` inside their game prefix. A framework-dependent build forces exactly that.

**Test on the Wine build the users have, not the newest one.** Proton versions differ from plain Wine. Symlink behavior in particular varies by ntdll build. Record the versions you tested.

**A byte-identical container is the real pass condition.** "The harness printed no errors" is weaker. Compression is where a native library difference would show, and a subtly different container can still fail to load in the game.

**Check case sensitivity explicitly.** Create a test that opens `GLOBAL/GlobalB.lzc` when the file on disk is `GLOBAL/GLOBALB.LZC`. If that fails, every path comparison in our own code needs normalization, and the game profile work in step 7 gets harder.

**Do not skip the game launch.** The container can write, verify, and still refuse to load. Only the running game proves the round trip.

## Done when

The published harness runs inside the Wine prefix, produces a byte-identical container, and the game launches with the mods in effect. The link-behavior results are recorded for step 5.

## Results

**Every work item passes. Step 3 is done.**

The game launched from the scratch directory inside the GE-Proton10-34 prefix and the URL career races ran one lap. The container came from the single-file `win-x64` publish, written by the Proton runner. That is the exact shape that users will run.

### The run matrix

Two Wine builds, two publish shapes. All four apply `1 Lap URL Races.end` with no error.

| Run | Runner | Publish | Result | `LZCompressLib.dll` |
| --- | ------ | ------- | ------ | ------------------- |
| 1 | Wine 11.13 | multi-file | Pass | resolved beside the executable |
| 2 | Wine 11.13 | single-file | Pass | resolved from the bundle |
| 3 | GE-Proton10-34 | multi-file | Pass | resolved beside the executable |
| 4 | GE-Proton10-34 | single-file | Pass | resolved from the bundle |

**Every run wrote the same bytes.** `GlobalB.lzc` has MD5 `1d11b99c09c15f57541446b2a4655ad0` in all four. `GLOBALA.BUN` has MD5 `3f1b442c59b9503c0e1b1b52a1c6882f` in all four. The container does not depend on the Wine build or on the publish shape. **This closes risk 1.** The `LZCompressLib.dll` P/Invoke works, and it produces identical output across two independent Wine builds.

No Windows machine was available, and a native Linux run cannot get far enough to write a container. Two Wine builds is the strongest comparison this environment offers.

### Links and paths — risk 2 and risk 3

Probed against the game directory on ext4 with `--probe`.

| Method | Wine 11.13 | GE-Proton10-34 |
| ------ | ---------- | -------------- |
| Hard link | works | works |
| Symbolic link | works | works |
| Copy | works | works |
| Letter case | insensitive | insensitive |
| Backslash separator | works | works |

**No privilege problem appeared on either build.** `SeCreateSymbolicLinkPrivilege` did not block anything. **Step 5 can plan for hard links as the default**, with the symbolic link and the copy as fallbacks. Do not hardcode that choice. Call `LinkSupport.Probe` against the real target directory, because a hard link still fails across filesystems.

A native Linux run of the same probe reports the opposite for paths: case sensitive, and the backslash is not a separator. A test asserts this, so the difference stays visible.

### Two traps that cost time

**A Proton build ships no `winepath` program.** Its `files/bin` holds `wine`, `wine64`, `wineserver`, and `msidb`, and nothing else. A wrapper that calls `winepath` therefore falls through to system Wine, which starts a wineserver of the wrong version in the Proton prefix. Every later call then fails with `wine client error:0: version mismatch 956/864`, which names neither the cause nor the prefix. **Convert paths with `"$runner" winepath.exe -w`**, so the conversion uses the same build.

**Never mix Wine builds in one prefix.** Once a wineserver of the wrong version runs, every call to that prefix fails until the server stops. Put the directory of the runner first on `PATH`, so that `wine` and `wineserver` come from one build.

### One correction to the pitfalls above

The pitfall on `IncludeNativeLibrariesForSelfExtract` says the DLL "must exist as a real file next to the host at run time". That is not what happens. A single-file publish extracts the native libraries to a temporary directory, and the P/Invoke resolves them from there. The file is not beside the executable and the call still works.

The flag is still mandatory. The wording was wrong, and a harness check built on that wording rejected a good build. **Test the resolution, not the file.** `NativeLibrary.TryLoad` against the Nikki assembly answers the real question.

### 2026-08-25: a release is no longer self-contained

Step 16 added the installer and the update check. It changed the shape that users run, so the pitfall above no longer describes a release.

The pitfall reads: **"Self-contained publishing is the point. Linux users must not need `winetricks dotnet` inside their game prefix. A framework-dependent build forces exactly that."** That sentence stays, because it records what step 3 found. Four facts now sit beside it.

1. **A release is framework-dependent, by decision.** The download is small, and `tools/pack.ps1` builds it.
2. **On Windows, `Setup.exe` installs the .NET 10 Desktop Runtime.** Velopack takes the `--framework net10.0-x64-desktop` argument and closes the gap there.
3. **Under Wine it does not close the gap, and this is the cost of the decision.** A Wine user installs that runtime into the prefix, or publishes self-contained from source. `tools/run-app.sh` still does the second one, so the zero-configuration path of step 3 remains available to a developer. **It is no longer what a release gives.**
4. **Whether `Setup.exe` and `Update.exe` run under Wine at all is unverified.** Both are native programs of Velopack, and neither one uses WPF. Step 16 owns that check.

One part is verified already. The managed side of Velopack runs under Wine. `BlackboxModManager.exe --veloapp-install 0.1.0` ran under Wine 11.16, opened no window, exited 0, and wrote its lines to `update.log`.

Also note that `PublishSingleFile` left the build. Velopack wants a directory of loose files and it writes the single distributable itself, so `IncludeNativeLibrariesForSelfExtract` no longer applies to a release. `LZCompressLib.dll` sits beside the host as a normal file, which is the simplest case for the P/Invoke.
