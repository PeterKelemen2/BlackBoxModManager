# Step 17 — the consistency pass

Remove the duplicate surfaces of the window, and repair the input paths that feel clunky. This step changes what the window shows and how it takes input. **It changes no deploy rule and no byte that a deploy writes.**

Steps 10, 11, and 12 built the window one layer at a time. Each layer was correct on its own. Together they left twelve places where the application says one thing twice, says one thing two ways, or invites an action that it cannot take.

An audit of the view layer found them. This file holds one part per finding. Each part states the problem, the evidence, the decision, the change, and the check.

## What this step must not change

These rules come from steps 10, 11, and 12. All of them still hold.

1. **The window runs under Wine with the software rasterizer.** `Rendering.Apply` forces `RenderMode.SoftwareOnly` there. Use no resting `Opacity`, no `DropShadowEffect`, and no live `VisualBrush`.
2. **Name no font family outside `Theme/Typography.xaml`.** A family that the machine does not hold reaches `MS.Internal.Invariant.FailFast`. That kills the process with no dialog and no catchable exception. **This bans every icon font.** Every new glyph is `Path` geometry in `Theme/Icons.xaml`.
3. **Write no color literal outside `Theme/Colors.xaml`.** Do not use `Opacity` as a color tool.
4. **`RowBorder` is a name that code reads.** `FindRowElement` and `IsControlSurface` in `MainWindow.xaml.cs` match that exact string. A rename kills the drag, the drop, and the row selection with no build error.
5. **`ConfigWindow` holds no logic.** It takes the `MainViewModel` as its `DataContext` and binds to commands that already exist. Read step 12, Part H, before you add any code to it.
6. **A profile fully determines the deployed result.** No change of this step may move a profile value into the settings file, or a settings value into the profile.
7. **Every icon button needs a `ToolTip`.** The label is gone, so the tooltip is the only name that the control has.

## The order to work in

Parts A, E, and B carry the most weight for a user. Part E may be a defect and not a preference, so measure it first.

1. Part E, the drop target. Confirm the failure before you write code.
2. Part A, the Folders window and the settings list.
3. Part B, the name `Settings`.
4. Parts C and D, the path rows of the settings window.
5. Parts F, G, and H, the input paths.
6. Parts I, J, K, and L.
7. Part M, the small fixes. Each one is independent.

---

## Part A — the Folders window duplicates the settings list

**The problem.** The settings window lists the game install, the mod store, and the workspace, each with a path and a status line. The same window then holds a `Folders` button that opens a second window. That second window lists the same three directories again, plus four more. The two lists disagree about what a user can do. The settings window can change a path and cannot open it. The Folders window can open a path and cannot change it.

The button also sits in the dialog button row, next to `Close`. That position reads as an action of the dialog, and the button is a destination.

**The evidence.**

| Place | What it holds |
| --- | --- |
| `Views/ConfigWindow.xaml`, the `ShowFoldersCommand` button | The `Folders` button, in the bottom row beside `Close` |
| `ViewModels/MainViewModel.cs`, `ShowFolders` | Seven `FolderRow` values |
| `Views/FoldersWindow.xaml` | The row list, with `Open` and `Copy path` |

`ShowFolders` builds these seven rows. Three of them repeat a group of the settings window.

| Row | The settings window also holds it |
| --- | --- |
| Game install | Yes, as the `Game install` group |
| Workspace | Yes, as the `Workspace` group |
| Mod store | Yes, as the `Mod store` group |
| Staging copy | No |
| Vanilla copy | No |
| Application data | No |
| Logs | No |

**The decision.** **One directory gets one row in one window.** The settings window owns every directory that a user can change. The Folders window owns the four that a user can only look at.

**The change.**

