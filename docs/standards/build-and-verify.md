# Build and verify

This file lists the commands that prove a change is sound. CI runs the same commands, so a local pass predicts a green run.

## Bootstrap

Fable and Fantomas are local tools, so `dotnet tool restore` must run first.

```sh
dotnet tool restore
npm ci
```

The Lua guardrails need StyLua and lua-language-server on `PATH`. CI pins StyLua 2.5.2 and
lua-language-server 3.19.1. On macOS, `brew install stylua lua-language-server` installs both.

The Lua core suite needs Neovim on `PATH`. It also needs network access, because lazy.nvim fetches
mini.test and luassert into `.tests/` on each run. On Linux, install the readline headers
(`libreadline-dev` on Debian and Ubuntu). hererocks builds Lua 5.1 for luassert, and that build
stops without them.

The Neovim suite needs the same Neovim and network access, the .NET SDK, `curl`, `pgrep`, and a C
compiler on `PATH` as `cc`. `tests/nvim/run.sh` builds the tree-sitter JSON parser with `cc`. To use
a JSON parser that is already built, set `NVIM_TEST_JSON_PARSER` to the path of its library.
`tests/nvim/neovim-pin.json` holds the Neovim versions that CI installs: `floor` for the Lua type
check, and `stable` for the two Neovim suites.

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
| `dotnet fsi scripts/generate-lua.fsx --check` | The committed files in `lua/fshttp/` match `Refusals.fs` and `package.json`. Without `--check`, the script writes the files again. |
| `stylua --check .` | The Lua layout matches `stylua.toml`. `.styluaignore` skips the files that `generate-lua.fsx` writes. |
| `npm run compile` | The companion publishes, Fable emits, and esbuild bundles. |
| `npm run package` | `npm run compile` runs, then the `.vsix` builds. |
| `./scripts/check-vsix-holds-no-lua.sh` | The `.vsix` holds no Lua file. Run it after `npm run package`. |
| `npm run smoke` | The bundled renderer runs under node. |
| `./scripts/check-lua-types.sh` | lua-language-server finds no problem in the LuaCATS annotations. The Neovim API types come from the `nvim` on `PATH`, or from `$VIMRUNTIME` when you set it. CI uses the Neovim 0.11 types. |
| `nvim -l tests/minit.lua --minitest` | The Lua core suite passes. Each core module loads in an environment with no `vim` global. |
| `./tests/nvim/run.sh` | The Neovim suite passes. The script publishes the test HTTP server and the companion, and then drives a child Neovim against them. `NVIM_TEST_SKIP_BUILD=1` uses the builds that are already in `out/`. |
| `./tests/ui.Tests/run.sh` | The UI suite, which is the release gate. See `docs/standards/release-gate.md`. |

`./scripts/verify.sh` first runs the steps of `.github/workflows/ci.yml` in the same order: the
banned-patterns check, then each command above except `npm run compile`, the Neovim suite, and the
UI suite. Then it runs the Neovim suite, as `nvim-tests.yml` does.
Its last line is `verify: green` or `verify: red`, and the feedback skills use it as the gate.
The script leaves out `npm run compile`, because `npm run package` runs it. The script leaves out
the UI suite, because `ci.yml` leaves it out. `ui-tests.yml` runs the UI suite separately.
The script runs the Neovim suite because that suite needs only Neovim and the .NET SDK.
When you change a step in `ci.yml` or `nvim-tests.yml`, make the same change in the script.

`package.json` holds the individual `build:*` scripts that `compile` composes. Use one of those
scripts only when you rebuild a single side.

## Golden fixtures

The F# tests write each Golden fixture into `tests/golden/`, and the Lua core suite reads the same
files. A test fails when its output is different from the committed Golden fixture. When you change
that output on purpose, run the tests with the update flag to write the Golden fixtures again. Then
examine the diff before you commit it.

```sh
UPDATE_GOLDEN_FIXTURES=1 dotnet test FsHttp.Studio.slnx
```
