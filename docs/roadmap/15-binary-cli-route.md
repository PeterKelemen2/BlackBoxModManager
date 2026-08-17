# Step 15 — the Binary CLI route

## Why

The container engine applies a Binary mod through Nikki and Endscript, in this process. That route works and it stays the default.

The route has one structural limit. `CommandGate` refuses a command that this application does not run. A mod that needs such a command cannot deploy at all. Two verbs carry a refusal today, and every unclassified verb carries a warning.

Binary 2.8.3 pins the same commits of Nikki and Endscript that we build. So it runs that mod, and it writes the same bytes. See step 14, fact 1. The application already needs a Binary install for the `mainkeys` hash lists, so the executable is always present.

This step adds Binary as a second route. A profile picks the default. One mod can override it. Every log line names the route that ran.

## The command line

The brief records the entry point. Read `project_brief.md`, the section "Fallback: the CLI of Binary".

```
Binary.exe <user|modder> <VERSN1-manifest-path> <VERSN2-script-path>
```

Three properties of that program shape the whole route. None of them is a choice that we can make differently.

1. **The manifest must say `Modder`.** `CLI.LoadProfile` throws on any other value. So the route writes a manifest of its own and never hands over the manifest of the mod.
2. **The exit code is always zero.** Binary writes a parse error and an apply error with `Console.WriteLine` and returns. So the route reads `EndError.log` and never the exit code.
3. **A question blocks the run.** Binary reads an answer from its own console. It calls `AllocConsole` first, so a redirected pipe does not reach it, and `Console.ReadLine` never returns under Wine anyway. So the route hands over a script that asks nothing.

The first argument is dead code. Binary parses it and then reads `Usage` from the manifest. The shape is positional, so the route passes `modder` anyway.

## What was built

| File | Holds |
| --- | --- |
| `Core/Deploy/BinaryRoutePlan.cs` | The route of every enabled Binary mod. One place decides. |
| `Core/Deploy/BinaryRouteEngine.cs` | The router that splits the Binary kind over the two engines. |
| `Core/Deploy/BinaryCliDeployEngine.cs` | The process run, the manifest, and the verdict. |
| `Core/Deploy/BinaryVariantScope.cs` | Narrows the enabled variants to the mods of one engine call. |
| `Core/Mods/ScriptEmitter.cs` | The script that answers every question of a mod. |
| `Core/ProcessRunner.cs` | The process seam, so a test can replace `Binary.exe`. |

`Profile.BinaryRoute` holds the default of the profile. `ProfileEntry.Route` holds the override of one mod. `Profile.RouteOf` resolves the pair. Read the route from that method and never from the two fields.

## Six facts that carry forward

1. **`DeployService` groups the mods by kind, so two engines must not claim one kind.** The container engine and the CLI engine both apply `ModKind.Binary`. `BinaryRouteEngine` claims the kind and hands each mod to the engine underneath. It cuts the mods into runs of one route and keeps the profile order across both, because the edits composite through the disk.

2. **`ContainerDeployEngine` read every enabled variant and ignored its `mods` argument.** That was harmless while one engine owned the kind. With two engines it would apply the mods of the other route as well, and every edit would run twice. The engine now calls `BinaryVariantScope.Of`. This was a defect of its own.

3. **A hard link cannot survive an outside program.** `TreeReplicator` links every file that nothing writes, so a staging file, a vanilla file, and a live file are one file with three names. The container engine breaks that share for every file that it writes, because it reads the script first and knows the list. Binary reads nothing to us. So `TreeReplicator.Build` takes `linkFiles`, and a deploy that uses the CLI route copies every byte of the install. That is the price of the route, and the log states it. See defect 16.

4. **A refused command must not stop the CLI route.** `CommandGate.Check` takes `refuseUnsupported`. A refused command is a limit of this application, and a mod that needs one is the reason this route exists. **The escape rule never relaxes.** A path outside the staging copy reaches the real system, and no revert undoes it. `ConflictPreflight` makes the same split, and it reports a refusal of a CLI mod in `BinaryHandled` instead of `Rejections`.

5. **`ResolvedEdit.Text` makes the script emitter safe.** It holds the original text of the command, so the emitter copies text that the parser already produced. A rebuild from the parsed arguments would corrupt a float such as `-0.19500002`. `EndScriptParser` splices an `append` and never returns one, and `ScriptFlattener` never emits a question, an `if`, or an `end`. So the output holds plain commands only.

