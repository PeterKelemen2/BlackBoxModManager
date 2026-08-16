# Step 13 — the import shows its work

## The report

A user added `NFSMWUHUD11302024a.7z`, which is 98 MB. The log wrote `Import NFSMWUHUD11302024a.7z.` and then nothing. The mod list stayed as it was. Nothing said whether the import ran, waited, or died.

The import ran. It needed more than 30 minutes.

## The cause

The archive is a solid 7z. It holds 1205 entries and 1.12 GB behind one compressed block.

SharpCompress decodes that block from its start for every entry, so the cost of an import grows with the square of the entry count. The measurement and the numbers sit in [98-known-upstream-defects.md](98-known-upstream-defects.md), defect 14.

`7z.exe` writes the same 1205 files in 3.9 seconds.

## The result

| Path                            | Time      |
| ------------------------------- | --------- |
| SharpCompress, before this step | 30+ min   |
| 7-Zip, after this step          | 3.6 s     |
| The whole import, after         | **~18 s** |

The rest of the 18 seconds is the inspect step. It opens each of the 1205 files to decide the kind of the mod.

## Part A — the import row

The mod list draws one row for each import that still runs, under the mods. That is where the finished mod lands, so the row does not move when the import ends.

The row carries the name that the mod takes, the step, the count, the file that the step reads, and a bar. `ImportRowViewModel` holds it. `MainViewModel.Imports` holds the rows, and `ImportAsync` adds one before the work starts and drops it in a `finally` block.

**The import rows stay out of `ModList`.** That control carries the drag, the drop, and the row menu. Each of those three acts on a mod that the store holds, and an import has no mod yet. A second `ItemsControl` under the first one draws them, and both sit in one `StackPanel` inside the scroll viewer.

**The empty state reads two counts.** It shows while `Mods.Count` is 0. The first import into an empty profile draws a row, so the panel now tests `Imports.Count` as well. A single `DataTrigger` cannot test two bindings. Use a `MultiDataTrigger`.

## Part B — the progress contract

`Core/Store/ImportProgress.cs` holds the report. It carries the step, the count, the total, and the name of the file.

`ModImporter.Import` takes an `IProgress<ImportProgress>`. The three steps are `Unpack`, `Inspect`, and `Store`.

**Build the `Progress<T>` on the window thread.** That class keeps the synchronization context of the thread that builds it, and it posts every report back to that context. A `Progress<T>` that a background thread builds posts to the thread pool, and the report then writes view model properties from the wrong thread.

**A report costs a message to the window thread.** A zip of ten thousand small files writes faster than a window draws, and one report for each file fills the message queue. `StageReporter` holds the rate to one report each 50 milliseconds. The first file and the last file always report, so the bar starts at once and ends full.

**The log takes one line for each step, and no more.** The window writes the count once and the name of each step once. The row carries the moving numbers, and the log stays readable.

## Part C — 7-Zip

`Core/Store/SevenZipTool.cs` starts `7z.exe` as a child process. The files sit in `src/BlackboxModManager.App/7-Zip/` and travel to the output directory. Read [that README](../../src/BlackboxModManager.App/7-Zip/README.md) before you touch them.

A child process needs no interop layer, it cannot corrupt the memory of this process, and a crash of the decoder arrives as an exit code.

**Close the standard input of the child.** An archive that wants a password stops and waits for an answer. A closed input gives the program an end of file, so it fails in a second instead of waiting forever.

**Exit code 1 is a warning, and the extraction is still good.** Code 2 and above is a failure. Treat only those as an error.

**`-bb1` names directories too.** The switch writes one line for each item, in the form `- path\to\name`. A line that ends with a separator names a directory. Count the other lines.

**A failure of 7-Zip does not fall back to SharpCompress.** The fallback would repeat a broken read for half an hour and report the same failure at the end. The fallback covers one case only, which is a build with no `7z.exe` beside it.

## Part D — the safety guard moved

`ArchiveExtractor.SafePath` refuses an entry name that writes outside the target directory. An archive comes from the internet, and its entry names are not trustworthy.

7-Zip writes the files, so our code no longer names each file as it writes it. **`ReadListing` therefore runs the guard over the whole listing before any extractor writes one byte.** It reads the header through SharpCompress, which costs milliseconds, and it throws for an unsafe name and for an entry that needs a password.

That listing also gives the window the total for the bar.

## Part E — the directory import

A directory import copies the tree. `CopyTree` was recursive, and a recursive walk cannot count the files before it starts.

It now walks the directories first and creates each one, then walks the files with `SearchOption.AllDirectories`. The count is then known, and an empty directory of the source still reaches the target.

## What this step did not do

The inspect step is now the longest part of a big import, at about 14 seconds for 1205 files. `ModClassifier.Classify` opens each file to test it for a manifest. Nobody measured whether the test can read fewer bytes.
