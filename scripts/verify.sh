#!/usr/bin/env bash
# The gate. It runs the steps of .github/workflows/ci.yml in the same order,
# and then the Neovim suite of .github/workflows/nvim-tests.yml. Thus a green
# local run predicts a green CI run. The last line is
# `verify: green` or `verify: red`, and the feedback skills read that line.
# When you change a step in ci.yml or nvim-tests.yml, make the same change in
# this script.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"

steps=(
  "./scripts/check-banned-patterns.sh"
  "stylua --check ."
  "dotnet tool restore"
  "dotnet fantomas --check ."
  "dotnet fsi scripts/generate-lua.fsx --check"
  "dotnet build FsHttp.Studio.slnx"
  "dotnet test FsHttp.Studio.slnx --no-build"
  "npm ci"
  "npm run package"
  "./scripts/check-vsix-holds-no-lua.sh"
  "npm run smoke"
  "./scripts/check-lua-types.sh"
  "nvim -l tests/minit.lua --minitest"
  "./tests/nvim/run.sh"
)

for step in "${steps[@]}"; do
  echo "verify: running $step"
  if ! $step; then
    echo "verify: failed at $step"
    echo "verify: red"
    exit 1
  fi
done

echo "verify: green"
