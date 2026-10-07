#!/usr/bin/env bash
# PreToolUse check for Write/Edit on Markdown or F# source: deny the call when
# the pending text matches a pattern that `.banned-patterns` forbids.
#
# This is enforcement. The pattern list decides the answer with no judgment, so
# the agent learns at the moment of writing rather than in CI.
# scripts/check-banned-patterns.sh stays the complete check, because this hook
# cannot see a file that a Bash command writes.
# AGENTS.md states the rule.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

case "$file_path" in
  *.md | *.markdown | *.fs | *.fsx) ;;
  *) exit 0 ;;
esac

case "$file_path" in
  */.agents/* | */.claude/skills/* | */node_modules/* | */obj/* | *.banned-patterns) exit 0 ;;
esac

root="${CLAUDE_PROJECT_DIR:-.}"
[ -f "$root/.banned-patterns" ] || exit 0

patterns=()
messages=()
while IFS=$'\t' read -r pattern message; do
  case "$pattern" in '' | '#'*) continue ;; esac
  patterns+=("$pattern")
  messages+=("${message:-Rewrite the text.}")
done < "$root/.banned-patterns"
[ ${#patterns[@]} -eq 0 ] && exit 0

# Write carries the whole file, Edit carries the replacement text alone.
pending="$(jq -r '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' <<<"$input")"
[ -n "$pending" ] || exit 0

# Match the stripping that scripts/check-banned-patterns.sh applies, so the hook
# and CI agree on what counts as prose.
strip_markdown='
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
'

strip_fsharp='
  /^[[:space:]]*\/\// { gsub(/`[^`]*`/, ""); print; next }
  {
    out = ""
    rest = $0
    while (match(rest, /"[^"]*"/)) {
      out = out " " substr(rest, RSTART + 1, RLENGTH - 2)
      rest = substr(rest, RSTART + RLENGTH)
    }
    gsub(/`[^`]*`/, "", out)
    print out
  }
'

case "$file_path" in
  *.md | *.markdown) stripped="$(printf '%s\n' "$pending" | awk "$strip_markdown")" ;;
  *) stripped="$(printf '%s\n' "$pending" | awk "$strip_fsharp")" ;;
esac

hits=""
for i in "${!patterns[@]}"; do
  match="$(printf '%s\n' "$stripped" | grep -ioE -- "${patterns[$i]}" | head -1 || true)"
  [ -n "$match" ] || continue
  hits="$hits"$'\n'"  \"$match\" -- ${messages[$i]}"
done

[ -n "$hits" ] || exit 0

jq -n --arg hits "$hits" --arg path "$file_path" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "deny",
    permissionDecisionReason: ("This text matches a pattern that .banned-patterns forbids, in " + $path + ":" + $hits + "\n\nAGENTS.md states the rule, and scripts/check-banned-patterns.sh fails CI on the same hit. Rewrite the text and try again. To name banned text inside a rule that forbids it, put that text in backticks.")
  }
}'
