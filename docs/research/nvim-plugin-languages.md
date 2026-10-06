# Research: implementation routes for a Neovim client

Issue: [#241](https://github.com/tw0po1nt/FsHttp.Studio/issues/241), "Which routes can a Neovim
plugin be written in, F# included?" Map: [#239](https://github.com/tw0po1nt/FsHttp.Studio/issues/239).

> **Scope.** This note gathers facts for the language decision in
> [#245](https://github.com/tw0po1nt/FsHttp.Studio/issues/245). The decision belongs to that
> ticket. All facts are current on 2026-10-06. The current Neovim stable release is v0.12.5
> (2026-08-23), and `master` is the 0.13 development cycle [N1].

## The job that each route must do

The Neovim client replaces the extension host. ADR-0002 keeps the companion and the envelope
unchanged. Each route must do these four things:

1. Start the companion as `dotnet <companion.dll>`, with three stdio pipes (`src/host/Companion.fs`).
2. Write each envelope as a frame: a 4-byte big-endian length, then the UTF-8 JSON payload
   (`src/host/Envelope.fs`, `src/companion/Envelope.fs`).
3. Read frames from a stream of stdout chunks, and buffer a partial frame across chunks.
4. Match each response to the oldest outstanding request, because the companion answers one frame
   at a time, in request order (`src/host/Companion.fs`).

The `ok` envelope carries the response body as base64 (`bodyBase64`), so the client also needs a
base64 decoder for a body that it shows as text.

### What `src/host` holds

`src/host` holds 1608 lines in 12 files. The table sorts them by what a non-VSCode client can use.

| File | Lines | Depends on | Use outside VSCode |
| --- | --- | --- | --- |
| `Envelope.fs` | 42 | `System.Text` only | Pure F#. Compiles for .NET and for Fable. |
| `Protocol.fs` | 232 | nothing | Pure F#. `tests/host.Tests` runs it on .NET. Some functions carry VSCode names: `toVscodeLine`, and `scriptFileNameFor`, which reads a VSCode URI scheme. |
| `Refusals.fs` | 115 | nothing | Pure F#. `tests/host.Tests` runs it on .NET. The `companionStopped` text says "Reload the window", which is a VSCode action. |
| `Js.fs` | 16 | Fable JS | Fable JS only. |
| `Node.fs` | 48 | Fable JS, Node.js | Bindings for `child_process`, `fs`, and `path`. Node.js only. |
| `Companion.fs` | 206 | `Js`, `Node`, `Envelope`, `Protocol`, `Refusals` | The companion client. It runs in any Node.js process. |
| `Vscode.fs`, `CodeLensProvider.fs`, `ResponseViewer.fs`, `StatusBar.fs`, `RunCommand.fs`, `Extension.fs` | 949 | the `vscode` module | VSCode only. |

The first six files hold 659 lines. The VSCode-only files hold the other 949 lines.

## Route 1: plain Lua, with `vim.system` or `vim.uv`

### Facts

- Neovim Lua code targets Lua 5.1. "Lua 5.1 is the permanent interface for Nvim Lua" [N2].
- `vim.system({cmd}, {opts}, {on_exit})` starts a process. With `stdin = true`, the plugin writes
  to the pipe through `SystemObj:write()`. A `stdout` function receives each chunk of output.
  With `text` unset, Neovim does not change line endings, so the bytes of a frame stay intact [N3].
- `vim.system` is new in 0.10 [N4]. The 0.9.5 manual has no `vim.system` [N5].
- `vim.uv` exposes the luv bindings for libuv, which include `uv.spawn()` and pipes [N6]. 0.9.5
  and earlier name the same module `vim.loop` [N5]. `vim.loop` is now deprecated in favor of
  `vim.uv` [N7].
- A `vim.uv` callback runs in a fast event context. A direct `vim.api` call in that callback is an
  error. The plugin must wrap the handler in `vim.schedule_wrap` [N6]. This also applies to the
  `stdout` handler of `vim.system`, which runs on luv.
- Lua 5.1 has no bit operators. Neovim always provides `require("bit")`, also in a build with PUC
  Lua [N2]. Plain arithmetic (`b1 * 16777216 + b2 * 65536 + ...`) also decodes the length prefix.
- `vim.json.encode` and `vim.json.decode` handle JSON. The manual documents `vim.json` from 0.7 [N8].
- `vim.base64.encode` and `vim.base64.decode` are new in 0.10 [N4].

### Neovim version floor

- **0.10** with `vim.system`, `vim.uv`, and `vim.base64`.
- **0.7** with `vim.loop.spawn` and `vim.json`, if the plugin also brings its own base64 decoder.

Distribution packages set a practical floor. Ubuntu 24.04 LTS (noble) ships Neovim 0.9.5, and
Ubuntu 26.04 (resolute) ships 0.11.6 [D1]. Debian 13 (trixie) ships 0.10.4, and Debian 12
(bookworm) ships 0.7.2 [D2]. A floor of 0.10 excludes the noble package.

### User install prerequisites

- Neovim at or above the floor.
- The .NET SDK that the companion needs today (ADR-0002, `src/host/Companion.fs`).
- No other runtime. Lua runs inside Neovim, and the plugin has no build step.

### Reuse of `src/host`

None at the source level. Lua cannot load F# code. The Lua client must reimplement the frame
parser, the request queue, the envelope decode, the `RunResult` parse, and the refusal text.

- The frame parser and the request queue are small. `Envelope.fs` holds 42 lines.
- `Refusals.fs` holds every user-facing sentence for a refused block. A Lua copy is a second
  source for the same text, and the two copies can drift.
- `Protocol.fs` and `Refusals.fs` stay usable as the reference behavior. Their tests in
  `tests/host.Tests` state the rules that a Lua port must match.

## Route 2: F# compiled to Lua by Fable

### Facts

- Fable has no Lua target. The `--lang` switch in `src/Fable.Cli/Entry.fs` on `main` accepts
  JavaScript, TypeScript, Python, PHP, Dart, Rust, and Beam (Erlang) [F1]. The latest release is
  Fable 5.19.0 (2026-10-03) [F2].
- The Fable documentation lists seven targets with a status: JavaScript and TypeScript stable,
  Dart and Python beta, Rust alpha, PHP and Beam experimental. Lua is absent from the list [F3].
- The first Lua work, PR #2509 "[WIP] Lua Language support", closed unmerged on 2022-05-20 [F4].
- PR #3290 "Lua spike (placeholder)" carries that work forward. It is a draft. Its last commit is
  from 2024-06-07, and its last comment is from 2024-08-30 [F5].
- A Fable maintainer wrote in 2023: "As Python is maintained and Lua target is kinda dead, perhaps
  it is worth trying to consume the API using Python instead" [F6].
- In PR #3290, the spike author named Neovim scripting as a use. A maintainer replied that the
  `fable-library` runtime size can concern Neovim users [F7].
- NuGet has no package that adds Lua output to Fable [F8].

### Neovim version floor

No floor applies, because the compiler does not exist in a release. Any output would have to
match the Lua 5.1 interface [N2].

### User install prerequisites

The user needs the same items as Route 1. The F# toolchain and Fable are build-time tools for the
plugin author only.

### Reuse of `src/host`

In principle, `Envelope.fs`, `Protocol.fs`, and `Refusals.fs` (389 lines) could compile to Lua,
because they are pure F#. `Companion.fs`, `Js.fs`, and `Node.fs` bind to Node.js and could not.
This reuse needs a Lua target that the project would have to build or maintain itself.

## Route 3: a remote plugin on the Neovim Node host, in F# through Fable JS

### Facts about remote plugins

- Remote plugins are in the current manual, in 0.12.5 and on `master`. A remote plugin is a
  coprocess that talks to Neovim over msgpack-RPC. A "plugin host" for each language loads the
  plugins [N9].
- After the user installs, updates, or deletes a remote plugin, the user must run
  `:UpdateRemotePlugins`. This command writes the manifest `rplugin.vim` [N9].
- Neovim supports Node.js remote plugins through the `neovim` npm package. The manual tells the
  user to run `npm install -g neovim` [N10]. The 0.13 news adds bun support for Node.js plugins [N11].
- The health check warns for Node.js below 6.0.0. It also warns when the global `neovim` package
  is missing [N12].
- `deprecated.txt` in 0.12.5 and on `master` lists no remote plugin item [N7].
- Neovim issue #27949, "simplify remote plugins, massively", is open on the 0.13 milestone. It
  proposes to drop the plugin host, the manifest, `:UpdateRemotePlugins`, and the global
  `neovim-node-host`. A remote plugin becomes a plain Lua plugin that starts its own API client
  process, which it calls a "remote module" [N13].
- Neovim issue #27119, also on the 0.13 milestone, proposes to remove the `provider` pattern. The
  Node.js host is one such provider [N14].

### Facts about the `neovim` npm package

- The latest npm release is 5.5.0 (2026-09-11) [P1]. The repository has commits from 2026-10-05 [P2].
- `package.json` declares `"node": ">=14"` and the binary `neovim-node-host` [P3]. The README
  says that the project tests Node.js 16 and later [P4].
- The README says that the package "works like any other NPM package" outside a remote plugin.
  Its `attach()` function connects to Neovim over a process, a socket, or a pair of streams [P4].
- The package writes its logs in place of `console.log`, because a write to stdout breaks the
  stdio RPC channel [P4].
- NuGet has no Fable binding for the `neovim` npm package [F9]. An F# plugin must write its own
  bindings, as `src/host/Node.fs` and `src/host/Vscode.fs` do for Node.js and VSCode.

### Two forms of this route

- **Form 3a: a remote plugin.** The Fable JS output sits in `rplugin/node/` and runs in the shared
  `neovim-node-host` [N9][P4].
- **Form 3b: a remote module.** A Lua file in `plugin/` starts `node main.js` with
  `jobstart(..., { rpc = true })`, and the Node.js process calls `attach()` [N15][P4]. This is the
  model that issue #27949 proposes [N13]. It needs no global `neovim` package, no plugin host, and
  no `:UpdateRemotePlugins`.

### Neovim version floor

- Form 3a: the remote plugin and Node.js provider support in the current manual [N9][N10]. The 0.13
  work in #27949 and #27119 can change this support [N13][N14].
- Form 3b: the floor of the Lua shim. RPC jobs (`rpc` in `jobstart`) are part of the channel
  model [N15].

### User install prerequisites

- Form 3a: Node.js, the global `neovim` npm package, a run of `:UpdateRemotePlugins` after each
  install or update, and the .NET SDK.
- Form 3b: Node.js and the .NET SDK. The plugin can ship the `neovim` package inside its own files.
  ADR-0005 already bundles the extension host with esbuild. This note did not test an esbuild
  bundle of the `neovim` package.

Both forms put two runtimes on the user's machine, Node.js and .NET, because the companion stays
a .NET process.

### Reuse of `src/host`

This route reuses the most code. `Envelope.fs`, `Protocol.fs`, `Refusals.fs`, `Js.fs`, `Node.fs`,
and `Companion.fs` (659 lines) compile under Fable JS and run in a Node.js process. `Companion.fs`
already starts the companion through `child_process` and decodes frames from Node.js buffers.

- `Extension.fs` resolves the `dotnet` path and checks the SDK version. That logic sits in a
  VSCode-only file and would need a move to a shared file.
- The VSCode-only files (949 lines) do not apply. The plugin needs new F# bindings for the
  Neovim API.

## Outside the three routes

The companion is a .NET process, and a .NET library for the Neovim RPC API exists on NuGet
(`NvimClient.API`) [F10]. The companion could talk to Neovim directly. ADR-0002 says that a second
editor reuses the companion unchanged and reimplements only the extension host. This option
changes the companion, so it conflicts with ADR-0002. This note records the option without
an investigation.

## Facts the language decision depends on

1. Fable has no Lua target in any release. The only Lua work is a draft PR with no commit since
   June 2024 [F1][F5].
2. Plain Lua needs Neovim 0.10 for `vim.system` and `vim.base64`. Ubuntu 24.04 LTS ships 0.9.5
   [N4][D1].
3. Plain Lua adds no runtime for the user. Each Node.js route adds Node.js beside the .NET SDK.
4. Plain Lua reuses no F# source. The Lua client must copy the refusal text and the
   envelope parse, and keep them in step with `Refusals.fs` and `Protocol.fs`.
5. Fable JS reuses 659 lines of `src/host` as they are. These lines include the companion client.
6. Remote plugins work in 0.12.5. An open 0.13 issue proposes to replace the plugin host and
   `:UpdateRemotePlugins` with a Lua-started client process [N9][N13].
7. Form 3b, a Lua shim that starts a Fable JS process, follows that proposed model today. It needs
   no global npm package [N13][P4].
8. No Fable binding for the Neovim API exists. An F# client must write and maintain one [F9].

## Sources

- [N1] Neovim releases: https://github.com/neovim/neovim/releases
- [N2] `:help lua-compat`, `:help lua-luajit`, `:help lua-bit` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua.txt
- [N3] `:help vim.system()`, `:help vim.SystemObj` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua.txt
- [N4] Neovim 0.10 news, `vim.system()` and `vim.base64`: https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/news-0.10.txt
- [N5] Neovim 0.9.5 Lua manual, `vim.loop` and no `vim.system`: https://github.com/neovim/neovim/blob/v0.9.5/runtime/doc/lua.txt
- [N6] `:help vim.uv`, `:help lua-loop-callbacks` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua.txt
- [N7] `deprecated.txt` (v0.12.5 and master): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/deprecated.txt, https://github.com/neovim/neovim/blob/master/runtime/doc/deprecated.txt
- [N8] Neovim 0.7.0 Lua manual, `vim.json`: https://github.com/neovim/neovim/blob/v0.7.0/runtime/doc/lua.txt
- [N9] `:help remote-plugin`, `:help remote-plugin-manifest` (master): https://github.com/neovim/neovim/blob/master/runtime/doc/remote_plugin.txt
- [N10] `:help provider-nodejs` (master): https://github.com/neovim/neovim/blob/master/runtime/doc/provider.txt
- [N11] Neovim 0.13 development news, "provider: add bun support for Node.js plugins": https://github.com/neovim/neovim/blob/master/runtime/doc/news.txt
- [N12] Node.js provider health check (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/lua/vim/provider/health.lua
- [N13] Neovim issue #27949, "simplify remote plugins, massively": https://github.com/neovim/neovim/issues/27949
- [N14] Neovim issue #27119, "arch: eliminate provider pattern in favor of Lua hooks": https://github.com/neovim/neovim/issues/27119
- [N15] `:help channel-rpc`, `:help job-control` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/channel.txt
- [D1] Ubuntu Neovim packages: https://launchpad.net/ubuntu/+source/neovim
- [D2] Debian Neovim packages: https://sources.debian.org/src/neovim/
- [F1] Fable `--lang` parse in `Entry.fs`: https://github.com/fable-compiler/Fable/blob/main/src/Fable.Cli/Entry.fs
- [F2] Fable releases: https://github.com/fable-compiler/Fable/releases
- [F3] Fable documentation, target list and status: https://fable.io/docs/
- [F4] Fable PR #2509, "[WIP] Lua Language support": https://github.com/fable-compiler/Fable/pull/2509
- [F5] Fable PR #3290, "Lua spike (placeholder)": https://github.com/fable-compiler/Fable/pull/3290
- [F6] Fable maintainer comment on the Lua target: https://github.com/fable-compiler/Fable/pull/2509#issuecomment-1817528182
- [F7] Fable maintainer comment on Neovim and `fable-library`: https://github.com/fable-compiler/Fable/pull/3290#issuecomment-2310772697
- [F8] NuGet search, "fable lua": https://www.nuget.org/packages?q=fable+lua
- [F9] NuGet search, "neovim": https://www.nuget.org/packages?q=neovim
- [F10] `NvimClient.API` on NuGet: https://www.nuget.org/packages/NvimClient.API
- [P1] `neovim` on npm: https://www.npmjs.com/package/neovim
- [P2] `neovim/node-client` commits: https://github.com/neovim/node-client/commits/master
- [P3] `neovim/node-client` `package.json`: https://github.com/neovim/node-client/blob/master/packages/neovim/package.json
- [P4] `neovim/node-client` README: https://github.com/neovim/node-client/blob/master/README.md
