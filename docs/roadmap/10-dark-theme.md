# Step 10 — the dark theme and the design system

Give the window one dark theme, one font, and one style per element. Replace the mod grid with a row list that the user can drag into order.

The window works and it looks like a WPF sample. It shows the default light theme, black text on white, with aliased type. Every button carries its own padding, and a new button needs the same three attributes again. The mod list is a `DataGrid` with seven columns and no way to drag a row.

This step is about the look and the input, and about nothing else. **No behavior of the application changes.** Every command, every deploy rule, and every dialog answer stays the same. The one exception is the drag reorder, which needs one new method in `Profile`.

## The rules that this step must respect

Read these four before you write any XAML. Steps 5 and 9 paid for all of them.

1. **The window runs under Wine with the software rasterizer.** `Rendering.Apply` sets `RenderMode.SoftwareOnly` there, because a hardware popup paints solid black. Every pixel of this theme is drawn on the CPU.
2. **A font family that the machine does not hold kills the process.** WPF reaches `MS.Internal.Invariant.FailFast`, and no handler catches it. The current XAML therefore names no family at all.
3. **Never scroll a list from inside its own `CollectionChanged` handler.** A drop that rebuilds the mod list must not call `ScrollIntoView` in that handler.
4. **A dialog is the only way to ask the user anything.** `Console.ReadLine` never returns on a Wine console.

## Part A — the token layer

`Theme.xaml` holds 27 lines and three styles. It becomes a set of dictionaries under `src/BlackboxModManager.App/Theme/`.

| File              | Holds                                                              |
| ----------------- | ------------------------------------------------------------------ |
| `Colors.xaml`     | Every color, as a `SolidColorBrush` with a key.                    |
| `Metrics.xaml`    | Corner radii, spacing, control heights, and the type sizes.        |
| `Typography.xaml` | The font family resources and the `TextBlock` styles.              |
| `Controls.xaml`   | One style per built-in control. This is the largest file.          |
| `Parts.xaml`      | The application parts. Cards, the mod row, the tab strip, the log. |

`App.xaml` merges the five in that order. **No color literal may appear outside `Colors.xaml`.** The current XAML holds `#40808080` in three places, and each one becomes a token reference.

### The colors

One name per role, never per value. A later light theme then needs one new `Colors.xaml` and no other change.

| Key              | Value     | Use                                             |
| ---------------- | --------- | ----------------------------------------------- |
| `SurfaceBase`    | `#16181D` | The window background.                          |
| `SurfaceRaised`  | `#1E2127` | A card, a tab body, a list.                     |
| `SurfaceOverlay` | `#262A32` | A dropdown, a tooltip, a dialog body.           |
| `SurfaceHover`   | `#2E333D` | The hover state of a row or a button.           |
| `SurfacePressed` | `#383E4A` | The pressed state.                              |
| `BorderDefault`  | `#333944` | Every resting border.                           |
| `BorderStrong`   | `#4A5261` | A focused border, a splitter.                   |
| `TextPrimary`    | `#E6E9EF` | Body text and headings.                         |
| `TextSecondary`  | `#A0A8B6` | The `Hint` style. It replaces `Opacity="0.75"`. |
| `TextDisabled`   | `#6B7280` | A disabled control.                             |
| `AccentDefault`  | `#4C8DFF` | The primary button, the selected row.           |
| `AccentHover`    | `#669EFF` | —                                               |
| `AccentPressed`  | `#3A78E0` | —                                               |
| `SuccessDefault` | `#3FB950` | The success button. Deploy.                     |
| `SuccessHover`   | `#57C767` | —                                               |
| `SuccessPressed` | `#2F9C3E` | —                                               |
| `DangerDefault`  | `#E5534B` | The error button. Remove, Revert to vanilla.    |
| `DangerHover`    | `#F0665E` | —                                               |
| `DangerPressed`  | `#C7423B` | —                                               |
| `WarningDefault` | `#D29922` | A conflict line, a mod problem.                 |
| `OnAccent`       | `#0B0D11` | The text on top of an accent fill.              |