1. Give each of the three settings groups an `Open` icon button beside its existing `Browse` or `Change` button. Use `IconButtonSmall` from `Theme/Parts.xaml` and a new `IconFolder` geometry in `Theme/Icons.xaml`. The tooltip reads `Open this directory in the file manager.`
2. Move the `Open` code out of `Views/FoldersWindow.xaml.cs` into a static helper that both windows call. `FoldersWindow.Open` already holds the shell handler, the `explorer.exe` fallback, and the failure text. Put it in `Services/DirectoryOpener.cs` and leave the behavior alone. **`Copy path` must keep working when `Open` fails.** A Wine prefix has no guaranteed file manager. Step 9, fact 9, records that reason.
3. Cut `ShowFolders` down to the four rows that the settings window does not hold: Staging copy, Vanilla copy, Application data, and Logs.
4. Move the `Folders` button out of the dialog button row. Put it inside the `Workspace` group, because the staging copy and the vanilla copy live in the workspace. Give it the label `Other directories` and the tooltip `Open the staging copy, the vanilla copy, the application data, or the logs.`
5. Keep the `Close` button alone in the bottom row.

**A second option, if the four remaining rows do not earn a window.** Delete `FoldersWindow` and add a `Staging copy`, a `Vanilla copy`, and a `Logs` line to the `Workspace` group, each with `Open` and `Copy path`. **Do not take this option until Part C lands.** Part C already gives every path row an `Open` and a `Copy path`, so this becomes a small change after it.

**The check.** Open the settings window. Every directory that it names has one row. No directory appears twice. `Open` works on each one, and the failure text of `Open` still names `Copy path`.

---

## Part B — the name "Settings" means two things

**The problem.** The gear button opens a window whose title is `Settings`. The right panel of the main window holds a tab whose header is `Settings`. The window holds the paths of the application. The tab holds the `.ini` keys of the selected ASI mod. The two share no content.

`CLAUDE.md` states the rule that this breaks: give one thing one name.

**The evidence.** `Views/ConfigWindow.xaml` sets `Title="Settings"`. `MainWindow.xaml` sets `<TabItem Header="Settings">`. `MainViewModel.SettingsHeader`, `MainViewModel.SettingsFiles`, and `ViewModels/AsiSettingsViewModel.cs` all serve the tab.

**The decision.** The window keeps the name `Settings`. A user of a desktop application expects that word for the application settings. **The tab takes a new name.** It shows the options that one mod ships, so it is `Mod options`.

**The change.**

1. Change the tab header to `Mod options` in `MainWindow.xaml`.
2. Change `SettingsHeader` to read `Select a mod to see its options.` and change the other three messages that it takes. They live in `LoadSettings` and `OnSettingChanged` in `MainViewModel.cs`.
3. Change the word `settings` to `options` in the user-facing strings of that tab only. `"\"{row.Name}\" ships no .ini file, so it has no options that this window can change."` is the shape.
4. **Rename no type and no property.** `SettingsFileViewModel`, `AsiSettingsFile`, and `SettingsWrite` name a file format and not a window. The rename is user-facing text only.
5. Leave the `Status` line of `OnSettingChanged` as it is. It reports a count and it names no panel.

**The check.** Search the user-facing strings for the word `settings`. Every hit belongs to the settings window or to the deploy log, and no hit belongs to the mod tab.

---

## Part C — one presentation for a path

**The problem.** The settings window draws a path in two different ways. Two groups show a selectable read-only text box under a status line. Two groups bury the path inside the status sentence, where a user cannot select it or copy it.

**The evidence.**

| Group | The path | How the window draws it |
| --- | --- | --- |
| Game install | `GamePath` | A read-only `TextBox` |
| Workspace | `WorkspacePath` | A read-only `TextBox` |
| Mod store | Inside `ModStoreStatus` | Text of a sentence |
| Binary install | Inside `BinaryStatus` | Text of a sentence |

`OpenStore` in `MainViewModel.cs` builds `ModStoreStatus` as `The mod store is at {root}`. `RefreshBinary` builds `BinaryStatus` the same way.

**The decision.** **Every path row has the same three lines and the same buttons.** The status line says what the state is. The path box holds the path and nothing else. The hint line, where a group has one, says what the choice costs.

**The change.**

1. Add `ModStorePath` and `BinaryPath` to `MainViewModel`. Each one holds a path and no sentence. Set both where the code sets the status today.
2. Take the path out of `ModStoreStatus` and out of `BinaryStatus`. The status of the mod store becomes `This is the default place.` or `The settings name this place.` The status of the Binary install keeps its own words and loses the path.
3. Add the read-only `TextBox` to the `Mod store` group and to the `Binary install` group. Copy the four properties that the other two groups use: `Padding="0"`, `Background="Transparent"`, `BorderThickness="0"`, and `IsReadOnly="True"`.
4. Give all four groups the `Open` button of Part A, and give all four a `Copy path` button.
5. **A group with no path yet hides the box and grays the two buttons.** An empty box with a border of no width reads as a layout defect.

