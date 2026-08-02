#!/usr/bin/env bash
# Builds the step 1 harness for Windows and runs it under Wine.
#
# The harness must run under Wine. Two reasons:
#   1. LZCompressLib.dll is a native Windows x64 library.
#   2. The manifests declare GLOBAL\GLOBALB.LZC and the file on disk is GLOBAL/GlobalB.lzc.
#      Wine resolves the separator and the case. Native Linux .NET does not.
#
# Usage:
#   tools/run-harness.sh <manifest path> [harness options...]
#   tools/run-harness.sh --show-binary
#   tools/run-harness.sh --set-binary '<windows path>'
#   tools/run-harness.sh --link-test '<windows path>'
#
# The manifest path can be a Linux path. This script converts it for Wine.
# Every other option goes to the harness without a change. See --help.
#
# Environment:
#   HARNESS_RUNNER      "wine" (default) or the path to another wine executable.
#   HARNESS_WINEPREFIX  The prefix to run in.
#   HARNESS_OUT         The publish directory.
#   HARNESS_SINGLEFILE  "1" publishes one executable. Step 3 needs this shape.
#   HARNESS_QUIET       "0" keeps the graphics driver noise of Wine.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${HARNESS_OUT:-$root/artifacts/harness}"
prefix="${HARNESS_WINEPREFIX:-$HOME/.local/share/blackbox-harness-wine}"
runner="${HARNESS_RUNNER:-wine}"
quiet="${HARNESS_QUIET:-1}"

manifest=""

# A first argument that is not an option is the manifest. The Binary install commands
# and the link probe take no manifest, so they start with an option and this block
# does not run.
if [ $# -ge 1 ] && [ "${1#--}" = "$1" ]; then
	manifest="$1"
	shift

	if [ ! -f "$manifest" ]; then
		echo "ERROR: The manifest $manifest does not exist." >&2
		exit 2
	fi
fi

publish=(dotnet publish "$root/tools/Harness/Harness.csproj"
	-c Release -r win-x64 --self-contained true
	-o "$out" --nologo -v quiet)

if [ "${HARNESS_SINGLEFILE:-0}" = "1" ]; then
	# IncludeNativeLibrariesForSelfExtract is mandatory. Nikki loads LZCompressLib.dll by
	# name, so that file must exist beside the host at run time.
	publish+=(-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
fi

# The third-party projects emit many warnings. Show the build output only when it fails.
if ! build_log="$("${publish[@]}" 2>&1)"; then
	echo "$build_log" >&2
	echo "ERROR: The publish failed." >&2
	exit 2
fi

export WINEPREFIX="$prefix"
export WINEDEBUG="${WINEDEBUG:--all}"

mkdir -p "$prefix"

# A wineserver started by a different Wine build refuses to talk to this one. The error
# reads "version mismatch". Put the runner's own directory first, so that wine and
# wineserver come from the same build.
runner_dir="$(dirname "$(command -v "$runner" || echo "$runner")")"
if [ -x "$runner_dir/wineserver" ]; then
	export PATH="$runner_dir:$PATH"
fi

args=()

if [ -n "$manifest" ]; then
	# Convert with the runner itself, through winepath.exe. Do not call the winepath
	# program. A Proton build ships no winepath, so that call falls back to system Wine,
	# which starts a wineserver of the wrong version in this prefix. The error then reads
	# "version mismatch" and names nothing useful.
	manifest_win="$("$runner" winepath.exe -w "$(realpath "$manifest")" 2>/dev/null | tr -d '\r')"

	if [ -z "$manifest_win" ]; then
		echo "ERROR: The runner could not convert the manifest path." >&2
		exit 2
	fi

	args+=(--manifest "$manifest_win")
fi

args+=("$@")

# Wine initializes a graphics driver even for a console program. That prints libEGL and
# pci id lines that hide the output of the harness.
if [ "$quiet" = "1" ]; then
	set +e
	"$runner" "$out/Harness.exe" "${args[@]}" 2> >(grep -v -E 'libEGL|pci id|failed to create dri2|wineserver: using' >&2)
	status=$?
	set -e
	exit $status
fi

exec "$runner" "$out/Harness.exe" "${args[@]}"
