# A YAML or shell line contributes the text of its `#` comment. A `#` starts a
# comment at the start of a line or after a space, outside a quoted string and
# outside a `${...}` parameter expansion. Thus "#", ${var#prefix}, and $# start
# no comment. A quote resets at the end of a line, because a YAML block scalar
# can contain a single unpaired quote.
NR == 1 && /^#!/ { print ""; next }
{
  line = $0
  n = length(line)
  out = ""
  quote = ""
  depth = 0
  for (i = 1; i <= n; i++) {
    c = substr(line, i, 1)
    if (quote != "") {
      if (c == "\\" && quote == "\"") i++
      else if (c == quote) quote = ""
      continue
    }
    if (c == "\\") { i++; continue }
    if (c == "\"" || c == "\047") { quote = c; continue }
    if (c == "$" && substr(line, i + 1, 1) == "{") { depth++; i++; continue }
    if (c == "}" && depth > 0) { depth--; continue }
    if (c == "#" && depth == 0 && (i == 1 || substr(line, i - 1, 1) ~ /[[:space:]]/)) {
      out = substr(line, i + 1)
      break
    }
  }
  gsub(/`[^`]*`/, "", out)
  print out
}
