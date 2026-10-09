# The Neovim client lives at the repo root, and both Clients ship under one version

The Neovim client lives in this repo, at the root: `lua/`, `plugin/`, `doc/`, `build.lua`, and
`lazy.lua` sit beside `package.json`. lazy.nvim loads the repo root, so a LazyVim user writes
`{ "tw0po1nt/FsHttp.Studio" }` and adds no runtimepath code. `package.json` sets one version for
the VSCode extension, the Neovim client, and the companion. Each version gets one tag
`v<version>` and one GitHub Release, which carries the `.vsix` and the Companion archive.

A reader can find Lua at the root of a VSCode extension repo strange. The root is the only place
where lazy.nvim loads a plugin and reads `build.lua` with no user code.

## Considered Options

- **A subfolder.** Each lazy.nvim user must add runtimepath code, and lazy.nvim reads `build.lua`
  from the root only.
- **A mirror repo that CI splits out.** The mirror is a second moving part and a second place for
  issues.
- **A separate repo.** The generator of `refusals.lua` and the Golden fixtures must then cross two
  repos.
- **A separate version for the Neovim client.** The client must then name the companion version
  that it needs. lazy.nvim accepts only an optional `v` before the version, so a tag such as
  `nvim-v0.1.0` gets no semver pin.

## Consequences

A fix to one Client ships as a new version of both Clients, and waits on the gates of both
([ADR-0009](0009-ui-suite-gates-the-release.md)). `.vscodeignore` excludes the Lua paths, and a
guardrail checks that the `.vsix` contains no Lua file. Each user's plugin spec names this repo, so a
later move is expensive.

See [docs/spec/0015](../spec/0015-neovim-client.md), decision A2.