**Drop `Opacity` as a color tool.** Opacity on a `TextBlock` composites on every frame, and the software rasterizer pays for it. A dimmer brush costs nothing.

### The metrics

- Corner radius: `RadiusSmall` 4, `RadiusDefault` 6, `RadiusCard` 10.
- Type size: `FontSizeSmall` 11, `FontSizeBody` 13, `FontSizeHeading` 15.
- Control height: `ControlHeight` 28. A row of the mod list is taller. See Part D.
- Spacing: `SpaceTight` 4, `SpaceDefault` 8, `SpaceLoose` 16.

## Part B — the font

The window must name a font family that the machine holds for certain. **Embed the font file in the assembly.** A resource font does not depend on the prefix, on `fontconfig`, or on `tools/run-app.sh`.

### What the folder holds

`src/BlackboxModManager.App/Fonts/` is **ready, and this part of the step is done**. It holds three font files, two license files, two download notes, and a `README.md` that repeats the rules below.

The two Google Fonts downloads that landed there first came to 30 MB and 96 static files. The window uses three of them, so the rest is pruned. Read the "What goes in the repository" section for what went and why. Both `OFL.txt` files stay, because the SIL Open Font License 1.1 asks for the text and it permits the bundle.

**No font tool is a dependency of this step.** The three files are ready to use as they are.

### The decision — Inter for the window, IBM Plex Sans for the log

**Inter carries every text of the window. IBM Plex Sans carries the log, the conflict list, and the paths.**

Inter wins the interface because it is drawn for an interface at a small size. It has a tall x-height and open counters, and the theme sets 11, 13, and 15 points.

IBM Plex Sans marks the log as a different kind of text. One weight is enough there, because a log line and a conflict line carry no heading.

**IBM Plex Sans is not monospace, so a column of paths does not align.** IBM Plex Mono is the family that aligns, and the folder does not hold it. Add `IBM_Plex_Mono` to the folder and change one resource if the alignment matters later. **Never name a system monospace family such as `Consolas` instead.** That is the failure of step 5, fact 9.

### The three files that ship

These three sit in `Fonts/` and nothing else does. **No italic ships, and no variable file ships.**

| File                      | nameID 1              | nameID 16    | nameID 17  | `usWeightClass` | Size   |
| ------------------------- | --------------------- | ------------ | ---------- | --------------- | ------ |
| `Inter_18pt-Regular.ttf`  | `Inter 18pt`          | `Inter 18pt` | `Regular`  | 400             | 335 KB |
| `Inter_18pt-SemiBold.ttf` | `Inter 18pt SemiBold` | `Inter 18pt` | `SemiBold` | 600             | 336 KB |
| `IBMPlexSans-Regular.ttf` | `IBM Plex Sans`       | —            | —          | 400             | 213 KB |

Those values come from the name table of each file. Four conclusions follow, and they decide the rest of Part B.

**The Inter family is named `Inter 18pt`, not `Inter`.** The Inter download splits the optical size axis into three families: `Inter 18pt`, `Inter 24pt`, and `Inter 28pt`. **Take the 18pt set.** It is the small end of the axis, and the theme never sets a size above 15 points. A pack URI that says `#Inter` resolves to nothing.

**Two weights are enough.** Regular at 400 carries the body text. SemiBold at 600 carries a heading, a button, a card title, and a mod name. The download also holds Medium at 500, and the theme does not need a third step.

**No file needs any work, and no font tool is a dependency of this step.** All three files are fixed instances with no `fvar` table, and the weight class of each one is correct. **Never ship a variable file instead.** WPF reads a variable font as its default instance alone and never reads an axis, so a variable file gives one weight and a synthetic bold for the rest.

**The SemiBold file declares its own family name.** `Inter 18pt SemiBold` sits at nameID 1, and the typographic family `Inter 18pt` sits at nameID 16. Regular and Bold share one nameID 1, because they are a RIBBI pair. Medium and SemiBold do not. The next section holds the consequence.

