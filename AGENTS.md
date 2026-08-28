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
the skill before you post the text, not after. A one-line code comment is still prose.

**Name a domain concept with the term that `CONTEXT.md` defines.** This binds an identifier, a
comment, a log string, an envelope tag, a test name, an issue title, and a commit message. A concept
that you cannot state in glossary terms is a signal: either the glossary is missing it, or your
language is wrong. Resolve that instead of reaching for a synonym.

**Keep your prose clear of the words in `.banned-words`.** The file at the repo root lists them, and
`scripts/check-banned-words.sh` runs in CI and fails the build on a hit. A backstop hook refuses a
`Write` or an `Edit` that carries one. To name a banned word inside a rule that forbids it, put the
word in backticks. To ban another word, add one line to the file.

**Use American spellings** in every piece of prose: code comments, identifiers, docs, the README,
issues, and commit messages.

**A vendored skill is never edited.** `.agents/` holds skills that other authors wrote, and
`skills-lock.json` tracks them. They keep their authors' prose and spelling. Where this repo needs
different behavior, a document of its own **overlays** the skill, and the overlay wins. Where a
hook fires before a risky command, that hook is a **backstop**: it states the rule at the moment of
risk, and it cannot check that you obeyed. The rule is still the requirement.

## Read before you act

- **Before you write F#**, read `docs/standards/coding-standards.md`. It holds the house rules that
  Fantomas and `.editorconfig` cannot check.
- **Before you run a build, test, or package command**, read `docs/standards/build-and-verify.md`.
  It lists the full command set that CI runs.
- **Before you explore the codebase**, read `CONTEXT.md` and the ADRs in `docs/adr/` that touch your
  area. `docs/agents/domain.md` states how to use them, and what to do when your output contradicts
  an ADR.
- **Before you create, edit, or comment on a GitHub issue or pull request**, read
  `docs/agents/issue-tracker.md`. Issues live as GitHub issues on `tw0po1nt/FsHttp.Studio`, and that
  file holds the `gh` command lines and the wayfinding operations.
- **Before you write a spec**, read `docs/standards/spec-writing.md`. The full text belongs in
  `docs/spec/`, and the issue keeps a short summary and a link to it.
- **Before you open a pull request that changes `src/renderer/`, `src/webview/`, or
  `src/host/ResponseViewer.fs`**, read `docs/standards/ui-screenshots.md`. That pull request must
  carry a screenshot of the running editor.
- **Before you publish a release**, read `docs/standards/release-gate.md`. The UI suite is the
  release gate, and that file states the gaps it leaves open.

## Terminology

**"spec", never `PRD`.** The document that `/to-spec` produces is a spec. `.banned-words` holds the
other word, so CI fails on it. Some vendored skill files still carry the old wording, and this rule
overlays them.

**Triage labels.** The five triage roles, where each label string is equal to its role name:

- `needs-triage` — a maintainer must evaluate this issue
- `needs-info` — the reporter must supply more information
- `ready-for-agent` — fully specified, ready for an AFK agent
- `ready-for-human` — a person must implement this
- `wontfix` — this will not be fixed

When a skill names a role, use the label string of the same name.
