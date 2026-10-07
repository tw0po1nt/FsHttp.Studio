#!/usr/bin/env bash
# The gate. It runs the steps of .github/workflows/ci.yml in the same order,
# so a green local run predicts a green CI run. The last line is
# `verify: green` or `verify: red`, and the feedback skills read that line.
# Change this script and ci.yml together.
set -uo pipefail

cd "$(git rev-parse --show-toplevel)"

steps=(
  "./scripts/check-banned-patterns.sh"
  "dotnet tool restore"
  "dotnet fantomas --check ."
  "dotnet build FsHttp.Studio.slnx"
  "dotnet test FsHttp.Studio.slnx --no-build"
  "npm ci"
  "npm run compile"
  "npm run smoke"
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
