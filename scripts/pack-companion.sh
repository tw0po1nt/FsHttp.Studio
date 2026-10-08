#!/usr/bin/env bash
# Builds the Companion archive and its .sha256 file.
#
# Usage: scripts/pack-companion.sh <output-dir> [<published-companion-dir>]
#
# The script writes fshttp-studio-companion-<version>.tar.gz and the .sha256 file beside it into
# <output-dir>. The version comes from package.json. Without a second argument, the script
# publishes the companion first. The archive holds the files of the published companion at its top
# level. The Neovim suite and the release both use this script.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: $0 <output-dir> [<published-companion-dir>]" >&2
  exit 2
fi

OUT="$1"
PUBLISHED="${2:-}"
VERSION="$(sed -n 's/^  "version": "\(.*\)",$/\1/p' "$ROOT/package.json" | head -n 1)"
if [[ -z "$VERSION" ]]; then
  echo "pack-companion: package.json holds no version" >&2
  exit 1
fi

if [[ -z "$PUBLISHED" ]]; then
  PUBLISHED="$(mktemp -d)"
  trap 'rm -rf "$PUBLISHED"' EXIT
  dotnet publish "$ROOT/src/companion/Companion.fsproj" -c Release -o "$PUBLISHED"
fi

if [[ ! -e "$PUBLISHED/Companion.dll" ]]; then
  echo "pack-companion: $PUBLISHED holds no Companion.dll" >&2
  exit 1
fi

mkdir -p "$OUT"
NAME="fshttp-studio-companion-$VERSION.tar.gz"
rm -f "$OUT/$NAME" "$OUT/$NAME.sha256"

PUBLISHED="$(cd "$PUBLISHED" && pwd)"

# tar reads a drive letter in an archive name as a host name (D:/a/out), which breaks on Windows in
# Git Bash. The archive therefore gets a relative name inside $OUT.
cd "$OUT"
tar -czf "$NAME" -C "$PUBLISHED" .

if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$NAME" > "$NAME.sha256"
else
  shasum -a 256 "$NAME" > "$NAME.sha256"
fi
echo "pack-companion: wrote $OUT/$NAME and $OUT/$NAME.sha256"