6. **The generated script must sit beside the launcher of the mod.** Seventeen commands read a file relative to the directory of the launcher, and the parser resolves an append against that same directory. A file in a scratch directory would break every one of those paths. The route writes `.blackbox-cli.end` there and deletes it in a `finally` block. The manifest can sit in the scratch directory, because `Endscript` holds a full path and `Path.Combine` returns a rooted second argument unchanged.

## The submenu of the mod row

The context menu of a mod row offers the route under "Deploy this mod with". That item was the first submenu in this application, and it opened nothing at first.

**`MenuItem` needs one template for each role that it plays.** WPF reads the `Role` property and picks the template by it. The theme carried one template for every role, and that template held no `Popup` and no items host. A leaf drew correctly. An item with children drew its header and opened nothing, with no error and no warning. `IsSubmenuOpen` still turned true on the click, so the input worked and only the drawing was absent.

Three details of the fix matter.

1. **The popup must be named `PART_Popup`.** `MenuItem` declares that template part and finds it by name to place and to close the child menu.
2. **`IsOpen` takes a `Binding` and not a `TemplateBinding`.** A `TemplateBinding` runs one way. The popup closes itself when the user clicks away, and that close has to reach `IsSubmenuOpen`, or the item keeps a stale open state and the next click does nothing.
3. **The leaf template holds a mark column.** A menu that offers three routes has to show which one the mod takes now. `IsCheckable` stays false, so a click runs the command and never toggles the mark by itself.

Add a role template before you add the second submenu anywhere else.

## How the verify stays strict

`StagingVerifier` reports every staged file that differs from the vanilla state and that no mod claimed. The route feeds that check from two sources.

1. **The prediction.** `gate.Containers` and `gate.WritePaths` name what the script says it writes. `ContainerReportBuilder` turns those into the report, the same way the container engine does.
2. **The fact.** The route records which files differ from the baseline before the first process starts, and again after the last one ends. Every new difference belongs to Binary and becomes a `ScriptWrite`.

The fact list keeps the verify free of false failures. The difference between the two lists is worth a log line of its own, and the route writes one. It is the only measure of how much of a Binary run this application can predict.

## Limits

- **A mod that asks a question and also holds an `if` command cannot take this route.** The emitter would write both branches of the `if`, because the branch depends on the containers of the game. The route stops that mod and names the container engine.
- **The exit code of Binary proves nothing.** The route reads `EndError.log`.
- **Stdout is unreliable.** Binary calls `AllocConsole`, so the pipe often stays empty. The route logs whatever arrives and trusts none of it.
- **Binary needs the .NET Core 3.1 Desktop runtime.** This application is self-contained and does not supply it. A start failure names the runtime.
- **The route copies the whole game directory for every deploy.** See fact 3.

## How to verify

1. Run the tests.

   ```
   dotnet test tests/BlackboxModManager.Tests/BlackboxModManager.Tests.csproj
   ```

   `BinaryRouteTests`, `BinaryRouteEngineTests`, `BinaryCliEngineTests`, `CommandGateRouteTests`, `ScriptEmitterTests`, and `FullCopyStagingTests` cover this step. None of them needs a Binary install, because `IProcessRunner` replaces the program.

2. Deploy one Binary mod through the container engine. Confirm that the log names that engine.

3. Turn on "Deploy through Binary" in the config window. Deploy the same mod. Confirm four things.

   - The log names Binary and the install path.
   - The log states that the staging copy holds a private copy of every file.
   - The verify passes and the swap runs.
   - No `.blackbox-cli.end` file stays in the mod store.

4. Compare the two results byte for byte. The containers should match. See step 14, fact 1.

5. Take a mod that the container engine refuses. Deploy it through Binary. Confirm that the refusal became a log line and that the deploy finished.

6. Point a command at a container that the game does not hold. Confirm that the failure message carries the text of `EndError.log`, and that the game directory did not change.

7. Set one mod of three to the other route. Confirm that the log applies the three in profile order.

## Results

**Not run against a real Binary install yet.** Every unit test passes, and the route is covered by 41 tests through the process seam. Items 2 to 7 above need a Windows machine with Binary 2.8.3 and a game install. Nobody has run them.
