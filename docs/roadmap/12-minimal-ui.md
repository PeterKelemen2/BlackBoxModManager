# Step 12 — the minimal window

Take every path setting off the main window, and reduce the mod actions to four glyphs. The application keeps every behavior that it has today.

Step 11 left a window that shows everything at once. Four things crowd it.

1. **The `Game` card carries a picker, five status lines, and four buttons.** A user reads those lines once and sets those paths once.
2. **The profile row carries a picker and three more buttons.** That is a second full row above the mod list.
3. **`Import archive` and `Import folder` take the two widest labels in the window.** They do one job, and they sit next to five more buttons.
4. **`Deploy` and `Revert to vanilla` paint a solid green fill and a solid red fill.** Both shout across the window, and neither one is the thing that a user looks at most.

The load order is the point of this application. The mod list has to win the window, and it does not.

**No behavior of the application changes.** No command changes its meaning. No deploy rule changes. A deploy writes the same bytes.

## The rules that this step must respect

These four come from steps 5, 10, and 11. All four still hold.

1. **The window runs under Wine with the software rasterizer.** `Rendering.Apply` forces `RenderMode.SoftwareOnly` there. No resting `Opacity`, no `DropShadowEffect`, and no live `VisualBrush`.
2. **Name no font family outside `Theme/Typography.xaml`.** A family that the machine does not hold reaches `MS.Internal.Invariant.FailFast`, which kills the process with no dialog and no catchable exception. **This bans every icon font.** Every glyph of this step is `Path` geometry.
3. **No color literal outside `Theme/Colors.xaml`**, and no `Opacity` as a color tool.
4. **`RowBorder` is a name that code reads.** `FindRowElement` and `IsControlSurface` match that exact string. A rename kills the drag, the drop, and the row selection with no build error.

## The result

```
┌────────────────────────────────────────────────────────────────┐
│ [Underground 2 ▾]  [Default ▾] [⋯]                        [⚙]  │
├──────────────────────────────────┬─────────────────────────────┤
│ [+]              [▲][▼]   [🗑]   │  Mod | Settings | Loader |…  │
│ ┌──────────────────────────────┐ │                             │
│ │ ⠿ (o ) 1  Widescreen Fix     │ │                             │
│ │ ⠿ ( o) 2  Extra Options      │ │                             │
│ └──────────────────────────────┘ │                             │
├──────────────────────────────────┴─────────────────────────────┤
│                                       [Deploy]  [Revert]       │
├────────────────────────────────────────────────────────────────┤
│ Ready.                     Underground 2 is ready. Vanilla.    │
└────────────────────────────────────────────────────────────────┘
```

Five rows became four. The `Game` card and the profile row became one bar. The five status lines became one line in the status bar, with the three paths in its tooltip.

## Part A — the icon set

`Theme/Icons.xaml` holds six `Geometry` resources and nothing else. `App.xaml` merges it after `Metrics.xaml` and before `Typography.xaml`.

| Key | Glyph | Used by |
| --- | --- | --- |
| `IconPlus` | a cross | the import button |
| `IconChevronUp` | a chevron | `Move up` |
| `IconChevronDown` | a chevron | `Move down` |
| `IconTrash` | a lid, a handle, a body, two ribs | `Remove` |
| `IconSettings` | two rails, one knob on each | the config button |
| `IconMore` | three dots | the profile menu |

**Every geometry starts at the origin and carries its true size.** `IconGlyph` centers it and never scales it. Do not add `Stretch="Uniform"`. WPF scales a stroked geometry by rules that are hard to predict at 13 pixels. Step 11, Part C, records the same rule for the check mark.

**A gear is the other convention for the settings glyph, and this step refuses it.** A gear needs a dozen teeth, and a tooth of that size lands below one pixel on the software rasterizer. Two rails with a knob each read as a settings control and draw cleanly.

**Each dot of `IconMore` is a small circle of two arcs, and never a zero length line.** A zero length segment draws as a dot only when the renderer applies the round cap to it, and no step of this project tests that rule on the software rasterizer of Wine.

Every geometry carries `po:Freeze="True"`. A frozen geometry costs one build and no copy, and the CPU draws every one of these.

## Part B — the icon button

`Theme/Parts.xaml` gains two keyed styles.

`IconGlyph`, for `Path`, sets the stroke from the button above it:

```xml
<Setter Property="Stroke"
        Value="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" />
```

**That one binding gives every icon its disabled color and both outlined colors for free.** The `Button` template sets `Foreground` on the button for the disabled state and for every `Kind`, so the glyph follows with no trigger of its own.

