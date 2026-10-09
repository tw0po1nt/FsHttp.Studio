#!/usr/bin/env bash
# Fails when lua-language-server finds a problem in the LuaCATS annotations of
# the Lua files. .luarc.json reads the Neovim API types from $VIMRUNTIME/lua.
#
# The script sets VIMRUNTIME from the nvim on PATH, so the check uses the API
# types of that Neovim version. CI puts Neovim 0.11 on PATH, because 0.11 is the
# oldest version that the Neovim client supports. Set VIMRUNTIME to use the
# types of a different Neovim.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

if [ -z "${VIMRUNTIME:-}" ]; then
  VIMRUNTIME="$(nvim --clean --headless --cmd 'lua io.stdout:write(vim.env.VIMRUNTIME)' --cmd 'qa!')"
  export VIMRUNTIME
fi

if [ ! -d "$VIMRUNTIME/lua/vim" ]; then
  echo "check-lua-types: $VIMRUNTIME contains no Neovim runtime. Set VIMRUNTIME, or put nvim on PATH." >&2
  exit 2
fi

echo "check-lua-types: the Neovim API types come from $VIMRUNTIME"

# lua-language-server writes a log for each run. Keep it out of the tree.
log_dir="$(mktemp -d)"
trap 'rm -rf "$log_dir"' EXIT

lua-language-server --check . --checklevel=Warning --logpath="$log_dir"
