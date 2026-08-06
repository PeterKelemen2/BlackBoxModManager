# Step 11 — the polish pass over the dark theme

Fix three defects that step 10 left behind, and add one control. The window keeps every behavior that it has today.

Step 10 gave the window the dark theme, the embedded font, and the mod list that a drag reorders. Four things need work now.

1. **The delete-profile question opens a default dialog.** `Dialogs.Confirm` still calls `MessageBox.Show`. It is the last light surface of normal operation. `New` and `Rename` both use `TextPromptWindow` and look correct.
2. **The drag reorder flickers, and it draws a line instead of a slot.** The insertion line blinks under Wine as the pointer crosses each gap between two rows. A 2 pixel line does not tell the user which slot the row lands in.
3. **The check mark sits low and right inside its box**, and it can touch the border.
4. **There is no toggle pill.** The enabled state of a mod is the one switch that the user changes most, and a 16 pixel check box is a small target for it.

**No behavior of the application changes.** No command changes its meaning. No deploy rule changes. A drop produces the same load order as it does today.

## The rules that this step must respect

These four come from steps 5, 9, and 10. All four still hold.

1. **The window runs under Wine with the software rasterizer.** Every pixel of the theme is drawn on the CPU.
2. **Never scroll a list from inside its own `CollectionChanged` handler.** Step 5, fact 7.
3. **A dialog is the only way to ask the user anything.** `Console.ReadLine` never returns on a Wine console.
4. **No color literal may appear outside `Colors.xaml`**, and no `Opacity` and no bitmap effect may serve as a color tool. Step 10, Part A.

## Part A — the confirm dialog

Add `Views/ConfirmWindow.xaml` with its code-behind. Model it on `MessageWindow`, which holds the same `SurfaceOverlay` body and the same static helper shape.

```csharp
public static bool Ask(Window owner, string question, string confirmLabel, bool destructive)
```

- The window holds two buttons. The confirm button carries `ui:Kind.Value="Danger"` when `destructive` is true, and `Primary` when it is false.
- **`No` holds the focus, and it is the cancel button.** `Enter` on a destructive question must not delete anything.
- `Escape` closes the window and answers false. `DialogResult` carries the answer, as it does in `TextPromptWindow`.
- Set the window background and the three text options in the window itself. A window inherits none of them from the application. Step 10, Part F.

`IUserInteraction.Confirm` gains two optional parameters, so the five call sites that exist today still compile.

```csharp
bool Confirm(string question, string confirmLabel = "Yes", bool destructive = false);
```

`Dialogs.Confirm` then calls `ConfirmWindow.Ask` and no longer calls `MessageBox`. **`App.xaml.cs` keeps its own `MessageBox` fallback.** A render failure can break a new WPF window, and the dispatcher handler is the last place that can report it.

Mark the destructive questions in `MainViewModel`.

| Question                      | Label       | Destructive |
| ----------------------------- | ----------- | ----------- |
| Is this the install?          | `Yes`       | no          |
| Delete the profile            | `Delete`    | **yes**     |
| Remove the mod from the store | `Remove`    | **yes**     |
| Revert to vanilla             | `Revert`    | **yes**     |
| Move the mod store            | `Move them` | no          |

The mod store question is the reason that the label is a parameter at all. `No` there is a second legitimate action and not a cancel. It reads the new directory and leaves the mods where they are.

`--dialogtest` opens `MessageWindow` alone today. It must also open `ConfirmWindow` in both modes and `TextPromptWindow` once. A run under Wine is the only test that these windows get.

## Part B — the drag reorder, with a ghost slot

**Replace the marker model with a live reorder.** The dragged row moves inside the `Mods` collection to the position where it would land, and it draws as a ghost. No placeholder object exists. Nothing leaves the list, and no insertion line remains that can blink.

Three causes of the flicker disappear together.

1. `ModList_DragOver` calls `ClearDropMarkers` on every mouse move. That sweeps every row and repaints the whole list.
2. The 6 pixel gap between two rows belongs to the panel. A pointer that crosses a gap reaches the branch that finds no row, which clears the marker, and the line then disappears for one frame.
3. `ModList_DragLeave` clears every marker without a test. It fires when the pointer crosses from one row into the next.

