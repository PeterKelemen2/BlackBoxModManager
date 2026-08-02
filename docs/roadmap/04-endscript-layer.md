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
