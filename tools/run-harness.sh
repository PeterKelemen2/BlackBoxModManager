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
#
# The manifest path can be a Linux path. This script converts it for Wine.
# Every other option goes to the harness without a change. See --help.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${HARNESS_OUT:-$root/artifacts/harness}"
prefix="${HARNESS_WINEPREFIX:-$HOME/.local/share/blackbox-harness-wine}"

if [ $# -lt 1 ]; then
	echo "Usage: tools/run-harness.sh <manifest path> [harness options...]" >&2
	exit 2
fi

manifest="$1"
shift

if [ ! -f "$manifest" ]; then
	echo "ERROR: The manifest $manifest does not exist." >&2
	exit 2
fi

dotnet publish "$root/tools/Harness/Harness.csproj" \
	-c Release -r win-x64 --self-contained true \
	-o "$out" --nologo -v quiet

export WINEPREFIX="$prefix"
export WINEDEBUG="${WINEDEBUG:--all}"

mkdir -p "$prefix"

manifest_win="$(winepath -w "$(realpath "$manifest")" 2>/dev/null)"

exec wine "$out/Harness.exe" --manifest "$manifest_win" "$@"