### Name the face, and do not ask WPF to match a weight

WPF may group `Inter 18pt` and `Inter 18pt SemiBold` into one family through nameID 16, so that `FontWeight="SemiBold"` selects the second file. It may instead read nameID 1 alone and treat `Inter 18pt SemiBold` as a family of its own, the way it treats `Segoe UI Semibold`. **Do not bet the theme on which one it does.** A wrong bet gives synthetic bold, which the software rasterizer smears at 13 points, and the fault is hard to see.

Write each family resource so that both models land on the same file. A `FontFamily` takes a list, and WPF walks it in order.

```xml
<FontFamily x:Key="UiFontFamily">
  /BlackboxModManager;component/Fonts/#Inter 18pt
</FontFamily>

<!-- The first name wins if WPF reads nameID 1. The second name wins if WPF groups
     the two files through nameID 16, and FontWeight then picks SemiBold out of the
     group. Both roads reach Inter_18pt-SemiBold.ttf. -->
<FontFamily x:Key="UiFontFamilyStrong">
  /BlackboxModManager;component/Fonts/#Inter 18pt SemiBold,
  /BlackboxModManager;component/Fonts/#Inter 18pt
</FontFamily>

<FontFamily x:Key="LogFontFamily">
  /BlackboxModManager;component/Fonts/#IBM Plex Sans
</FontFamily>
```

A style that wants the heavy face sets **both** the family and the weight:

```xml
<Setter Property="FontFamily" Value="{StaticResource UiFontFamilyStrong}" />
<Setter Property="FontWeight" Value="SemiBold" />
```

**`--fonttest` still has to confirm it.** Put the two families side by side at every size, with `Inter 18pt` at `FontWeight` Normal and SemiBold, and `UiFontFamilyStrong` below them. The two heavy lines must look the same, and both must look heavier than the Regular line and cleaner than a synthetic bold.

### What goes in the repository — done

The two downloads held 96 static files and 30 MB. The window uses three of them, so the directory now holds 924 KB.

The prune kept the three files that ship, both `OFL.txt` files, and both `README.txt` files. It deleted the two `static/` directories, the two variable files, and the two italic variable files. Three reasons:

1. The italic files, the Condensed and SemiCondensed sets, and the 24pt and 28pt Inter families have no use in this window and no plan to gain one.
2. A directory of 96 fonts invites the next person to name one that the build does not ship.
3. Google Fonts serves the same download again if a later step needs another weight. The two `README.txt` files name the download.

The three files moved out of the `static/` directories to `Fonts/` itself. A path that reads `Fonts/Inter_18pt-Regular.ttf` says that the file ships. A path under `static/` said that it was one of 54. The two license files and the two download notes carry a family prefix now, because they share one directory: `Inter-OFL.txt`, `Inter-README.txt`, `IBMPlexSans-OFL.txt`, and `IBMPlexSans-README.txt`.

`Fonts/README.md` holds the result: which three files ship, which family each one declares, the three rules below, and where to get a fourth weight. **Read that file before you add a font.** It exists so that nobody reads a name table twice.

**Do not subset the fonts.** A subset that drops a glyph shows an empty box for it. A mod name comes out of an archive that somebody else built, and it can hold any character. The three files cost 884 KB in the assembly, which is not worth the risk.

### Wire it up

The three files are in place. Three things remain.

1. Give each of the three the `Resource` build action in `BlackboxModManager.App.csproj`. **Not `Content`, and not `Embedded Resource`.** A pack URI reads a `Resource`. **Name the three files, and never glob `Fonts/*.ttf`.** A glob picks up a fourth file that somebody drops in, and a second face with the same family name then confuses WPF.
2. Declare the three family resources of the section above in `Typography.xaml`. **The text after `#` is the family name inside the file, not the file name.**
3. Reference those resources everywhere. No other XAML file may write a family string of its own.

