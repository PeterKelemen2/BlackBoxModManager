# Step 16 — the release and the update

## Why

Steps 0 to 15 built an application that a developer can run. They built no way to give it to anybody.

Three gaps closed here.

1. **No version.** No project file named one, so every build reported the SDK default of `1.0.0`. Nothing showed it, so a defect report named no build.
2. **No pipeline.** Every build was a local build. A broken commit reached `main` and nobody knew.
3. **No install and no update.** A user needed the .NET SDK, three submodules, and a publish command. A user who managed all of that had no way to learn about the next release.

This step adds the version, the two GitHub Actions workflows, and a Velopack installer that updates itself.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Update framework | Velopack 1.2.0, MIT | It writes the installer, the feed, and the update logic. Its `net10.0` target carries no dependency, so nothing else reaches the build. |
| Release shape | Framework-dependent `win-x64` | The download stays small. `Setup.exe` installs the runtime. |
| Runner | `windows-latest` for everything | The application is WPF. A Linux runner can build it and cannot run it. |
| Signing | None yet | No certificate exists. SmartScreen warns on the first run, and the README says so. |

## The version

`src/Directory.Build.props` holds it. **That file sits in `src` and not in the repository root, and the placement is load-bearing.** MSBuild walks up from each project directory and stops at the first `Directory.Build.props`. A file in the root would also reach the three forks under `third_party`. It would stamp our version onto them. `THIRD-PARTY-NOTICES.md` pins those three by commit, so each one must keep the version of its own author. A file in `src` reaches the application and the core library, and nothing else.

Verified after the change: `BlackboxModManager.Core` reports `0.1.0`, and `Nikki` still reports `1.6.5`.

**Pass `VersionPrefix` and `VersionSuffix`, and never one `Version`.** `AssemblyVersion` holds four numbers and it cannot hold a prerelease label. The SDK builds `Version` from the two properties and `AssemblyVersion` from the numeric part alone. Two properties make that constraint visible in the build file.

`IncludeSourceRevisionInInformationalVersion` is off. The SDK otherwise appends a plus sign and the commit hash. Every reader of the string would then have to split it. `AppVersionTests` fails if somebody removes the switch.

`AppVersion.Display` reads the attribute for display. **It reads the assembly that holds the class, and never `Assembly.GetEntryAssembly()`.** A test run starts `testhost.exe`, so the entry assembly there is `testhost`.

**Never compare `AppVersion.Display` against the feed.** A check reads `UpdateManager.CurrentVersion`, which comes from the package metadata. A build out of a publish directory carries a version in the assembly and no package at all.

## The tag

`v<SemVer 2>`. For example `v0.1.0-alpha.1` or `v0.2.0`.

**Velopack refuses a version that is not SemVer 2.** A four-part number such as `0.1.0.0` is not SemVer 2. A label after the dash marks the release as a prerelease on GitHub.

Two places check the shape, and both fail in the first seconds. A build takes minutes, and a failure after it wastes them.

## What was built

| File | Holds |
| --- | --- |
| `global.json` | The SDK pin, so the runner and the developer machine agree. |
| `src/Directory.Build.props` | The version of the application. |
| `src/BlackboxModManager.Core/AppVersion.cs` | The version for display. |
| `src/BlackboxModManager.App/UpdateLog.cs` | The Velopack logger. |
| `src/BlackboxModManager.App/Services/UpdateService.cs` | The only class that calls Velopack. |
| `tools/pack.ps1` | The three packaging stages. |
| `.github/workflows/ci.yml` | Build and test, on every push and every pull request. |
| `.github/workflows/release.yml` | Publish and release, on a `v*` tag. |
| `tests/.../ExampleModsFactAttribute.cs` | The skip attributes for the tests that need the example mods. |

## The call order in Program.cs

`VelopackApp.Build().SetLogger(...).Run()` sits after the four culture assignments and before the first test switch. Both halves of that placement matter.

**The culture block stays first.** A comma-decimal locale reads the script float `-0.19500002` as a different number.

