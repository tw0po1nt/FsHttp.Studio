#!/usr/bin/env bash
# PreToolUse reminder for Write/Edit on an F# source file: name the house rules
# at the moment the agent is about to write F#.
# See docs/standards/coding-standards.md.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

case "$file_path" in
  *.fs|*.fsx) ;;
  *) exit 0 ;;
esac

case "$file_path" in
  */.agents/*|*/node_modules/*) exit 0 ;;
esac

jq -n '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    additionalContext: "Reminder (docs/standards/coding-standards.md): this is F# source, so the house rules apply. Rule 1: name a domain concept with the term CONTEXT.md defines. Rule 5: write a comment, // or ///, only for what a reader cannot derive from the code, and cite nothing off the page: no spec path, no ADR number, no URL, no issue or PR number. Rule 6: a record of closures needs strong justification. Read the file for rules 2 to 4 when you touch the envelope wire, a child process, or process-global state. Verify the change with: dotnet build FsHttp.Studio.slnx"
  }
}'
