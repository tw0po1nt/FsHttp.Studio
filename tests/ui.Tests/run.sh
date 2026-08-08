#!/usr/bin/env bash
# Single local entry point for the UI test suite. CI calls this same script in a later ticket.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SUITE="$(cd "$(dirname "$0")" && pwd)"
cd "$SUITE"

STORAGE="${STORAGE:-/tmp/fshttp-ui-test-resources}"
EXT_DIR="${EXT_DIR:-/tmp/fshttp-ui-test-extensions}"
SERVER_OUT="$ROOT/out/ui-test-server"
SERVER_BIN="$SERVER_OUT/UiTestServer"
FIXTURES="$SUITE/fixtures"
SIDECAR="$FIXTURES/sidecar.json"
FIXTURE="$FIXTURES/setup.fsx"
BUNDLE="$ROOT/out/ui-tests/suite.bundle.cjs"
SERVER_PID=""

mkdir -p "$ROOT/out/ui-tests"
rm -rf "$STORAGE/settings" 2>/dev/null || true
mkdir -p "$STORAGE" "$EXT_DIR" "$(dirname "$SIDECAR")"

cleanup() {
  if [[ -n "${SERVER_PID}" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  # Anchored on this run's extensions directory, so an interrupted run never kills the companion
  # behind the developer's own editor.
  pkill -f "$EXT_DIR.*Companion.dll" 2>/dev/null || true
}
trap cleanup EXIT

if [[ "${UI_TEST_SKIP_SERVER_BUILD:-}" == "1" ]]; then
  if [[ ! -x "$SERVER_BIN" ]]; then
    echo "UI_TEST_SKIP_SERVER_BUILD is set but $SERVER_BIN is missing — build the test server in CI first" >&2
    exit 1
  fi
  echo "==> test server (prebuilt at $SERVER_OUT)"
else
  echo "==> build test server"
  dotnet publish "$SUITE/server/UiTestServer.fsproj" -c Release -o "$SERVER_OUT"
fi

echo "==> start test server"
rm -f "$SIDECAR"
"$SERVER_BIN" &
SERVER_PID=$!

for _ in $(seq 1 100); do
  if [[ -f "$SIDECAR" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "test server exited before writing the sidecar" >&2
    exit 1
  fi
  sleep 0.1
done

if [[ ! -f "$SIDECAR" ]]; then
  echo "test server failed to write sidecar at $SIDECAR" >&2
  exit 1
fi

if [[ "${UI_TEST_DEMO_BROKEN_SETUP:-}" == "1" ]]; then
  echo "==> demo: kill test server before ExTester (broken proven-live)"
  kill "$SERVER_PID" 2>/dev/null || true
  wait "$SERVER_PID" 2>/dev/null || true
  SERVER_PID=""
fi

echo "==> package extension"
if [[ -z "${VSIX:-}" ]]; then
  (cd "$ROOT" && npm run package)
  VSIX="$(ls -1 "$ROOT"/fshttp-studio-*.vsix | tail -1)"
fi
echo "    vsix: $VSIX"

echo "==> build UI test suite"
(cd "$ROOT" && dotnet tool restore >/dev/null)
(cd "$ROOT" && npm run build:fable:ui-tests)
(cd "$ROOT" && npm run build:bundle:ui-tests)

echo "==> ExTester: download VSCode and ChromeDriver (version from extester.config.json)"
(cd "$ROOT" && npx extest get-vscode -s "$STORAGE" --config "$SUITE/extester.config.json")
(cd "$ROOT" && npx extest get-chromedriver -s "$STORAGE" --config "$SUITE/extester.config.json")

echo "==> ExTester: install packaged .vsix"
unset EXTENSION_DEV_PATH || true
(cd "$ROOT" && npx extest install-vsix -s "$STORAGE" -e "$EXT_DIR" -f "$VSIX" --config "$SUITE/extester.config.json")

export UI_TEST_SIDECAR="$SIDECAR"
export UI_TEST_EXTENSIONS_DIR="$EXT_DIR"
export NODE_OPTIONS="${NODE_OPTIONS:+$NODE_OPTIONS }--enable-source-maps"

echo "==> ExTester: run suite"
RUN_ARGS=(
  "$BUNDLE"
  -s "$STORAGE"
  -e "$EXT_DIR"
  -o "$SUITE/settings.json"
  -m "$SUITE/.mocharc.js"
  # The folder first, so VSCode opens it as the workspace root and the extension activates against
  # a real workspace; then the script, so a tab is open too. `--open_resource` is variadic, which
  # is why it stays last and the bundle glob leads the list.
  -r "$FIXTURES" "$FIXTURE"
)

if [[ "$(uname -s)" == "Linux" ]]; then
  (cd "$ROOT" && xvfb-run -a npx extest run-tests "${RUN_ARGS[@]}" --config "$SUITE/extester.config.json")
else
  (cd "$ROOT" && npx extest run-tests "${RUN_ARGS[@]}" --config "$SUITE/extester.config.json")
fi

echo "==> OK"