**The Velopack call comes before everything else.** Velopack starts this program again with a hook argument such as `--veloapp-install`. `Run` handles that argument and then ends the process. Three consequences follow.

1. The three test switches read `args` themselves. No hook argument matches one of them today. `Run` first makes that a guarantee and not a coincidence.
2. `Rendering.Apply` picks a render mode. A hook process opens no window and must not touch the render pipeline.
3. `new App()` and `App.OnStartup` show a window. A hook must never reach them.

**Register no hook that opens a window.** Velopack gives a hook about 30 seconds. A dialog there waits for a person who cannot see it, and the timeout then kills the process mid-install. `UpdateLog` is how a hook reports a problem, because a hook has no window and no console.

## Pitfalls

**`--packId` is permanent. Never change it.** `BlackBoxModManager` is the identity of the install and the name of the directory under `%LOCALAPPDATA%`. An installed copy looks for its own id in the feed. A new id makes every existing install stop seeing updates, and **neither side reports an error**. The casing matches `AppPaths.FolderName`, so one name means one thing.

**`--mainExe` takes a file name and not a path.** It is `BlackboxModManager.exe`, with a lower case `b` in `box`, while the pack id has a capital `B`. That difference is old. Do not "fix" `AssemblyName`, because that renames the executable that every script and every roadmap file names.

**Leave `--channel` out.** The default for a Windows build is `win`, and `UpdateService` reads that same default. Naming it in one place and not the other is how a feed goes silently empty.

**Leave `--framework` in.** The build is framework-dependent, so `Setup.exe` has to install the runtime. **The value `net10.0-x64-desktop` is not in the Velopack documentation, which lists 5.0 to 9.0.** The documentation says that every version from 5.0 up works. See the risks below.

**Never set `PublishSingleFile` for a release.** Velopack wants a directory of loose files and writes the single distributable itself. This removes the problem of step 3, fact 4. `LZCompressLib.dll` lands beside the host as a normal file. That is the simplest case for the P/Invoke.

**The settings live outside the install directory on purpose.** Velopack replaces the `%LOCALAPPDATA%` directory on every update. `AppPaths.Root` is `%APPDATA%\BlackBoxModManager`, which is a different directory, so an update keeps every mod and profile. Do not move application data into the install directory.

**Ship no GitHub token.** `GithubSource` takes null, which gives 60 requests an hour for one address. That covers a button and one check per start. A token in a public build is a token that somebody else uses.

**The startup check opens no dialog and downloads nothing.** A machine with no network reaches it at every start. `CheckForUpdatesAtStartAsync` writes one line and stops. The button is the only thing that downloads.

**Do not turn on `TreatWarningsAsErrors` in either workflow.** The three forks emit about 21 warnings, and step 0 records why they stay.

## The tests and a clean runner

`example_mods` is in `.gitignore`, and `git ls-files example_mods` returns nothing. **No runner ever has that data.**

`ExampleMods.Root` used to be a throwing static initializer. Every test that touched it therefore failed with `TypeInitializationException`, and the message named reflection instead of the missing directory. Measured on a checkout with no `example_mods`: **53 tests across six classes failed.**

`Root` is now lazy, and `ExampleMods.Exists` reports the directory. `ExampleModsFactAttribute` and `ExampleModsTheoryAttribute` set `Skip` when it is absent. xUnit 2.9.3 has no `Assert.Skip`, so setting the property in the constructor is the way that xUnit 2 supports.

**A CI filter was the other option, and it is worse.** A `FullyQualifiedName!~` list drops whole classes. It would also drop the tests in those six classes that need no example mod. It rots too. A new example-mods test in an unlisted class turns CI red, for a reason that looks like a real failure.

Measured after the change, with no `example_mods`: **0 failed, 382 passed, 53 skipped.**

## Verified