**A note on the status bar.** The tooltip of the status bar shows `GamePath`, `BinaryStatus`, and `ModStoreStatus` (`MainWindow.xaml`, the `StateSummary` tooltip). Two of those three lose their path in this part. Bind the tooltip to the four path properties instead, so it keeps showing three paths.

**The check.** All four groups draw the same shape. A user can select any path with the mouse. The tooltip of the status bar still names the game directory, the Binary install, and the mod store.

---

## Part D — one interaction model for a path setting

**The problem.** The four path settings use two different interaction models for the same act.

| Setting | What a press does |
| --- | --- |
| Game install | Opens a directory picker |
| Binary install | Opens a directory picker |
| Mod store | Opens a choice window, then a directory picker |
| Workspace | Opens a choice window, then a directory picker |

The choice window exists because the mod store and the workspace both support a default place. `SetModStore` and `SetWorkRoot` in `MainViewModel.cs` build a `UserChoice` list of `Choose another directory` and `Use the default place`. A user who wants another directory therefore answers two dialogs.

**The decision.** **A press on `Change` opens the picker.** The default place becomes a second button in the group, and it appears only while the setting does not already hold the default.

**The change.**

1. Split `SetModStore` into two commands. `SetModStore` opens the picker and then calls `MoveStore`. `UseDefaultModStore` calls `MoveStore` with `AppPaths.ModsDirectory`.
2. Split `SetWorkRoot` the same way, into `SetWorkRoot` and `UseDefaultWorkRoot`. **Both must keep the call to `WorkspaceIsSafeToMove` at the top.** That guard refuses a move while the game directory holds a deployed profile, and the workspace holds the only vanilla copy.
3. Move the text of the choice window into the hint line of the group. The mod store hint already carries the volume rule. Add the workspace rule to the workspace hint the same way.
4. Bind the visibility of each `Use the default place` button to a new `ModStoreIsDefault` and `WorkspaceIsDefault` property. `Settings.ModStoreIsDefault` already answers the first one.
5. Keep the confirmation that `MoveStore` asks before it moves files. That question is not a duplicate. It asks whether to move the mods that the store holds today, and the answer changes what happens on disk.
6. Give every `PickDirectory` call a start directory. Part M holds the list of the calls that pass none.

**The check.** Each of the four settings needs one press to reach the picker. A setting that already holds its default place shows no `Use the default place` button.

---

## Part E — the drop target covers the rows and nothing else

**The problem.** The mod list invites a drop that it cannot take. The empty state says `The mods deploy in this order.` and its button tooltip says `Add a mod. Choose an archive or a directory, or drop one on this list.` The drop reaches no target on an empty list.

**The mechanism.** `MainWindow.xaml` sets `AllowDrop="True"` on the `ModList` `ItemsControl` alone. The default template of an `ItemsControl` paints no background, so the control answers a hit test only where a row `Border` paints. Two cases follow.

1. **An empty profile.** `ModList` holds no item, so its height is zero. The empty state panel covers the area, and that panel takes no drop.
2. **The space below the last row.** That area belongs to the `ScrollViewer` and to the `StackPanel` inside it. Neither one sets `AllowDrop`.

**Confirm this before you write code.** Start the application with an empty profile. Drag a mod directory onto the middle of the panel. Watch the cursor. A drop that works logs a line, and a drop that reaches nothing logs nothing.

**The decision.** **The whole mod panel is the drop target.** A user aims at the panel and never at a row.

**The change.**

1. Move `AllowDrop="True"`, `DragOver`, `Drop`, `DragLeave`, `GiveFeedback`, and `QueryContinueDrag` from `ModList` up to the `Grid` in `Grid.Row="1"` of the mod card, or to a `Border` that wraps it.
2. Give that element `Background="{StaticResource SurfaceRaised}"` or `Background="Transparent"`. **A null background answers no hit test.** `DragHandleArea` in `Theme/Parts.xaml` records the same rule for the drag handle.
3. Keep `PreviewMouseLeftButtonDown`, `PreviewMouseMove`, and `PreviewMouseRightButtonDown` on `ModList`. Those three read a row, and a press outside a row must still start nothing.
4. `ModList_DragOver` finds the row under the pointer with `FindRowElement`. That call already returns null on a miss and changes nothing. The comment above it states that rule. It survives this move with no edit.
5. Draw a drop state on the panel while a file drag is over it. A one pixel `AccentDefault` border on the wrapper is enough, and it costs the rasterizer nothing.

