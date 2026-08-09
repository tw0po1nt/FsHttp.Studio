# UI test suite, spec 6 of 7: Compile Error names its source

Spec 6 of 7 for the UI test suite that retires `docs/manual-check.md`. This one adds the check named
**Compile Error names its source**.

Decisions come from a wayfinder map held locally (`.local/wayfinder/ui-tests/`, gitignored). The map
is not a GitHub issue, so this spec restates every decision it depends on rather than linking to one.

**Blocked by** #146 (the harness and its setup) and spec 2 (the core path). **Not blocked by #144** —
this check's fixture never leaves the machine and reads no sidecar.

## Problem Statement

`docs/manual-check.md`'s *Run outcomes* section has a person do something no other step asks for:
**edit the script mid-walk**.

> 3. Add a type error to the script, above the block.
> 4. Run the block, and confirm that the viewer reports the error at its source location.
> 5. Remove the type error.

That is the manual proof of `docs/spec/0001-report-setup-compile-error.md`. When the Setup above a
block does not compile, the Run must report **the Setup's own fault, at its own source location**.
Not a misleading complaint about `http` at the block. The point of that spec was that the message
was not absent. It was *wrong*, and a wrong location sends the user to the wrong line.

What no existing suite observes:

- That a compile error reaches the response viewer at all, over the wire a response takes.
- That the rendered text carries a **source position** the user can act on.
- That the position matches **the line the user actually broke**.
- That the Run reads the **unsaved editor buffer**, not the file on disk — because a user who types a
  type error and clicks Run has not saved.

`companion.Tests` pins the compile-error outcome and its diagnostics. `host.Tests` pins the formatting
of a diagnostic list into viewer text. Neither can type into an editor, and neither observes a webview.
The gap is the last few inches. Buffer to companion, diagnostic to text, text to rendered pixel —
with a line number that has to survive all of it.

## Solution

One check in the UI suite, named **Compile Error names its source**, over one checked-in fixture.

The check edits the open buffer to introduce a type error at a **known line** above the block. It
runs the block. It asserts in the webview DOM that the viewer reports a compile error carrying
**that line's** position. It then restores the buffer, and asserts the buffer is clean.

The fixture on disk is never modified. No second file is written.

After this spec lands, steps 3–5 of *Run outcomes* are automated.

## User Stories

1. As a maintainer, I want a compile error asserted in the webview DOM, so that the compile-error path
   is proven end to end and not from a viewer-update object.
2. As a maintainer, I want the check to break a line it chose, so that the reported position can be
   asserted against a known answer rather than against whatever was reported.
3. As a maintainer, I want the reported line number asserted exactly, so that an off-by-one in the
   coordinate conversion between the companion and the viewer fails the build.
4. As a maintainer, I want the reported column asserted, so that the position matches the editor's own
   Ln/Col readout the user is looking at.
5. As a maintainer, I want the render asserted to name a compile error, so that a compile error dressed
   as a runtime error is caught.
6. As a maintainer, I want the render asserted to carry no status line, so that a compile error is never
   dressed as a response.
7. As a maintainer, I want the error's own message text present, so that the user is told what is wrong
   and not only where.
8. As a maintainer, I want the Run to be driven from the **unsaved buffer**, so that the path a real
   user takes — type, then click, without saving — is the path under test.
9. As a maintainer, I want the fixture on disk left untouched, so that a run of the suite never dirties
   the working tree or the repository.
10. As a check author, I want the buffer restored before the check ends, so that the checks after it do
    not inherit a broken script.
11. As a check author, I want the restoration asserted rather than assumed, so that a failed restore
    fails *this* check instead of the next one.
12. As a check author, I want a positive tell before I assert the buffer is clean, so that I am not
    asserting against an edit that has not applied yet.
13. As a check author, I want the restore to run even when the check body fails partway, so that a red
    assertion leaves no broken buffer behind.
14. As a check author, I want a fixture that needs no network and no sidecar, so that this check is not
    blocked on #144 and cannot fail for a server's reasons.
