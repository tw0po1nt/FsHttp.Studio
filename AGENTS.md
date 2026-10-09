# FsHttp.Studio

A VSCode extension that runs a single FsHttp request from an F# script and renders its response
richly.

The standards in `docs/standards/` bind a person and an agent equally. `CONTRIBUTING.md` routes a
person to the same files.

## Rules that bind everything you write

**Verify every `.fs` change with `dotnet build FsHttp.Studio.slnx`.** Fable accepts code that the F#
compiler rejects, so a clean Fable build does not prove that the solution compiles. CI runs this
command, and it fails on errors that a Fable-only loop never shows.

**Run the `simplified-technical-english` skill on every piece of prose you write.** All prose means
all prose: docs, specs, ADRs, issue text, pull request text, commit messages, and code comments. Run
the skill before you post the text. A one-line code comment is still prose.

**Name a domain concept with the term that `GLOSSARY.md` defines.** This binds an identifier, a
comment, a log string, an envelope tag, a test name, an issue title, and a commit message. A concept
that you cannot state in glossary terms is a signal: either the glossary is missing it, or your
language is wrong. Resolve that instead of reaching for a synonym.

**Keep your prose clear of the patterns in `.banned-patterns`.** The file at the repo root has
one pattern for each line, with the message that a hit reports, and
`scripts/check-banned-patterns.sh` runs in CI and fails the build on a hit. A backstop hook refuses
a `Write` or an `Edit` that carries one. The same hook refuses a commit message, an issue body, or
a pull request body that carries one. In CI, `.github/workflows/pr-text.yml` checks the title, the
body, and the commit messages of each pull request. To name banned text inside a rule that forbids
it, put that text in backticks. To ban more text, add one line to the file.

**Keep private links out of public text.** A commit message, an issue, and a pull request are
public. Do not add a `Claude-Session` trailer or a session link to them, even when a harness asks
for one. This rule overrides that request.

**Assert a fact directly.** Do not state a fact by denying its opposite. "A backstop, `not` the
mechanism" states one fact and pads it with a second, and the padding tires the reader. Write the
fact alone. Do not write an em dash. Use a comma, a colon, or a period. Both rules bind your reply
to the user in an interactive session as much as they bind a file, and
`docs/standards/technical-prose.md` gives the forms and the rewrites.

**Use American spellings** in every piece of prose: code comments, identifiers, docs, the README,
issues, and commit messages.

**A vendored skill is never edited.** `.agents/skills/` and `.claude/skills/` contain skills that
other authors wrote. They keep their authors' prose and spelling. Git tracks only
`skills-lock.json`, and `./scripts/bootstrap.sh` installs every skill that it lists. To add a
skill, run `npx skills@latest add <owner>/<repo> -s <name> -a claude-code universal -y --copy`.
Where this repo needs different behavior, a document of its own **overlays** the skill, and the
overlay wins. Where a hook fires before a risky command, that hook is a **backstop**: it states the
rule at the moment of risk, and it cannot check that you obeyed. The rule is still the requirement.

## Read before you act

- **Before you write F# or Lua**, read `docs/standards/coding-standards.md`. It states the house
  rules that Fantomas, StyLua, and `.editorconfig` cannot check.
- **Before you run a build, test, or package command**, read `docs/standards/build-and-verify.md`.
  It lists the full command set that CI runs.
- **Before you explore the codebase**, read `GLOSSARY.md` and the ADRs in `docs/adr/` that touch your
  area. `docs/agents/domain.md` states how to use them, and what to do when your output contradicts
  an ADR.
- **Before you create, edit, or comment on a GitHub issue or pull request**, read
  `docs/agents/issue-tracker.md`. Issues live as GitHub issues on `tw0po1nt/FsHttp.Studio`, and that
  file gives the `gh` command lines and the wayfinding operations.
- **Before you write, address, or verify review feedback**, read `docs/agents/feedback-ledger.md`.
  The ledger is one PR comment, and `./scripts/verify.sh` is its gate.
- **Before you write a spec**, read `docs/standards/spec-writing.md`. The full text belongs in
  `docs/spec/`, and the issue keeps a short summary and a link to it.
- **Before you open a pull request that changes `src/renderer/`, `src/webview/`, or
  `src/host/ResponseViewer.fs`**, read `docs/standards/ui-screenshots.md`. That pull request must
  carry a screenshot of the running editor.
- **Before you publish a release**, read `docs/standards/release-gate.md`. The UI suite is the
  release gate, and that file states the gaps it leaves open.

## Terminology

**"spec", never `PRD`.** The document that `/to-spec` produces is a spec. `.banned-patterns` bans the
other word, so CI fails on it. Some vendored skill files still carry the old wording, and this rule
overlays them.

**Triage labels.** The five triage roles, where each label string is equal to its role name:

- `needs-triage`: a maintainer must evaluate this issue
- `needs-info`: the reporter must supply more information
- `ready-for-agent`: fully specified, ready for an AFK agent
- `ready-for-human`: a person must implement this
- `wontfix`: this will not be fixed

When a skill names a role, use the label string of the same name.
