#!/usr/bin/env bash
# One-command restore for a fresh clone. It installs every skill in
# skills-lock.json and copies each skill into the directory of each agent in
# `agents`.
#
# `npx skills experimental_install` fills only .agents/skills, so Claude Code
# finds no skill after it runs. `add --copy` writes a real copy for each agent.
set -euo pipefail

# The agents that this project targets.
agents=(claude-code universal)

cd "$(git rev-parse --show-toplevel)"

# Print one line for each source: the source, then the names of its skills.
node -e '
  const { skills } = require("./skills-lock.json");
  const bySource = {};
  for (const [name, { source }] of Object.entries(skills)) {
    (bySource[source] ??= []).push(name);
  }
  for (const [source, names] of Object.entries(bySource)) {
    console.log(source, ...names);
  }
' | while read -r source names; do
  # `names` holds a list, so it must split into words. The CLI must not read
  # stdin, because stdin holds the remaining lines of the list.
  # shellcheck disable=SC2086
  npx skills@latest add "$source" -s $names -a "${agents[@]}" -y --copy </dev/null
done