**The check.** Drop a mod directory on an empty profile. The import starts. Drop one on the space below the last row of a full profile. The import starts. Drag a row inside a full list. The reorder still works, and the ghost still follows the pointer.

---

## Part F — the drag handle promises what the row already gives

**The problem.** Each mod row draws a handle of three dashes on its left edge. That handle says `take hold here`. The row sets `Cursor="SizeAll"` across its whole width, and `ModList_PreviewMouseLeftButtonDown` starts a drag from any point that is not a button. So the handle names one grip and the row gives another.

**The evidence.** `Theme/Parts.xaml`, `ModRowTemplate`, sets `Cursor="SizeAll"` on `RowBorder`. The `DragHandle` and `DragHandleArea` styles sit in the same file. `MainWindow.xaml.cs`, `ModList_PreviewMouseLeftButtonDown`, calls `IsControlSurface` and returns only for a button.

**The decision.** **Keep the row-wide drag and keep the handle.** The row-wide drag is the easier target, and the handle is the only mark that says the list takes a drag at all. Repair the cursor instead, because the cursor is the part that misleads.

**The change.**

1. Take `Cursor="SizeAll"` off `RowBorder`.
2. Put `Cursor="SizeAll"` on `DragHandleArea`. That border is 26 pixels wide and the full height of the row.
3. Leave the drag itself alone. A press anywhere on the row still starts one.
4. Raise the contrast of the handle on hover. Add a trigger to `ModRowTemplate` that sets the `Stroke` of the handle to `TextPrimary` while `RowBorder` reports `IsMouseOver`.

**Do not restrict the drag to the handle.** A 26 pixel target is small, and the row already works.

**The check.** The pointer over the handle shows the move cursor. The pointer over the name of the mod shows the normal cursor. A press on the name still starts a drag.

---

## Part G — the mod list takes no keyboard

**The problem.** The mod list is an `ItemsControl`. That control has no selection, no focus, and no key handling of its own. Four things follow.

1. The arrow keys move nothing.
2. The Delete key removes nothing.
3. A user cannot deselect a mod. `SelectedMod` goes back to null only when the game changes.
4. `CanActOnMod` therefore reports true forever after the first click. The three toolbar buttons that gray out on no selection gray out once, at the start, and never again.

**The evidence.** `MainWindow.xaml`, the `ModList` element. `MainViewModel.CanActOnMod` and `MainViewModel.SelectedMod`. The comment in `SelectedMod` states the intent that point 4 defeats.

**The decision.** **The mod list keeps its `ItemsControl` and gains the keyboard by hand.** A `ListBox` would bring its own selection, its own item container, and its own mouse handling. All three fight the drag of step 11. The cost of a swap is higher than the four handlers below.

**The change.**

1. Give the mod card a `KeyDown` handler. Up and Down move `SelectedMod` by one row and stop at each end.
2. Delete runs `RemoveModCommand`. That command already asks for a confirmation, so no key removes a mod on its own.
3. A press on the panel that finds no row sets `SelectedMod` to null. `ModList_PreviewMouseLeftButtonDown` already returns early on that case. Set the property there instead of returning.
4. Escape sets `SelectedMod` to null while no drag runs.
5. Make the mod card focusable, so the keys reach it. Set `Focusable="True"` and `FocusVisualStyle="{x:Null}"`, and give the card focus on a press.
6. **Add `SetModRouteCommand` to `NotifySelectionCommands`.** Part M holds the reason.

**The check.** Click a row and press Down. The selection moves. Press Escape. The selection clears and the three toolbar buttons gray out. Press Delete on a selected row. The confirmation appears.

---

## Part H — Detect game asks one question for each candidate

**The problem.** `DetectGameAsync` in `MainViewModel.cs` runs a `foreach` over the candidates and calls `Confirm("Is this the install?")` inside it. Three candidates mean three modal dialogs, one after the other. A user who answers `No` to all of them gets no message and no log line. The command returns and the window looks unchanged.

