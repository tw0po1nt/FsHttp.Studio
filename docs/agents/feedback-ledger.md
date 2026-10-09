# The feedback ledger

The **ledger** is one document for each pull request. It records every **finding** that a review
has raised, with the **state** of each. Three skills share it. `to-feedback` creates and extends
it. `address-feedback` works it. `verify-feedback` judges it and writes the **verdict**.

## Location

The ledger is one PR comment. Its first line is the marker `<!-- feedback-ledger -->`.

When the branch has no PR, the ledger is the file `.scratch/feedback-<branch>.md`, where each
`/` in the branch name becomes `-`. The next `to-feedback` run that finds a PR moves the file
into a comment and deletes the file.

## Find the ledger

1. Run `gh pr view --json number,headRefOid,headRefName` on the current branch. When the user
   passes a PR number, put it after `view`.
2. When the command prints a PR, run this query. It prints the comment id, or nothing.

   ```bash
   gh api "repos/{owner}/{repo}/issues/<pr>/comments" \
     --jq '.[] | select(.body | startswith("<!-- feedback-ledger -->")) | .id'
   ```

3. When the query prints an id, fetch the body:

   ```bash
   gh api "repos/{owner}/{repo}/issues/comments/<id>" --jq .body
   ```

4. When the command prints no PR, read `.scratch/feedback-<branch>.md`.

The result is the ledger text, or the knowledge that none exists.

## Write the ledger

Create the comment when the query printed nothing:

```bash
gh pr comment <pr> --body-file ledger.md
```

Replace the comment in place when the query printed an id:

```bash
gh api --method PATCH "repos/{owner}/{repo}/issues/comments/<id>" -F body=@ledger.md
```

## Gate

The **gate** is `./scripts/verify.sh` at the repo root. Its last line is `verify: green` or
`verify: red`. On a PR, the CI checks that `gh pr checks <pr>` reports run the same gate.

## Format

```markdown
<!-- feedback-ledger -->
# Feedback: PR #165

Base: `main` at `a8393de`. Spec: issue #151. Standards: `docs/standards/coding-standards.md`.

## Summary

Reviewed at `36ca10e`. Open: SP1, ST2. Addressed: ST1. Declined: ST7. Blocked: none.

## Verdict

Pending.

## Findings

### SP1. `OVERLAY_WIDTH` cannot fit the widest score

- State: open
- Kind: required
- Where: `tetris-tui/src/layout.rs:33-34` at `36ca10e`
- Rule: "The overlay shows the final Score, the final Level, and the cleared line count."
- Problem: The interior is 16 columns. `Score: 4294967295` is 17 columns, and `score` is `u32`.
- Fix: Raise `OVERLAY_WIDTH` to 19. Add a widget test that renders a `u32::MAX` score.
- Done when: The test renders the full number, and both ending snapshots are regenerated.

### ST1. False fact in the `OVERLAY_WIDTH` comment

- State: addressed in `b1c2d3e`
- Kind: required
- Where: `tetris-tui/src/layout.rs:33-34` at `36ca10e`
- Rule: `coding-standards.md` rule 5, a comment states a fact.
- Problem: The comment claims the widest score fits. It does not.
- Fix: Fixing SP1 makes the comment true.
- Done when: The comment names the true bound.

### ST7. `overlay(matrix)` is a second layout entry point

- State: declined: the diff is small, and `Areas` gains a field when a second overlay appears.
- Kind: optional
- Where: `tetris-tui/src/layout.rs:87` at `36ca10e`
- Rule: Baseline smell, divergence from the `layout::areas()` pattern.
- Problem: Every other rect comes out of `Areas`.
- Fix: Add an `overlay` field to `Areas`.
- Done when: `draw.rs` reads `areas.overlay`.
```

## Rules

- **Id**: `SP<n>` for a Spec finding, `ST<n>` for a Standards finding. Numbers count up from 1,
  and each number names one finding for the life of the ledger. A new review continues from the
  highest existing number on that axis. A finding that matches an existing one (same file, same
  problem) keeps the existing id.
- **State**: one of `open`, `addressed in <sha>`, `declined: <reason>`, `blocked: <reason>`.
  A reopened finding returns to `open` with a `Reopened:` line under it that says why.
- **Kind**: `required` for a breach of a documented standard or of the spec. `optional` for a
  baseline smell, a design note, or a style nit. Only an optional finding can be declined.
- **Where**: a `path:line` range pinned to the commit the review read. The pin keeps that commit
  after the lines move.
- **Rule**: the quoted standard or spec line the finding rests on.
- **Done when**: a condition a reader can check from the diff. `verify-feedback` tests it, so
  write an observable fact. "The test renders the full number" is checkable. "Improve the test"
  gives the verifier nothing to check.
- **Summary**: the commit the last review read, then the ids in each state, with `none` for a
  state that has no id. `to-feedback` writes the commit. Every skill that changes a state
  updates the ids.
- **Verdict**: `Pending.`, `Ready to merge at <sha>.`, or `Not ready: <reasons>.`, where a
  reason is `<ids> open` or `gate red at <sha>`. `verify-feedback` writes the verdict. Any other
  skill that changes a finding sets the verdict to `Pending.`.
