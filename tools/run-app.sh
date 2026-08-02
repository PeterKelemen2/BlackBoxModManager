#!/usr/bin/env bash
# Builds the application for Windows and starts it under Wine.
#
# The application must run under Wine on this machine. Three reasons:
#   1. It is a WPF application, and WPF is Windows only.
#   2. LZCompressLib.dll is a native Windows x64 library. Step 6 needs it.
#   3. The manifests declare GLOBAL\GLOBALB.LZC and the file on disk is GLOBAL/GlobalB.lzc.
#      Wine resolves the separator and the case. Native Linux .NET does not.
#
# Usage:
#   tools/run-app.sh
#
# Environment:
#   APP_RUNNER      "wine" (default) or the path to another wine executable.
#   APP_WINEPREFIX  The prefix to run in.
#   APP_OUT         The publish directory.
#   APP_QUIET       "0" keeps the graphics driver noise of Wine.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${APP_OUT:-$root/artifacts/app}"
prefix="${APP_WINEPREFIX:-$HOME/.local/share/blackbox-app-wine}"
runner="${APP_RUNNER:-wine}"
quiet="${APP_QUIET:-1}"

# A self-contained publish carries the .NET runtime and the Windows Desktop runtime, so
# the prefix needs no installed runtime.
publish=(dotnet publish "$root/src/BlackboxModManager.App/BlackboxModManager.App.csproj"
	-c Release -r win-x64 --self-contained true
	-o "$out" --nologo -v quiet)

# The third-party projects emit many warnings. Show the build output only when it fails.
if ! build_log="$("${publish[@]}" 2>&1)"; then
	echo "$build_log" >&2
	echo "ERROR: The publish failed." >&2
	exit 2
fi

export WINEPREFIX="$prefix"
export WINEDEBUG="${WINEDEBUG:--all}"

mkdir -p "$prefix"

# Give the prefix real font files.
#
# A fresh prefix holds an empty drive_c/windows/Fonts. WPF enumerates that directory
# itself and it does not read the fontconfig of the host. A font family that it cannot
# resolve ends in MS.Internal.Invariant.FailFast, which kills the process with no dialog
# and no catchable exception. Link the fonts of the host, so that the window has more than
# one family to fall back to.
fonts="$prefix/drive_c/windows/Fonts"

if [ -d "$prefix/drive_c/windows" ] && [ -z "$(ls -A "$fonts" 2>/dev/null)" ]; then
	mkdir -p "$fonts"

	for dir in /usr/share/fonts/liberation /usr/share/fonts/TTF /usr/share/fonts/dejavu \
		/usr/share/fonts/truetype/liberation /usr/share/fonts/truetype/dejavu; do
		[ -d "$dir" ] || continue

		for file in "$dir"/*.ttf; do
			[ -f "$file" ] || continue
			ln -sf "$file" "$fonts/$(basename "$file")"
		done
	done

	echo "Linked $(ls -A "$fonts" | wc -l) font files into the prefix."
fi

# A wineserver started by a different Wine build refuses to talk to this one. The error
# reads "version mismatch". Put the runner's own directory first, so that wine and
# wineserver come from the same build.
runner_dir="$(dirname "$(command -v "$runner" || echo "$runner")")"
if [ -x "$runner_dir/wineserver" ]; then
	export PATH="$runner_dir:$PATH"
fi

if [ "$quiet" = "1" ]; then
	set +e
	"$runner" "$out/BlackboxModManager.exe" "$@" 2> >(grep -v -E 'libEGL|pci id|failed to create dri2|wineserver: using' >&2)
	status=$?
	set -e
	exit $status
fi

exec "$runner" "$out/BlackboxModManager.exe" "$@"
