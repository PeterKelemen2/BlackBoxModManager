# Step 4 — Our layer over Endscript

Build the model that sits above the libraries. This layer discovers mods, models their options, resolves selections without a user, and extracts the edits that conflict detection needs.

**All of this is testable against `example_mods` with no UI.** Write it with tests. Step 5 and step 6 consume it.

## The model

```
ModPackage
  ├── Variants     : IReadOnlyList<BinaryVariant>   // from sibling VERSN1 manifests — multi-select
  └── each Variant ├── Manifest (Launch)
                   └── OptionSet? (from combobox or checkbox)  // single-select or boolean, may be null
```

A mod folder maps to **one** `ModPackage` with N variants. It does not map to N unrelated mods.

A variant can hold both mechanisms at once. Several manifests can each point at a script that holds a `combobox`. Do not collapse the two concepts into one list.

## Work

### 4.1 Variant discovery

1. Scan the mod root for files whose first line is `[VERSN1]`. Do not filter on the `.end` extension alone. Both file types share it.
2. Call `Launch.Deserialize` on each hit. Set `ThisDir` immediately after.
3. Reject a mod whose `Game` does not map to a supported `GameINT`. Mark it unsupported. Do not install it hopefully.
4. Treat any other `VERSNn` header as a hard parse error that names the file.

### 4.2 Option extraction

1. Parse each variant's `Endscript` with `EndScriptParser`.
2. Walk the commands for `ComboboxCommand` and `CheckboxCommand`.
3. For a combobox, record the description and the option names from `Options`.
4. For a checkbox, record the description. The options are always `disabled` at index 0 and `enabled` at index 1.
5. Store the extracted option set on the variant.

### 4.3 Selection persistence

1. Store the chosen option **by name**, not by index.
2. Store selections in the profile, so a profile fully determines the resolved edit list.
3. Resolve a stored name back to an index with `ISelectable.ParseOption` at deploy time.

### 4.4 Non-interactive resolution

1. Write the resolver that answers a `ProcessScript()` pause from stored selections.
2. Validate the resolved index against `Options.Length` before assigning `Choice`.
3. When no selection is stored, default to index 0 and log the assumption. Never block on a prompt.

### 4.5 Resolved command extraction

1. Flatten the selected branches into one linear command list, with `append` splices resolved.
2. Produce the conflict key for each edit command: `(targetFile, keyPath)`.
3. `targetFile` is the first argument. The value is the last token. Everything between is the key path.
4. Do not hardcode argument counts. Parse to the general shape.

## Pitfalls

**Store selections by name, not by index.** A mod update that reorders or inserts options silently changes what an index means. The user's saved choice then applies to the wrong branch, and nothing reports an error.

**Validate `Choice` before you assign it.** An out-of-range value surfaces as "Unable to find end to a selectable statement", which names neither the file nor the real problem. Throw your own error naming the mod, the script, and the option set. See defect 5.

**`IfStatementCommand` implements `ISelectable` but never pauses.** `ProcessScript` executes it inline. Do not present it as a user-facing option. Filter your option extraction on the concrete command types.

**Checkbox block headers use fixed names.** The script must contain blocks named `disabled` and `enabled`. Do not invent your own labels for the resolver. Display text is a UI concern, and the underlying names are fixed.

**Do not build conflict detection on `Files`.** It is a load-and-verify superset, not an edit list. Every `1 Lap` manifest declares `GLOBALA.BUN`, which no command touches. Keying on `Files` reports a false conflict between any two mods that merely load the same container.

**Do not build conflict detection on `Links`.** All four inspected manifests hold identical `Links`, by two unrelated authors. It is per-game boilerplate that Binary emits. Keying on it flags every pair of Underground 2 mods.

**Same key with the same value is benign.** The `1 Lap` `ALL` variant is the union of the other four. A user may legitimately enable `ALL` and `URL` together. Report only differing values as conflicts.

**Compare paths and key segments case-insensitively.** Normalize separators first. `GLOBAL\GLOBALB.LZC` and `GLOBAL/GlobalB.lzc` are the same target.

**Write a quote-aware tokenizer, or use the library one.** `CoreExtensions.Text.RegX.SmartSplitString` toggles on quotes and splits only on the space character. It does not treat tabs as separators. It strips the quotes from the tokens it emits. A plain `Split(' ')` breaks every `combobox` line.

