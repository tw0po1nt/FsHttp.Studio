#!/usr/bin/env bash
# Runs each case of a stripper in scripts/strippers/ under BSD awk, mawk, and
# gawk. The stripper must give the same result under each. In CI, the run fails
# when one of the three is missing. Elsewhere, the run uses the awks that it finds.
set -uo pipefail

strippers="$(cd "$(dirname "$0")/../../scripts/strippers" && pwd)"

# The BSD awk is `awk` on macOS and `original-awk` on Linux. On Linux, `awk` is
# a link to mawk or gawk, so its version line identifies the BSD awk.
awks=()
for candidate in original-awk awk; do
  if command -v "$candidate" > /dev/null && "$candidate" --version 2> /dev/null | grep -q '^awk version'; then
    awks+=("$candidate")
    break
  fi
done
for candidate in mawk gawk; do
  command -v "$candidate" > /dev/null && awks+=("$candidate")
done

if [ -n "${CI:-}" ] && [ ${#awks[@]} -lt 3 ]; then
  echo "strippers: CI needs BSD awk, mawk, and gawk. Found: ${awks[*]}"
  echo "strippers: red"
  exit 1
fi

failed=0
passed=0

# $1 is the stripper, $2 names the case, $3 is the input, and $4 is the output.
check() {
  local stripper="$1" name="$2" input="$3" expected="$4" awk actual
  for awk in "${awks[@]}"; do
    actual="$(printf '%s\n' "$input" | "$awk" -f "$strippers/$stripper.awk")"
    if [ "$actual" = "$expected" ]; then
      passed=$((passed + 1))
    else
      failed=1
      echo "FAIL ($awk) $stripper: $name"
      echo "  expected: $(printf '%q' "$expected")"
      echo "  actual:   $(printf '%q' "$actual")"
    fi
  done
}

check strip_hash "a comment line is prose" \
  '# A comment.' \
  ' A comment.'
check strip_hash "an indented comment line is prose" \
  '    # A comment.' \
  ' A comment.'
check strip_hash "a comment after a space is prose" \
  'contents: write # push the tag' \
  ' push the tag'
check strip_hash "a code line contributes nothing" \
  'run: echo hello' \
  ''
check strip_hash "the shebang line is skipped" \
  $'#!/usr/bin/env bash\n# A comment.' \
  $'\n A comment.'
check strip_hash "a shebang after the first line is a comment" \
  $'set -e\n#!x' \
  $'\n!x'
check strip_hash "inline code in backticks is removed" \
  '# Run `a, not b` here.' \
  ' Run  here.'
check strip_hash "a # in a double-quoted string starts no comment" \
  'echo "a # b"' \
  ''
check strip_hash "a # in a single-quoted string starts no comment" \
  "case \"\$p\" in '' | '#'*) continue ;; esac" \
  ''
check strip_hash "an escaped quote does not end a double-quoted string" \
  'echo "a \" # b"' \
  ''
check strip_hash "a comment after a quoted string is prose" \
  'echo "a # b" # A comment.' \
  ' A comment.'
check strip_hash "a # in a parameter expansion starts no comment" \
  'v="${tag#v}"' \
  ''
check strip_hash "a # in a parameter expansion after a space starts no comment" \
  'v="${tag:- #}"; w=${tag:- #}' \
  ''
check strip_hash "a nested parameter expansion closes" \
  'v=${a:-${b#x}} # A comment.' \
  ' A comment.'
check strip_hash "the length of an array starts no comment" \
  'if [ ${#failed[@]} -gt 0 ]; then' \
  ''
check strip_hash "the count of arguments starts no comment" \
  '[ $# -eq 3 ] || exit 2' \
  ''
check strip_hash "a # inside a word starts no comment" \
  'echo a#b' \
  ''
check strip_hash "an escaped # starts no comment" \
  'echo \#b' \
  ''
check strip_hash "a quote resets at the end of a line" \
  $'  A \'block scalar.\n# A comment.' \
  $'\n A comment.'
check strip_hash "an apostrophe in a plain YAML value starts no quote" \
  "name: Don't go # A comment." \
  ' A comment.'
check strip_hash "a # in a single-quoted value after = starts no comment" \
  "x='#' # A comment." \
  ' A comment.'
check strip_hash "an escaped quote does not end a \$'...' string" \
  "echo \$'a\\' # b' # A comment." \
  ' A comment.'
check strip_hash "an escape in a single-quoted string does not skip the close quote" \
  "echo 'a\\' # A comment." \
  ' A comment.'

if [ "$failed" -ne 0 ]; then
  echo "strippers: red"
  exit 1
fi
echo "strippers: $passed checks passed under ${awks[*]}"