### Antialiasing

Set three properties on every window, and inherit them from there.

- `TextOptions.TextFormattingMode="Ideal"` — this gives real subpixel positioning and the correct shape of the glyph. `Display` snaps stems to whole pixels and looks aliased at small sizes.
- `TextOptions.TextRenderingMode="Grayscale"` — **not `ClearType`.** ClearType writes color fringes that assume a known subpixel order. Under Wine, on the software rasterizer, on any rotated or scaled surface, that assumption can fail and the text then shows red and blue edges.
- `UseLayoutRounding="True"` — this keeps a 1 pixel border at 1 pixel.

**One probe answers all of Part B before the theme grows.** Add a `--fonttest` switch beside `--dialogtest` in `Program.cs`. It opens one window that shows three things side by side: both families at every size of the theme, the two weight-matching forms of the section above, and both text formatting modes. Run it under Wine and look at it. A wrong family name in a resource font may still reach `FailFast`, so this probe is also the safety check for the pack URIs.

## Part C — the controls

Restyle every control that the window uses. **A `Background` setter alone is not enough.** The default templates hardcode their own gradients, borders, and hover brushes, so a dark background under a default `ComboBox` template gives a dark box with a light gray arrow well. Each control below needs a full `ControlTemplate`.

| Control                     | Notes                                                                         |
| --------------------------- | ----------------------------------------------------------------------------- |
| `Button`                    | Four modes. See below.                                                        |
| `TextBox`                   | Rounded border, focus border, read-only variant, placeholder is out of scope. |
| `ComboBox`                  | The toggle, the arrow, the popup, and the item. The popup is the risky part.  |
| `CheckBox`                  | A rounded box with a drawn check mark. Use a `Path`, never a glyph font.      |
| `TabControl`, `TabItem`     | The tab strip of the right panel. See Part E.                                 |
| `ListBox`, `ListBoxItem`    | The log, the conflict list, and the choice dialog.                            |
| `ScrollViewer`, `ScrollBar` | Both must be styled. A default scrollbar is the loudest light element left.   |
| `GroupBox`                  | Becomes the card of Part E.                                                   |
| `ToolTip`                   | The `?` marker of the settings panel depends on it.                           |
| `StatusBar`                 | The bottom line.                                                              |
| `GridSplitter`              | A 1 pixel line with a wider hit area.                                         |
| `Window`                    | Background, foreground, font, and the text options.                           |

**Replace the focus visual.** The WPF default draws a dotted rectangle from the era of Windows 2000. Set `FocusVisualStyle="{x:Null}"` in the base styles and draw focus inside each template with a `BorderStrong` edge.

### The button modes

The user changes the mode and nothing else:

```xml
<Button Content="Deploy" ui:Kind.Value="Success" Command="{Binding DeployCommand}" />
```

Build it this way.

1. Add an attached property, `Kind.Value`, of an enum type `ButtonKind` with the members `Default`, `Primary`, `Success`, `Danger`, and `Quiet`.
2. Write one implicit `Style TargetType="Button"` with one `ControlTemplate`.
3. Bind the fill and the border of the template to a brush resource, and switch that brush from a `MultiDataTrigger` on the kind and on `IsMouseOver`, `IsPressed`, and `IsEnabled`.

**Put the state triggers inside the `ControlTemplate.Triggers`, not in the `Style.Triggers`.** A `Style` setter for `Background` loses against a template that binds `TemplateBinding Background`, and the resulting order of precedence is hard to read later.

A `Quiet` button carries no fill until the pointer enters it. Use it for `Folders` and for the per-row `Reset` in the settings panel.

**Every button keeps its current text.** This step renames no command and moves no button to another panel.

## Part D — the mod list

Replace the `DataGrid` with an `ItemsControl` of rows. Three reasons: the `DataGrid` template is the largest one to restyle, its column headers and cell editors fight a dark theme in six places, and it makes drag reordering harder than a plain row list does.