**Preserve the original text of every value.** Observed floats include `-0.19500002` and `2.746582`. A default `ToString()` round trip corrupts them. Carry values as strings plus a parsed hint. Parse with `InvariantCulture`.

**Fail loudly on an unknown verb.** The vocabulary holds 48 entries and we handle few. A skipped edit produces an install that is wrong in a way the user cannot see. Name the file and the line.

**Detect `append` cycles.** Splice recursively with a visited set and a depth cap. Appended files carry their own `[VERSN2]` header, so tolerate and skip a header in spliced content.

## Done when

Both example mods parse into the model, their options extract correctly, a stored selection resolves without a prompt, and the flattened command list produces the expected conflict keys. All of it under test, with no UI.

## Results

Step 4 is done. The layer lives in `src/BlackboxModManager.Core/Mods`. It reads text only. It never touches a game directory, so every test runs on native Linux with no Wine and no game present.

| Type                                    | Holds                                                             |
| --------------------------------------- | ----------------------------------------------------------------- |
| `ModPackage`, `ModVariant`              | One folder, N variants. 4.1.                                      |
| `ModOptionSet`, `ModOption`             | One question and its options. 4.2.                                |
| `ModPackageReader`                      | Finds manifests, reads them, extracts the questions. 4.1 and 4.2. |
| `ModSelections`, `VariantSelection`     | The stored answers, by name. 4.3.                                 |
| `SelectionResolver`                     | Answers a pause with no user. 4.4.                                |
| `ScriptFlattener`, `ResolvedScript`     | The linear edit list of the chosen branches. 4.5.                 |
| `EditKey`, `ResolvedEdit`               | The conflict key and the original value text. 4.5.                |
| `ConflictDetector`                      | Same key and a different value. The seam for step 6.              |
| `ScriptAppendGraph`                     | Finds an append loop before the library parser recurses.          |
| `ScriptReader`, `ScriptText`, `ModPath` | Parse failures that name a place, the tokenizer, path resolution. |

`tests/BlackboxModManager.Tests` holds 109 tests. They run against the real `example_mods` and against hand-built mods in a temporary directory.

### The numbers

| Fact                       | Value                                                 |
| -------------------------- | ----------------------------------------------------- |
| 1 Lap package              | 1 package, 5 variants, 0 questions                    |
| Camera package             | 1 package, 1 variant, 1 combobox with 2 options       |
| `1 Lap URL Races` resolved | 51 edits, all `update_incareer`, all keyed            |
| Camera, option 0           | 450 edits, all from `[1]_Camera_MOD_NFSMW_TO_U2.end`  |
| Camera, option 1           | 744 edits, all from `[0]_Restore_Camera_Settings.end` |
| `ALL` against `URL`        | 0 conflicts, and `ALL` covers every `URL` key         |

The 450 and 744 in step 3 were per-file parse counts. They are also the resolved branch sizes. The two readings agree.

### Four corrections

**There are five `1 Lap` manifests, not four.** `ALL`, `CIRCUIT`, `STREET`, `SUV`, and `URL`. The brief and step 1 both said four. Both now say five. The statement "`ALL` is the union of the other four" was always right and stays.

**The model needs a list of option sets, not one nullable option set.** A script can hold more than one selectable, and `ProcessScript` then pauses more than once. `ModVariant.OptionSets` is a list. The resolver answers the pauses in order.

**This layer resolves paths itself.** A manifest writes `MOD\URL.end` and the file sits at `MOD/URL.end`. Wine resolves the separator and the case, and a native run resolves neither. `ModPath.Resolve` handles both, so the layer works on Linux with no Wine. Without it every test failed with `BadScript`.

**An `if` command stops a static flatten.** `ProcessScript` evaluates one against the loaded containers, and this layer has none. `ScriptFlattener` reports that and never guesses a branch. Neither example mod uses `if`. Step 8 owns the fix.

### Two notes for the next steps

**An unknown verb and an option block header both parse to `OptionalCommand`.** The library gives them the same type, and `Type` reads `invalid` for both. Only the enclosing question tells them apart. `ScriptFlattener` compares the token against the options of the enclosing selectable. A header is a jump. Anything else stops the run and names the file and the line.

**`EditKeyExtractor.KeyedVerbs` holds four verbs today.** They are `update_collection`, `update_incareer`, `update_string`, and `update_texture`. The source confirms that all four follow the shape `verb file name... value`, with no fixed argument count. Every other verb produces `EditKind.Other` and no key. Step 8 extends the set. Add a verb only after the source confirms its shape.
