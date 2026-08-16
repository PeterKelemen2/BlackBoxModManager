# 7-Zip

The application ships these files and starts `7z.exe` as a child process. It unpacks a 7z
or a rar with it. `Core/Store/SevenZipTool.cs` holds the call.

## Why

**SharpCompress decodes a solid 7z once for each entry.** The cost of an import grows with
the square of the entry count. The archive `NFSMWUHUD11302024a.7z` holds 1205 entries and
1.12 GB behind 98 MB, and SharpCompress needs more than 30 minutes for it. 7-Zip writes the
same files in 3.9 seconds.

See `docs/roadmap/98-known-upstream-defects.md`, defect 14, for the measurement.

SharpCompress stays in the project. It reads the listing of every archive, and it unpacks
the files when `7z.exe` is not there.

## The files

| File                | What it is                                    |
| ------------------- | --------------------------------------------- |
| `7z.exe`            | The console program. 7-Zip 26.02, x64.        |
| `7z.dll`            | The codecs. `7z.exe` does nothing without it. |
| `7-Zip-License.txt` | The license text of the release.              |
| `7-Zip-readme.txt`  | The notes of the release.                     |

`BlackboxModManager.App.csproj` names each file. **Never glob `7-Zip\*`.** The program and
the library must come from one release, and a glob would carry a stray file into the build.

## The license

7-Zip is under the GNU LGPL. Some code of `7z.dll` carries the unRAR license restriction.
The license asks for two things, and both hold here:

1. **Ship the license text.** `7-Zip-License.txt` travels with the two binaries.
2. **Keep the binaries unmodified.** Both files are byte-for-byte copies of the release.

Copyright (C) 1999-2026 Igor Pavlov. Read `7-Zip-License.txt` for the full text.

## To take a new version

1. Download the x64 release from <https://www.7-zip.org/>.
2. Replace `7z.exe`, `7z.dll`, `7-Zip-License.txt`, and `7-Zip-readme.txt`. Take all four
   from one release.
3. Write the new version in the table above.
4. Import a big solid 7z and read the log. The unpack step must still count up.
