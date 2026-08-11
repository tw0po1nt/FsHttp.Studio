#!/usr/bin/env bash
# PreToolUse reminder for gh pr create|edit: nudge the agent to put a screenshot
# in the pull request when the branch changed the user interface.
# See docs/agents/ui-screenshots.md.
set -euo pipefail

input="$(cat)"
command="$(jq -r '.tool_input.command // empty' <<<"$input")"

echo "$command" | grep -Eq 'gh +pr +(create|edit)' || exit 0

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

base="$(git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>/dev/null || echo origin/main)"
changed="$(git diff --name-only "$base"...HEAD 2>/dev/null || true)"

# The paths that paint something a user sees. docs/agents/ui-screenshots.md lists them.
echo "$changed" | grep -Eq '^src/(renderer|webview)/|^src/host/ResponseViewer\.fs' || exit 0

# A screenshot already committed on this branch satisfies the rule, so stay quiet.
echo "$changed" | grep -q '^docs/screenshots/' && exit 0

jq -n '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    additionalContext: "Reminder (docs/agents/ui-screenshots.md): this branch changes the user interface, and no screenshot is committed under docs/screenshots/. A pull request that changes the interface must carry a screenshot of the running editor. Capture one through the UI suite before this command runs, or state in the pull request why the diff paints nothing."
  }
}'
