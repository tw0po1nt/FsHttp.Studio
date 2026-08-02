# FsHttp.Studio

## Agent skills

### Issue tracker

Issues live as GitHub issues on `tw0po1nt/FsHttp.Studio`. Manage them with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Spec writing

A spec's full text lives in `docs/spec/`, not in the issue body. See `docs/agents/spec-writing.md`.

### Triage labels

The five canonical triage roles. Each label string is equal to its role name. See `docs/agents/triage-labels.md`.

### Domain docs

One context. `CONTEXT.md` and `docs/adr/` are at the repo root. See `docs/agents/domain.md`.

### Coding standards

F# house rules that go beyond the rules Fantomas and `.editorconfig` enforce. See `docs/coding-standards.md`.

### Technical prose

Running the `simplified-technical-english` skill on every piece of prose you write is mandatory, not optional. See `docs/agents/technical-prose.md`.

## Terminology

- **"spec", never "PRD".** The document that `/to-spec` produces is a spec, because that is what it is. Matt Pocock's skills gave the rationale for this rename in v1.1. Do not use the "PRD" wording in anything you write: specs, issues, tickets, or comments. Some vendored skill files still carry the old "PRD" wording. Ignore that wording and use "spec".

- **American spellings.** Use American English in every piece of prose you write: code comments, identifiers, docs, the README, issues, and commit messages. For example, write `color` not `colour`, `serialize` not `serialise`, `behavior` not `behaviour`, `honored` not `honoured`, and `canceled` not `cancelled`. CSS and platform API names that are already American (`color`, `--vscode-*`) do not change. Vendored files under `.agents/` keep their authors' spelling. Do not rewrite them.
