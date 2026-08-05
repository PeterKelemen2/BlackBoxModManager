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

| Signal                         | Editor     | Example                    |
| ------------------------------ | ---------- | -------------------------- |
| The value is `0` or `1`        | Check box  | `FixHUD = 1`               |
| The value parses as an integer | Number box | `ResX = 0`                 |
| The value parses as a decimal  | Number box | `LeftStickDeadzone = 10.0` |
| Everything else                | Text box   | `SAVEGAMES`                |

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

## Results

**Step 9 is done.** The window shows the options of an ASI mod grouped by section, a question mark marker holds the comment of each key, and a deploy writes the answers into the staging copy. Two mods that both ship `dinput8.dll` produce one prompt, one deployed file, and log lines that name the winner and every loser.

**The mods in the tests are synthetic.** This repository holds no real ASI mod. `tests/BlackboxModManager.Tests/AsiFixture.cs` reproduces the layout and the settings text that this file documents for the Widescreen Fix. **Replace that text with a real sample when one arrives**, and keep the awkward cases that it carries. Read "What is open".

### Part A — the settings editor

`src/BlackboxModManager.Core/Asi/IniDocument.cs` holds the reader and the writer.

**The raw text of every line is the truth.** The reader keeps each line with its terminator, and it records where the value starts and how long it is. The writer replaces those characters and leaves every other character alone. So the comment, the alignment, the blank lines, and the spelling of every key survive, and a user who compares the deployed file to the original sees one difference per changed value.

The writer also keeps the comment in its column. It grows or shrinks the whitespace run between the value and the comment character by the change in the length of the value, and it never leaves less than one space. A test compares the column of the comment before and after a change from `0` to `1920`.

Four rules of the format, each with a test.

| Rule                                           | What the reader does                                             |
| ---------------------------------------------- | ---------------------------------------------------------------- |
| A line in brackets starts a section.           | Every key below it belongs to that section.                      |
| A `key = value` line holds one option.         | The value is the text up to the comment, trimmed.                |
| A comment starts at `;` or at `#`.             | Both work. The line remembers which character the file used.     |
| A comment beside a key is the help text of it. | A comment on its own line belongs to the file or to the section. |

Five awkward cases, each with a test.

1. **A key outside every section is legal.** It lands in a section whose name is empty. The window shows it under "Keys above the first section".
2. **A duplicate key is legal and it makes an edit ambiguous.** Both lines stay in the model. The writer edits the first one, the reader warns, and the panel shows one row.
3. **A line with no equal sign passes through unchanged** and produces a warning.
4. **A comment character inside quotes is part of the value.**
5. **A value that the writer would break gets cleaned.** A line terminator would split one option into two lines, and a semicolon would turn the rest of the value into a comment. The writer removes both.

### The editor guess

The editor comes from the value alone, per the table above. **The application never reads the comment to build an editor.** A test asserts that `FMVWidescreenMode = 1` with the comment `(1 = Cropped | 2 = Stretched)` still gets a check box and not a drop-down.

**Every row carries a `Text` toggle.** That is the way back to free text entry that the work list asks for. A row with the toggle set shows a plain text box whatever the value looks like, so `FPSLimit = -1` and `ImproveGamepadSupport = 0` never trap the user. A changed row also shows a `Reset` button that puts the value of the mod back.

`IniValue.Classify` reads a decimal with the invariant culture. A comma-decimal locale would read `10.0` as one hundred, and the deployed file would then hold the wrong number.

### Where the values live

`ProfileEntry.IniSettings` holds the answers. The outer key is the path of the file inside the game directory and the inner key is `SECTION/Key`.

**The profile holds the differences from the mod and not a full copy of the file.** `ProfileEntry.SetIni` removes an answer whose value matches the value of the file. A user who sets a value back to the original therefore leaves no answer behind, and the deployed file matches the mod store byte for byte again.

`DeployPolicy.WritableExtensions` already held `.ini`, so the deploy copies a settings file instead of linking it. That is what makes the write safe. **Do not remove `.ini` from that set.** A write through a hard link would edit the mod store of the user and the vanilla copy at the same time.

### Part B — one loader

`src/BlackboxModManager.Core/Asi/ProxyScanner.cs` finds every loader file across the enabled mods and reads the answer of the profile.

**A deploy asks no question.** A profile fully determines the result of a deploy, and step 9 keeps that rule. So the deploy stops when a contested loader has no answer, and the message names every candidate with its size and its version. The window asks first and stores the answer, then the deploy runs. `LoaderPreflight` holds that rule and `DeployService` calls it before anything writes.

**The chosen mod supplies the file whatever the load order says.** The link engine reads `ProxyPlan.SkipByMod` and never places the copy of a mod that lost. A test switches on two mods and chooses the earlier one, then reads the deployed bytes. Before this step the later mod always won.

### The version of a candidate

`ProxyIdentityReader` reads three sources and stops at the first one that gives a version.

| Order | Source                                     | What it gives                              |
| ----- | ------------------------------------------ | ------------------------------------------ |
| 1     | `FileVersionInfo.GetVersionInfo`           | The version, the product, and the company. |
| 2     | A build string inside the bytes            | The name of a known loader build.          |
| 3     | The SHA-256, shortened to eight characters | Always an answer.                          |

**The hash runs for every candidate whatever the first two sources say.** Two candidates with one hash are the same file, and `ProxyContest.AllSameFile` reports that. The dialog then says that the choice changes nothing.

