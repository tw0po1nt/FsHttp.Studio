#!/usr/bin/env bash
# PROTOTYPE — one command for ticket 005. Throwaway.
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

STORAGE="${STORAGE:-/tmp/fshttp-005-resources}"
EXT_DIR="${EXT_DIR:-/tmp/fshttp-005-extensions}"
SIDECAR="$ROOT/fixtures/sidecar.json"
SERVER_LOG="$ROOT/.scratch/server.log"
SERVER_PID=""

# Remove a stale settings dir whose path can exceed macOS's ~104-char IPC socket limit
# when STORAGE lives under a deep .local tree.
mkdir -p "$ROOT/.scratch"
rm -rf "$STORAGE/settings" 2>/dev/null || true
mkdir -p "$STORAGE" "$EXT_DIR"

cleanup() {
  if [[ -n "${SERVER_PID}" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "==> npm install (prototype)"
if [[ ! -d node_modules ]]; then
  npm install
fi

echo "==> build test server"
npm run build:server

echo "==> start test server"
mkdir -p "$(dirname "$SIDECAR")" "$ROOT/.scratch"
rm -f "$SIDECAR"
: >"$SERVER_LOG"
./server/publish/TestHttpServer "$SIDECAR" >>"$SERVER_LOG" 2>&1 &
SERVER_PID=$!
disown "$SERVER_PID" 2>/dev/null || true

# Wait until the sidecar exists (server bound and wrote it).
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

# The companion evaluates script *text* via FSI EvalInteraction — it does not set
# __SOURCE_DIRECTORY__ to the .fsx path. Bake the base URL into the fixture for this
# prototype so the slice can prove packaging → lens → viewer without that product gap.
FIXTURE="$ROOT/fixtures/core-path.fsx"
cat >"$FIXTURE" <<EOF
#r "nuget: FsHttp, 15.0.3"
open FsHttp

http {
    GET "${BASE_URL}/json"
}
EOF
echo "    fixture baked with $BASE_URL"

# Keep the server alive across the long ExTester child process (nohup + disown).
# Also prove the port answers before we launch VSCode.
if ! curl -sf "$BASE_URL/json" | grep -q vertical-slice-005; then
  echo "test server not answering at $BASE_URL/json" >&2
  cat "$SERVER_LOG" >&2
  exit 1
fi
echo "    server healthcheck OK"

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
mkdir -p "$STORAGE" "$EXT_DIR"
npx extest get-vscode -s "$STORAGE"
npx extest get-chromedriver -s "$STORAGE"

echo "==> ExTester: install packaged .vsix (not source)"
# Do not set EXTENSION_DEV_PATH — destination requires the packaged artifact.
unset EXTENSION_DEV_PATH || true
npx extest install-vsix -s "$STORAGE" -e "$EXT_DIR" -f "$VSIX"

echo "==> ExTester: run suite with fixtures open"
# Put the test glob BEFORE -r: --open_resource is variadic and would swallow the test path.
RUN_ARGS=(
  "$ROOT/out/core-path.test.js"
  -s "$STORAGE"
  -e "$EXT_DIR"
  -o "$ROOT/settings.json"
  -m "$ROOT/.mocharc.js"
  -r "$ROOT/fixtures/core-path.fsx"
)
if [[ "$(uname -s)" == "Linux" ]]; then
  xvfb-run -a npx extest run-tests "${RUN_ARGS[@]}"
else
  npx extest run-tests "${RUN_ARGS[@]}"
fi

echo "==> OK"
