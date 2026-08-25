# Third-party notices

BlackBox Mod Manager is under the MIT license. Read `LICENSE` for its terms.

This application uses the software below. Each entry names the license and the copyright
holder. The license texts follow in full.

The three forks live under <https://github.com/PeterKelemen2>. Run `git submodule status` to
read the commit that this build pins.

**The quoted license texts are verbatim.** Do not rewrite them, and do not apply the writing
rules of this repository to them.

## What this application ships

| Software | Version | License |
| --- | --- | --- |
| [Nikki](https://github.com/SpeedReflect/Nikki) | fork, branch `net10-retarget`, commit `4b84271` | MIT |
| [Endscript](https://github.com/SpeedReflect/Endscript) | fork, branch `net10-retarget`, commit `2a68b88` | MIT |
| [CoreExtensions](https://github.com/MaxHwoy/CoreExtensions) | fork, branch `net10-retarget`, commit `e64ba8e` | MIT |
| `LZCompressLib.dll` | the x64 build from the Nikki repository | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | 10.0.10 | MIT |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 1.0.0 | MIT |
| [Velopack](https://github.com/velopack/velopack) | 1.2.0, and the `Setup.exe` and `Update.exe` that it writes | MIT |
| [7-Zip](https://www.7-zip.org/) | 26.02, x64 (`7z.exe`, `7z.dll`) | GNU LGPL 2.1 or later, with an unRAR restriction on some code |
| [Inter](https://github.com/rsms/inter) | Regular and SemiBold, 18pt static | SIL Open Font License 1.1 |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) | Regular | SIL Open Font License 1.1 |
| [IBM Plex Sans](https://github.com/IBM/plex) | Regular | SIL Open Font License 1.1 |
| [.NET runtime](https://github.com/dotnet/runtime) | net10.0, and a release ships no part of it | MIT |

## What this application does not ship

**[Binary](https://github.com/SpeedReflect/Binary) by MaxHwoy is GPL-3.0, and this
application ships no part of it.** The user installs Binary 2.8.3 themselves and names the
directory. Three points follow, and each one is a rule that the code keeps.

1. **No code of Binary reaches this application.** The repository is not a submodule, no
   project references it, and nothing in this source tree comes from it. See `CLAUDE.md`.
2. **The Binary route starts `Binary.exe` as a separate process.** It passes two file paths on
   a command line and reads two log files back. The two programs share no memory and no link.
   See `docs/roadmap/15-binary-cli-route.md`.
3. **The hash lists stay where the user installed them.** Binary ships `mainkeys/<game>.txt`,
   and this application reads those files from the install of the user. It copies none of them
   and it redistributes none of them.

The same holds for `SpeedReflect.asi`. This application never places that file. A script that
runs the `speedreflect` command through the Binary route makes Binary copy it, out of the
install of the user and into the game of the user.

## The MIT license

Nikki, Endscript, and CoreExtensions carry one text with one copyright holder.

```
MIT License

Copyright (c) 2020 MaxHwoy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

`LZCompressLib.dll` is a native library of the Nikki repository, so the text above covers it.

### CommunityToolkit.Mvvm

Copyright (c) .NET Foundation and Contributors. All rights reserved. Licensed under the MIT
license, which the text above states.

### System.IO.Hashing

Copyright (c) Microsoft Corporation. All rights reserved. Licensed under the MIT license,
which the text above states.

### SharpCompress

Copyright (c) 2025 Adam Hathcock. Licensed under the MIT license, which the text above states.

### Velopack

Copyright © Velopack Ltd. All rights reserved. Authors: Velopack Ltd, Caelan Sayler, and
Kevin Bost. Licensed under the MIT license, which the text above states.

Velopack builds the installer and it runs the update check. `Services/UpdateService.cs` holds
the only call into the library.

**The build ships two programs of Velopack, and no csproj entry names either one.** `vpk pack`
injects them, so a reader who looks for a `Content` entry finds nothing. The two programs are
`Update.exe`, which sits beside the application and applies an update, and `Setup.exe`, which
is the installer that a release carries.

The package holds no dependency on its `net10.0` target, so nothing else reaches the build
through it.

### The .NET runtime and libraries

Copyright (c) .NET Foundation and Contributors. All rights reserved. Licensed under the MIT
license, which the text above states.

**A release ships no part of the runtime.** The build is framework-dependent. `Setup.exe` asks
Microsoft for the .NET 10 Desktop Runtime and installs it, so this application redistributes
none of those files. A developer who publishes with `--self-contained true` does carry the
runtime, and the text above covers that copy.

## 7-Zip

The application ships `7z.exe` and `7z.dll` and starts `7z.exe` as a child process. It unpacks
a 7z archive and a rar archive with it. `Core/Store/SevenZipTool.cs` holds the call, and
`src/BlackboxModManager.App/7-Zip/README.md` records why.

Copyright (C) 1999-2026 Igor Pavlov.

The release states the license of each file:

- `7z.dll` — the GNU LGPL as the main license for most of the code, the GNU LGPL with the
  unRAR license restriction for some code, the BSD 3-clause license for some code, and the
  BSD 2-clause license for some code.
- Every other file — the GNU LGPL.

**The full text travels with the two binaries.** `7-Zip-License.txt` and `7-Zip-readme.txt`
sit beside `7z.exe` in every build, because the release asks a redistribution in binary form
to reproduce that information. Read `7-Zip-License.txt` for the complete terms, including the
unRAR restriction and the two BSD texts.

Three facts satisfy the license, and each one is a rule that the build keeps.

1. **The binaries are unmodified.** Both files are byte-for-byte copies of the 7-Zip 26.02
   x64 release.
2. **The license text ships with them.** `BlackboxModManager.App.csproj` names all four files
   one by one. Never glob that directory.
3. **The source of 7-Zip is available.** Download it from <https://www.7-zip.org/>.

The application starts `7z.exe` as a separate process and links no part of 7-Zip into itself.
The application also runs without it. `Core/Store/ArchiveExtractor.cs` falls back to
SharpCompress when the program is absent.

## The fonts

The application embeds three families as WPF resources. `src/BlackboxModManager.App/Fonts/`
holds the files, and `Fonts/README.md` records why each one is there.

| Family | Copyright |
| --- | --- |
| Inter | Copyright 2020 The Inter Project Authors (<https://github.com/rsms/inter>) |
| JetBrains Mono | Copyright 2020 The JetBrains Mono Project Authors (<https://github.com/JetBrains/JetBrainsMono>) |
| IBM Plex Sans | Copyright © 2017 IBM Corp. with Reserved Font Name "Plex" |

All three carry the SIL Open Font License, version 1.1. **That license asks a copy that
bundles a font to carry the copyright notice and the license text.** The three license files
therefore travel with the executable, next to the notices that you are reading.

- `Inter-OFL.txt`
- `JetBrainsMono-OFL.txt`
- `IBMPlexSans-OFL.txt`

The license reserves a name. Do not release a modified Inter, JetBrains Mono, or IBM Plex Sans
under the original family name. This application modifies none of them, and it embeds each
file byte for byte.

## How to update this file

Change this file when the build starts to ship something new. Three changes count.

1. A new `PackageReference` in `src/BlackboxModManager.App` or `src/BlackboxModManager.Core`.
2. A new file that a `Content` entry or a `None Update` entry copies to the output directory.
3. A new file that the packaging step puts into the release. `tools/pack.ps1` runs `vpk pack`,
   and that program adds files of its own that no csproj entry names.

Read the license out of the package or the release, and never from memory. The `.nuspec` file
of a package holds a `license` element.
