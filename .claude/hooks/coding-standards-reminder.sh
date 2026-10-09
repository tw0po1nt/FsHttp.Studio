#!/usr/bin/env bash
# PreToolUse reminder for Write/Edit on an F# or Lua source file: name the house
# rules of that language at the moment the agent is about to write it.
# See docs/standards/coding-standards.md.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

case "$file_path" in
  */.agents/*|*/.claude/skills/*|*/.tests/*|*/node_modules/*) exit 0 ;;
esac

case "$file_path" in
  *.fs|*.fsx)
    reminder="Reminder (docs/standards/coding-standards.md): this is F# source, so the house rules apply. Rule 1: name a domain concept with the term GLOSSARY.md defines. Rule 5: write a comment, // or ///, only for what a reader cannot derive from the code, and cite nothing off the page: no spec path, no ADR number, no URL, no issue or PR number. Rule 6: a record of closures needs strong justification. Read the file for rules 2 to 4 when you touch the envelope wire, a child process, or process-global state. Verify the change with: dotnet build FsHttp.Studio.slnx"
    ;;
  *.lua)
    first_line="$(head -n 1 "$file_path" 2>/dev/null || true)"
    if [[ "$first_line" == "-- Generated from"* ]]; then
      reminder="Reminder (docs/standards/build-and-verify.md): scripts/generate-lua.fsx writes this Lua file. Make the change in the generator or in its source file. Then run: dotnet fsi scripts/generate-lua.fsx. Verify the change with: dotnet fsi scripts/generate-lua.fsx --check"
    else
      reminder="Reminder (docs/standards/coding-standards.md): this is Lua source, so rules 1, 2, 3, and 5 apply. Rule 1: name a domain concept with the term GLOSSARY.md defines. Rule 2: read and write the wire only through fshttp.frame and fshttp.envelope. Rule 3: give vim.system a timeout, and kill the process handle when a wait expires. Rule 5: write a comment, -- or ---, only for what a reader cannot derive from the code, and cite nothing off the page: no spec path, no ADR number, no URL, no issue or PR number. The tag and the type of a ---@ annotation are exempt, and free text after the type follows rule 5. Verify the change with: stylua --check . && ./scripts/check-lua-types.sh && nvim -l tests/minit.lua --minitest && ./tests/nvim/run.sh"
    fi
    ;;
  *) exit 0 ;;
esac

jq -n --arg reminder "$reminder" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    additionalContext: $reminder
  }
}'
