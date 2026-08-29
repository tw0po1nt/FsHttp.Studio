---
Status: accepted
---

# Standards live in `docs/standards/`, and `docs/agents/` holds only the paths a vendored skill pins

`docs/standards/` holds the rules that bind every contributor: `build-and-verify.md`,
`coding-standards.md`, `release-gate.md`, `spec-writing.md`, `technical-prose.md`, and
`ui-screenshots.md`. `docs/agents/` holds `issue-tracker.md` and `domain.md`. `AGENTS.md` routes an
agent into both directories, and `CONTRIBUTING.md` routes a person into `docs/standards/`. Both
routers point at one set of files, so no rule has two homes.

Every moved file states a rule that binds a person as much as an agent. `ui-screenshots.md` says so
in its own second sentence. `.github/PULL_REQUEST_TEMPLATE.md` already sent a human contributor into
`docs/agents/`. The old name told a contributor to keep out of the only place the standards lived,
and this repository has no other contributor documentation.

A rename of the whole directory was not available. `code-review/SKILL.md` reads
`docs/agents/issue-tracker.md` by path, and `setup-matt-pocock-skills/SKILL.md` writes
`docs/agents/issue-tracker.md` and `docs/agents/domain.md` by name. Both skills are vendored, and
`AGENTS.md` forbids an edit to a vendored file. The two pinned files are also the two that hold
agent mechanics: `gh` command lines and skill wiring. Ownership and audience agree here, so the
pinned paths give the line its shape.

`docs/agents/triage-labels.md` is deleted rather than moved. No skill reads it. The vendored
`triage` skill carries its own copy of the five roles, and `AGENTS.md` now states the role names
directly. A re-run of `/setup-matt-pocock-skills` recreates the file, which is an explicit restart
and not a routine operation.

Two alternatives were rejected. A rename of every file breaks the two vendored skills. A new
`CONTRIBUTING.md` on its own leaves the standards under a name that excludes the reader it wants.

The move costs five stale links. Specs 0003, 0004, 0005, 0007, and 0011 name `docs/release-gate.md`.
A spec records what one change asked for, so these files keep their original text. A reader who
follows one of those links must look in `docs/standards/`.
