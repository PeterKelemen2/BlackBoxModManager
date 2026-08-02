#!/usr/bin/env bash
# Installs both example mods into a scratch copy of the game, under Wine, and reverts.
#
# This is the success criterion of the project brief. It runs the same Core code that the
# window runs, so a pass here is a pass for the application.
#
# **The test never touches the vanilla install.** This script copies the game first, and it
# passes the copy to the application. The swap replaces the directory that it gets.
#
# Usage:
#   tools/run-deploy-test.sh
#
# Environment:
#   DEPLOY_VANILLA    The game to copy. The script only reads it.
#   DEPLOY_SCRATCH    The copy. The script rebuilds this unless DEPLOY_KEEP is 1.
#   DEPLOY_BINARY     The Binary 2.8.3 install.
#   DEPLOY_KEEP       "1" keeps the copy of the last run. A 1.7 GB copy is slow.
#   DEPLOY_NO_REVERT  "1" leaves the deploy in place, so that you can start the game.
#   DEPLOY_RUNNER     "wine" (default) or the path to another wine executable.
#   DEPLOY_WINEPREFIX The prefix to run in.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${DEPLOY_OUT:-$root/artifacts/app}"
prefix="${DEPLOY_WINEPREFIX:-$HOME/.local/share/blackbox-app-wine}"
runner="${DEPLOY_RUNNER:-wine}"

vanilla="${DEPLOY_VANILLA:-/mnt/Data/Games/WinePrefixes/NFSU2ModTest/drive_c/Program Files (x86)/EA GAMES/Need for Speed Underground 2}"
scratch="${DEPLOY_SCRATCH:-/mnt/Data/Games/DeployTest/Need for Speed Underground 2}"
binary="${DEPLOY_BINARY:-/mnt/Data/Games/Binary_v2.8.3}"

for path in "$vanilla" "$binary" "$root/example_mods"; do
	if [ ! -d "$path" ]; then
		echo "ERROR: The directory $path does not exist." >&2
		exit 2
	fi
done

# The scratch copy must not be the vanilla install, and it must not sit inside it.
case "$scratch/" in
	"$vanilla/"*)
		echo "ERROR: The scratch copy sits inside the vanilla install." >&2
		exit 2
		;;
esac

if [ "$scratch" = "$vanilla" ]; then
	echo "ERROR: The scratch copy is the vanilla install." >&2
	exit 2
fi

if [ "${DEPLOY_KEEP:-0}" = "1" ] && [ -d "$scratch" ]; then
	echo "Keep the scratch copy of the last run."
else
	if [ -d "$scratch" ]; then
		echo "Remove the scratch copy of the last run."

		# The install holds read-only files. server.dll is one, and a recursive delete
		# stops on it.
		chmod -R u+w "$scratch"
		rm -rf "$scratch"
	fi

	# The workspace of an earlier run sits beside the copy.
	rm -rf "$scratch.blackbox"

	echo "Copy the game. This takes a while for 1.7 GB."
	mkdir -p "$(dirname "$scratch")"
	cp -a "$vanilla" "$scratch"
	chmod -R u+w "$scratch"
fi

publish=(dotnet publish "$root/src/BlackboxModManager.App/BlackboxModManager.App.csproj"
	-c Release -r win-x64 --self-contained true
	-o "$out" --nologo -v quiet)

if ! build_log="$("${publish[@]}" 2>&1)"; then
	echo "$build_log" >&2
	echo "ERROR: The publish failed." >&2
	exit 2
fi

export WINEPREFIX="$prefix"
export WINEDEBUG="${WINEDEBUG:--all}"

mkdir -p "$prefix"

# A wineserver of another build refuses to talk to this one. Put the runner's own
# directory first, so that wine and wineserver come from one build.
runner_dir="$(dirname "$(command -v "$runner" || echo "$runner")")"
if [ -x "$runner_dir/wineserver" ]; then
	export PATH="$runner_dir:$PATH"
fi

# Convert with the runner itself. A Proton build ships no winepath program, and a call to
# that program starts a wineserver of the wrong version in the prefix.
to_windows() {
	"$runner" winepath.exe -w "$1" 2>/dev/null | tr -d '\r'
}

game_win="$(to_windows "$scratch")"
binary_win="$(to_windows "$binary")"
mods_win="$(to_windows "$root/example_mods")"

for value in "$game_win" "$binary_win" "$mods_win"; do
	if [ -z "$value" ]; then
		echo "ERROR: The runner could not convert a path." >&2
		exit 2
	fi
done

set +e
keep=""
if [ "${DEPLOY_NO_REVERT:-0}" = "1" ]; then
	keep="keep"
fi

"$runner" "$out/BlackboxModManager.exe" --deploytest "$game_win" "$binary_win" "$mods_win" $keep \
	2> >(grep -v -E 'libEGL|pci id|failed to create dri2|wineserver: using' >&2)
status=$?
set -e

echo
echo "The scratch copy sits at $scratch."
echo "Start SPEED2.EXE there to confirm that the game reads the result."

exit $status
