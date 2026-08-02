# Step 1 — Console harness

Prove the whole library pipeline end to end, with no UI. This is the highest-value step in the project. It de-risks everything else.

**Goal:** apply one manifest from `example_mods` to a scratch copy of Underground 2 and produce a changed `GLOBALB.LZC` that the game accepts.

**Throw the harness away later.** Do not grow it into the application. It exists to answer one question: do the libraries work.

## Prerequisites

- A vanilla Underground 2 install that you can copy. Never point the harness at the install you play.
- A Binary 2.8.3 install, for the `mainkeys` hash list. Hardcode the path for this step. Step 2 replaces it with discovery.
- The three submodules building. See the README.

Both paths for this machine sit in [00-test-environment.md](00-test-environment.md). Read that file first. It also records two facts that will otherwise cost you an afternoon: the container file case does not match the manifest, and the shipped `LZCompressLib.dll` is the wrong architecture.

**Run the harness under Wine, not on native Linux .NET.** The manifests declare `GLOBAL\GLOBALB.LZC` and the file on disk is `GLOBAL/GlobalB.lzc`. Wine resolves that case difference. Native .NET on a case-sensitive filesystem does not, and `CheckFiles` throws `FileNotFoundException` for a file you can see.

## Work

1. Create `tools/Harness/Harness.csproj` as a console project targeting `net10.0`.
2. Add a `ProjectReference` to `third_party/Endscript/Endscript/Endscript.csproj`. That reference pulls in Nikki and CoreExtensions.
3. Set `<PlatformTarget>x64</PlatformTarget>`. `LZCompressLib.dll` is x64-only.
4. Set the culture as the first statement in `Main`. Write `CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture`. Do the same for `DefaultThreadCurrentUICulture`. Script floats such as `-0.19500002` parse wrong under a comma-decimal locale.
5. Copy the vanilla game to a scratch directory at startup. Delete and re-copy on every run. The harness must start from a known state.
6. Follow the call order in [99-api-notes.md](99-api-notes.md).
7. Print every string in the `Load` result array. Print every string in the `Save` result array. Print every `EndError` in `manager.Errors`. Do not swallow any of them.
8. Answer the option pause from a command line argument, not from `Console.ReadLine`. The harness must run without a human.
9. Return exit code 0 only when `manager.Errors` is empty and both error arrays are empty.

## Verification

Run these four checks in order. Each one answers a different question.

1. **Manifest round trip.** For every `VERSN1` file in `example_mods`, call `Launch.Deserialize` and then `Launch.Serialize` to a temporary path. Compare the bytes against the original. This proves that the backslash dialect survives our retarget. Keep this as a permanent test, not as harness output.
2. **The 1 Lap mod, URL variant.** Apply `1 Lap URL Races.end`. Expect 53 `update_incareer` commands. Expect no errors.
3. **The camera mod, either branch.** Apply `Install.end` with choice 0 and then with choice 1. Expect 744 commands for one branch and 450 for the other. Expect no errors.
4. **The game runs.** Copy the scratch directory over a real install and launch it. Confirm that career races run one lap, or that the camera moved. A clean save proves nothing on its own — the container must still load in the game.

Check 4 is the one that matters. Checks 1 to 3 can all pass while the written container is unreadable by the game.

## Pitfalls

**Set `ThisDir` after every `Deserialize`.** The property carries `[JsonIgnore]`, so it stays null. Every relative path resolves through it. See defect 2.

**Set both hash list statics before `Load`.** `Load` calls `LoadHashList` as its first step. A null `MainHashList` fails there, not at the point where you forgot the assignment.

**Never point `CustomHashList` into the Binary install.** `Save` writes that file and creates its directory. Point it at a scratch path for the harness. See defect 7.

**Set `Usage` to `Modder`.** The example manifests ship as `User`. `Modder` is the automation mode, and it requires `Directory` to hold a real path.

**Point `Directory` at the scratch copy, never at a real install.** This is the single most destructive mistake available in this step. The libraries edit containers in place.

**Expect `Files` to name containers the script never edits.** All four `1 Lap` manifests declare `GLOBALA.BUN`, but every command targets `GLOBALB.LZC`. Both files must exist under `Directory` or `CheckFiles` throws.

**Validate `Choice` before you set it.** An out-of-range value produces the message "Unable to find end to a selectable statement", which names neither the file nor the real problem. See defect 5.

**Call `CommandChase()` before the first `ProcessScript()`.** Without it the jump targets stay unresolved and every selectable command fails.

**Do not call `ProcessScript()` again without setting `Choice`.** The manager tracks a waiting flag. A re-entry that ignores the pause consumes the flag and walks the wrong branch.

**A script can apply and still produce errors.** Check `manager.Errors` even when `ProcessScript` returned `true`. Treat any entry as a failed deploy.

**`Load` returns an empty array when `Files` is empty.** It loads nothing and reports nothing. An empty result is not proof of success.

**Watch for a silent P/Invoke failure.** `LZCompressLib.dll` must sit beside the harness executable. Confirm that it is there before you blame the container code.

## Done when

The harness applies both example mods to a scratch copy, reports no errors, and the game launches with the mods visibly in effect.
