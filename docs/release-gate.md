# Release gate

This document states what the UI test suite covers and what it does not. The suite is still
growing. Spec 1 ships only the harness and the setup self-check. Later specs add the six product
checks that retire `docs/manual-check.md`.

## Prerequisites

- Before you publish a release, merge or close each open pin-update pull request.

## Honest gaps

**The suite tests one VSCode version.**
The UI suite drives the VSCode version that `extester.config.json` pins, and it runs on
Linux only. `package.json` states `"engines": { "vscode": "^1.66.0" }`. The suite does not
test that minimum version, because ExTester cannot drive it. No other check tests it either — the
repository has no `@types/vscode` dependency, so no tool checks the declared minimum. A release can
ship a defect that occurs only on a VSCode version older than the pin. The manual check that this
suite replaced had the same gap.

**The pin becomes stale.**
ExTester supports the three most recent VSCode minor releases, so the pin is useful for about
three months. A weekly workflow opens a pull request that updates the pin. The CI run on that pull
request is the gate run for the new version. If a pin is outside the ExTester support window,
ExTester can fail to download the matching ChromeDriver, and the gate then fails.

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

## What the suite covers today

- Spec 1 (harness): packaged `.vsix` in a pinned headless VSCode, test HTTP server with sidecar,
  proven-live setup, budgets, and the setup self-check.

Product checks land in later specs.
