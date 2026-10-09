# Release gate

This document states what the suites of the Release gate cover and what they do not. The Release
gate is the UI suite, the Neovim suite, and the Lua core suite. A green Actions run of
`release.yml` is the record of what was verified.

## How a release runs the gate

`release.yml` has three stages. The build job runs the guardrails of `ci.yml`. Then it packages the
`.vsix` and the Companion archive, writes a `.sha256` file for each, and uploads the four files as
workflow artifacts. The UI suite job downloads the `.vsix` and drives it. The four Neovim legs
download the Companion archive. Each leg runs the Lua core suite and the Neovim suite against that
archive. The publish job needs each gate to be green, and attaches the same four files to the draft
Release.

The `force` input skips each suite, and the run log shows a warning for the skip. The guardrails
always run.

## Prerequisites

- Before you publish a release, merge or close each open pin-update pull request.
- Before you publish the draft Release, replace the **Clients changed** line of its notes. Name the
  Client that changed: the VSCode extension, the Neovim client, or both.

## Honest gaps

**The suite tests one VSCode version.**
The UI suite drives the VSCode version that `extester.config.json` pins, and it runs on
Linux only. `package.json` states `"engines": { "vscode": "^1.66.0" }`. The suite does not
test that minimum version, because ExTester cannot drive it. No other check tests it either. The
repository has no `@types/vscode` dependency, so no tool checks the declared minimum. A release can
ship a defect that occurs only on a VSCode version older than the pin. The by-hand walk that this
suite replaced had the same gap.

**The pin becomes stale.**
ExTester supports the three most recent VSCode minor releases, so the pin is useful for about
three months. A weekly workflow opens a pull request that updates the pin. The CI run on that pull
request is the gate run for the new version. If a pin is outside the ExTester support window,
ExTester can fail to download the matching ChromeDriver, and the gate then fails.

**The pin is frozen below 1.123.0, and the freeze has a clock on it.**
VSCode 1.123.0 and later load every file of a folder workspace twice, which the suite reads as a
document that contains each block two times. `tests/ui.Tests/vscode-pin-freeze.json` records the freeze,
and the weekly workflow opens no pull request while it is in force. The `//pin` note in
`tests/ui.Tests/extester.config.json` carries the measurement and the version bisect.

The freeze and the support window pull against each other. Each week the pin stays at 1.122.0, it
falls one release further behind, and the paragraph above states what happens at about three
months: ExTester can fail to fetch a matching ChromeDriver, and the whole suite goes red for a
reason that has nothing to do with the product. A freeze therefore only delays the problem.
Run the workflow by hand with the probe input to test the latest release against the suite. Delete
the freeze file when a release passes.

The workflow dispatches the gate run for a pin update. GitHub raises no `pull_request` event for
anything the built-in `GITHUB_TOKEN` does. The pin-update pull request therefore shows no status
checks of its own. The workflow starts the UI tests job against the branch and links the run from a
comment. That comment link gives the gate result for a pin update.

**CI retries the suite three times, and a green third attempt reports green.**
The CI job runs the suite up to three times and passes if any attempt passes. A check that fails
two runs in three therefore reports a green job. Each budget is asserted after the check precisely
to catch that kind of drift. The retry is a workflow-level construct that cannot see why the suite
failed. It cannot tell a stuck runner from a check that is genuinely going bad. This is a known and
accepted gap. Without the retry, the environment dependencies ExTester carries would make unrelated
pull requests fail. A re-run attempt count above 1 is a signal of possible drift rather than an expected condition.

**Linux only.**
A defect that appears only in `dotnet` discovery or in companion-process handling on macOS or
Windows ships uncaught. This is a known and accepted cost.

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
[ADR-0003](../adr/0003-block-location-in-companion.md).

**A new untestable surface belongs in this section.**
A spec that finds a surface no suite drives records that surface here. Prefer to automate the
surface. Do not leave the instruction only inside a shipped spec.

## What the suite covers today

- Spec 1 (harness): packaged `.vsix` in a pinned headless VSCode, test HTTP server with sidecar,
  proven-live Harness setup, budgets, and the Harness self-check.
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

## The Neovim suite

The Neovim suite drives a child Neovim against a real companion, the test HTTP server, and the
Sidecar of the UI suite. lazy.minit loads the client in the child as a lazy.nvim spec.
`nvim-tests.yml` runs the Neovim suite and the Lua core suite on four legs: Linux with Neovim 0.11,
and Linux, macOS, and Windows with the pinned stable Neovim. The composite action
`.github/actions/run-nvim-suite` contains the suite steps. The suite has no retry, so one red Check
gives a red leg. `update-neovim-pin.yml` opens a pull request when a newer Neovim ships, and it
dispatches `nvim-tests.yml` against the pin branch.