15. As a check author, I want the viewer wait to use the harness's viewer-update deadline, so that this
    check invents no timeout.
16. As a check author, I want no fixed `sleep`, so that the check costs what the product costs.
17. As a check author, I want every lens click to find-and-click inside one retry, so that a stale
    handle does not redden a green run.
18. As a maintainer, I want the check to fit the harness's flat per-check budget, so that drift shows as
    a budget failure.
19. As a contributor, I want a failure to name the `.fs` line and leave a screenshot, so that I can see
    what the viewer reported instead.
20. As a maintainer, I want a mismatch between the broken line and the reported line treated as a
    finding, so that a loosened assertion cannot absorb a real defect.

## Implementation Decisions

### The fixture

One new checked-in fixture under the suite's `fixtures/` directory, owned by this check alone. It holds
a single reachable block and, above it, a stable region the check can break.

**The fixture reads no sidecar and needs no live server.** A Setup that does not compile never sends a
request. Its URL is an inert literal on loopback. A regression that *does* evaluate the block then fails
fast, rather than succeeding against a real route.

This is what makes the check independent of #144.

The fixture's shape must make the broken line's number **stable and obvious to a reader**. A check
that asserts "line N" against a fixture whose N is an accident of formatting is a check nobody can
maintain. A comment in the fixture marks the line the check breaks, and names its purpose.

### The buffer is mutated, and the file is not

The check edits the **open editor buffer** and never saves. That is not merely convenient. It is
the behavior under test. A user who introduces a type error and clicks Run has not saved. The
extension sends the document's current text, not the file's contents. Driving the check from a
saved file would skip the path a user takes.

Consequences, all deliberate:

- **No second fixture file is written**, and the fixture on disk is never rewritten. A suite run must
  leave the working tree clean.
- The check's edit is applied through the editor, so the document becomes dirty. It is reverted before
  the check ends.
- The check must **not** trigger a save at any point, including through any command that saves as a side
  effect.

### Restoration is part of the check, not cleanup

The buffer is restored through the workbench's own revert-file command — not by a keystroke-driven undo,
whose behavior depends on how many edits were coalesced.

Two requirements:

- **The restore is asserted.** After it reverts, the check asserts the document is no longer dirty
  and the broken text is gone. A failed restore must fail *this* check, with this check's name on it.
  Otherwise it surfaces as a baffling failure in the next one.
- **The restore runs even when the body fails.** It belongs in the check's own teardown. A red
  assertion mid-check then cannot leave a broken buffer for the rest of the session. This is the one
  check in the suite that mutates shared state, and the one check that needs a teardown of its own.

### What the check does

In order, every wait through `eventually`:

1. Open the fixture. Assert the block renders a `▶ Run request` lens (lens-appearance deadline).
2. Edit the buffer: introduce a type error at the fixture's marked line, above the block. Assert the
   edit applied — the document is dirty and the text is present. **This is the tell that licenses
   everything after it.**
3. Click the block's lens, find-and-click inside one retry.
4. Assert in the webview DOM that the viewer reports a compile error (viewer-update deadline), and that
   the render carries **no status line and no headers section**.
5. Assert the rendered text carries the **position of the broken line** — the line number the check
   itself chose, with its column — and the compiler's own message.
6. Revert the buffer. Assert the document is clean and the broken text is gone.

### The line-number assertion is exact, and a mismatch is a finding

Step 5 asserts the **exact line** the check broke. Not "some position", not "a position within a range".

The coordinate path is where this could go wrong, so the assertion is deliberately sharp. The
companion reports positions in the compiler's own numbering: 1-based lines, 0-based columns. The
host shifts the column to 1-based when it formats the viewer's text. That makes the printed
position match VSCode's own Ln/Col readout, which is where the user looks. So there are three
representations and two conversions between the compiler and the pixel. An off-by-one in any of
them sends the user to the wrong line, while every unit test stays green.

