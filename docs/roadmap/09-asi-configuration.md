# Step 9 — ASI configuration and the loader

Let the user configure an ASI mod from the window, and settle which `dinput8.dll` the game gets.

Step 5 deploys an ASI mod as a set of drop-in files. That is enough to install it and no more. Two things stay out of reach.

1. **The settings live in an `.ini` file that the user has to edit by hand.** The application shows nothing.
2. **Every ASI mod ships its own `dinput8.dll`.** The game directory holds one file at that path, so the mods overwrite each other in load order. The application never says which one won.

This step closes both.

## Part A — the `.ini` editor

An ASI plugin reads its settings from an `.ini` file beside the plugin. The Widescreen Fix for Underground 2 is the reference sample. Read `NFSUnderground2.WidescreenFix.ini` in the test data before you write the parser.

### The format

```ini
[MAIN]
ResX = 0                    ; Use this option to control the horizontal resolution.
FixHUD = 1                  ; Corrects HUD aspect ratio.
FMVWidescreenMode = 1       ; FMVs will appear in fullscreen for 16:9. (1 = Cropped | 2 = Stretched)

[MISC]
LeftStickDeadzone = 10.0    ; Controls the deadzone of the left analog stick.
CustomUserFilesDirectoryInGameDir = SAVEGAMES    ; Use '0' to disable.
```

Four things matter.

- A line in brackets starts a section. Every key below it belongs to that section until the next bracket line.
- A `key = value` line holds one option.
- A comment starts at `;` or at `#`. The sample uses `;` alone. Accept both, and remember which character the file used.
- A comment on the same line as a key is the help text of that key. A comment on its own line belongs to the file or to the section.

### What the window shows

One panel per selected ASI mod. One group per section. One row per key.

The row shows the key name, an editor for the value, and a `?` marker when the key has a trailing comment. The `?` shows the comment text in a tooltip. A key with no comment shows no marker.

The editor type comes from the value alone. **The application never reads the comment to build an editor.**

| Signal                             | Editor     | Example                    |
| ---------------------------------- | ---------- | -------------------------- |
| The value is `0` or `1`            | Check box  | `FixHUD = 1`               |
| The value parses as an integer     | Number box | `ResX = 0`                 |
| The value parses as a decimal      | Number box | `LeftStickDeadzone = 10.0` |
| Everything else                    | Text box   | `SAVEGAMES`                |

A comment such as `(1 = Cropped | 2 = Stretched)` reads like a list of choices. **Do not turn it into a drop-down.** The key keeps a plain input field, and the `?` marker shows the full comment. The comment already names the choices, so the user reads them there and types the number.

**The editor type is a guess, and a wrong guess must not trap the user.** `FPSLimit = -1` looks like a number and means "monitor refresh rate". `ImproveGamepadSupport = 0` looks like a check box and holds five states. Give every row a way back to free text entry.

### Where the values live

The profile holds the answers. The file in the mod store never changes.

`ProfileEntry.Selections` already holds the answer of a Binary combobox. Add a parallel map for the `.ini` answers, keyed by section and key. A deploy then writes the file into the staging copy with the answers applied.

`DeployPolicy.WritableExtensions` already holds `.ini`, so the deploy copies the file instead of linking it. That is what makes the write safe. **Do not remove `.ini` from that set.**

### Work

1. Write the reader. It returns sections, keys, values, comments, and the raw text of every line.
2. Write the writer. It takes the parse result and a map of new values, and it returns the file text.
3. Add the answer map to `ProfileEntry.Selections`.
4. Apply the answers during the deploy, after the link engine places the file.
5. Add the panel to the window.

## Part B — one `dinput8.dll`

`dinput8.dll` is the ASI loader. The game loads it because the name matches a system library that the game imports. The loader then reads every `.asi` file in the `scripts` directory.

The game directory holds exactly one file at that path. Several mods ship one each. In practice one loader runs the plugins of every mod, whatever mod supplied it.

**The last mod in load order wins today, and the log never mentions it.** That is the defect.

### What to build

1. Find every `dinput8.dll` across the enabled mods during the deploy.
2. When two or more mods supply the file, ask the user which one to use. Store the answer in the profile.
3. Deploy that one file. Skip the rest.
4. Log the choice, and log every mod whose copy the deploy skipped.
5. Show the same choice in the window, so the user can change it without a new import.