**The rule that replaces them: act on a hit, and never clear on a miss.** A pointer in a gap, over the padding, or over the scroll bar leaves the preview where it is.

### The floating copy

A copy of the row floats under the pointer for the whole drag, the way a window follows the pointer. The recessed slot in the list shows where the row lands. The floating copy shows what travels.

`DragGhost` holds it. It is an `Adorner` that draws one bitmap and one 1 pixel `BorderStrong` outline.

**Host it on `DragLayer` and never on the mod list.** `DragLayer` is the name of the root `Grid` of the window. `AdornerLayer.GetAdornerLayer` returns the nearest layer above the element it gets, and **a `ScrollContentPresenter` carries a layer of its own**. The mod list sits inside a `ScrollViewer`, so a copy hosted there reaches that layer and clips to the viewport of the list. The layer above `DragLayer` comes from the `AdornerDecorator` of the window template, and nothing inside the window clips it.

Every point that positions the copy therefore comes in the coordinates of `DragLayer`. `MoveGhost` takes one of those.

**Capture the row once, at the start of the drag.** `DragGhost.Attach` renders the row into a `RenderTargetBitmap`, and a move then costs one blit of a small rectangle. A `VisualBrush` of the live row would draw the whole row again on every frame, and the software rasterizer pays that on the CPU.

**Capture before the row becomes the ghost slot.** The row recesses as soon as `IsDragSource` turns true, and the floating copy has to hold the resting look.

**Render through a `DrawingVisual`, not through `RenderTargetBitmap.Render(row)`.** `Render` reads a visual at the offset that its parent arranged it at, so a row that carries a margin comes out shifted inside the bitmap. A `VisualBrush` in a rectangle of its own has no offset.

**The copy draws at 0.8 alpha, so the user reads the list under it.** `DragGhost.Solidity` holds the value, and `OnRender` applies it with `PushOpacity`. Step 10 banned opacity as a color tool, and this does not break that rule. The ban covers a resting element, where the rasterizer composites the value on every repaint for as long as the window is open. This is one rectangle, for the length of one drag, and the alpha is the point of it.

**The copy travels the whole window and no further.** The adorner layer of the window spans every panel, so the copy crosses the right panel and the button bar. It stops at the window edge. A copy that reaches the desktop needs a top-level surface with `AllowsTransparency`, which is the layered-surface path that paints a hardware dropdown black under Wine. **Do not take that path for a drag visual.**

**The floating copy is cosmetic.** `Attach` returns null when the window holds no adorner layer or when the capture throws, and the drag then works with the slot alone.

This is the one adorner of the window. Step 10 kept the insertion line out of the adorner layer, because a marker on every row needs a render pass for each row. One floating visual is what an adorner is for, and it has to draw outside the list, which nothing inside the list can do.

### The cursor and the handle

**A `Path` answers a hit test only where it paints.** The handle draws three lines of 1.5 pixels with 2.5 pixel gaps, and the handle style carried the `SizeAll` cursor. The cursor therefore changed on every gap, which read as a flicker, and the target was three thin lines rather than the block that the icon suggests.

Two changes fix it.

1. `RowBorder` carries `Cursor="SizeAll"`. A drag starts anywhere on the row, so the whole row says so. The toggle pill sets `Hand` in its own style, which marks it as a control and not a drag zone.
2. `DragHandleArea` wraps the icon in a `Border` of 26 pixels, the full height of the row, with a transparent fill. **A transparent fill answers a hit test and a null fill does not.** The `DragHandle` style sets no cursor now.

### The view model

`ModRowViewModel` loses `DropBefore` and `DropAfter` with their fields. `IsSelected` and `IsDragSource` stay.

`MainViewModel` gains three members.

| Member                        | Holds                                                                       |
| ----------------------------- | --------------------------------------------------------------------------- |
| `PreviewMove(int, int)`       | Calls `Mods.Move` and renumbers `Order`. It touches no profile.             |
| `CancelPreview()`             | Moves the row back to the index that it started from. It saves nothing.     |
| `ResyncOrder()`               | Renumbers `Order` in place, sets `Status`, and refreshes conflicts and loaders. |

