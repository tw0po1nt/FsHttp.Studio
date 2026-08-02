#!/usr/bin/env bash
# PreToolUse reminder for gh issue/pr create|edit|comment: nudge the agent to
# apply the simplified-technical-english skill before the text is posted.
# See docs/agents/technical-prose.md.
set -euo pipefail

input="$(cat)"
command="$(jq -r '.tool_input.command // empty' <<<"$input")"

if echo "$command" | grep -Eq 'gh +(issue|pr) +(create|edit|comment)'; then
  jq -n '{
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      additionalContext: "Reminder (docs/agents/technical-prose.md): this gh command posts or edits prose visible to a human reader. Before it runs, confirm the simplified-technical-english skill has been applied to the text. If not, run it now, then retry."
    }
  }'
fi