### Honest gaps of the Neovim suite

**The suite tests two Neovim versions.** The legs use Neovim 0.11 (`floor` in
`tests/nvim/neovim-pin.json`) and the pinned stable version (`stable`). A defect that occurs only on
another version ships uncaught. Neovim 0.11 runs on Linux only.

**The suite gets the companion from `companion_path`.** A release run unpacks the Companion
archive that it ships, and each Check that sets `companion_path` uses that folder. A run in
`nvim-tests.yml` publishes the companion from the checkout.

**No Check downloads from a real GitHub Release URL.** The download Checks get the Companion archive
from the test HTTP server. In a release run, that archive is the file that the release attaches. A
defect in the release URL, or in the redirect of GitHub, ships uncaught.

**No Check compares the pixels of an image.** The image Checks run with no snacks.nvim, or with a
stub in place of snacks.nvim. The stub records each image placement and the bytes of the image
file. A defect that occurs only in the drawn image ships uncaught.

**The suite Budgets come from few CI runs.** The values are 60 s for Harness setup and 30 s for
each Check on each system. The suite Budget is the slowest measured suite of the system plus about
40%. The slowest Linux suite took 181.6 s, so the Linux Budget is 255 s. The slowest macOS suite
took 189.7 s, so the macOS Budget is 265 s. These values come from 12 runs of `nvim-tests.yml` on
2026-10-09. The Windows suite took 223.1 s on the first passing run, so the Windows Budget is 315 s.
One run is a small sample, so a slow Windows runner can exceed that Budget. `harness.lua` has one
row of Budgets for each operating system. Each leg writes its timing table to the job summary. The suite Budget starts at the first Check, so it leaves out Harness setup, as in the UI
suite.

**The suites test lazy.nvim only.** The `vim.pack` route gets no Check.

**A Beta runs the Lua core suite only.** A Beta does not run the Neovim suite.

**A user with no lualine sees no companion state until the user runs a command.** `:FsHttp status`
shows the state. No Check drives a statusline other than lualine.

### What the Neovim suite covers today

- The Harness: a child Neovim that loads the client through lazy.nvim, the test HTTP server with
  its Sidecar, Harness setup to Proven-live, Budgets, and the Proven-live self-check.
- The Harness watchdog: a hung child Neovim stops, a new child Neovim answers, and the next Check
  finds no frozen companion.
- The start sequence: a second Script starts no second companion. `VimLeavePre` stops the
  companion. A `dotnet_path` that names a missing file gives the WARN notice, and no companion
  starts.
- The version check: a companion of the client version gives no version WARN notice. A companion
  of a different version gives one WARN notice that names both versions, the companion stays up,
  and a Run succeeds.
- `:FsHttp run`: the command completes `run`, and `<Plug>(FsHttpRun)` has no key. A Run fills the
  Response buffer in a split on the right, and the next Run replaces it in the same window. After
  the user closes that window, the next Run opens it again. The Request and Response headers folds
  start closed, and the winbar cuts the start of the URL in a narrow split. A 404 shows as a
  response, and a Dead port shows as a Runtime error. A Run of the loop Block gives the WARN notice
  and opens no Response buffer. A cross-block Refused Run shows the refused text, and the Script
  gets no diagnostic. The Request fold shows what a POST sent. `request_timeout_ms` bounds a Run.
  The two no-Block notices show, and a stopped companion gives the stopped WARN notice.
- The Status line text: `status()` gives the rows of the UI suite Status line text Checks for clean
  scripts, an `.fs` module, scripts with a Parse failure, a buffer that is not F#, a buffer switch,
  and a second open script. A companion death during a Run shows the stopped text in the Response
  buffer. Then `status()` gives nil while the Response buffer has focus, and `companion stopped`
  on the script. No notice shows. Each change fires the `User` autocmd, and so does a switch to a
  buffer that is not F#. `:FsHttp status` echoes the row, or the companion state row in a buffer
  that is not F#. The lualine entry shows the row, and `status_line.lualine = false` removes it. A
  `dotnet_path` that names a missing file gives the `.NET SDK not found` row. In that state,
  `:FsHttp run` shows the WARN notice of the SDK again.
- `:help fshttp`: `:helptags` finds no duplicate tag, and `:help fshttp` opens `doc/fshttp.txt`.
  Each `:FsHttp` subcommand, each option key, and each `<Plug>(FsHttp…)` map has a help tag.
