# Release gate

This document states what the UI test suite covers and what it does not. The suite is still growing;
spec 1 ships only the harness and the setup self-check. Later specs add the six product checks that
retire `docs/manual-check.md`.

## Honest gaps

**The suite tests one VSCode version.**
The UI suite drives the VSCode version that `extester.config.json` pins, and it runs on Linux
only. `package.json` states `"engines": { "vscode": "^1.66.0" }`. The suite does not test that
minimum version, because ExTester cannot drive it. No other check tests it either — the repository
has no `@types/vscode` dependency, so no tool checks the declared minimum. A release can ship a
defect that occurs only on a VSCode version older than the pin. The manual check that this suite
replaced had the same gap.

**The pin becomes stale, and nothing yet refreshes it.**
ExTester supports the three most recent VSCode minor releases, so the pin is useful for about
three months. Today nothing updates it: a person must edit `extester.config.json` by hand. If the
pin falls outside the ExTester support window, ExTester can fail to download the matching
ChromeDriver, and the gate then fails on an unrelated pull request. There is no scheduled
pin-update workflow, and no release prerequisite covering one.

**Linux only** — a defect that appears only in `dotnet` discovery or companion process handling on
macOS or Windows ships uncaught. That is a known and accepted cost.

## What the suite covers today

- Spec 1 (harness): packaged `.vsix` in a pinned headless VSCode, test HTTP server with sidecar,
  proven-live setup, budgets, and the setup self-check.

Product checks land in later specs.
