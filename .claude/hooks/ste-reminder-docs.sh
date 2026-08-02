#!/usr/bin/env bash
# PreToolUse reminder for Write/Edit on docs/adr or docs/spec: nudge the agent
# to apply the simplified-technical-english skill before the file is written.
# See docs/agents/technical-prose.md.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

if echo "$file_path" | grep -Eq 'docs/(adr|spec)/.*\.md$'; then
  jq -n '{
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      additionalContext: "Reminder (docs/agents/technical-prose.md): this file is an ADR or a spec, prose a human will read. Before you write it, confirm the simplified-technical-english skill has been applied to the text. If not, run it now, then retry."
    }
  }'
fi