### The row

One `ModRowViewModel` per row, as now. The template holds:

- A drag handle at the left. A visible handle tells the user that the order is draggable.
- The enabled check box.
- The order number.
- The mod name in `TextPrimary`, with the kind, the game, the file count, and the size below it in `Hint`.
- A `SurfaceRaised` fill, `RadiusDefault` corners, and a `BorderDefault` edge.

Add these members to `ModRowViewModel`:

| Member         | Holds                                             |
| -------------- | ------------------------------------------------- |
| `IsSelected`   | The selection, so the row template can draw it.   |
| `IsDragSource` | True while the user drags this row. The row dims. |
| `DropBefore`   | True when the drop lands above this row.          |
| `DropAfter`    | True when the drop lands below this row.          |

Hover, selection, and the drag state all read from the row. Selection replaces the `SelectedItem` binding of the grid, and `MainViewModel.SelectedMod` keeps its meaning.

### The drag reorder

**Draw the insertion line from the row, not from an adorner.** Bind a 2 pixel `Border` at the top and the bottom of the row template to `DropBefore` and `DropAfter`. An adorner layer needs its own render pass, and the row already redraws.

The input side:

1. On `PreviewMouseLeftButtonDown`, record the point and the row. Do not start a drag yet.
2. On `PreviewMouseMove` with the button down, start the drag after the pointer passes `SystemParameters.MinimumHorizontalDragDistance`. A drag that starts on the first pixel breaks every click.
3. Call `DragDrop.DoDragDrop` with the mod id.
4. On `DragOver`, find the row under the pointer. Compare the pointer against the vertical middle of the row and set `DropBefore` or `DropAfter`.
5. On `Drop`, compute the target index and call the profile.
6. On `DragLeave` and after the drop, clear every flag. A stale insertion line is worse than none.

`Profile.Move(modId, offset)` moves by one position and cannot serve a drop. **Add `Profile.MoveTo(string modId, int index)`** beside it. It clamps the index, it returns false when nothing moves, and it holds the one awkward rule of the operation: after the entry leaves the list, every index above it shifts down by one. Test that in `BlackboxModManager.Tests`, which runs on Linux with no Wine.

**Keep the `Move up` and `Move down` buttons.** Two reasons. A keyboard user needs them, and `DoDragDrop` under Wine is unverified. If the drag fails there, the buttons are the fallback and the step still ships.

After a drop, `MainViewModel` saves the profile and calls `RefreshMods`. That clears and refills the collection. **Set the selection after the refill and never call `ScrollIntoView` from the `CollectionChanged` handler.** Step 5 fact 7 covers this.

## Part E — the right panel and the other parts

- **The card.** One style replaces every `GroupBox` in the window. It holds a header row in `Heading` and a `SurfaceRaised` body with `RadiusCard` corners. The Game box, the mod list box, and each section of the settings panel use it.
- **The tab strip.** `Mod`, `Settings`, `Loader`, `Log`, and `Conflicts`. An unselected tab shows `TextSecondary` and no fill. The selected tab shows `SurfaceRaised`, `TextPrimary`, and a 2 pixel `AccentDefault` edge. The body joins the selected tab with no seam.
- **The list row.** The variant list, the loader list, and the folder list all use the same `#40808080` bottom border today. Replace all three with one `ListRow` style over `BorderDefault`.
- **The settings row.** The key, the `?` marker, the editor, and the `Text` toggle. The `?` marker becomes a round `Quiet` glyph with a hover fill.
- **The log and the conflict list.** `LogFontFamily`, which is IBM Plex Sans, at `FontSizeSmall` over `SurfaceRaised` with no border. A conflict line reads in `WarningDefault`. Plex Sans is proportional, so a column of paths does not align. Part B says what to do about that.
- **The status bar.** One line of `TextSecondary` over `SurfaceBase`, with a top border.

## Part F — the dialogs

