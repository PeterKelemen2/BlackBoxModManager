# Step 5 — MVP shell

Build the application around the simplest mod types. ASI plugins and loose files are drop-in files with no capture step and no container work.

**Why these first:** they exercise profiles, load order, staging, deployment, and revert without touching Nikki. A working manager for simple mods is useful on its own, and step 6 reuses all of its plumbing.

## Work

### 5.1 Scaffolding

1. Create the WPF application targeting `net10.0-windows`. The libraries stay on plain `net10.0`.
2. Add CommunityToolkit.Mvvm. Do not add Prism unless a need appears.
3. Set `InvariantCulture` in the entry point, before anything else runs.
4. Set `<PlatformTarget>x64</PlatformTarget>`.

### 5.2 Game detection

1. Find installs through the registry with `Microsoft.Win32.Registry`.
2. Let the user browse for a path when detection fails.
3. Validate a candidate directory by checking for known game files.
4. Store the confirmed path per game.

### 5.3 Mod import

1. Extract archives. Use `System.IO.Compression` for zip. Use `SharpCompress` for rar and 7z.
2. Import into a managed mod store outside the game directory.
3. Classify each mod by type. Detect `.asi` files. Detect `VERSN1` manifests. Treat the rest as loose files.

### 5.4 Profiles and load order

1. A profile holds the enabled mod set, the load order, and every option selection.
2. A profile must fully determine the deployed result, with no prompting.
3. Support several profiles per game.

### 5.5 Deploy engine

1. Implement a link deployer with three strategies, tried in order: hardlink, symlink, copy.
2. Use `CreateHardLinkW` and `CreateSymbolicLinkW` through direct P/Invoke.
3. Record which strategy succeeded, so the UI can explain a slow deploy.
4. Deploy in load order. A later mod overrides an earlier one.

### 5.6 Staging and revert

1. Snapshot the vanilla state before the first deploy. Hash the content. Do not use size and modification time.
2. Build the staging copy. Use hardlinks or block cloning where available. Fall back to a full copy.
3. Apply to staging. Verify. Then swap into the game folder.
4. Keep enough state to revert to vanilla cleanly.

## Pitfalls

**Never write to the live install.** Apply to staging, verify, then swap. This rule holds for every mod type, including the simple ones in this step.

**Do not identify files by size and modification time.** Archive extraction resets timestamps. Hash the content. Use `System.IO.Hashing` with XxHash for internal diffing.

**Ignore `.bacc` files during snapshot.** `GLOBAL/GLOBALA.BUN.bacc` and `GLOBAL/GLOBALB.LZC.bacc` sit beside the real files in a real install. They are the backup bookkeeping of Binary, not game content. Treating them as content corrupts the vanilla baseline.

**Hardlinks cannot cross volumes.** A mod store on a different drive from the game silently falls through to copy. Report the strategy so a user can understand the disk use.

**Hardlinks share content.** Editing a deployed file edits the file in the mod store too. This matters for loose files that a mod expects to stay pristine. Copy where an edit is possible.

**Symlinks need a privilege on Windows.** `SeCreateSymbolicLinkPrivilege` means administrator rights or Developer Mode. The fallback chain exists for this reason. Use the Wine results from step 3 for the Linux target.

**Test under Wine continuously, not at the end.** Every deploy strategy in this step has a different Wine story.

**Set the culture before any parsing.** A comma-decimal locale corrupts float values. Binary itself forces `en-US` on its main thread. Do not inherit the right behavior by luck.

**Keep the deploy engine behind an interface.** Step 6 adds a container-based deployment that shares staging, backup, and revert but not the link strategy.

## Done when

The application detects a game, imports ASI and loose-file mods, orders them, deploys them through the link engine, and reverts to vanilla cleanly. All of it verified under Wine.
