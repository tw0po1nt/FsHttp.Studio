# Spec writing: check the full spec into the repo

A spec as a GitHub issue is grounding for implementation, but the full text is long, and a long
issue overwhelms a human reader who opens the issue list. Keep the grounding. Move the bulk of the
text into the repo.

## The rule

When an agent produces a spec, whether through the `to-spec` skill or otherwise, it completes
these steps immediately, before it moves to another task:

1. Write the full spec to `docs/spec/NNNN-slug.md`. Use the next sequential number, zero-padded to
   four digits, independent of the GitHub issue number. This matches the numbering that
   `docs/adr/` already uses. The file has no frontmatter, and its first line is a sentence-case
   `#` heading that states the feature, not the issue title.
2. Retitle the GitHub issue. Replace the `Spec:` prefix with `Feature:`. Keep the rest of the
   title unchanged.
3. Replace the issue body with a short paragraph that states the problem and the solution in
   plain terms, then a link to the checked-in file. A reader who wants the full detail, the user
   stories, the implementation decisions, and the test plan, follows the link.

Do not leave an issue in the full-text `Spec:` state once these three steps are possible. The
overwhelming issue is the state this rule exists to end.

## Why the issue keeps a short body instead of nothing

A link with no summary forces every reader to open the file to learn even the problem statement.
A one-paragraph summary lets a reader triage from the issue list, and the file is still the source
of the full spec.

## What this does not change

- `to-spec` itself is a vendored skill (`mattpocock/skills`, tracked in `skills-lock.json`). An
  agent must not edit it to perform these steps internally. This doc is the project's own overlay,
  applied as the step that follows the skill's own output.
- The `ready-for-agent` triage label, and every other issue-tracker convention in
  `docs/agents/issue-tracker.md`, stay as they are.
- A spec that a skill has already published as a full-text `Spec:` issue, before this rule
  existed, is not retitled automatically. Convert it the next time an agent works on it.

## Template

`docs/spec/NNNN-slug.md` carries the same sections that `to-spec` already produces: Problem
Statement, Solution, User Stories, Implementation Decisions, Testing Decisions, Out of Scope, and
Further Notes. Carry the text over unchanged, apart from the heading.
