# This file maps a file path to the kind of the stripper that reads its prose.
# The stripper for a kind is strip_<kind>.awk in this directory.
# scripts/check-banned-patterns.sh and .claude/hooks/banned-patterns-check.sh
# both source this file, so they read the same files. To check a new file type,
# add one arm here.

# Sets `kind` for the path in $1. `kind` is empty when the check skips the file.
# The path can be relative to the repo root or absolute. Each pattern matches
# the path with a `/` before it, so `*/doc/*.txt` also matches `doc/fshttp.txt`.
stripper_kind() {
  case "/$1" in
    */.agents/* | */.claude/skills/* | */node_modules/* | */obj/* | */.banned-patterns) kind="" ;;
    *.md | *.markdown) kind="markdown" ;;
    *.fs | *.fsx) kind="fsharp" ;;
    *.lua) kind="lua" ;;
    */doc/*.txt) kind="vimhelp" ;;
    *.yml | *.yaml | *.sh) kind="hash" ;;
    *) kind="" ;;
  esac
}