`IconButton`, for `Button`, is based on the implicit `Button` style. It sets `Width` and `Height` to `ControlHeight`, `Padding` to 0, and **`MinWidth` to 0**. The implicit style holds `MinWidth` 80, and an icon button without that override draws as a wide slab with a small mark in the middle.

**Every icon button carries a `ToolTip`.** The label is gone, so the tooltip is the only name that the control has.

## Part C — the two outlined button kinds

`ButtonKind` gains `SuccessOutline` and `DangerOutline`. Each takes one trigger group in the `Button` template of `Controls.xaml`.

| State | Fill | Border and text |
| --- | --- | --- |
| Rest | transparent | `SuccessDefault` / `DangerDefault` |
| Hover | `SuccessSubtle` / `DangerSubtle` | `SuccessHover` / `DangerHover` |
| Pressed | `SuccessSubtle` / `DangerSubtle` | `SuccessPressed` / `DangerPressed` |
| Disabled | the shared disabled trigger | `BorderDefault` and `TextDisabled` |

`Colors.xaml` gains `SuccessSubtle` and `DangerSubtle`. Both sit near `SurfaceRaised` and carry a trace of the color of the role. **These two tokens exist because a hover state cannot use `Opacity`.** Step 10, Part A, bans it, and the software rasterizer composites an alpha value on every repaint.

**`Success` and `Danger` keep their solid fill.** `ConfirmWindow` paints the confirm button of a destructive question with `Danger`, and step 11 made that choice on purpose. A dialog that asks a question needs a louder button than a toolbar does.

`Deploy` takes `SuccessOutline`. `Revert to vanilla` and the mod `Remove` button take `DangerOutline`.

## Part D — the menus

`Controls.xaml` gains `ContextMenu`, `MenuItem`, and a `Separator` style. Without them a menu draws light Windows chrome inside a dark window.

**`HasDropShadow` decides whether the popup surface takes transparency, and it draws no shadow here.** WPF hosts a `ContextMenu` in a `Popup` that it creates itself, and that `Popup` reads the property for `AllowsTransparency`. The template holds no shadow element, so a true value buys the rounded corners and nothing else. A false value would square the corners off against an opaque surface.

**A menu takes `MenuItem.SeparatorStyleKey` and never the implicit `Separator` style.** `Menu` asks for that key by name, so an implicit style never reaches a separator inside a menu. `Controls.xaml` holds both entries, and the keyed one carries the whole definition.

The import button opens a menu of two items, which run the two commands that already exist. Neither command changes.

| Item | Command |
| --- | --- |
| `Archive…` | `ImportArchiveCommand` |
| `Folder…` | `ImportFolderCommand` |

The profile button opens `New profile`, `Rename this profile`, and `Delete this profile`.

## Part E — the mod row context menu

`ModRowTemplate` puts a `ContextMenu` on `RowBorder` with `Set the game of this mod` and `Remove from the store`. `Set game` left the toolbar, because only a mod that the store imported before metadata version 2 needs it.

**A `ContextMenu` is not in the visual tree of the window**, so a binding with `RelativeSource AncestorType=Window` finds nothing inside one. Every menu of this step reads its `DataContext` from its placement target instead.

The row carries the `ModRowViewModel`, and the two commands live on the `MainViewModel`. `RowBorder` therefore carries the bridge:

```xml
Tag="{Binding DataContext, RelativeSource={RelativeSource AncestorType=Window}}"
```

The menu then takes `PlacementTarget.Tag` as its own `DataContext`, and each item binds to a command by name.

**`ModList_PreviewMouseRightButtonDown` selects the row before the menu opens.** Without it the two commands act on the row that the user selected last. The handler never marks the event handled, because WPF opens the menu on the button up that follows.

## Part F — the selection state

`RemoveModCommand`, `MoveUpCommand`, `MoveDownCommand`, and `SetModGameCommand` all used `CanExecute = nameof(IsIdle)`. They stayed enabled with no mod selected and then returned early. A button with a label survives that. A button with a glyph and no label does not, because a press that does nothing tells the user nothing.

`CanActOnMod` replaces `IsIdle` on all four, and the `SelectedMod` setter notifies the four commands.

The early-return guards stay. A command guard and a `CanExecute` guard are not the same guard, and the code-behind can still set `SelectedMod` to null.

**Out of scope: disabling `Move up` at the first row and `Move down` at the last row.** That needs a notify on every order change, and `Profile.Move` already refuses a move past either end.

## Part G — the main window

**Row 0, the bar.** The game picker, the profile picker, the profile menu button, and the config button.

