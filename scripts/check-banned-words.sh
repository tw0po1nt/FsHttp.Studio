#!/usr/bin/env bash
# Fails when a banned word reaches the prose of this repo. The word list is
# `.banned-words` at the repo root, and AGENTS.md states the rule.
#
# The check reads Markdown files and the `//` comment lines of F# source. It
# strips fenced code blocks and inline code spans first, so a rule can name the
# word it forbids by writing it in backticks. A file that git ignores is skipped.
# A new file that git does not track yet is still read.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

words=()
while IFS= read -r line; do
  line="${line%%#*}"
  line="$(printf '%s' "$line" | tr -d '[:space:]')"
  [ -n "$line" ] && words+=("$line")
done < .banned-words

if [ ${#words[@]} -eq 0 ]; then
  echo "check-banned-words: .banned-words holds no word. Nothing to check."
  exit 0
fi

strip_markdown='
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
'
strip_fsharp='
  /^[[:space:]]*\/\// { gsub(/`[^`]*`/, ""); print; next }
  { print "" }
'

found=0
while IFS= read -r file; do
  case "$file" in
    .agents/*|*/obj/*|.banned-words|scripts/check-banned-words.sh) continue ;;
  esac

  case "$file" in
    *.md) stripped="$(awk "$strip_markdown" "$file")" ;;
    *.fs) stripped="$(awk "$strip_fsharp" "$file")" ;;
    *) continue ;;
  esac

  for word in "${words[@]}"; do
    while IFS= read -r hit; do
      [ -n "$hit" ] || continue
      echo "$file:${hit%%:*}: banned word \"$word\""
      found=1
    done < <(printf '%s\n' "$stripped" | grep -inw -- "$word" || true)
  done
done < <(git ls-files --cached --others --exclude-standard -- '*.md' '*.fs')

if [ "$found" -eq 1 ]; then
  echo
  echo "Each hit above uses a word that .banned-words forbids. Rewrite the text."
  echo "To name a banned word inside a rule that forbids it, put it in backticks."
  exit 1
fi

echo "check-banned-words: clean."