`FileVersionInfo` throws on a file that it cannot read. A truncated download and a file that another process holds both reach that path. The reader catches per candidate, reports `unknown`, and keeps every other candidate in the list.

The marker scan compares the ASCII form and the UTF-16 form of each marker, because a resource string inside a DLL is UTF-16. It reads at most 4 MB.

### The window

Two tabs joined the right-hand panel.

**Settings** shows one panel per settings file of the selected mod, one group per section, and one row per key. The row holds the key name, the editor, the question mark marker, the `Text` toggle, and the `Reset` button. A file that matches no plugin gets its own heading and a line that says so, because not every `.ini` beside a plugin is the settings of that plugin.

**Loader** shows one row per loader file with its current supplier, the version of that file, and a `Choose` button. The dialog lists every candidate with the mod name, the load order position, the size, and the version. It ranks nothing and it preselects only the current answer. The last row of the dialog is "Ask me again", which clears the stored answer.

**A resupply needs a redeploy, and the window says so.** Every row of the Loader tab that holds a contest ends with "A change needs a new deploy", and the status bar says the same after a change. The Settings header says it too.

`ChoiceWindow` is a list and not a drop-down. Each row carries three facts about a file, and a drop-down would hide two of them until the user opens it. A dropdown also paints black under Wine on the hardware path. See fact 8 of step 5.

### The types

| Type                                     | Holds                                                                  |
| ---------------------------------------- | ---------------------------------------------------------------------- |
| `Core/Asi/IniDocument.cs`                | `IniKey`, `IniLine`, `IniEntry`, `IniSection`, the reader, the writer. |
| `Core/Asi/IniValue.cs`                   | The editor guess and the flag helpers.                                 |
| `Core/Asi/AsiLayout.cs`                  | The plugin list, the settings files, and the name match.               |
| `Core/Asi/ProxyLoader.cs`                | `ProxyNames`, `ProxyIdentity`, the three-source version read.          |
| `Core/Asi/ProxyScanner.cs`               | `ProxyCandidate`, `ProxyContest`, `ProxyPlan`, the scan.               |
| `Core/Deploy/IniPlan.cs`                 | The answers of one mod, applied to the staging copy.                   |
| `Core/Deploy/LoaderPreflight.cs`         | The rule that stops a deploy with an unanswered contest.               |
| `App/ViewModels/AsiSettingsViewModel.cs` | The settings panel rows, groups, and files.                            |
| `App/ViewModels/LoaderRowViewModel.cs`   | One loader row and its dialog choices.                                 |
| `App/Views/ChoiceWindow.xaml`            | The pick-one dialog.                                                   |
| `tests/IniTests.cs`                      | 30 tests of the reader, the writer, and the guess.                     |
| `tests/AsiConfigurationTests.cs`         | 22 tests of the layout, the profile, the deploy, and the loader.       |
| `tests/AsiFixture.cs`                    | The synthetic ASI mod. **Replace with a real sample.**                 |

### The run

`dotnet test` reports 293 passing tests, up from 241.

`tools/run-deploy-test.sh` still passes end to end, and `GlobalB.lzc` grew from 5,145,778 to 8,263,472 bytes. That matches steps 1, 6, and 8 to the byte, so this step changed no container output.

The window starts under Wine, and the two new tabs load. That proves the XAML parses. **Nobody has looked at the panels with a real ASI mod in the store.**

### Facts that carry forward

1. **The raw line is the truth for an `.ini` file.** Never rebuild a line from the model. `IniLine` records the character span of the value for exactly that reason.
2. **An edited file cannot be verified against the mod store.** `DeployedFile.Edited` says that the deploy changed the content on purpose, and `StagingVerifier` then checks existence and a length above zero instead of a hash. Any future engine that rewrites a placed file must set that flag.
3. **The profile holds the differences and not the file.** An answer that matches the mod leaves the profile. That keeps the verify honest and it keeps a profile small.
4. **`LoaderPreflight` blocks and the window asks.** The same split as step 8: a preflight reports, and a gate inside the deploy path enforces. Keep both.
5. **A comment is not a schema.** The editor guess reads the value and never the comment, and every row has a way back to free text.

### What is open

**No real ASI mod has been through this.** Every test builds its mod by hand from the format that this file documents. The parser handles the documented shape and five awkward cases, and a real Widescreen Fix release could hold a sixth. **Import a real ASI mod, open the Settings tab, change a value, deploy, and read the file** before you call part A proven.

**The proxy name list holds one name.** `ProxyNames.Default` holds `dinput8.dll` alone, because that is the name that the samples use. `ProxyNames.Known` holds five more, and a mod that supplies one of those produces a note in the log that says that the last mod of the load order wins it. Move a name from `Known` to `Default` when a real mod uses it.

**No version resource has been read from a real loader.** The three-source read is tested against synthetic bytes. Source 1 needs a real DLL with a resource, and this machine holds none.

**A `scripts/ASILoader.asi` loader is not handled.** The pitfall names it. It is an `.asi` file and not a proxy DLL, so the name list does not reach it. No sample exists, so no code exists.

**The window says nothing when a deployed settings file differs from the last deploy.** The pitfall asks for that line. The staging copy is rebuilt from the vanilla copy on every deploy, so a file that the game or the user changed in the live directory is lost at the next deploy with no warning. Closing this needs the deploy to read the live file before the swap and compare it. That work is not in this step.