**The game picker names the game with an `ItemTemplate` and never with `DisplayMemberPath`.** The closed box of a `ComboBox` draws `SelectionBoxItem` through `SelectionBoxItemTemplate`, and the template in `Controls.xaml` binds that one property. `DisplayMemberPath` reaches the open list and misses the closed box, which then falls back to `ToString` and reads `Need for Speed Underground 2 (Underground2)`. An `ItemTemplate` covers both.

**Row 1, the content.** The three-column grid keeps its splitter and its `TabControl`. The mod list card gains a toolbar row above the list.

**Row 2, the actions.** `Deploy` and `Revert to vanilla`, right-aligned. The `Full verify` check box, the `Folders` button, and the five mod buttons all left this row.

**Row 3, the status bar.** The left item holds `Status`. The right item holds `StateSummary`, which joins `GameStatus` and `DeployedState`. Its tooltip carries `GamePath`, `BinaryStatus`, and `ModStoreStatus`.

`RefreshStateSummary` runs at the end of every path through `RefreshDeployedState`, and `RefreshGame` sets the game line before it calls that. So the status bar never shows one half of the state.

## Part H — the config window

`Views/ConfigWindow.xaml` holds five sections. **The window takes the `MainViewModel` as its `DataContext` and binds to the commands that already exist.** It holds no logic. The code-behind opens it, sets the dialog owner, and nothing else.

| Section | Shows | Buttons |
| --- | --- | --- |
| Game install | `GameStatus`, `GamePath` | `Detect`, `Browse` |
| Binary install | `BinaryStatus` | `Browse` |
| Mod store | `ModStoreStatus` | `Change` |
| Workspace | `WorkspaceStatus`, `WorkspacePath` | `Change` |
| Deploy | a `TogglePill` for `FullVerify` | — |

The footer holds `Folders` and `Close`. Every path shows in a read-only `TextBox` with no border, so a user can select and copy part of it, the way `FoldersWindow` does it.

### The dialog owner

**A modal config window breaks every picker that these commands open.** `MainWindow` implements `IUserInteraction`, and every method passed `this` to `Dialogs`. While the config window is modal, `MainWindow` is disabled, and a file dialog owned by a disabled window can open behind that window and take no input.

`MainWindow.DialogOwner` fixes it. Every `IUserInteraction` method passes `this.DialogOwner ?? this`. `ConfigWindow.Show` sets the property before `ShowDialog` and clears it in a `finally`. **The `finally` matters.** A main window that keeps a closed window as its dialog owner opens every later picker against a dead handle.

### The workspace command

`Settings.WorkRootOverride` had no UI before this step. `SetWorkRootCommand` follows the shape of `SetModStore`: a `PickChoice` between another directory and the default place.

**The workspace holds the only vanilla copy of an install.** A move while the game directory holds a deployed profile points the application at an empty workspace, and `Revert` then throws because no vanilla copy exists. `WorkspaceIsSafeToMove` reads `workspace.ReadState().IsVanilla` first and refuses that case with a message that says to revert.

**The old directory stays on disk.** This application deletes nothing that it did not write in the same operation, and the vanilla copy is the last way back. The log names the old path.

### Full verify becomes a setting

`Settings.FullVerify` joins the settings file. **`Version` stays at 2.** A file that step 9 wrote holds no such key, and a missing key reads as false, which is the value that the window used before.

`OnFullVerifyChanged` writes the field and saves. The constructor reads the value back into the backing field and never through the property, because the property setter saves the file that the value just came from.

## Part I — the drop onto the list

`ModList_DragOver` and `ModList_Drop` test `DataFormats.FileDrop` first.

1. `DragOver` sets `DragDropEffects.Copy` and returns. **A file from outside is an import and never a reorder**, and it carries no dragged row for the preview to move.
2. `Drop` reads the paths and calls `MainViewModel.ImportDropAsync`.

`ImportDropAsync` imports the first path and logs a line when the drop carried more. **One import runs at a time**, because the library statics of Nikki make a second one on a second thread unsafe. See defect 8. It also returns at once while `IsBusy` is true, because a drop carries no `CanExecute` and the window cannot gray a drop target out.

`ImportDropped` in the code-behind is the one `async void` method of this window, and it catches. An `async void` method that throws ends the process.

**The menu is the primary path and the drop is the second one.** A drop from a Linux file manager into a Wine window is not proven.

Known gap: the empty-state overlay sits over the list, so a drop onto the `+` tile or its two text blocks does not reach the list. A drop onto the rest of the panel works.

## Pitfalls

**An icon font kills the process under Wine.** Step 5, fact 9. Every glyph of this application is `Path` geometry, and a future control that needs a new glyph adds a `Geometry` to `Icons.xaml`.

