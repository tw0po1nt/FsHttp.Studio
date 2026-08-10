# Release gate

This document states what the UI test suite covers and what it does not. The suite is the
release gate. A green Actions run of the suite is the record of what was verified.

## Prerequisites

- Before you publish a release, merge or close each open pin-update pull request.

## Honest gaps

**The suite tests one VSCode version.**
The UI suite drives the VSCode version that `extester.config.json` pins, and it runs on
Linux only. `package.json` states `"engines": { "vscode": "^1.66.0" }`. The suite does not
test that minimum version, because ExTester cannot drive it. No other check tests it either — the
repository has no `@types/vscode` dependency, so no tool checks the declared minimum. A release can
ship a defect that occurs only on a VSCode version older than the pin. The by-hand walk that this
suite replaced had the same gap.

**The pin becomes stale.**
ExTester supports the three most recent VSCode minor releases, so the pin is useful for about
three months. A weekly workflow opens a pull request that updates the pin. The CI run on that pull
request is the gate run for the new version. If a pin is outside the ExTester support window,
ExTester can fail to download the matching ChromeDriver, and the gate then fails.

**The pin is held below 1.123.0, and the hold has a clock on it.**
VSCode 1.123.0 and later load every file of a folder workspace twice, which the suite reads as a
document that holds each block two times. `tests/ui.Tests/vscode-pin-hold.json` records the hold,
and the weekly workflow opens no pull request while it is in force. The `//pin` note in
`tests/ui.Tests/extester.config.json` carries the measurement and the version bisect.

The hold and the support window pull against each other. Each week the pin stays at 1.122.0, it
falls one release further behind, and the paragraph above states what happens at about three
months: ExTester can fail to fetch a matching ChromeDriver, and the whole suite goes red for a
reason that has nothing to do with the product. A hold is therefore a delay and not a resolution.
Run the workflow by hand with the probe input to test the latest release against the suite. Delete
the hold file when a release passes.

The workflow dispatches the gate run for a pin update. GitHub raises no `pull_request` event for
anything the built-in `GITHUB_TOKEN` does. The pin-update pull request therefore shows no status
checks of its own. The workflow starts the UI tests job against the branch and links the run from a
comment. That comment link, not the status-check list, holds the gate result for a pin update.

**CI retries the suite three times, and a green third attempt reports green.**
The CI job runs the suite up to three times and passes if any attempt passes. A check that fails
two runs in three therefore reports a green job. Each budget is asserted after the check precisely
to catch that kind of drift. The retry is a workflow-level construct that cannot see why the suite
failed. It cannot tell a stuck runner from a check that is genuinely going bad. This is a known and
accepted gap. Without the retry, the environment dependencies ExTester carries would make unrelated
pull requests fail. A re-run attempt count above 1 is a signal of possible drift, not an expected
condition.

**Linux only.**
A defect that appears only in `dotnet` discovery or in companion-process handling on macOS or
Windows ships uncaught. This is a known and accepted cost, not an oversight.

**The suite does not exercise the Beta channel.**
The suite does not cut a Beta, open its pre-release, or download the `.vsix`. A broken Beta
workflow can therefore ship while packaging and install stay green.

**A machine with no .NET is not covered.**
A machine with no .NET, or a misconfigured runtime path, is not covered. The by-hand walk that this
suite replaced never covered it either.

**A script first opened after the companion stops shows no lens.**
The stopped lens stands on the ranges of the last locate. A script that no locate ever covered has
none. The suite drives the covered case only. It opens the fixture while the companion is ready,
and then kills the companion. A regression in the uncovered case therefore ships uncaught. See
[ADR-0003](./adr/0003-block-location-in-companion.md).

**A new untestable surface belongs in this section.**
A spec that finds a surface no suite drives records that surface here. Prefer to automate the
surface. Do not leave the instruction only inside a shipped spec.

## What the suite covers today

- Spec 1 (harness): packaged `.vsix` in a pinned headless VSCode, test HTTP server with sidecar,
  proven-live setup, budgets, and the setup self-check.
- Spec 2 (the core path): open the fixture, Run one block, then replace that response with the next.
- Spec 3 (Run outcomes render honestly): a real 404 renders as a response with no failure. A
  dead-port Run renders as plain runtime-error text with no status line.
- Spec 4 (loop lens refuses with toast): a block inside a loop shows a refusal lens. A click raises
  a warning toast and opens no response viewer.
- Spec 5 (cross-block Refused Run): a block that uses a value another block binds reports a Refused
  Run, and marks no fault in the script.
- Spec 6 (Compile Error names its source): a type error above the block reports at its source
  location in the viewer.
- Spec 7 (companion death is visible and recoverable): a kill during a Run leaves `Running…` for the
  stopped message, and leaves every `▶ Run request` lens for `⊘ Cannot run: the companion stopped`.
  A window reload recovers a successful Run.
