# An MSBuild project file contributes the text of its `<!-- -->` comments. The
# stripper removes all other text, such as an element value or an attribute. A
# comment can span many lines. Each line prints the comment text on that line,
# so a hit keeps the line number of its source line.
{
  line = $0
  out = ""
  while (line != "") {
    if (incomment) {
      end = index(line, "-->")
      if (end == 0) {
        out = out line
        line = ""
      } else {
        out = out substr(line, 1, end - 1)
        line = substr(line, end + 3)
        incomment = 0
      }
    } else {
      start = index(line, "<!--")
      if (start == 0) {
        line = ""
      } else {
        # A space keeps two comments on one line as separate words.
        if (out != "") out = out " "
        line = substr(line, start + 4)
        incomment = 1
      }
    }
  }
  gsub(/`[^`]*`/, "", out)
  print out
}