`ObservableCollection.Move` raises one `Move` notification, and WPF then moves the container that exists instead of building a new one. **Never call `ScrollIntoView` from any of the three.**

`MoveModTo` calls `RefreshMods` today, which clears `Mods` and builds every row again. That destroys and recreates the container of every row on each drop, which the software rasterizer shows as a flash. It also drops the scroll position. `MoveModTo` calls `ResyncOrder` instead.

**Conflict detection depends on the load order, so `ResyncOrder` must call `RefreshConflicts` and `RefreshLoaders`.** It first compares the identifier sequence of `Mods` against the entries of the profile, and it falls back to `RefreshMods` when the two disagree. That guard keeps one honest path instead of two.

`MoveSelected`, which serves the `Move up` and `Move down` buttons, takes the same path. One reorder path, not two.

**Keep the `Move up` and `Move down` buttons.** A keyboard user needs them, and they are the fallback if a drag fails under Wine.

### The row template

`ModRowTemplate` in `Parts.xaml` drops the two 2 pixel borders and the three-row grid that holds them. The row content becomes the direct child of `RowBorder`.

`RowBorder` gains `MinHeight="{StaticResource ModRowHeight}"`. That metric sits in `Metrics.xaml` today with no consumer. A uniform row height makes the ghost read as a slot.

The `IsDragSource` trigger sets `Opacity` today. **Drop that setter.** The ghost instead reads as recessed: `RowBorder` takes the `SurfaceBase` fill, and both text blocks take `TextSecondary`. A dashed outline marks the slot. Use one `Rectangle` with `StrokeDashArray`, `RadiusX`, and `RadiusY`, collapsed while nothing drags. A `Border` cannot draw a dash, and a collapsed `Rectangle` costs nothing.

### The input

The handlers stay on the `ItemsControl` in `MainWindow.xaml.cs`, because the row template is a shared resource with no code-behind.

1. `PreviewMouseLeftButtonDown` records the point, the row, and the index that the row starts from. It **returns early when the press lands inside a `ButtonBase`.** A press on the toggle of a row must never arm a drag.
2. `PreviewMouseMove` starts the drag after the pointer passes the minimum drag distance, as it does today.
3. `DragOver` finds the row under the pointer. It returns with no change when it finds none. On a hit it computes the target index from the vertical middle of that row, and it calls `PreviewMove` only when the target differs from the current index.
4. `Drop` reads the current index of the dragged row out of the collection and calls `MoveModTo`. The collection already shows that order, so the commit moves nothing on screen.
5. `DragLeave` does nothing. `ClearDropMarkers` disappears.
6. `GiveFeedback` sets `UseDefaultCursors` to false and holds one cursor for the whole drag. The default OLE cursors change between move and no-drop under Wine, and that change is itself a flicker.
7. `QueryContinueDrag` cancels the drag when the user presses `Escape`.
8. `DoDragDrop` returns the effect. When it returns `None`, which covers `Escape` and a drop outside the list, the handler calls `CancelPreview`. That path saves nothing.

**Auto-scroll near the top and the bottom edge is out of scope.** The list does not virtualize, and a scroll during a drag needs a Wine check of its own.

## Part C — the check mark

The `Path` of the `CheckBox` template carries no size, no margin, and no alignment. The default `Stretch` is `None`, so the layout box of the path runs from the origin to the far end of the geometry. The leading offset of `M 3,8` therefore inflates the top left and pushes the mark down and right inside the 14 pixel content area. The right tip at `x=13` plus the stroke reaches the border.

**Move the geometry to the origin, and center the box.**

```xml
<Path
  x:Name="Check"
  HorizontalAlignment="Center"
  VerticalAlignment="Center"
  Data="M 0,3.5 L 3.5,7 L 9.5,0"
  Stroke="{StaticResource OnAccent}"
  StrokeThickness="2" />
```

The bounds then run 0 to 9.5 across and 0 to 7 down. The desired size is 11.5 by 9 with the stroke, and it centers inside 14 by 14 with room on every side.

**Do not use `Stretch="Uniform"` here.** It scales a stroked geometry by rules that are hard to predict at this size.