- The version reaches `BlackboxModManager.Core` and not the three forks.
- A framework-dependent publish carries `LZCompressLib.dll`, all four 7-Zip files, both license files, and the three font licenses. `tools/pack.ps1` gates on exactly that list.
- `System.GC.Server` stays true in `runtimeconfig.json` after a framework-dependent publish. Step 14 needs it.
- The hook path works. `BlackboxModManager.exe --veloapp-install 0.1.0` under Wine 11.16 opened no window, exited 0, and wrote to `update.log`. The managed side of Velopack therefore runs under Wine.
- `keys.txt` never reaches the output. The `None Update` entry in `Nikki.csproj` names a file that does not exist, so that entry does nothing. The content gate does not ask for it.

## Risks

| Risk | How to verify | Fallback |
| --- | --- | --- |
| **`net10.0-x64-desktop` is unverified.** `vpk` on Linux packs a Linux AppImage only, so no local test can answer this. A rejected value fails the release job. A value that `vpk` accepts and writes wrongly fails `Setup.exe` on a clean machine, which is worse. | Run the release workflow through `workflow_dispatch`, which packs and uploads nothing. Then run the `Setup.exe` on a Windows machine with no .NET 10. | Drop `--framework` and name the runtime as a prerequisite in the README. Or publish self-contained, which also restores the Wine story of step 3. |
| **`Setup.exe` and `Update.exe` under Wine are unverified.** Both are native programs of Velopack. Neither uses WPF, so the install is the likelier survivor and the restart is the likelier failure. | `Setup.exe --silent` in a fresh prefix, then force an update from one alpha to the next. | The portable zip, and `tools/run-app.sh` for a self-contained build. |
| **`tools/pack.ps1` has never run.** This machine has no PowerShell, so the script went out unexecuted. | Run it on Windows before the first tag. | — |
| **The status bar and the Updates group were never seen.** The session here is Wayland, so no capture of the Wine window was possible. | Start the application on Windows and open the settings. | — |
| **The uninstaller must leave `%APPDATA%\BlackBoxModManager` alone.** | Install, import a mod, uninstall, and check that the directory survives. | **A mod manager that deletes the library of the user on uninstall is a serious defect.** Fix it before any release that a person other than the author installs. |

## Results

### 2026-08-25: the installer works, and the runtime installs

A dispatch run packed version `0.1.0-alpha.1`, and the `Setup.exe` of that run installed on Windows.

**`--framework net10.0-x64-desktop` is correct.** `vpk` accepted the value, and `Setup.exe` fetched and installed the .NET 10 Desktop Runtime on a machine that had no .NET 10. **The largest risk of this step is closed.** The Velopack documentation still lists 5.0 to 9.0 only, so leave the note above in place for the next reader.

The application then started and ran. A deploy of one mod into Underground 2 finished.

Still open after this run.

- **No tag has gone out.** The release workflow ran through `workflow_dispatch`, which uploads nothing.
- **`Setup.exe` and `Update.exe` under Wine stay unverified.**
- **No update from one release to the next has run.** That needs a second tag.

### What the first install found, and it was not about the release

The deploy failed the first time, and the cause sat in `GameSwap` and not in this step. The game lived under `C:\Program Files (x86)\EA GAMES`, the swap could not rename that directory, and the fallback started to delete the live install. Read the dated section at the end of [05-mvp-shell.md](05-mvp-shell.md).

Two facts belong here, because they change what a release has to say to a user.

1. **A game under `Program Files` needs elevation for a deploy.** A run as administrator finished the same deploy with no error. The application now tests for the rights first and offers the restart, so a user meets a reason and a fix rather than a failure.
2. **Do not solve that with `requestedExecutionLevel` in a manifest.** An elevated process takes no drop from a non-elevated Explorer window. The drag and drop import of step 13 would then stop working, with no message. An elevated run also leaves files that a later normal run cannot write. `AccessPreflight` plus the "Restart as administrator" action is the route that this project took.

**A release therefore has to say where to install a game.** The README now names Program Files as the case that needs administrator rights.

Fill the rest after the first tag and the first update.
