#!/usr/bin/env bash
# Fails when the .vsix holds a file outside the set that ships. vsce packs each
# file at the repo root that .vscodeignore does not match, so a new root file
# ships until a person adds it to .vscodeignore. This check catches that file.
#
# Usage: check-vsix-holds-only-shipped-files.sh [<file.vsix>]
# With no argument, the script reads fshttp-studio-<version>.vsix at the repo
# root, which `npm run package` writes. package.json holds the version.
set -euo pipefail

if [ $# -gt 1 ]; then
  echo "Usage: $0 [<file.vsix>]" >&2
  exit 2
fi

if [ $# -eq 1 ]; then
  vsix="$1"
else
  cd "$(git rev-parse --show-toplevel)"
  vsix="fshttp-studio-$(node -p "require('./package.json').version").vsix"
fi

if [ ! -f "$vsix" ]; then
  echo "check-vsix-holds-only-shipped-files: $vsix does not exist. Run npm run package first." >&2
  exit 2
fi

extra_files="$(unzip -Z1 "$vsix" | grep -vE '^(extension/dist/.+|extension/media/icon\.png|extension/package\.json|extension/readme\.md|extension/LICENSE\.txt|extension/SECURITY\.md|extension/THIRD-PARTY-NOTICES\.md|extension\.vsixmanifest|\[Content_Types\]\.xml)$' || true)"

if [ -n "$extra_files" ]; then
  echo "check-vsix-holds-only-shipped-files: $vsix holds these files that do not ship:"
  printf '%s\n' "$extra_files"
  echo
  echo "Add each path to .vscodeignore, then package again."
  exit 1
fi

echo "check-vsix-holds-only-shipped-files: $vsix holds only the files that ship."