**The decision.** **One dialog lists every candidate.** `ChoiceWindow` already does this job for the mod store and for the workspace.

**The change.**

1. Build one `UserChoice` for each candidate. The title is the directory name, and the detail is the full path.
2. Call `PickChoice` once. The question names the game and says that every row is a suggestion.
3. A null answer means that the user canceled. Write one log line and return.
4. Keep the `ShowMessage` of the zero-candidate case as it is. It already names `Browse` as the way forward.
5. `ChoiceWindow.Ask` preselects nothing unless a current key matches. Pass no current key. **The locator ranks nothing, and the dialog must not suggest that it does.** Step 9, fact 6, records the same rule for the ASI loader.

**The check.** Run `Detect` on a machine with two or more candidates. One dialog opens. Cancel it. The log holds one line that says so.

---

## Part I — the right panel mixes two kinds of tab

**The problem.** The tab strip holds five tabs of two kinds. `Mod` and `Mod options` follow the selected mod. `Loader`, `Log`, and `Conflicts` describe the whole profile. A click on a mod row rewrites two of the five, and the strip gives no sign of which two.

The strip also hides state. A profile with three conflicts and a profile with none draw the same `Conflicts` header. A loader contest that no answer settles draws the same `Loader` header as a settled one.

**The decision.** **Keep one strip and put the state on the headers.** Two strips cost a row of height, and the window already gives its height to the mod list.

**The change.**

1. Add `ConflictCount` to `MainViewModel`. Set it where `RefreshConflictsAsync` writes the panel.
2. Draw the `Conflicts` header from a template that shows the count as a suffix while the count is above zero. The header reads `Conflicts` or `Conflicts (3)`.
3. Add `LoaderNeedsAnswer` to `MainViewModel`. `PlanLoaders` already returns `plan.IsSettled`, and `RefreshLoaders` already reads it.
4. Draw a `WarningDefault` dot after the `Loader` header while `LoaderNeedsAnswer` is true. Use `Path` geometry and no glyph font.
5. **Order the tabs by kind.** Put `Mod` and `Mod options` first, then `Loader`, `Conflicts`, and `Log`. `Log` goes last, because it belongs to no mod and to no profile.

**The check.** Enable two mods that write the same field. The `Conflicts` header shows a count. Enable two mods that both ship `dinput8.dll`. The `Loader` header shows the dot, and the dot goes away after the user chooses.

---

## Part J — the Check again button on the Conflicts tab

**The problem.** The `Conflicts` tab holds a `Check again` button. The check already runs after every change that can produce a conflict. `RefreshMods`, `OnModToggled`, and `OnVariantChanged` all call `RefreshConflicts`. The button is the only manual refresh in the window, and its label says nothing about why a user would need it.

**The decision.** **Keep the button and say what it is for.** The check reads the mod store from disk, and a user who edits a mod folder outside this application has no other way to ask again. The button is the recovery path for that case.

**The change.**

1. Give the button a `ToolTip`. The text reads `Read the mods again. Use this after you change a mod folder outside this application.`
2. Move the button to the top right of the tab, beside the header line, and give it the `Quiet` kind. It is a recovery path and not the action of the panel.
3. Show the time of the last check in the header line of the tab, so the button has a state to refresh.

**The check.** The tab names when it last ran. The tooltip says why the button exists.

---

## Part K — the window forgets its size and its split

**The problem.** The window opens at 1040 by 700 on every start. The splitter between the mod list and the tab panel goes back to its start ratio on every start. `Settings` holds no field for either one, and no code reads `RestoreBounds`.

**The decision.** **The settings file remembers the window and the splitter.** The values belong to the machine and not to the profile, so they go in `Settings` and never in a profile.

**The change.**

1. Add four fields to `BlackboxModManager.Core/Settings.cs`: `WindowWidth`, `WindowHeight`, `WindowMaximized`, and `ModListWidth`. Use a nullable number for each size, so an absent value means "use the default".
2. Read them in `MainWindow`, after `InitializeComponent` and before the window shows.
3. Write them on `Closing`. **Read `RestoreBounds` and never `Width` and `Height` when the window is maximized.** A maximized window reports the screen size, and the next start would then open at that size with no way back.
4. Clamp the restored size against `SystemParameters.VirtualScreenWidth` and `SystemParameters.VirtualScreenHeight`. A user who unplugs a second monitor must not lose the window off the edge.
5. Save no position. A position needs the same monitor set, and the clamp above does not cover a monitor that is gone.
6. `ModListWidth` sets the width of the first `ColumnDefinition`. The `MinWidth` of both columns already stops a column of no width.

