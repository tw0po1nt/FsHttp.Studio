# A YAML or shell line contributes the text of its `#` comment. A `#` starts a
# comment at the start of a line or after a space, outside a quoted string and
# outside a `${...}` parameter expansion. Thus "#", ${var#prefix}, and $# start
# no comment. A `'` after a letter or a digit is an apostrophe, and it starts no
# quote. In a $'...' string, a `\` escapes the next character, as it does in a
# "..." string. A quote resets at the end of a line, because a YAML block scalar
# can contain a single unpaired quote. The stripper reads one line at a time, so
# a `#` line in a YAML block scalar or in a shell heredoc counts as a comment.
NR == 1 && /^#!/ { print ""; next }
{
  line = $0
  n = length(line)
  out = ""
  quote = ""
  escapes = 0
  depth = 0
  for (i = 1; i <= n; i++) {
    c = substr(line, i, 1)
    if (quote != "") {
      if (c == "\\" && escapes) i++
      else if (c == quote) quote = ""
      continue
    }
    if (c == "\\") { i++; continue }
    prev = i > 1 ? substr(line, i - 1, 1) : ""
    if (c == "\"") { quote = c; escapes = 1; continue }
    if (c == "\047" && prev !~ /[[:alnum:]]/) { quote = c; escapes = (prev == "$"); continue }
    if (c == "$" && substr(line, i + 1, 1) == "{") { depth++; i++; continue }
    if (c == "}" && depth > 0) { depth--; continue }
    if (c == "#" && depth == 0 && (prev == "" || prev ~ /[[:space:]]/)) {
      out = substr(line, i + 1)
      break
    }
  }
  gsub(/`[^`]*`/, "", out)
  print out
}
