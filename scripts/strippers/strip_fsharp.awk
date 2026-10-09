# A comment line is prose. A code line contributes its string literals alone,
# because a shipped string is prose that a user reads.
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
