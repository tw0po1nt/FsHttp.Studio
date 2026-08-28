#!/usr/bin/env bash
# PreToolUse check for Write/Edit on Markdown or F# source: deny the call when
# the pending text carries a word that `.banned-words` forbids.
#
# This is enforcement, not a reminder. The word list decides the answer with no
# judgment, so the agent learns at the moment of writing rather than in CI.
# scripts/check-banned-words.sh stays the complete check: this hook cannot see a
# file that a Bash command writes.
# See AGENTS.md.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

case "$file_path" in
  *.md|*.markdown|*.fs|*.fsx) ;;
  *) exit 0 ;;
esac

case "$file_path" in
  */.agents/*|*/node_modules/*|*/obj/*) exit 0 ;;
esac

root="${CLAUDE_PROJECT_DIR:-.}"
[ -f "$root/.banned-words" ] || exit 0

words=()
while IFS= read -r line; do
  line="${line%%#*}"
  line="$(printf '%s' "$line" | tr -d '[:space:]')"
  [ -n "$line" ] && words+=("$line")
done < "$root/.banned-words"
[ ${#words[@]} -eq 0 ] && exit 0

# Write carries the whole file, Edit carries the replacement text alone.
pending="$(jq -r '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' <<<"$input")"
[ -n "$pending" ] || exit 0

# Match the stripping that scripts/check-banned-words.sh applies, so the hook and
# CI agree on what counts as prose. A fenced block and an inline code span are not
# prose, which lets a rule name the word it forbids.
strip_markdown='
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
'
strip_fsharp='
  /^[[:space:]]*\/\// { gsub(/`[^`]*`/, ""); print; next }
  { print "" }
'

case "$file_path" in
  *.md|*.markdown) stripped="$(printf '%s\n' "$pending" | awk "$strip_markdown")" ;;
  *) stripped="$(printf '%s\n' "$pending" | awk "$strip_fsharp")" ;;
esac

hits=""
for word in "${words[@]}"; do
  if printf '%s\n' "$stripped" | grep -qiw -- "$word"; then
    hits="$hits $word"
  fi
done

[ -n "$hits" ] || exit 0

jq -n --arg hits "${hits# }" --arg path "$file_path" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "deny",
    permissionDecisionReason: ("This text uses a word that .banned-words forbids: " + $hits + " (in " + $path + "). AGENTS.md states the rule, and scripts/check-banned-words.sh fails CI on the same hit. Rewrite the sentence and try again. To name a banned word inside a rule that forbids it, put the word in backticks.")
  }
}'
