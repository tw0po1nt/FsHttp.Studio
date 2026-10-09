#!/usr/bin/env bash
# One-command restore for a fresh clone. It installs every skill in
# skills-lock.json and copies each skill into the directory of each agent in
# `agents`.
#
# `npx skills experimental_install` fills only .agents/skills, so Claude Code
# finds no skill after it runs. `add --copy` writes a real copy for each agent.
#
# A source can be a private repo that a contributor cannot read. A failed
# source is skipped, so the other sources still install, and the script exits
# 1 at the end with the list of failed sources.
set -euo pipefail

# The agents that this project targets.
agents=(claude-code universal)

# A source that needs credentials fails at once. Git does not stop at a prompt.
export GIT_TERMINAL_PROMPT=0

cd "$(git rev-parse --show-toplevel)"

failed=()

# Read one line for each source: the source, then the names of its skills.
# The loop reads from process substitution, because a loop at the end of a
# pipe runs in a subshell and would lose `failed`.
while read -r source names; do
  # `names` contains a list, so it must split into words. The CLI must not read
  # stdin, because stdin contains the remaining lines of the list.
  # shellcheck disable=SC2086
  if ! npx skills@latest add "$source" -s $names -a "${agents[@]}" -y --copy </dev/null; then
    failed+=("$source")
  fi
done < <(node -e '
  const { skills } = require("./skills-lock.json");
  const bySource = {};
  for (const [name, { source }] of Object.entries(skills)) {
    (bySource[source] ??= []).push(name);
  }
  for (const [source, names] of Object.entries(bySource)) {
    console.log(source, ...names);
  }
')

if [ ${#failed[@]} -gt 0 ]; then
  echo >&2
  echo "bootstrap: these sources did not install: ${failed[*]}" >&2
  echo "bootstrap: the skills from each other source are installed." >&2
  echo "bootstrap: a private source needs git access to its repo." >&2
  exit 1
fi
