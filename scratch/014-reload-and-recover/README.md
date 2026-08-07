# PROTOTYPE — 014 companion death, reload, recover (throwaway)

Answers: does the **companion stops** section of `docs/manual-check.md` survive as an
automated check — in particular, does the ExTester/Selenium session survive a window reload,
and can a Run succeed afterwards?

Not production. Lives under `.local/` on purpose. Reuses 005's plumbing.

## One command

From this directory:

```bash
./run.sh
```

Requires: Node 24+, .NET SDK 10+, repo-root `npm ci` already done once.
Set `NEGATIVE=1` to flip the final assertion to a marker that cannot match — the negative
control 005 omitted.

## What it does

1. Builds and starts the test server: `/json` (fast), `/slow` (hangs), `/release` (control).
2. Bakes a two-block fixture with the server's base URL.
3. Packages the extension, Fable-compiles and bundles the F# suite.
4. Runs one ExTester suite: click the slow lens, SIGKILL the companion mid-Run, assert the
   stopped message, observe the lens, reload the window, click the fast lens, assert 200.
5. Prints a wall-clock table, step by step.
