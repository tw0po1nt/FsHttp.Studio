#!/usr/bin/env bash
# Fails when a banned pattern reaches the prose of this repo. The pattern list is
# `.banned-patterns` at the repo root, and AGENTS.md states the rule.
#
# Each list line holds an extended regular expression, a tab, and the message to
# report. The check reads Markdown files, the `//` comment lines of F# source,
# and the string literals of F# source. It strips fenced code blocks and inline
# code spans first, so a rule can name the text it forbids by writing that text
# in backticks. A file that git ignores is skipped. A new file that git does not
# track yet is still read.
#
# `--text <label> <file>` checks one file of text as Markdown, and reports each
# hit under <label>. CI uses this mode for the text of a pull request: the
# title, the body, and the commit messages. No tracked file holds that text.
set -euo pipefail

if [ "${1:-}" = "--text" ]; then
  [ $# -eq 3 ] || { echo "Usage: $0 --text <label> <file>" >&2; exit 2; }
  text_label="$2"
  text_file="$(cd "$(dirname "$3")" && pwd)/$(basename "$3")"
fi

cd "$(git rev-parse --show-toplevel)"

patterns=()
messages=()
while IFS=$'\t' read -r pattern message; do
  case "$pattern" in '' | '#'*) continue ;; esac
  patterns+=("$pattern")
  messages+=("${message:-Rewrite the text.}")
done < .banned-patterns

if [ ${#patterns[@]} -eq 0 ]; then
  echo "check-banned-patterns: .banned-patterns holds no pattern. Nothing to check."
  exit 0
fi

# A fenced block and an inline code span are not prose, which lets a rule name
# the text it forbids. Blanking a stripped line keeps the line numbers true.
strip_markdown='
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
'

# A comment line is prose. A code line contributes its string literals alone,
# because a shipped string is prose that a user reads.
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

found=0

# Prints each hit in the stripped text of one source. $1 is the label for a hit.
report_hits() {
  local label="$1" stripped="$2" i hit
  for i in "${!patterns[@]}"; do
    while IFS= read -r hit; do
      [ -n "$hit" ] || continue
      echo "$label:${hit%%:*}: \"${hit#*:}\" -- ${messages[$i]}"
      found=1
    done < <(printf '%s\n' "$stripped" | grep -inoE -- "${patterns[$i]}" || true)
  done
}

if [ -n "${text_file:-}" ]; then
  report_hits "$text_label" "$(awk "$strip_markdown" "$text_file")"
else
  while IFS= read -r file; do
    case "$file" in
      .agents/* | */obj/* | .banned-patterns | scripts/check-banned-patterns.sh) continue ;;
    esac

    case "$file" in
      *.md) stripped="$(awk "$strip_markdown" "$file")" ;;
      *.fs | *.fsx) stripped="$(awk "$strip_fsharp" "$file")" ;;
      *) continue ;;
    esac

    report_hits "$file" "$stripped"
  done < <(git ls-files --cached --others --exclude-standard -- '*.md' '*.fs' '*.fsx')
fi

if [ "$found" -eq 1 ]; then
  echo
  echo "Each hit above matches a pattern that .banned-patterns forbids."
  echo "Rewrite the text. To name banned text inside a rule that forbids it, put"
  echo "that text in backticks."
  exit 1
fi

echo "check-banned-patterns: clean."