**The check.** Resize the window, move the splitter, and close. The next start holds both. Maximize, close, and start again. The window opens maximized, and a restore gives the size from before the maximize.

---

## Part L — the error dialog prints its title twice

**The problem.** `Dialogs.ShowError` calls `MessageWindow.Show(owner, title, title, message, "Copy error")`. The same string reaches the window title and the heading. The dialog therefore shows `The operation failed.` in the title bar and again in bold red above the message.

**The decision.** The title bar names the application. The heading names the failure.

**The change.**

1. Change the call to pass `"Blackbox Mod Manager"` as the title and `title` as the heading.
2. `MessageWindow.OnCopy` puts the heading and the message on the clipboard together. That behavior is correct and it stays.
3. `ShowMessage` already passes a null heading, so the neutral dialog needs no change.

**The check.** Force a failure. The title bar names the application. The red line names the failure once.

---

## Part M — the small fixes

Each item below is independent of every other part.

**M1. `ShowFoldersCommand` carries no `CanExecute`.** Every other command that touches the disk carries `CanExecute = nameof(IsIdle)`. `ShowFolders` reads `this._store` and calls `WorkspaceOf`, and both of those read state that a running deploy changes. Add `CanExecute = nameof(IsIdle)`, and add the command to `NotifyCommands`.

**M2. `SetModRouteCommand` reaches neither notify list.** The command declares `CanExecute = nameof(CanActOnMod)`. `NotifyCommands` does not name it, and `NotifySelectionCommands` does not name it either. A `RelayCommand` of the toolkit raises `CanExecuteChanged` only when the code asks it to, so nothing ever re-reads that value. The menu works today, because the `DataContext` binding of the context menu resolves on each open and the command re-reads its state then. **That is luck and not design.** Add the command to `NotifySelectionCommands`.

**M3. Three calls pass no start directory.** `PickDirectory` takes a start directory, and three callers pass none.

| Caller | What to pass |
| --- | --- |
| `SetBinary` | The current Binary directory |
| `ImportFolderAsync` | The directory of the last import |
| `SetWorkRoot`, in the branch that Part D adds | `WorkspacePath` |

Add a `LastImportDirectory` field to `Settings` for the second row. Write it after a successful import.

**M4. The application name carries two spellings.** `README.md` writes `BlackBox Mod Manager`. Every window title writes `Blackbox Mod Manager`. Pick `BlackBox Mod Manager`, because the games come from EA Black Box and the two words are separate there. Change the title of `MainWindow`, of the four dialog windows, and of `Dialogs.ShowMessage`. **Change no namespace and no assembly name.** `BlackboxModManager` is an identifier, and this project renames no identifier for a text fix.

**M5. `DetectGameAsync` writes no line when it starts.** It sets `Status` and it writes nothing to the log. Every other long operation writes one line through `RunAsync`. Write one line that names the game before the search starts.

---

## What this step does not do

These came up in the audit and this step refuses them. Each one needs its own decision.

1. **A `ListBox` for the mod list.** Part G states the reason. The drag of step 11 is worth more than the free key handling.
2. **A second tab strip, or a details pane below the mod list.** Part I states the reason. The height belongs to the mod list.
3. **A search box or a filter over the mod list.** No profile of the test set holds enough mods to need one.
4. **A rename of `SettingsFileViewModel` and its neighbors.** Part B states the reason. Those types name a file format.
5. **Multi-select in the mod list.** Every command of the toolbar acts on one mod, and a multi-select would change the meaning of all four.

## The check for the whole step

1. The application builds with no new warning.
2. `tools/run-deploy-test.sh` passes.
3. The container bytes match step 6.
4. Every part above passes its own check.
5. The window opens under Wine, and the software rasterizer draws every new element. **Test the new `IconFolder` geometry there.** Step 12, Part A, records why a small stroked geometry needs that test.
