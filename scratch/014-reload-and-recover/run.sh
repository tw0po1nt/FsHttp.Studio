#!/usr/bin/env bash
# PROTOTYPE — one command for ticket 014. Throwaway.
# Grown from 005/run.sh: same plumbing, plus a hang route and a fixture with two blocks.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
# Walk up until we find the extension package.json (works from .local/... or scratch/...).
REPO="$ROOT"
while [[ "$REPO" != "/" ]]; do
  if [[ -f "$REPO/package.json" ]] && grep -q '"name": "fshttp-studio"' "$REPO/package.json"; then
    break
  fi
  REPO="$(dirname "$REPO")"
done
if [[ ! -f "$REPO/package.json" ]]; then
  echo "could not find repo root from $ROOT" >&2
  exit 1
fi
cd "$ROOT"

STORAGE="${STORAGE:-/tmp/fshttp-014-resources}"
EXT_DIR="${EXT_DIR:-/tmp/fshttp-014-extensions}"
SIDECAR="$ROOT/fixtures/sidecar.json"
FIXTURE="$ROOT/fixtures/companion-death.fsx"
SERVER_LOG="$ROOT/.scratch/server.log"
SERVER_PID=""

mkdir -p "$ROOT/.scratch"
# Remove a stale settings dir whose path can exceed macOS's ~104-char IPC socket limit.
rm -rf "$STORAGE/settings" 2>/dev/null || true
mkdir -p "$STORAGE" "$EXT_DIR"

cleanup() {
  if [[ -n "${SERVER_PID}" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  # A killed companion can leave the previous run's process behind on a failed attempt.
  pkill -f 'dist/companion/Companion.dll' 2>/dev/null || true
}
trap cleanup EXIT

echo "==> npm install (prototype)"
if [[ ! -d node_modules ]]; then
  npm install
fi

echo "==> build test server"
npm run build:server

echo "==> start test server"
rm -f "$SIDECAR"
: >"$SERVER_LOG"
./server/publish/TestHttpServer "$SIDECAR" >>"$SERVER_LOG" 2>&1 &
SERVER_PID=$!
disown "$SERVER_PID" 2>/dev/null || true

for _ in $(seq 1 50); do
  if [[ -f "$SIDECAR" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "test server exited early; log:" >&2
    cat "$SERVER_LOG" >&2
    exit 1
  fi
  sleep 0.1
done
if [[ ! -f "$SIDECAR" ]]; then
  echo "test server failed to write sidecar; log:" >&2
  cat "$SERVER_LOG" >&2
  exit 1
fi
BASE_URL="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["baseUrl"])' "$SIDECAR")"
echo "    sidecar: $(cat "$SIDECAR") (pid $SERVER_PID)"

if ! curl -sf "$BASE_URL/json" | grep -q reload-and-recover-014; then
  echo "test server not answering at $BASE_URL/json" >&2
  cat "$SERVER_LOG" >&2
  exit 1
fi
echo "    server healthcheck OK"

# Two blocks. Block 0 hangs until /release; block 1 answers at once. Ticket 008's product fix
# (#144) is not in yet, so the base URL is baked in rather than read from the sidecar.
cat >"$FIXTURE" <<EOF
#r "nuget: FsHttp, 15.0.3"
open FsHttp

// Block 0 — hangs until the suite hits /release. The companion dies mid-flight.
http {
    GET "${BASE_URL}/slow"
}

// Block 1 — the recovery Run, after the window reloads.
http {
    GET "${BASE_URL}/json"
}
EOF
echo "    fixture baked with $BASE_URL"

echo "==> package extension (repo root)"
if [[ -z "${VSIX:-}" ]]; then
  (cd "$REPO" && npm run package)
  VSIX="$(ls -1 "$REPO"/fshttp-studio-*.vsix | tail -1)"
fi
echo "    vsix: $VSIX"

echo "==> build F# UI test"
(cd "$REPO" && dotnet tool restore >/dev/null)
(cd "$REPO" && dotnet tool run fable "$ROOT/uitests" --outDir "$ROOT/out/fable" --sourceMaps)
node "$ROOT/esbuild.mjs"

echo "==> ExTester: download VSCode + ChromeDriver"
npx extest get-vscode -s "$STORAGE"
npx extest get-chromedriver -s "$STORAGE"

echo "==> ExTester: install packaged .vsix (not source)"
unset EXTENSION_DEV_PATH || true
npx extest install-vsix -s "$STORAGE" -e "$EXT_DIR" -f "$VSIX"

echo "==> ExTester: run suite with the fixture open"
# Put the test glob BEFORE -r: --open_resource is variadic and would swallow the test path.
RUN_ARGS=(
  "$ROOT/out/reload-recover.test.js"
  -s "$STORAGE"
  -e "$EXT_DIR"
  -o "$ROOT/settings.json"
  -m "$ROOT/.mocharc.js"
  -r "$FIXTURE"
)
export FIXTURE SIDECAR
# `--enable-source-maps` is what turns a bundle frame into `uitests/ReloadRecover.fs:112`.
# It has to arrive through NODE_OPTIONS: ExTester builds Mocha in-process, so `.mocharc.js`'s
# `node-option` is ignored.
export NODE_OPTIONS="${NODE_OPTIONS:-} --enable-source-maps"
if [[ "$(uname -s)" == "Linux" ]]; then
  xvfb-run -a npx extest run-tests "${RUN_ARGS[@]}"
else
  npx extest run-tests "${RUN_ARGS[@]}"
fi

echo "==> OK"
