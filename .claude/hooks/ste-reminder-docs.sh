#!/usr/bin/env bash
# PreToolUse reminder for Write/Edit on any Markdown file: nudge the agent to
# apply the simplified-technical-english skill before the file is written.
# See docs/agents/technical-prose.md.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

# Vendored trees keep their authors' prose. AGENTS.md forbids a rewrite there.
if echo "$file_path" | grep -Eq '(^|/)(\.agents|node_modules)/'; then
  exit 0
fi

if echo "$file_path" | grep -Eq '\.(md|markdown)$'; then
  jq -n '{
    hookSpecificOutput: {
      hookEventName: "PreToolUse",
      additionalContext: "Reminder (docs/agents/technical-prose.md): this file is Markdown, prose a human will read. All prose in this repo carries the STE rule. Before you write it, confirm the simplified-technical-english skill has been applied to the text. If not, run it now, then retry."
    }
  }'
fi
