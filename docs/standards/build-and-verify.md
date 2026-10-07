# Build and verify

This file lists the commands that prove a change is sound. CI runs the same commands, so a local pass predicts a green run.

## Bootstrap

Fable and Fantomas are local tools, so `dotnet tool restore` must run first.

```sh
dotnet tool restore
npm ci
```

## The compiler is the check that matters

```sh
dotnet build FsHttp.Studio.slnx
```

Run this after every change to a `.fs` file. Fable accepts code that the F# compiler rejects, so
a clean Fable build is not evidence that the solution compiles. CI runs this command, and the
build fails on errors that a Fable-only loop never shows.

## The rest of the gate

| Command | What it proves |
| --- | --- |
| `dotnet test FsHttp.Studio.slnx --no-build` | The unit suites pass. |
| `dotnet fantomas --check .` | The formatting matches. Tooling owns layout. See `docs/standards/coding-standards.md`. |
| `npm run compile` | The companion publishes, Fable emits, and esbuild bundles. |
| `npm run smoke` | The bundled renderer runs under node. |
| `npm run package` | The `.vsix` builds. |
| `./tests/ui.Tests/run.sh` | The UI suite, which is the release gate. See `docs/standards/release-gate.md`. |

`./scripts/verify.sh` runs the CI steps in the order that `.github/workflows/ci.yml` runs them:
the banned-patterns check, then each command above from `dotnet fantomas` through `npm run smoke`.
Its last line is `verify: green` or `verify: red`, and the feedback skills use it as the gate.
The script leaves out `npm run package` and the UI suite, because `ci.yml` leaves them out.
`ui-tests.yml` runs the UI suite separately. When you change a step in `ci.yml`, make the same
change in the script.

`package.json` holds the individual `build:*` scripts that `compile` composes. Use one of those
scripts only when you rebuild a single side.
