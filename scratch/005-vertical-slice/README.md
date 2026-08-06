# PROTOTYPE — 005 vertical slice (throwaway)

Answers: does ExTester + packaged `.vsix` + local .NET test server + Fable.Mocha
reach the response viewer DOM on a real VSCode, including on `ubuntu-latest`?

Not production. Lives under `.local/` on purpose.

## One command

From this directory:

```bash
./run.sh
```

Requires: Node 24+, .NET SDK 10+, repo-root `npm ci` already done once.

## What it does

1. Builds a small .NET test server and starts it (JSON `GET /json`).
2. Writes `fixtures/sidecar.json` with the base URL.
3. Packages the extension at the repo root (`npm run package`) unless `VSIX` is set.
4. Fable-compiles and esbuild-bundles the F# UI test.
5. Downloads VSCode + ChromeDriver, installs the `.vsix`, opens `fixtures/`, runs the suite.
