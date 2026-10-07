#!/usr/bin/env bash
# Fails when the .vsix holds a Lua file. The Neovim client lives at the repo
# root beside the VSCode extension, and .vscodeignore keeps its files out of
# the .vsix. This check catches a Lua file that a change to .vscodeignore lets
# through.
#
# Usage: check-vsix-holds-no-lua.sh [<file.vsix>]
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
  echo "check-vsix-holds-no-lua: $vsix does not exist. Run npm run package first." >&2
  exit 2
fi

lua_files="$(unzip -Z1 "$vsix" | grep -iE '\.lua$' || true)"

if [ -n "$lua_files" ]; then
  echo "check-vsix-holds-no-lua: $vsix holds these Lua files:"
  printf '%s\n' "$lua_files"
  echo
  echo "Add each path to .vscodeignore, then package again."
  exit 1
fi

echo "check-vsix-holds-no-lua: $vsix holds no Lua file."
