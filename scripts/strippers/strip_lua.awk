# A Lua line contributes the text of each comment and each string literal. A
# long comment or a long string can span lines, so `closing` holds the bracket
# that ends it, such as "]]" or "]==]". A quoted string spans lines after a
# trailing "\" or "\z", so `quote` holds its open quote character.
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