**If the reported line does not match the broken line, raise it as a product issue.** Do not loosen
the assertion. The check exists to prove the user is sent to the right line.

### Which error to introduce

A **type error**, as the manual check says, and one whose compiler message is stable across the F#
versions the repository pins. Prefer a mismatch that produces a short, deterministic diagnostic over a
construct whose message includes inferred type names or suggestions, which vary between compiler
versions.

The check asserts the message's presence and the position's exactness. It does not assert the
compiler's full sentence word for word. Those words belong to the F# compiler, not to this product.
Pinning them would make an F# upgrade look like a product regression.

### Session state

The check inherits a session with the response viewer open. It leaves the viewer showing the compile
error, and the fixture buffer clean.

### Waits and budgets

No new deadline and no new budget. The lens assertion uses the harness's lens-appearance deadline.
The viewer assertion uses the viewer-update deadline. The harness measures the check against its
flat per-check budget, and prints the result to the timing table.

## Testing Decisions

**What makes a good test here.** The fidelity floor, unchanged. Response viewer content in the
webview DOM, not a viewer-update object. A real click on a real lens. This check adds one channel:
**a real edit in a real editor**. Steps 3 and 5 of the manual walk are edits, and there is no
honest way to automate them without making one.

**Seam.** No new seam. The packaged `.vsix` driven through ExTester. **No test-only seam is added to
the shipping extension.** The edit goes through the editor, not through a command the product ships
for the test's benefit.

**Modules under test.** Four things as one, with the coordinate conversion running through the
middle: the document text the host sends on a Run, the companion's compile-error outcome and its
diagnostics, the host's formatting of a diagnostic list into viewer text, and the webview's error
render.

**Prior art in this repository:**

- `docs/spec/0001-report-setup-compile-error.md` — the spec this check verifies in a workbench. Its
  Implementation Decision 3 explains why the wording names the Setup.
- `tests/companion.Tests/` — pins the compile-error outcome and diagnostics.
- `tests/host.Tests/` — pins the formatting of diagnostics into viewer text, including the column shift.
- Spec 2's core path check — the viewer-reading vocabulary this check reuses.

**Negative verification.** Run the check once asserting a line number one off from the broken line.
Confirm CI goes red with a named `.fs` line. This check's whole value is an exact number, so
proving that the number is really compared is not optional.

**One more thing worth verifying by hand once.** After a full suite run, confirm `git status` is clean.
This check is the only one that can dirty the working tree, and the failure mode is silent.

## Out of Scope

- **Enumerating compile-error shapes.** One type error above the block. Errors in the block itself,
  multiple diagnostics, and warnings are `companion.Tests`'s and `host.Tests`'s business.
- **Asserting the compiler's exact sentence.** The message's presence, yes. Its wording, no. Those
  words belong to the F# compiler.
- **Editor diagnostics.** FsHttp.Studio deliberately contributes none. Spec 5 asserts that position.
- **Navigating to the reported location.** The viewer reports a position as text. Making that position
  clickable is a product question, not a step of the manual walk.
- **Saving the fixture, or writing a second fixture file.** Explicitly rejected.
- **Changing the harness.** No new deadline, no new route, no CI change.
- **Changing `release.yml` or deleting `docs/manual-check.md`.** Those land with spec 7.
- **macOS and Windows.** Linux only, by decision.

## Further Notes

**Why this lands sixth.** It is the only check that mutates shared session state, so it goes as
late as possible. The riskiest check, companion death, still goes last. Its teardown is what keeps
that ordering safe.

**What the manual check said, for the record.** This check replaces *Run outcomes* steps 3–5. Steps 1–2
and 6–7 belong to spec 3.

**The property that is easy to lose sight of.** Three of this check's assertions are about *not
being something else*. Not a runtime error, not a response, not the wrong line. The compile-error
path's historical defect was never an absent message. It was a **wrong** one. A check that only
asserts "an error appeared" would have passed against the bug that `docs/spec/0001` was written to
fix.
