#!/usr/bin/env bash
# The one entry point of the Neovim suite. CI and a local run both call this script.
#
# The script publishes the test HTTP server of the UI suite and the companion, and builds the
# tree-sitter JSON parser. Then it runs the suite through lazy.minit. Set NVIM_TEST_SKIP_BUILD=1 to
# use the builds that are already in out/. Set NVIM_TEST_JSON_PARSER to the path of a JSON parser
# library to use that parser in place of the build.
#
# Set NVIM_TEST_COMPANION_ARCHIVE to the path of a Companion archive to drive that archive. Its
# .sha256 file must be beside it. The script verifies the checksum and unpacks the archive as the
# companion of the run. The download Checks get the same archive. The script then builds no
# companion and packs no archive. The release uses this path to drive the files that it ships.
#
# The script runs on Linux, on macOS, and on Windows in Git Bash. On Windows the script builds the
# parser with gcc, and it stops processes through PowerShell, because Git Bash has no pkill.
set -euo pipefail

case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    WINDOWS=1
    EXE=".exe"
    PARSER_EXT="dll"
    # `pwd -W` gives D:/a/repo. The plain form gives /d/a/repo, which Neovim cannot open.
    ROOT="$(cd "$(dirname "$0")/../.." && pwd -W)"
    ;;
  *)
    WINDOWS=0
    EXE=""
    PARSER_EXT="so"
    ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
    ;;
esac
SERVER_OUT="$ROOT/out/ui-test-server"
COMPANION_OUT="$ROOT/out/nvim-tests/companion"
ARCHIVE_OUT="$ROOT/out/nvim-tests/archive"
JSON_PARSER_OUT="$ROOT/out/nvim-tests/tree-sitter-json"
# tree-sitter-json v0.24.8. A pinned commit keeps an upstream change out of the suite.
TREE_SITTER_JSON_COMMIT="ee35a6ebefcef0c5c416c0d1ccec7370cfca5a24"

build_json_parser() {
  rm -rf "$JSON_PARSER_OUT"
  git init -q "$JSON_PARSER_OUT"
  git -C "$JSON_PARSER_OUT" fetch -q --depth 1 https://github.com/tree-sitter/tree-sitter-json.git "$TREE_SITTER_JSON_COMMIT"
  git -C "$JSON_PARSER_OUT" checkout -q FETCH_HEAD
  local compiler="${CC:-cc}"
  if ! command -v "$compiler" >/dev/null 2>&1; then
    compiler="gcc"
  fi
  "$compiler" -shared -fPIC -O2 -I "$JSON_PARSER_OUT/src" -o "$JSON_PARSER_OUT/json.$PARSER_EXT" "$JSON_PARSER_OUT/src/parser.c"
}

cleanup() {
  # The pattern contains the companion folder of this run, so the cleanup cannot stop a companion of
  # another editor.
  if [[ "$WINDOWS" == "1" ]]; then
    powershell.exe -NoProfile -NonInteractive -Command "Get-CimInstance Win32_Process | Where-Object { \$_.CommandLine -and (\$_.CommandLine.Replace('\\', '/').Contains('$COMPANION_OUT/Companion.dll') -or \$_.CommandLine.Replace('\\', '/').Contains('$SERVER_OUT/UiTestServer')) } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" >/dev/null 2>&1 || true
    return
  fi
  pkill -f "$COMPANION_OUT/Companion.dll" 2>/dev/null || true
  pkill -f "$SERVER_OUT/UiTestServer" 2>/dev/null || true
}
trap cleanup EXIT

