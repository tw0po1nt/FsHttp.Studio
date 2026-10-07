# Contributing to FsHttp.Studio

This file routes you to the standards that a change must meet. Each standard lives in
`docs/standards/`. An agent reads the same files through `AGENTS.md`, so a rule has one home and
one wording.

## Bootstrap

```sh
dotnet tool restore
npm ci
./scripts/bootstrap.sh
```

`scripts/bootstrap.sh` installs the agent skills that `skills-lock.json` lists.

## Verify a change

```sh
dotnet build FsHttp.Studio.slnx
```

Run this command after every change to a `.fs` file. Fable accepts code that the F# compiler
rejects, so a clean Fable build does not prove that the solution compiles.
`docs/standards/build-and-verify.md` lists the full command set that CI runs.
`./scripts/verify.sh` runs the steps of `ci.yml` in one command.

## Read before you act

- Before you write F#, read `docs/standards/coding-standards.md`. It holds the house rules that
  Fantomas and `.editorconfig` cannot check.
- Before you open a pull request that changes `src/renderer/`, `src/webview/`, or
  `src/host/ResponseViewer.fs`, read `docs/standards/ui-screenshots.md`. A pull request that
  changes what a user sees must carry a screenshot of the running editor.
- Before you write a spec, read `docs/standards/spec-writing.md`. The full text belongs in
  `docs/spec/`, and the issue keeps a short summary and a link.
- Before you publish a release, read `docs/standards/release-gate.md`. It states what the UI suite
  covers and what it does not.
- Before you explore the codebase, read `GLOSSARY.md` and the ADRs in `docs/adr/` that touch your
  area. `GLOSSARY.md` is the project glossary, and a name in your change uses its terms.

## Prose

This repository writes its prose in Simplified Technical English, which
`docs/standards/technical-prose.md` describes. The rule covers docs, issue text, pull request text,
commit messages, and code comments.

`.banned-patterns` lists the text that no prose here uses, and
`scripts/check-banned-patterns.sh` fails CI on a hit. Assert a fact directly, and do not write an em
dash. Use American spellings, such as `color` and
`serialize`. The skills in `.agents/skills/` and `.claude/skills/` are vendored, and they keep their
authors' spelling.

## Issues

Issues live as GitHub issues on `tw0po1nt/FsHttp.Studio`. Open one before a large change, so that
the design gets a review before the code does.
