# FsHttp.Studio

A VSCode extension that runs a single FsHttp request from an F# script and renders its response richly.

## Build and verify

Bootstrap once with `dotnet tool restore && npm ci`.

**Verify every `.fs` change with `dotnet build FsHttp.Studio.slnx`.** Fable alone is laxer than the F# compiler and lets errors through that CI then catches. See `docs/standards/build-and-verify.md` for the full command set.

## Agent skills

### Issue tracker

Issues live as GitHub issues on `tw0po1nt/FsHttp.Studio`. Manage them with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Spec writing

A spec's full text lives in `docs/spec/`, not in the issue body. See `docs/standards/spec-writing.md`.

### Triage labels

The five triage roles. Each label string is equal to its role name.

- `needs-triage` — a maintainer must evaluate this issue
- `needs-info` — the reporter must supply more information
- `ready-for-agent` — fully specified, ready for an AFK agent
- `ready-for-human` — a person must implement this
- `wontfix` — this will not be fixed

When a skill names a role, use the label string of the same name.

### Domain docs

One context. `CONTEXT.md` and `docs/adr/` are at the repo root. See `docs/agents/domain.md`.

### Coding standards

F# house rules that go beyond the rules Fantomas and `.editorconfig` enforce. See `docs/standards/coding-standards.md`.

### UI screenshots

A pull request that changes what the user sees must carry a screenshot of the running editor. See `docs/standards/ui-screenshots.md`.

### Release gate

The UI suite is the release gate. What it covers, and the gaps it does not, live in `docs/standards/release-gate.md`.

### Technical prose

Running the `simplified-technical-english` skill on every piece of prose you write is mandatory, not optional. All prose: docs, issues, pull requests, commit messages, and code comments. See `docs/standards/technical-prose.md`.

## Terminology

- **"spec", never `PRD`.** The document that `/to-spec` produces is a spec, because that is what it is. Matt Pocock's skills gave the rationale for this rename in v1.1. Do not use the `PRD` wording in anything you write: specs, issues, tickets, or comments. Some vendored skill files still carry the old wording. Ignore it and use "spec".

- **Banned words.** `.banned-words` at the repo root lists the words that no prose in this repo uses. `provenance` is one: write what the thing is, such as "the commit message holds that history". `scripts/check-banned-words.sh` runs in CI and fails the build on a hit. It reads Markdown and the `//` comment lines of F# source, and it strips code spans first, so a rule can name the word it forbids by putting it in backticks. To ban another word, add one line to `.banned-words`.

- **American spellings.** Use American English in every piece of prose you write: code comments, identifiers, docs, the README, issues, and commit messages. For example, write `color` not `colour`, `serialize` not `serialise`, `behavior` not `behaviour`, `honored` not `honoured`, and `canceled` not `cancelled`. CSS and platform API names that are already American (`color`, `--vscode-*`) do not change. Vendored files under `.agents/` keep their authors' spelling. Do not rewrite them.
