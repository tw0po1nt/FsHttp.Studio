# A Vim help line is prose, except in an example. A line that ends in ">", or
# in ">" and a language name, starts an example. In the example, an indented
# line is code. A line that starts with "<", or with a character that is not a
# space, ends the example.
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
