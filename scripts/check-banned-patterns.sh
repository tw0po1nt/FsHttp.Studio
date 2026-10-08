#!/usr/bin/env bash
# Fails when a banned pattern reaches the prose of this repo. The pattern list is
# `.banned-patterns` at the repo root, and AGENTS.md states the rule.
#
# Each list line holds an extended regular expression, a tab, and the message to
# report. The check reads these sources:
#
#   - Markdown files.
#   - The `//` comment lines and the string literals of F# source.
#   - The `--` comments and the string literals of Lua source.
#   - The Vim help files in doc/, which the Neovim client ships.
#
# It strips fenced code blocks, Vim help examples, and inline code spans first,
# so a rule can name the text it forbids by writing that text in backticks. A
# file that git ignores is skipped. A new file that git does not track yet is
# still read.
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

# A Lua line contributes the text of each comment and each string literal. A
# long comment or a long string can span lines, so `closing` holds the bracket
# that ends it, such as "]]" or "]==]". A quoted string spans lines after a
# trailing "\" or "\z", so `quote` holds its open quote character.
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

# A Vim help line is prose, except in an example. A line that ends in ">", or
# in ">" and a language name, starts an example. In the example, an indented
# line is code. A line that starts with "<", or with a character that is not a
# space, ends the example.
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
      *.lua) stripped="$(awk "$strip_lua" "$file")" ;;
      doc/*.txt) stripped="$(awk "$strip_vimhelp" "$file")" ;;
      *) continue ;;
    esac

    report_hits "$file" "$stripped"
  done < <(git ls-files --cached --others --exclude-standard -- '*.md' '*.fs' '*.fsx' '*.lua' 'doc/*.txt')
fi

if [ "$found" -eq 1 ]; then
  echo
  echo "Each hit above matches a pattern that .banned-patterns forbids."
  echo "Rewrite the text. To name banned text inside a rule that forbids it, put"
  echo "that text in backticks."
  exit 1
fi

echo "check-banned-patterns: clean."
