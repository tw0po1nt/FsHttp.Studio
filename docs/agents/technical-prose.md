# Technical prose: the STE skill is mandatory

An agent must run the `simplified-technical-english` skill on every piece of prose it writes in
this repo. This rule has no exception. It applies to:

- Issue titles and bodies
- Pull request titles and descriptions
- Comments on an issue or a pull request
- ADRs (`docs/adr/`)
- Specs (`docs/spec/`)
- Commit messages

Run the skill before you create or post the text, not after. A draft that you revise later is a
draft that a reviewer may already have read.

## Why this rule is strict

The skill exists in this repo already, and agents skip it most of the time. A soft reminder did
not change that. This rule states the requirement without a qualifier, so an agent cannot read it
as optional.

## The hook is a backstop, not the mechanism

A `PreToolUse` hook (`.claude/settings.json`) fires before a `gh issue`/`gh pr` create, edit, or
comment command, and before a `Write` or `Edit` on `docs/adr/**/*.md` or `docs/spec/**/*.md`. It
injects a reminder to run the STE skill first. The hook cannot verify that the skill ran, and it
cannot verify the quality of the text. It only guarantees that the reminder appears at the moment
the risk is highest: the moment before the text becomes visible to a human reader.

The rule above is still the requirement. The hook exists because `simplified-technical-english` is
a vendored skill (source: `TheAngryByrd/simplified-technical-english-skill`, tracked in
`skills-lock.json`). An agent must not edit the vendored skill file to strengthen it. This doc,
and the hook, are the project's own enforcement layer on top of it.
