# Fonts

The application embeds these fonts in the assembly. It names no font of the host.

**A font family that the machine does not hold kills the process.** WPF reaches
`MS.Internal.Invariant.FailFast`, which no handler catches. A resource font removes that
risk, because the file travels with the assembly. See step 5, fact 9, in
`docs/roadmap/README.md`.

## The files

| File                      | Family in the name table | Weight | Use                                    |
| ------------------------- | ------------------------ | ------ | -------------------------------------- |
| `Inter_18pt-Regular.ttf`  | `Inter 18pt`             | 400    | Every text of the window.              |
| `Inter_18pt-SemiBold.ttf` | `Inter 18pt SemiBold`    | 600    | A heading, a button, a card title.     |
| `IBMPlexSans-Regular.ttf` | `IBM Plex Sans`          | 400    | The log, the conflict list, the paths. |

Each file carries the `Resource` build action. A pack URI reads a `Resource`. It does not
read `Content` or `Embedded Resource`.

`Theme/Typography.xaml` holds the three family resources. **No other XAML file may write a
family string.**

## Three rules

1. **The Inter family is named `Inter 18pt`.** The Inter download splits the optical size
   axis into `Inter 18pt`, `Inter 24pt`, and `Inter 28pt`. A pack URI that says `#Inter`
   resolves to nothing. The 18pt set is the small end of the axis, and the window sets 11,
   13, and 15 points.
2. **Ship a static file and never a variable file.** WPF reads a variable font as its
   default instance alone. It never reads an axis, so a variable file gives one weight and
   a synthetic bold for every other weight.
3. **Do not subset a file.** A subset that drops a glyph shows an empty box. A mod name
   comes out of an archive that somebody else built, and it can hold any character.

## Where the files came from

Both families come from Google Fonts, under the SIL Open Font License 1.1.

- Inter — `Inter-OFL.txt` and `Inter-README.txt`
- IBM Plex Sans — `IBMPlexSans-OFL.txt` and `IBMPlexSans-README.txt`

Each download holds a variable file, an italic variable file, and a `static/` directory.
The two downloads come to 30 MB and 96 static files. This directory keeps the three files
that the window uses, plus the license and the notes of each download.

**Download the family again to add a weight.** Take the file out of the `static/`
directory, read its family name, and add a row to the table above. Read the family name
with `fc-scan` or with `fontTools`. The file name is not the family name.

**IBM Plex Sans is not monospace, so a column of paths does not align.** Add IBM Plex Mono
and change `LogFontFamily` if the alignment matters.
