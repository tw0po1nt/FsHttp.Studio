# Technical prose: the STE skill is mandatory

An agent must run the `simplified-technical-english` skill on every piece of prose it writes in
this repo. This rule has no exception, and **all prose** means all prose:

- Issue titles and bodies
- Pull request titles and descriptions
- Comments on an issue or a pull request
- ADRs (`docs/adr/`)
- Specs (`docs/spec/`)
- Agent-facing docs (`docs/agents/`) and every other Markdown file in the repo
- Commit messages
- Your reply to the user in an interactive session
- Code comments, including the one-line comments that rule 5 of `docs/standards/coding-standards.md` permits

A short piece of prose is still prose. A reply that you type in a session is prose that a person
reads, so it carries the rule with the same force as a file that lands in the repo.

A short piece of prose is still prose. A one-line code comment and a two-sentence issue body both
carry the rule.

Run the skill before you create or post the text, not after. A draft that you revise later is a
draft that a reviewer may already have read.

## Assert a fact directly

An **antithesis** states a fact and then denies its opposite. The denial carries no information.
It sets a rhythm, and the rhythm tires a reader who meets it in every paragraph. Write the fact
alone.

| Do not write | Write |
| --- | --- |
| `The hook is a backstop, not the mechanism.` | `The hook is a backstop.` |
| `Use one lock, not one lock for each access.` | `Use one lock for each logical operation.` |
| `It is not a lint. It is a convention.` | `It is a convention.` |
| `This is not just a reminder, it is enforcement.` | `This is enforcement.` |
| `Bundle with esbuild, not webpack.` | `Bundle with esbuild.` |

The banned forms are `X, not Y`, `not X but Y`, `not X but rather Y`, `not just`, `not merely`,
`not simply`, `not only`, and the same contrast split across two sentences.

`rather than` and `instead of` stay legal, because each half of the comparison carries information:

```text
Use a bounded wait rather than a blocking read.
```

That sentence names two real options and picks one. An antithesis names one option twice.

Where a rejected option matters, give it a sentence of its own with the reason. An ADR needs this,
because an ADR records a choice between real alternatives. Put the choice in the title, and put the
rejected option in the body:

```text
# Bundle with esbuild

We considered webpack. Its configuration surface is larger, and the build was slower on this
project. We chose esbuild.
```

## Do not write an em dash

Use a comma, a colon, or a period. An em dash breaks a sentence in a way that a reader must
re-parse, and a page of them reads as one long aside.

## The pattern list holds both rules

`.banned-patterns` at the repo root holds the antithesis forms and the em dash. Both patterns are
inactive while the existing text still carries them. The rules above bind from now, and the sweep
that rewrites the existing text turns the patterns on.

## Why this rule is strict

The skill exists in this repo already, and agents skip it most of the time. A soft reminder did
not change that. This rule states the requirement without a qualifier, so an agent cannot read it
as optional.

## The hook is a backstop, not the mechanism

A `PreToolUse` hook (`.claude/settings.json`) fires before a `gh issue`/`gh pr` create, edit, or
comment command, and before a `Write` or `Edit` on any Markdown file in the repo. It injects a
reminder to run the STE skill first. The hook cannot see a code comment inside a `.fs` change, so
the rule alone governs a source comment. The hook cannot verify that the skill ran, and it
cannot verify the quality of the text. It only guarantees that the reminder appears at the moment
the risk is highest: the moment before the text becomes visible to a human reader.

The rule above is still the requirement. This document is an overlay on the vendored
`simplified-technical-english` skill, and the hook is its backstop.