### The version of a candidate

The dialog shows the mod name, the file size, and the version of each candidate. A user who does not know the difference needs those three facts.

Read the version in this order. Stop at the first source that answers.

1. `FileVersionInfo.GetVersionInfo`. Read `FileVersion`, `ProductVersion`, `ProductName`, and `CompanyName`. A DLL that carries a version resource answers here.
2. The build string inside the file. An Ultimate ASI Loader build holds a text marker. Scan the bytes for it when step 1 gives nothing.
3. The SHA-256 of the file, shortened to eight characters. This always answers, and it lets the user tell two candidates apart even with no version at all.

**A missing version is normal, and it is not an error.** Show `unknown` in the version column and show the hash. Never hide a candidate because it carries no version.

Two candidates with the same hash are the same file. Say so in the dialog, and let the user pick either one without further thought.

### Changing the choice later

The stored answer is not final. The window shows the current supplier of each proxy name, and it lets the user pick another one at any time.

1. Show the loader choice as a row in the window, next to the mod list.
2. Open the same dialog from that row. It lists every enabled mod that supplies the file, with the version of each.
3. A new choice replaces the stored answer, and the next deploy places the new file.
4. Let the user return to "ask me again" and clear the stored answer.

**A resupply needs a redeploy, and the window must say so.** A changed answer alone changes nothing in the staging copy.

**Keep the first answer until the user changes it.** A deploy that already holds a valid choice asks nothing. A deploy where the chosen mod is gone, disabled, or no longer supplies the file asks again, and the message names the reason.

## Pitfalls

**A comment is not a schema.** The `(1 = Cropped | 2 = Stretched)` text is a human sentence that happens to read like a list. A mod author writes it any way they want. Pass the comment through to the tooltip and change nothing in it. A parser that reads choices out of it produces a wrong list, and a drop-down built from a wrong list locks the user out of a legal value.

**Round-tripping a file destroys it if you rebuild it from the model.** Keep the raw text of every line. Change the value inside the line that holds it, and leave the rest of the line alone. This preserves the comment, the alignment, and the blank lines. A user who compares the deployed file to the original must see one difference per changed value.

**Duplicate keys are legal in an `.ini` file and mean nothing good.** Two `FixHUD` lines in one section produce an ambiguous edit. Keep both in the model, edit the first, and warn.

**A key outside any section is legal.** The sample has none. Handle the case with an unnamed section rather than a crash.

**Not every `.ini` beside a plugin is the settings of that plugin.** The Widescreen Fix also ships a `.dat` file that holds the HUD offsets. Match the `.ini` name to the `.asi` name first. Show an unmatched `.ini` under its own heading, and do not claim that it belongs to a plugin.

**The game writes to some `.ini` files, and the user edits others outside the application.** The staging copy is the live file. A deploy overwrites it. Say so in the log when the deployed text differs from the text of the last deploy.

**`dinput8.dll` is not the only loader name.** Some mods use `dsound.dll`, `vorbisFile.dll`, or a separate `scripts/ASILoader.asi`. Build part B around a list of proxy names, not around one string. Start the list with `dinput8.dll` alone, because that is the name that the samples use.

**A proxy DLL forwards to the real system library.** A version that forwards wrongly breaks sound or input rather than the plugin. This is why the user chooses and the application does not. **Never pick a `dinput8.dll` automatically.**

**Version numbers on these files are often absent or wrong.** Many builds of the Ultimate ASI Loader carry no version resource at all, and a mod author who renames a file changes no resource inside it. The dialog shows what the file holds. It does not rank the candidates, and it does not preselect the highest number.

**`FileVersionInfo` throws on a file that it cannot read.** A truncated download and a file that another process holds both reach that path. Catch the failure per candidate, show `unknown`, and keep the other candidates in the list.

## Done when

The user opens an ASI mod in the window, sees its options grouped by section, hovers a `?` and reads the comment, changes a value, deploys, and finds the new value in the staging copy. Two mods that both ship `dinput8.dll` produce one prompt, one deployed file, and a log line that names the winner and the losers. The prompt shows a version or a hash for every candidate. The user changes the supplier later from the window, redeploys, and gets the other file.
