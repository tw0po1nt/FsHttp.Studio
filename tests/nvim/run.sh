#!/usr/bin/env bash
# The one entry point of the Neovim suite. CI and a local run both call this script.
#
# The script publishes the test HTTP server of the UI suite and the companion, and builds the
# tree-sitter JSON parser. Then it runs the suite through lazy.minit. Set NVIM_TEST_SKIP_BUILD=1 to
# use the builds that are already in out/. Set NVIM_TEST_JSON_PARSER to the path of a JSON parser
# library to use that parser in place of the build.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SERVER_OUT="$ROOT/out/ui-test-server"
COMPANION_OUT="$ROOT/out/nvim-tests/companion"
JSON_PARSER_OUT="$ROOT/out/nvim-tests/tree-sitter-json"
# tree-sitter-json v0.24.8. A pinned commit keeps an upstream change out of the suite.
TREE_SITTER_JSON_COMMIT="ee35a6ebefcef0c5c416c0d1ccec7370cfca5a24"

build_json_parser() {
  rm -rf "$JSON_PARSER_OUT"
  git init -q "$JSON_PARSER_OUT"
  git -C "$JSON_PARSER_OUT" fetch -q --depth 1 https://github.com/tree-sitter/tree-sitter-json.git "$TREE_SITTER_JSON_COMMIT"
  git -C "$JSON_PARSER_OUT" checkout -q FETCH_HEAD
  cc -shared -fPIC -O2 -I "$JSON_PARSER_OUT/src" -o "$JSON_PARSER_OUT/json.so" "$JSON_PARSER_OUT/src/parser.c"
}

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

if [[ -n "${NVIM_TEST_JSON_PARSER:-}" ]]; then
  echo "==> tree-sitter JSON parser ($NVIM_TEST_JSON_PARSER)"
elif [[ "${NVIM_TEST_SKIP_BUILD:-}" == "1" ]]; then
  NVIM_TEST_JSON_PARSER="$JSON_PARSER_OUT/json.so"
  if [[ ! -e "$NVIM_TEST_JSON_PARSER" ]]; then
    echo "NVIM_TEST_SKIP_BUILD is set, but $NVIM_TEST_JSON_PARSER is missing. Build it first." >&2
    exit 1
  fi
  echo "==> tree-sitter JSON parser (prebuilt in out/)"
else
  echo "==> build the tree-sitter JSON parser"
  build_json_parser
  NVIM_TEST_JSON_PARSER="$JSON_PARSER_OUT/json.so"
fi

export NVIM_TEST_SERVER="$SERVER_OUT/UiTestServer"
export NVIM_TEST_COMPANION_PATH="$COMPANION_OUT"
export NVIM_TEST_JSON_PARSER

echo "==> run the Neovim suite"
cd "$ROOT"
nvim -l tests/minit.lua --minitest tests/nvim/suite.lua

echo "==> OK"
