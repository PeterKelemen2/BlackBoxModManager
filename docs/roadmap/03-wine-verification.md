# Step 3 — Wine verification

Confirm that the harness from step 1 runs under Wine. This is the one remaining assumption that no amount of code reading can answer.

**Do this early.** A failure here changes the packaging plan and possibly the platform decision. A failure found at step 6 wastes all the UI work.

## What is actually at risk

Three things, in descending order of risk.

1. **The `LZCompressLib.dll` P/Invoke.** This is a native x64 PE DLL with no source. Nikki calls `BlockCompress` and `BlockDecompress` from `Nikki/Utils/Interop.cs`. Container compression needs it. Wine runs PE binaries natively, so this should work. Nothing proves it until it runs.
2. **Symlink and hardlink creation.** Windows normally needs `SeCreateSymbolicLinkPrivilege`, which means administrator rights or Developer Mode. Wine enforces this differently across builds. This gates the deploy engine in step 5, not the container work.
3. **File path case sensitivity.** The game files use mixed case, such as `GLOBALB.LZC` against `GlobalB.lzc`. Windows does not care. A Wine prefix on a case-sensitive Linux filesystem does care.

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
