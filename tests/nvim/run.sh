#!/usr/bin/env bash
# The one entry point of the Neovim suite. nvim-tests.yml and a local run both call this script.
#
# The script publishes the test HTTP server of the UI suite and the companion, and then runs the
# suite through lazy.minit. Set NVIM_TEST_SKIP_BUILD=1 to use the two builds that are already in
# out/.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SERVER_OUT="$ROOT/out/ui-test-server"
COMPANION_OUT="$ROOT/out/nvim-tests/companion"

cleanup() {
  # The pattern holds the companion folder of this run, so the cleanup cannot stop a companion of
  # another editor.
  pkill -f "$COMPANION_OUT/Companion.dll" 2>/dev/null || true
  pkill -f "$SERVER_OUT/UiTestServer" 2>/dev/null || true
}
trap cleanup EXIT

if [[ "${NVIM_TEST_SKIP_BUILD:-}" == "1" ]]; then
  for file in "$SERVER_OUT/UiTestServer" "$COMPANION_OUT/Companion.dll"; do
    if [[ ! -e "$file" ]]; then
      echo "NVIM_TEST_SKIP_BUILD is set, but $file is missing. Build it first." >&2
      exit 1
    fi
  done
  echo "==> test server and companion (prebuilt in out/)"
else
  echo "==> build the test server"
  dotnet publish "$ROOT/tests/ui.Tests/server/UiTestServer.fsproj" -c Release -o "$SERVER_OUT"
  echo "==> build the companion"
  dotnet publish "$ROOT/src/companion/Companion.fsproj" -c Release -o "$COMPANION_OUT"
fi

export NVIM_TEST_SERVER="$SERVER_OUT/UiTestServer"
export NVIM_TEST_COMPANION_PATH="$COMPANION_OUT"

echo "==> run the Neovim suite"
cd "$ROOT"
nvim -l tests/minit.lua --minitest tests/nvim/suite.lua

echo "==> OK"