## Part D — the toggle pill

Add a `TogglePill` style for `ToggleButton` to `Controls.xaml`. **Give it a key.** `ComboBoxToggleButtonStyle` is the only `ToggleButton` style today, and the `ComboBox` template names it. An implicit style would reach the wrong control.

- The track is a 36 by 20 `Border` with a 10 pixel radius. Off shows the `SurfaceRaised` fill and the `BorderDefault` edge. On shows `AccentDefault` for both.
- The knob is a 14 by 14 `Border` with a 7 pixel radius, aligned left with a 3 pixel margin. Off shows `TextSecondary`. On shows `OnAccent`.
- **Move the knob with a `TranslateTransform`, never with a margin.** Animate `X` over 120 ms in the `EnterActions` and the `ExitActions` of the `IsChecked` trigger. A render transform marks one small rectangle dirty and runs no layout pass. A `Thickness` animation invalidates the layout on every frame, which step 10 forbids.
- Hover shows `SurfaceHover` when off and `AccentHover` when on. Pressed shows `SurfacePressed` and `AccentPressed`. A disabled pill shows the `BorderDefault` edge and a `TextDisabled` knob. Focus shows a `BorderStrong` edge, and the style sets `FocusVisualStyle` to null.
- **Every state trigger belongs in `ControlTemplate.Triggers`.** Step 10, Part C.

The check box of the mod row becomes the pill. The binding to `Enabled` does not change, so `ModRowViewModel.Enabled` and `MainViewModel.OnModToggled` do not change either.

**Nothing else in the window becomes a pill in this step.** The ASI setting rows and the `Text` toggle keep their check boxes.

`ThemeTestWindow` gains a `Toggle pills` section in the shape of its check box section: one `Heading` and a `WrapPanel` of four toggles. Off, on, disabled off, and disabled on.

## Pitfalls

**A press on the pill must not start a drag.** The drag handlers sit on the `ItemsControl`, so they see every press inside a row. The pill is a bigger target than the check box was, so this defect gets worse if Part B skips the `ButtonBase` test.

**`RowBorder` is a name that code reads.** `FindRowElement` walks the visual tree and matches that exact string. A rename in `Parts.xaml` kills the drag, the drop, and the row selection with no build error.

**An adorner clips to the layer that hosts it, and a `ScrollViewer` brings a layer of its own.** The floating copy looked correct inside the list and vanished at the edge of it. The host, not the adorner, decides how far a drag visual travels.

**The preview must not save.** A drag that the user cancels writes nothing to the profile. Only `Drop` reaches `MoveModTo`.

**No test covers this step.** `BlackboxModManager.Tests` targets `net10.0` and references `Core` alone, so it cannot load a WPF dictionary. No type of `Core` changes here, so the test count stays at 327. `Profile.Move` and `Profile.MoveTo` already carry the reorder rules with their tests.

## Work

1. Add `ConfirmWindow`. Widen `IUserInteraction.Confirm` and point `Dialogs.Confirm` at the new window.
2. Mark the five questions of `MainViewModel` with a label and a destructive flag.
3. Extend `--dialogtest`. Run it under Wine.
4. Fix the check mark geometry.
5. Add the `TogglePill` style and the `--themetest` section. Run `--themetest` under Wine.
6. Add `PreviewMove`, `CancelPreview`, and `ResyncOrder` to `MainViewModel`. Point `MoveModTo` and `MoveSelected` at `ResyncOrder`.
7. Rewrite the row template and the drag handlers. Put the pill in the row.
8. Add `DragGhost` and the handle hit area. Point the cursor of the row at `SizeAll`.
9. Run the window under Wine. Drag over four rows, cancel one drag with `Escape`, and drop one.
10. Run `tools/run-deploy-test.sh` and the full test suite. Neither may change.

## Done when

Every dialog of the application is a themed window. A drag lifts a copy of the row under the pointer and marks the landing slot with a recessed ghost, and neither one flickers under Wine. `Escape` cancels the drag. The cursor over a row reads `SizeAll` everywhere except the toggle. The check mark sits centered in its box. The mod row carries a toggle pill. The load order, the profile on disk, and the deployed bytes all match the state before this step.
