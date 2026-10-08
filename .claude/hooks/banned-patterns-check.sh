#!/usr/bin/env bash
# PreToolUse check: deny the call when its pending prose matches a pattern that
# `.banned-patterns` forbids. Two kinds of call carry prose:
#
#   - A Write or an Edit on Markdown, F# source, Lua source, or a Vim help file
#     in doc/.
#   - A Bash command that publishes prose: `git commit`, a `gh` issue or pull
#     request create, edit, or comment, and a `gh api` call that sends a body.
#     The check reads the command text and each body file that the command
#     names. This stops the text before it is public. pr-text.yml checks a
#     pull request's text in CI only after it is public.
#
# This is enforcement. The pattern list decides the answer with no judgment, so
# the agent learns at the moment of writing rather than in CI.
# scripts/check-banned-patterns.sh stays the complete check for files, because
# this hook cannot see a file that a Bash command writes.
# AGENTS.md states the rule.
set -euo pipefail

input="$(cat)"
tool_name="$(jq -r '.tool_name // empty' <<<"$input")"

if [ "$tool_name" = "Bash" ]; then
  command="$(jq -r '.tool_input.command // empty' <<<"$input")"
  echo "$command" | grep -Eq 'git( +-[cC] +[^ ]+| +-[^ ]+)* +commit\b|gh +(issue|pr) +(create|edit|comment)\b|gh +api\b.*\bbody=' || exit 0

  cwd="$(jq -r '.cwd // empty' <<<"$input")"
  pending="$command"
  # A body file follows `--body-file`, `--file`, `-F`, or `body=@`. The name
  # `-` means stdin, and the command text already holds a heredoc.
  while IFS= read -r body_file; do
    body_file="${body_file#\"}"; body_file="${body_file%\"}"
    body_file="${body_file#\'}"; body_file="${body_file%\'}"
    [ "$body_file" = "-" ] && continue
    case "$body_file" in /*) ;; *) body_file="${cwd:-.}/$body_file" ;; esac
    [ -f "$body_file" ] && pending="$pending"$'\n'"$(cat "$body_file")"
  done < <(printf '%s\n' "$command" \
    | grep -oE '(--body-file|--file)[= ]+("[^"]*"|'"'"'[^'"'"']*'"'"'|[^ ]+)|-F +("[^"]*"|'"'"'[^'"'"']*'"'"'|[^ =]+( |$))|body=@("[^"]*"|[^ ]+)' \
    | sed -E 's/^(--body-file|--file)[= ]+//; s/^-F +//; s/^body=@//; s/ $//' || true)
  label="this command"
  kind="markdown"
else
  file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

  case "$file_path" in
    *.md | *.markdown) kind="markdown" ;;
    *.fs | *.fsx) kind="fsharp" ;;
    *.lua) kind="lua" ;;
    */doc/*.txt) kind="vimhelp" ;;
    *) exit 0 ;;
  esac

  case "$file_path" in
    */.agents/* | */.claude/skills/* | */node_modules/* | */obj/* | *.banned-patterns) exit 0 ;;
  esac

  # Write carries the whole file, Edit carries the replacement text alone.
  pending="$(jq -r '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' <<<"$input")"
  label="$file_path"
fi

[ -n "$pending" ] || exit 0

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

strip_lua='
  {
    line = $0
    n = length(line)
    out = ""
    i = 1
    while (i <= n) {
      if (closing != "") {
        j = index(substr(line, i), closing)
        if (j == 0) { out = out " " substr(line, i); break }
        out = out " " substr(line, i, j - 1)
        i += j - 1 + length(closing)
        closing = ""
        continue
      }
      if (quote != "") {
        j = i
        while (j <= n && substr(line, j, 1) != quote) {
          if (substr(line, j, 1) == "\\") j++
          j++
        }
        out = out " " substr(line, i, j - i)
        if (j > n) break
        quote = ""
        i = j + 1
        continue
      }
      rest = substr(line, i)
      if (substr(rest, 1, 2) == "--") {
        rest = substr(rest, 3)
        if (match(rest, /^\[=*\[/)) {
          closing = "]" substr(rest, 2, RLENGTH - 2) "]"
          i += 2 + RLENGTH
          continue
        }
        out = out " " rest
        break
      }
      if (match(rest, /^\[=*\[/)) {
        closing = "]" substr(rest, 2, RLENGTH - 2) "]"
        i += RLENGTH
        continue
      }
      c = substr(rest, 1, 1)
      if (c == "\"" || c == "\047") {
        quote = c
        i++
        continue
      }
      i++
    }
    gsub(/`[^`]*`/, "", out)
    print out
  }
'

strip_vimhelp='
  example && /^</ { example = 0; print substr($0, 2); next }
  example && /^[^[:space:]]/ { example = 0 }
  example { print ""; next }
  {
    line = $0
    if (match(line, /(^|[[:space:]])>[a-z]*$/)) {
      example = 1
      line = substr(line, 1, RSTART - 1)
    }
    gsub(/`[^`]*`/, "", line)
    print line
  }
'

case "$kind" in
  markdown) stripped="$(printf '%s\n' "$pending" | awk "$strip_markdown")" ;;
  lua) stripped="$(printf '%s\n' "$pending" | awk "$strip_lua")" ;;
  vimhelp) stripped="$(printf '%s\n' "$pending" | awk "$strip_vimhelp")" ;;
  *) stripped="$(printf '%s\n' "$pending" | awk "$strip_fsharp")" ;;
esac

hits=""
for i in "${!patterns[@]}"; do
  match="$(printf '%s\n' "$stripped" | grep -ioE -- "${patterns[$i]}" | head -1 || true)"
  [ -n "$match" ] || continue
  hits="$hits"$'\n'"  \"$match\" -- ${messages[$i]}"
done

[ -n "$hits" ] || exit 0

jq -n --arg hits "$hits" --arg label "$label" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "deny",
    permissionDecisionReason: ("This text matches a pattern that .banned-patterns forbids, in " + $label + ":" + $hits + "\n\nAGENTS.md states the rule, and scripts/check-banned-patterns.sh fails CI on the same hit in a file. Rewrite the text and try again. To name banned text inside a rule that forbids it, put that text in backticks.")
  }
}'
