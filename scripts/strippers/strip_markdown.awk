# A fenced block and an inline code span are not prose, which lets a rule name
# the text it forbids. Blanking a stripped line keeps the line numbers true.
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
