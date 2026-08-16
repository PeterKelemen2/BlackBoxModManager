# Fonts

The application embeds these fonts in the assembly. It names no font of the host.

**A font family that the machine does not hold kills the process.** WPF reaches
`MS.Internal.Invariant.FailFast`, which no handler catches. A resource font removes that
risk, because the file travels with the assembly. See step 5, fact 9, in
`docs/roadmap/README.md`.

## The files

| File                        | Family that WPF reads | Weight | Use                                    |
| --------------------------- | --------------------- | ------ | -------------------------------------- |
| `Inter_18pt-Regular.ttf`    | `Inter 18pt 18pt`     | 400    | Every text of the window.              |
| `Inter_18pt-SemiBold.ttf`   | `Inter 18pt 18pt`     | 600    | A heading, a button, a card title.     |
| `JetBrainsMono-Regular.ttf` | `JetBrains Mono`      | 400    | The log, the conflict list, the paths. |
| `IBMPlexSans-Regular.ttf`   | `IBM Plex Sans`       | 400    | A spare for the log. No style uses it. |

Each file carries the `Resource` build action. A pack URI reads a `Resource`. It does not
read `Content` or `Embedded Resource`.

`Theme/Typography.xaml` holds the four family resources. **No other XAML file may write a
family string.**

## Four rules

1. **Read the family name with WPF, and not out of the font file.** The name table of the
   Inter files says `Inter 18pt`. WPF finds the family under `Inter 18pt 18pt`, because
   Windows adds the optical size a second time. A pack URI with a wrong family name
   matches nothing, and WPF then draws in the fallback face with no error. This command
   prints each family of this folder:

   ```powershell
   Add-Type -AssemblyName PresentationCore
   [Windows.Media.Fonts]::GetFontFamilies("<path to this folder>") | ForEach-Object { $_.Source }
   ```

2. **Take the Inter 18pt set.** The Inter download splits the optical size axis into
   `Inter 18pt`, `Inter 24pt`, and `Inter 28pt`. The 18pt set is the small end of the
   axis, and the window sets 11, 13, and 15 points.
3. **Ship a static file and never a variable file.** WPF reads a variable font as its
   default instance alone. It never reads an axis, so a variable file gives one weight and
   a synthetic bold for every other weight.
4. **Do not subset a file.** A subset that drops a glyph shows an empty box. A mod name
   comes out of an archive that somebody else built, and it can hold any character.

## Where the files came from

The three families come from Google Fonts, under the SIL Open Font License 1.1.

- Inter — `Inter-OFL.txt` and `Inter-README.txt`
- JetBrains Mono — `JetBrainsMono-OFL.txt` and `JetBrainsMono-README.txt`
- IBM Plex Sans — `IBMPlexSans-OFL.txt` and `IBMPlexSans-README.txt`

Each download holds a variable file, an italic variable file, and a `static/` directory.
This directory keeps the four files that the project ships, plus the license and the notes
of each download.

**Download the family again to add a weight.** Take the file out of the `static/`
directory, read its family name with rule 1, and add a row to the table above. The file
name is not the family name.

**JetBrains Mono is monospace, so a column of paths aligns.** The log and the conflict
list both hold paths. `LogFontFamilyAlternate` still names IBM Plex Sans. Point
`LogLine` at that key to take the log back to a proportional family.
