# FsHttp.Studio

## Agent skills

### Issue tracker

Issues live as GitHub issues on `tw0po1nt/FsHttp.Studio`, managed via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles, each label string equal to its name. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Coding standards

F# house rules beyond what Fantomas/`.editorconfig` enforce. See `docs/coding-standards.md`.

### Technical prose

Use the `simplified-technical-english` skill for technical prose.

## Terminology

- **"spec", never "PRD".** The document `/to-spec` produces is a spec — that's what it actually is. Per the v1.1 rename rationale for Matt Pocock's skills, drop the "PRD" framing in anything you write (specs, issues, tickets, comments). Some vendored skill files still carry the old "PRD" wording; ignore it and use "spec".

- **American spellings.** Use American English in every piece of prose you write — code comments and identifiers, docs, the README, issues, and commit messages (`color` not `colour`, `serialize` not `serialise`, `behavior` not `behaviour`, `honored` not `honoured`, `canceled` not `cancelled`). CSS and platform API names that are already American (`color`, `--vscode-*`) stay as-is. Vendored files under `.agents/` keep their authors' spelling — don't rewrite them.