`ChoiceWindow`, `MessageWindow`, `TextPromptWindow`, and `FoldersWindow` inherit the theme from `App.xaml` as soon as Part C lands. Each one still needs a pass:

- Set the window background and the text options. A window does not inherit those from the application.
- `MessageWindow` gives its error body a `DangerDefault` heading and keeps the read-only `TextBox` as it is. The `Copy error` button stays.
- `FoldersWindow` uses the `ListRow` style of Part E.
- Check `--dialogtest` under Wine after the change.

## Pitfalls

**Do not use `DropShadowEffect` or `BlurEffect`.** A bitmap effect runs per frame on the CPU under the software rasterizer, and a shadow behind every card makes the window crawl. A border and a lighter surface give the same separation for free.

**Do not take over the window chrome.** A custom title bar needs `WindowStyle="None"` with `AllowsTransparency="True"`, and that makes the whole window a layered surface that WPF composites itself. That is the same path that paints a hardware dropdown black. **The window keeps the native frame, and the title bar therefore stays in the theme of the host.** Do not treat that as a defect of this step.

**A `Popup` is still the risk of the window.** The `ComboBox` dropdown and every tooltip live in a popup. The software rasterizer fixes the black paint, and this step does not change the render mode. Confirm each popup of the new theme under Wine anyway. A rounded corner on a popup border does not clip the popup itself, so keep a full opaque fill under it.

**No glyph font, no icon font, and no image asset.** Draw the check mark, the arrow, and the drag handle with `Path` geometry. A named glyph family is the failure of step 5, fact 9, in a new place.

**`Theme.xaml` disappears, and three keys in it do not.** `BoolToVisible`, `Heading`, and `Hint` appear across five XAML files. Keep all three keys and keep their names. Rename nothing in this step.

**A `StaticResource` cannot look forward.** A key must exist in a dictionary that `App.xaml` merges before the dictionary that reads it. This is why `Colors.xaml` and `Metrics.xaml` come first. A missing key throws at load time, and the window then never opens.

**The theme has no automated test.** `BlackboxModManager.Tests` targets `net10.0` and references `Core` alone, so it cannot load a WPF dictionary. The check is a run under Wine. `Profile.MoveTo` is the one part of this step that a test covers.

**Add a `--themetest` switch.** It opens one window that holds every control, in every mode, in every state: resting, hover, pressed, disabled, focused, and selected. One Wine run then checks the whole theme. Without it, each state needs a path through the real window to reach it.

**Keep the font linking in `tools/run-app.sh`.** The embedded font removes the dependency for the text that this theme names. It does not cover a family that WPF falls back to for a glyph that Inter lacks. Delete that block only after a run under a prefix with an empty `Fonts` directory works.

## Work

1. Write `Colors.xaml` and `Metrics.xaml`. Merge them in `App.xaml`.
2. Done. `Fonts/` holds the three files, the two licenses, the two download notes, and `README.md`.
3. Add `Typography.xaml` and `--fonttest`. **Run the probe under Wine. Confirm that the heavy face is real and not synthetic before you continue.**
4. Write `Controls.xaml` with the button, the text box, the check box, the tab, the list, the scrollbar, and the tooltip.
5. Add `--themetest`. Run it under Wine. Fix what it shows.
6. Convert `MainWindow.xaml` to the tokens and the card style. Remove every color literal.
7. Add `Profile.MoveTo` with tests.
8. Replace the `DataGrid` with the mod row list. Keep the move buttons.
9. Add the drag reorder. Confirm it under Wine.
10. Restyle the four dialogs. Run `--dialogtest`.
11. Run `tools/run-deploy-test.sh` and the full test suite. Neither may change.

## Done when

The window shows one dark theme under Wine, with antialiased type from the embedded font. Every button carries a mode and no local padding. The mod list shows one row per mod, and a drag reorders the load order and saves the profile. Hover, press, focus, and selection all read correctly on every control. `--themetest` and `--dialogtest` both open and show no light element. The deploy test still passes and the container bytes still match step 6.