# Copies the Companion archive at $1 and its .sha256 file into $ARCHIVE_OUT, verifies the checksum,
# and unpacks the archive into $COMPANION_OUT.
use_companion_archive() {
  local archive="$1"
  if [[ ! -f "$archive" || ! -f "$archive.sha256" ]]; then
    echo "NVIM_TEST_COMPANION_ARCHIVE is $archive, but the archive or its .sha256 file is missing." >&2
    exit 1
  fi
  local name
  name="$(basename "$archive")"
  rm -rf "$ARCHIVE_OUT" "$COMPANION_OUT"
  mkdir -p "$ARCHIVE_OUT" "$COMPANION_OUT"
  cp "$archive" "$archive.sha256" "$ARCHIVE_OUT/"
  # The .sha256 file names the archive without a folder, so the check runs in the archive folder.
  if command -v sha256sum >/dev/null 2>&1; then
    (cd "$ARCHIVE_OUT" && sha256sum -c "$name.sha256")
  else
    (cd "$ARCHIVE_OUT" && shasum -a 256 -c "$name.sha256")
  fi
  # tar reads a drive letter in an archive name as a host name on Windows, so the name is relative.
  # $ARCHIVE_OUT and $COMPANION_OUT are sibling folders in out/nvim-tests.
  (cd "$COMPANION_OUT" && tar -xzf "../archive/$name")
  if [[ ! -e "$COMPANION_OUT/Companion.dll" ]]; then
    echo "The Companion archive $archive contains no Companion.dll." >&2
    exit 1
  fi
}

ARCHIVE="${NVIM_TEST_COMPANION_ARCHIVE:-}"
if [[ -n "$ARCHIVE" ]]; then
  # Make the path absolute before a step changes the folder.
  ARCHIVE="$(cd "$(dirname "$ARCHIVE")" && pwd)/$(basename "$ARCHIVE")"
fi

if [[ "${NVIM_TEST_SKIP_BUILD:-}" == "1" ]]; then
  PREBUILT=("$SERVER_OUT/UiTestServer$EXE")
  if [[ -z "$ARCHIVE" ]]; then
    PREBUILT+=("$COMPANION_OUT/Companion.dll")
  fi
  for file in "${PREBUILT[@]}"; do
    if [[ ! -e "$file" ]]; then
      echo "NVIM_TEST_SKIP_BUILD is set, but $file is missing. Build it first." >&2
      exit 1
    fi
  done
  echo "==> ${PREBUILT[*]} (prebuilt)"
else
  echo "==> build the test server"
  dotnet publish "$ROOT/tests/ui.Tests/server/UiTestServer.fsproj" -c Release -o "$SERVER_OUT"
  if [[ -z "$ARCHIVE" ]]; then
    echo "==> build the companion"
    dotnet publish "$ROOT/src/companion/Companion.fsproj" -c Release -o "$COMPANION_OUT"
  fi
fi

if [[ -n "$ARCHIVE" ]]; then
  echo "==> verify and unpack the Companion archive ($ARCHIVE)"
  use_companion_archive "$ARCHIVE"
else
  echo "==> pack the Companion archive"
  "$ROOT/scripts/pack-companion.sh" "$ARCHIVE_OUT" "$COMPANION_OUT"
fi

if [[ -n "${NVIM_TEST_JSON_PARSER:-}" ]]; then
  echo "==> tree-sitter JSON parser ($NVIM_TEST_JSON_PARSER)"
elif [[ "${NVIM_TEST_SKIP_BUILD:-}" == "1" ]]; then
  NVIM_TEST_JSON_PARSER="$JSON_PARSER_OUT/json.$PARSER_EXT"
  if [[ ! -e "$NVIM_TEST_JSON_PARSER" ]]; then
    echo "NVIM_TEST_SKIP_BUILD is set, but $NVIM_TEST_JSON_PARSER is missing. Build it first." >&2
    exit 1
  fi
  echo "==> tree-sitter JSON parser (prebuilt in out/)"
else
  echo "==> build the tree-sitter JSON parser"
  build_json_parser
  NVIM_TEST_JSON_PARSER="$JSON_PARSER_OUT/json.$PARSER_EXT"
fi

export NVIM_TEST_SERVER="$SERVER_OUT/UiTestServer$EXE"
export NVIM_TEST_COMPANION_PATH="$COMPANION_OUT"
export NVIM_TEST_ARCHIVE_DIR="$ARCHIVE_OUT"
export NVIM_TEST_JSON_PARSER

echo "==> run the Neovim suite"
cd "$ROOT"
nvim -l tests/minit.lua --minitest tests/nvim/suite.lua

echo "==> OK"