**A menu popup is a layered window.** Step 5, fact 8, records that a hardware popup paints black under Wine. `Rendering.Apply` forces software rendering there, which fixed the `ComboBox` popup and covers these menus too. `--themetest` holds a menu for that check.

**A tooltip and a context menu both sit outside the visual tree.** Neither one inherits a `DataContext` that a binding can rely on. Both name `PlacementTarget` explicitly in this window.

**`IconButton` must reset `MinWidth`.** The implicit `Button` style holds 80.

**The disabled trigger of the `Button` template comes last and wins.** A disabled outlined button therefore takes the shared `SurfaceRaised` fill and the `BorderDefault` edge, and not its own color. That is what a disabled button should look like, and it needs no trigger of its own.

## Work

1. Add `Theme/Icons.xaml` and merge it in `App.xaml`.
2. Add `SuccessSubtle` and `DangerSubtle` to `Colors.xaml`. Add the two members to `ButtonKind`, and the two trigger groups to `Controls.xaml`.
3. Add the `ContextMenu`, `MenuItem`, and `Separator` styles to `Controls.xaml`.
4. Add `IconGlyph` and `IconButton` to `Parts.xaml`. Put the context menu on `RowBorder`.
5. Add `CanActOnMod` and point the four commands at it. Add `StateSummary`, `WorkspacePath`, `WorkspaceStatus`, `SetWorkRoot`, `ImportDropAsync`, and the `FullVerify` hook to `MainViewModel`.
6. Add `Settings.FullVerify`.
7. Rewrite `MainWindow.xaml`. Add the right button handler, the outside drop, `DialogOwner`, and the menu handlers to the code-behind.
8. Add `Views/ConfigWindow.xaml` and its code-behind.
9. Add the outlined buttons, the icon buttons, and a menu to `ThemeTestWindow`.
10. Run `--themetest` and the window under Wine. Run the full test suite and `tools/run-deploy-test.sh`.

## Results

**Done on Windows. The Wine run is open.**

Every part of this step is built and checked on Windows 10 against a real Underground 2 install.

1. **Every glyph draws.** The plus, both chevrons, the trash, the settings rails, and the three dots all render from `Path` geometry.
2. **The disabled state works through one binding.** With no mod selected, the two chevrons and the trash draw in `TextDisabled` with a `BorderDefault` edge. A right-click on a row selects it, and all three turn on. The trash then shows its red edge.
3. **Both menus open and draw dark.** The import menu shows `Archive…` and `Folder…`. The row menu shows both items with the separator between them.
4. **The config window shows all five sections with live values.**
5. **The dialog owner fix works.** `Browse` inside the modal config window opens the directory picker in front of that window, with focus, at the path that the setting holds.
6. **The game picker needed an `ItemTemplate`.** `DisplayMemberPath` alone showed `Need for Speed Underground 2 (Underground2)` in the closed box. See Part G.

The suite went from 317 discovered cases to 319. The two new tests cover `Settings.FullVerify`: one round trip, and one older file with no such key. Both pass.

### The Windows machine holds no example mods

**52 tests fail on this machine, and all 52 failed before this step too.** Every one of them throws the same exception:

```
System.IO.DirectoryNotFoundException : No example_mods directory above
D:\Programming\BlackBoxModManager\tests\BlackboxModManager.Tests\bin\Debug\net10.0\.
```

`.gitignore` line 1 excludes `example_mods`, so a fresh clone holds no copy. The affected files are `ManifestRoundTripTests`, `MergedLoadTests`, `ScriptGuardTests`, and every other test that reads a real mod.

**`tools/run-deploy-test.sh` cannot run here for the same reason**, and the script is written for Linux paths and Wine. A native run of `BlackboxModManager.exe deploytest` against a scratch copy of a real Underground 2 install reached the install validation and then had no mods to import. The scratch copy was deleted after that run. **This gate is unchecked for step 12.**

That gate matters less here than it did for steps 6 to 9. This step changed one Core file, and the change is one optional field on `Settings`. No engine, no staging, no profile, and no conflict code changed.

### What is open

Three checks wait for the Linux machine.

1. **`tools/run-deploy-test.sh`.** Run it there, where `example_mods` exists. The container bytes must match step 6.
2. **The full suite with the fixture present.** The 52 failures above must go back to passing.
3. **A Wine run of the window and of `--themetest`.** Three things to look at:
   - **The two menus.** A layered popup paints black under Wine unless software rendering is on. `Rendering.Apply` covers it, and only a run proves it.
   - **Every glyph at the Wine rasterizer.** The arcs of `IconSettings` and `IconMore` are the two to watch.
   - **The drop of a file from the file manager.** This is the second way into an import and never the only one, so a failure here costs the user nothing.
