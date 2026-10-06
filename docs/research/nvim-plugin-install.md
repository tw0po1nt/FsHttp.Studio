# Research: how Neovim plugin managers install a plugin that needs a .NET binary

Issue: [#242](https://github.com/tw0po1nt/FsHttp.Studio/issues/242). Map: [#239](https://github.com/tw0po1nt/FsHttp.Studio/issues/239).

> **Scope.** This document collects facts. The decisions that use these facts belong to
> [#246](https://github.com/tw0po1nt/FsHttp.Studio/issues/246) ("How does the Neovim client get the
> companion and the .NET SDK?") and [#247](https://github.com/tw0po1nt/FsHttp.Studio/issues/247)
> ("Where does the Neovim client live, and how does it ship?"). This document makes no decision.

All sources were read on 2026-10-06. Each source link is a permalink to the commit or release that
was read, where the host supports one. The numbers in square brackets refer to the
[Sources](#sources) list. A line range after a number, such as `[1, L318]`, points into that
source.

## Summary

- Each Git-based plugin manager clones the whole repo and uses the repo root as the plugin root.
  vim-plug and packer.nvim have an `rtp` option for a subdirectory. lazy.nvim and `vim.pack` have no
  such option. A user can add a subdirectory to `runtimepath` by hand in a config or `load`
  function. [1][7][10][11][13]
- Each manager has a hook that runs after install and after update. Among the Git-based managers,
  lazy.nvim alone runs a hook that the plugin supplies (`build.lua`, `lazy.lua`, or a rockspec)
  with no user code. rocks.nvim runs the build steps in the plugin's rockspec. In Neovim 0.12.x, a
  `vim.pack` user must write the hook as a `PackChanged` autocommand. [1][2][3][7][8]
- Real plugins get a native binary in five ways: download at first use (blink.cmp), download in
  the build hook (LanguageClient-neovim, fzf), build from source in the build hook
  (telescope-fzf-native), install through mason.nvim, or ask the user to install a .NET tool
  (easy-dotnet.nvim, Ionide-vim). [14][17][19][20][21][22][24][25]
- mason.nvim already installs .NET programs. One registry package downloads a VSCode `.vsix`
  from a GitHub release and runs a DLL inside it with `dotnet`. [22][23]
- The FsHttp.Studio `.vsix` on each GitHub release already carries the companion. The companion is
  the same set of files on every platform. [29][32]
- The companion needs a .NET 10 SDK or newer at Run time. Each install route below leaves that
  prerequisite in place. [27]

## 1. What a Neovim client must put on the machine

These facts come from the FsHttp.Studio source on `main` at `846f757`.

- The companion is a framework-dependent .NET 10 program. The extension host starts it as
  `dotnet <companion DLL>`. [26][31]
- The companion sets `RollForward=LatestMajor`. A newer major runtime can run it. [31]
- The extension host requires a .NET SDK at or above the companion's major version. The reason:
  FSI's `#r "nuget:"` restore drives `dotnet msbuild`, and a runtime-only install does not have
  it. [27]
- Git ignores `dist/`, so a clone of the repo contains no built companion. [28]
- The v0.2.0 `.vsix` is 11.1 MB. Its `extension/dist/companion/` folder holds 27.9 MB of files
  when unpacked. `FSharp.Compiler.Service.dll` is 19.4 MB of that. The publish has no runtime
  identifier, so the same files serve each platform. [29][31][32]
- Each release also ships a `.sha256` checksum file next to the `.vsix`. [29][30]

A Neovim client that clones the repo must therefore build the companion or download it. It must
also find a `dotnet` with an SDK of major version 10 or newer.

## 2. Build hooks in each plugin manager

| Manager | Hook | Who writes the hook | Runs when | Notes |
| --- | --- | --- | --- | --- |
| lazy.nvim | `build` in the spec. A string runs as a shell command, `:Cmd` runs a Neovim command, a function runs as Lua, `"rockspec"` runs `luarocks make`. | The user in the spec, or the plugin through `build.lua`, `lazy.lua`, or a rockspec at the plugin root | On install and on update | A shell `build` runs in the plugin directory through `$SHELL -c`, or `cmd.exe /c` on Windows. [1][2] |
| `vim.pack` (Neovim 0.12.x) | `PackChanged` autocommand. The event data has `kind` (`install`, `update`, `delete`) and `path`. | The user | After the change, on install, update, and delete | The user must create the autocommand before the first `vim.pack.add()` to see installs. [7] |
| `vim.pack` (Neovim `master`, unreleased) | A top-level `pkg.json` with `scripts.install`, `scripts.update`, `scripts.preupdate`, `scripts.preuninstall` | The plugin | After the matching event | Neovim sources the script with the plugin root as the current directory. [8] |
| mini.deps | `hooks.pre_install`, `post_install`, `pre_checkout`, `post_checkout` | The user | Around install and checkout | Its own doc recommends `vim.pack` on Neovim 0.12 and later. [9] |
| vim-plug | `do` (shell string or funcref) | The user | After install or update | [10] |
| packer.nvim | `run` | The user | After install or update | The repo states that it is unmaintained since August 2023. [11] |
| rocks.nvim | The rockspec `build` table, run by luarocks | The plugin author, in the rockspec | On install and on update | The README warns of a major rewrite (v3.0.0) that moves from luarocks to lux. [3][4][5] |
| rocks-git.nvim | `build` in the `rocks.toml` entry (shell, or `:Cmd`) | The user | After install or update | [6] |

### 2.1 lazy.nvim

- The `build` property "is executed when a plugin is installed or updated". [1, L318]
- If a plugin has no `build` in its spec but has a `build.lua` file, lazy.nvim runs that file.
  The code looks for `build.lua` and `build/init.lua` at the plugin root. [1, L1319][2]
- A `lazy.lua` file at the plugin root holds the plugin's own spec, and that spec can set `build`.
  lazy.nvim reads it from the plugin root only. [1, L587][34]
- A rockspec named `*-scm-1.rockspec`, `*-git-1.rockspec`, or `*-dev-1.rockspec` at the plugin root
  makes lazy.nvim build the rock with luarocks. It uses the rockspec only when the package has no
  `/lua` directory, has a complex build step, or has dependencies other than `lua`. [1, L591-L600][35]
- Build functions and `*.lua` build files run in a coroutine, so a long build does not block the
  editor. [1, L1321]
- The `version` field takes semver ranges and resolves them against Git tags. [1, L553-L562]

### 2.2 `vim.pack`

- `vim.pack` is the built-in plugin manager in Neovim 0.12. Its doc calls it "still considered
  experimental". Neovim 0.12.0 was released on 2026-03-29, and 0.12.5 on 2026-08-23. [7][33]
- It clones each plugin with a partial blobless `git clone` into `site/pack/core/opt/<name>`. It
  expects semver tags of the form `v1.2.0` or `1.2.0`. [7, L208-L224][7, L420-L431]
- In 0.12.x, the only hook is the `PackChanged` and `PackChangedPre` autocommand that the user
  writes. The doc example runs `make` in `ev.data.path` on `install` and `update`. [7, L360-L400]
- On `master`, a plugin can ship a `pkg.json` manifest whose `scripts` Neovim sources after each
  event. No 0.12.x release contains this feature. [8, L430-L479][7]

### 2.3 luarocks and rocks.nvim

- rocks.nvim installs plugins as luarocks packages. Its README states that it "shifts the
  responsibility of specifying dependencies and build steps from users to plugin authors". [3, L62-L66]
- A rockspec `build.type` can be `builtin`, `make`, `cmake`, `command`, or `none`. The `command`
  back-end runs `build.build_command` and `build.install_command`. [5, L74-L141]
- `build.copy_directories` copies directories from the source to the install prefix as-is. The
  names `lua`, `lib`, and `rock_manifest` are reserved. [5, L63-L64]
- luarocks has three rock types: source rocks (`.src.rock`), binary rocks for one platform, and
  pure-Lua rocks (`.all.rock`). [12]
- rocks.nvim puts a rock's Lua modules on `package.path`. It installs the runtime directories
  (`plugin/`, `ftdetect/`, and others) to a separate location. [3, L398-L413]
- The luarocks-tag-release GitHub Action builds a rockspec and uploads it to luarocks.org on each
  tag. Its default `copy_directories` lists the usual Neovim runtime directories. [16]
- rocks.nvim needs a Lua 5.1 or LuaJIT install with headers. When it installs a rock, it also
  searches the rocks-binaries server for a prebuilt rock. The NURR project packages many plugins
  and tree-sitter parsers for luarocks in its CI. [3, L124-L133][3, L175-L192]
- rocks-git.nvim installs a plugin from a Git repo with no rockspec. Its `build` field is a shell
  or Vim command that the user writes. [6, L79-L92]

## 3. How real plugins get their binary

### 3.1 Download a release asset at first use: blink.cmp (v1)

- The install doc says: "Blink uses a prebuilt binary for the fuzzy matcher which will be
  downloaded automatically when on a tag." It tells lazy.nvim users to set `version = '1.*'`. [14, L4-L32]
- The plugin code runs `git describe --tags --exact-match` in its own clone to find the tag. [15]
- It builds the asset URL from the tag and a target triple, for example
  `https://github.com/saghen/blink.cmp/releases/download/<tag>/<triple>.so`. It downloads a
  `.sha256` file next to it and verifies the checksum. [17]
- It writes the file under a temporary name and then renames it. The code comment states the
  reason: macOS caches the library in the kernel, so an update in place causes a crash. [17]
- When the download fails, it falls back to a Lua implementation and shows a warning. The user can
  build from source with `build = 'cargo build --release'` instead. [14][18]
- This route needs no build hook, so it works the same in each plugin manager.

### 3.2 Download a release asset in the build hook: LanguageClient-neovim and fzf

- LanguageClient-neovim's `install.sh` pins a version number in the script. It picks an asset from
  `uname -sm`, downloads it from the GitHub release, and falls back to `cargo` when no asset
  matches. The user runs it through the manager's hook, for example `'do': 'bash install.sh'`. [19][25]
- fzf keeps its Vim plugin at the root of its Go repo (`plugin/fzf.vim`). `fzf#install()` runs
  `install --bin`, which downloads the release binary for the pinned version. It falls back to
  `go install` when `go` is on `PATH`. [20]

### 3.3 Build from source in the build hook: telescope-fzf-native

- The README states: "you need to build it with either `cmake` or `make`. As of now, we do not
  ship binaries." Each manager example passes the build command as the hook. [21]

### 3.4 Install through mason.nvim

- mason.nvim installs packages into `stdpath("data")/mason`. Packages come from a registry, and a
  user can add more registries. [22]
- A `pkg:nuget` source runs `dotnet tool update --tool-path . --version <v> <package>`. It needs a
  `dotnet` SDK on the machine. `fsautocomplete` uses this source. [22][23]
- A `pkg:github` source downloads a release asset for each target platform. `netcoredbg` uses this
  source. [23]
- The `dotnet:` bin type writes a wrapper script that runs `dotnet "<path to DLL>"`. The
  `bicep-lsp` package downloads `vscode-bicep.vsix` from the Azure/bicep GitHub release and points
  `dotnet:` at the language server DLL inside it. [22][23]
- A new package in the core registry must meet one of four conditions: 100 GitHub stars, 5,000
  VSCode Marketplace downloads, approval at nvim-lspconfig, or a recommendation from a reputable
  organization. Each package category must be one of `Compiler`, `DAP`, `Formatter`, `LSP`,
  `Linter`, or `Runtime`. FsHttp.Studio had 15 stars on 2026-10-06. [23]

### 3.5 Ask the user to install a .NET tool: easy-dotnet.nvim and Ionide-vim

- easy-dotnet.nvim lists `dotnet tool install -g EasyDotnet` as a requirement. Its
  `:Dotnet _server update` command runs the same `dotnet tool install`. Its health check reports
  a missing `dotnet-easydotnet` executable. [24]
- Ionide-vim requires the .NET SDK and tells the user to run `dotnet tool install -g fsautocomplete`.
  [25]
- A .NET tool is a NuGet package that holds a console app. `dotnet tool install -g` puts it in
  `$HOME/.dotnet/tools` (Linux and macOS) or `%USERPROFILE%\.dotnet\tools` (Windows). [36]

## 4. Install from a repo subdirectory or from the repo root

### 4.1 What each manager does

- **lazy.nvim.** A spec names a repo by `[1]` or `url`, or a local directory by `dir`. A valid spec
  must have one of the three. [1, L249-L265] The loader adds the clone root (`plugin.dir`) to
  `runtimepath`. [13] The spec has no subdirectory field. The packer migration guide shows the
  replacement for packer's `rtp`: a `config` function that runs
  `vim.opt.rtp:append(plugin.dir .. "/custom-rtp")`. [1, L1163-L1169] lazy.nvim looks for
  `build.lua`, `lazy.lua`, and rockspecs at the clone root only. [2][34][35]
- **lazy.nvim with `dir`.** A `dir` spec can point at any local directory. That includes a
  subdirectory of a clone that the user made by hand. lazy.nvim does not install or update a local
  plugin, and it reports an error when the directory is missing. [1, L255][2]
- **`vim.pack`.** The `src` field is a URI for `git clone`, and the plugin directory is the clone.
  [7, L403-L418] The `load` option of `vim.pack.add()` accepts a function that "is fully
  responsible for loading plugin". [7, L445-L455] From that text, a user can add a subdirectory
  to `runtimepath` in that function. This research did not test it. The `pkg.json` manifest on `master` must be at the top level. [8, L432]
- **Built-in packages.** Neovim loads a plugin from each directory under `pack/*/start` or
  `pack/*/opt`. The doc shows a user who makes that directory and unpacks a plugin into it by hand.
  [7, L85-L97]
- **vim-plug.** The `rtp` option names the "Subdirectory that contains Vim plugin". The README
  example is `Plug 'nsf/gocode', { 'rtp': 'vim' }`. [10, L178-L179][10, L273]
- **packer.nvim.** The `rtp` field "Specifies a subdirectory of the plugin to add to runtimepath".
  [11, L390]
- **mini.deps.** It clones into `pack/deps/opt`. Its doc has no subdirectory option. [9]
- **luarocks and rocks.nvim.** The rockspec is a separate file that names the source by URL and
  tag. In the `builtin` back-end, each module path is relative to the source directory, so a module
  can come from any path in the repo. [5, L31-L52][5, L79-L92] This research did not confirm how
  `copy_directories` names a nested source directory, for example `editors/nvim/plugin`, at the
  install prefix.

### 4.2 Precedents

- **Plugin in a subdirectory.** OCaml's merlin keeps its Vim plugin in `vim/merlin/` of the merlin
  repo. Its README tells the user to run `:set rtp+=<SHARE_DIR>/merlin/vim`, where `<SHARE_DIR>` is
  the folder that opam installed it to. [37]
- **Plugin at the repo root of a larger project.** fzf keeps `plugin/fzf.vim` at the root of its Go
  repo, next to the Go source. [20]

### 4.3 Facts about the FsHttp.Studio repo root

- The repo root has no `lua/`, `plugin/`, `build.lua`, `lazy.lua`, `pkg.json`, or rockspec today.
- lazy.nvim and `vim.pack` both clone with `--filter=blob:none`. A blobless clone still checks out
  every file of the working tree. [1, L640-L646][7, L428]
- Release tags have the form `v0.2.0`. That form matches the semver tags that `vim.pack` and the
  lazy.nvim `version` field use. A Neovim client in the same repo shares those tags with the VSCode
  extension. [29][7, L221-L224]
- `global.json` at the repo root pins SDK `10.0.100` with `rollForward: latestFeature`. That policy
  accepts a 10.0.x SDK only. The .NET SDK searches for `global.json` from the current directory
  upward, and the MSBuild resolver searches from the project directory upward. [38][39] From these
  rules, a build hook that runs `dotnet publish src/companion/...` in a clone fails on a machine
  whose only SDKs have a major version above 10. This research did not test it.

## 5. Which facts from `runtime-distribution.md` also apply to Neovim

`docs/research/runtime-distribution.md` is on the `research/runtime-distribution` branch. It
covers the VSCode extension. The table lists each of its facts and whether the fact holds for a
Neovim client.

| Fact in `runtime-distribution.md` | Applies to Neovim? | Reason |
| --- | --- | --- |
| The companion is framework-dependent and needs a .NET runtime on the machine. | Yes | It is a property of the companion. [26][31] |
| The companion needs only a runtime. | Out of date for both editors | `main` now requires an SDK for `#r "nuget:"` restore. The need comes from the companion, so it holds in Neovim too. [27] |
| The repo pins SDK `10.0.200` in `global.json`. | Out of date | `main` pins `10.0.100` with `latestFeature`. [38] |
| Ionide's "`dotnet` was not found" failures come from `PATH`, `DOTNET_ROOT`, nonstandard install folders, and WSL. | Yes | A Neovim client must find `dotnet` in the same places. The VSCode extension answers this with the `fshttpStudio.dotnetPath` setting. A Neovim client needs its own equivalent. [27] |
| The .NET Install Tool (`ms-dotnettools.vscode-dotnet-runtime`), `extensionDependencies`, and `dotnet.acquire`. | No | These are VSCode extension APIs. Neovim has no equivalent. The closest general tool is the `dotnet-install` script. [40] |
| `dotnet.acquire` installs a runtime only. SDK installs are a separate, system-level command. | No | VSCode only. |
| Platform-specific `.vsix` packages through `vsce --target`, and the Marketplace serving the matching build. | No | The Marketplace mechanism is VSCode only. A GitHub release can carry one asset for each platform. blink.cmp does this. [17] |
| A self-contained .NET app is about 60 to 70 MB, and trimming is fragile with reflection. | Yes | The size and trimming facts hold for any self-contained build, for example a GitHub release asset. A self-contained companion still needs an SDK for `#r "nuget:"` restore. [27] |
| A framework-dependent publish is "a few hundred KB to single-digit MB". | Out of date | The real companion is 27.9 MB unpacked, mostly FCS. [29][32] |
| A user-set override path for `dotnet` (the `existingDotnetPath` pattern). | Yes | The pattern applies to any editor client. [27] |
| A runtime install pins major.minor and rolls the patch. | Yes, for the companion | The companion's `RollForward=LatestMajor` applies in each editor. [31] |

### The `dotnet-install` script

The `dotnet-install` scripts install the .NET SDK, or a runtime with `--runtime`, with no admin
rights. The default folder is `$HOME/.dotnet` on Linux and macOS and `%LocalAppData%\Microsoft\dotnet`
on Windows. The script changes `PATH` for the current session only, and it does not set
`DOTNET_ROOT`. It does not resolve the native dependencies of .NET. Microsoft states that the
intended use is CI, and that a person who sets up a development machine should use the installers.
[40]

## 6. Facts that the distribution decisions depend on

This section lists facts only. [#246](https://github.com/tw0po1nt/FsHttp.Studio/issues/246) and
[#247](https://github.com/tw0po1nt/FsHttp.Studio/issues/247) make the decisions.

1. **Root or subdirectory.** lazy.nvim and `vim.pack` load the repo root. A plugin in a
   subdirectory needs user code in each of them, or a `dir` spec that the user maintains by hand.
   vim-plug and packer.nvim support a subdirectory with `rtp`. A rockspec can draw Lua modules from
   any path. [1][7][10][11][5]
2. **Plugin-supplied hooks.** Among the Git-based managers, only lazy.nvim runs a hook that the
   plugin supplies with no user code. rocks.nvim runs the build steps in a rockspec. `vim.pack` in
   0.12.x needs a user autocommand. The `pkg.json` scripts exist on Neovim `master` only.
   [1][2][3][7][8]
3. **Hook-free install.** A download at first use, keyed to the checked-out Git tag, works in every
   manager. blink.cmp uses this pattern, with a checksum check and a rename for macOS. [14][15][17]
4. **The companion is already a release asset.** Each GitHub release has a `.vsix` with the
   companion inside and a `.sha256` file. The companion files are the same on each platform. [29][30][32]
5. **mason.nvim precedent.** mason.nvim already runs a framework-dependent DLL from inside a
   `.vsix` release asset with `dotnet:`. The core registry has entry conditions that FsHttp.Studio
   did not meet on 2026-10-06. A user can add a separate registry. [22][23]
6. **.NET tool route.** easy-dotnet.nvim and Ionide-vim ship their .NET programs as .NET tools that
   the user installs with `dotnet tool install -g`. That route needs the SDK, which the companion
   needs anyway. [24][25][36]
7. **Build from source.** A build hook that runs `dotnet publish` needs the SDK, which the
   companion needs anyway. The repo `global.json` limits that build to a 10.0.x SDK. The companion
   itself runs on any newer major version. [31][38][39]
8. **The SDK prerequisite stays.** No install route in this document removes the need for a .NET
   10 or newer SDK at Run time. A self-contained build removes the runtime need only. [27]
9. **SDK acquisition in Neovim.** Neovim has no counterpart to the .NET Install Tool. The
   `dotnet-install` script is the closest tool, and Microsoft aims it at CI. [40]
10. **Manager maturity.** `vim.pack` is marked experimental. packer.nvim is unmaintained. rocks.nvim
    announces a breaking v3.0.0 rewrite. mini.deps points 0.12 users to `vim.pack`. [7][11][3][4][9]

## Sources

1. lazy.nvim help, `doc/lazy.nvim.txt` at `306a055`. <https://github.com/folke/lazy.nvim/blob/306a05526ada86a7b30af95c5cc81ffba93fef97/doc/lazy.nvim.txt>
2. lazy.nvim build task, `lua/lazy/manage/task/plugin.lua` at `306a055`. <https://github.com/folke/lazy.nvim/blob/306a05526ada86a7b30af95c5cc81ffba93fef97/lua/lazy/manage/task/plugin.lua#L8-L84>
3. rocks.nvim README at `606e5fc`. <https://github.com/lumen-oss/rocks.nvim/blob/606e5fc8b271b92616048195673202be888e0339/README.md>
4. rocks.nvim issue #539, "Use lux instead of luarocks". <https://github.com/lumen-oss/rocks.nvim/issues/539>
5. luarocks rockspec format, `docs/rockspec_format.md` at `544a78c`. <https://github.com/luarocks/luarocks/blob/544a78c802e953180217ca5d34390491afed1ac1/docs/rockspec_format.md>
6. rocks-git.nvim README at `a647412`. <https://github.com/lumen-oss/rocks-git.nvim/blob/a647412d2e212a03c551d665005f5a1e0c575d1d/README.md>
7. Neovim `pack.txt` at `v0.12.5`. <https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/pack.txt>
8. Neovim `pack.txt` on `master` at `cae3a19`. <https://github.com/neovim/neovim/blob/cae3a197f74d1f406319ef00a0d2080d98322a00/runtime/doc/pack.txt>
9. mini.deps help, `doc/mini-deps.txt` at `fa90373`. <https://github.com/nvim-mini/mini.nvim/blob/fa9037326e9996381e34a14b6350e18168618532/doc/mini-deps.txt#L1-L13>, hooks at <https://github.com/nvim-mini/mini.nvim/blob/fa9037326e9996381e34a14b6350e18168618532/doc/mini-deps.txt#L288-L299>
10. vim-plug README at `4da42e9`. <https://github.com/junegunn/vim-plug/blob/4da42e9a5c948fb82970d522bd14cbab3d122ff1/README.md>
11. packer.nvim README at `ea0cc3c`. <https://github.com/wbthomason/packer.nvim/blob/ea0cc3c59f67c440c5ff0bbe4fb9420f4350b9a3/README.md>
12. luarocks, types of rocks, `docs/types_of_rocks.md` at `544a78c`. <https://github.com/luarocks/luarocks/blob/544a78c802e953180217ca5d34390491afed1ac1/docs/types_of_rocks.md>
13. lazy.nvim loader, `add_to_rtp`. <https://github.com/folke/lazy.nvim/blob/306a05526ada86a7b30af95c5cc81ffba93fef97/lua/lazy/core/loader.lua#L461-L492>
14. blink.cmp v1 install doc, `doc/installation.md` at `78336bc`. <https://github.com/saghen/blink.cmp/blob/78336bc89ee5365633bcf754d93df01678b5c08f/doc/installation.md>
15. blink.cmp v1 tag lookup, `lua/blink/cmp/fuzzy/download/git.lua`. <https://github.com/saghen/blink.cmp/blob/78336bc89ee5365633bcf754d93df01678b5c08f/lua/blink/cmp/fuzzy/download/git.lua#L22-L50>
16. luarocks-tag-release README at `c360599`. <https://github.com/lumen-oss/luarocks-tag-release/blob/c36059952206b5849d983aa986fcd6bd0c66848b/README.md>
17. blink.cmp v1 download code, `lua/blink/cmp/fuzzy/download/init.lua` and `system.lua`. <https://github.com/saghen/blink.cmp/blob/78336bc89ee5365633bcf754d93df01678b5c08f/lua/blink/cmp/fuzzy/download/init.lua#L186-L235>, <https://github.com/saghen/blink.cmp/blob/78336bc89ee5365633bcf754d93df01678b5c08f/lua/blink/cmp/fuzzy/download/system.lua#L5-L27>
18. blink.cmp v1 fuzzy doc, `doc/configuration/fuzzy.md`. <https://github.com/saghen/blink.cmp/blob/78336bc89ee5365633bcf754d93df01678b5c08f/doc/configuration/fuzzy.md>
19. LanguageClient-neovim `install.sh` at `103a881`. <https://github.com/autozimu/LanguageClient-neovim/blob/103a88198604a408f7624c33472210cfafce6132/install.sh>
20. fzf `install` and `plugin/fzf.vim` at `b1be3a8`. <https://github.com/junegunn/fzf/blob/b1be3a8be1b833ce5b92fbbac11637643d60a046/install#L150-L226>, <https://github.com/junegunn/fzf/blob/b1be3a8be1b833ce5b92fbbac11637643d60a046/plugin/fzf.vim#L157-L170>
21. telescope-fzf-native README at `b25b749`. <https://github.com/nvim-telescope/telescope-fzf-native.nvim/blob/b25b749b9db64d375d782094e2b9dce53ad53a40/README.md>
22. mason.nvim at `2a6940a`: README <https://github.com/mason-org/mason.nvim/blob/2a6940af80375532e5e9e7c1f2fc6319a1b7a69d/README.md>, NuGet manager <https://github.com/mason-org/mason.nvim/blob/2a6940af80375532e5e9e7c1f2fc6319a1b7a69d/lua/mason-core/installer/managers/nuget.lua#L12-L23>, `dotnet:` bin type <https://github.com/mason-org/mason.nvim/blob/2a6940af80375532e5e9e7c1f2fc6319a1b7a69d/lua/mason-core/installer/compiler/link.lua#L59-L74>
23. mason-registry at `1a4da54`: `bicep-lsp` <https://github.com/mason-org/mason-registry/blob/1a4da54a89c9bf901abde9f2647e660548b7ca3d/packages/bicep-lsp/package.yaml>, `netcoredbg` <https://github.com/mason-org/mason-registry/blob/1a4da54a89c9bf901abde9f2647e660548b7ca3d/packages/netcoredbg/package.yaml>, `fsautocomplete` <https://github.com/mason-org/mason-registry/blob/1a4da54a89c9bf901abde9f2647e660548b7ca3d/packages/fsautocomplete/package.yaml>, CONTRIBUTING <https://github.com/mason-org/mason-registry/blob/1a4da54a89c9bf901abde9f2647e660548b7ca3d/CONTRIBUTING.md#requirements>
24. easy-dotnet.nvim at `df60002`: README <https://github.com/GustavEikaas/easy-dotnet.nvim/blob/df600029a54754d90ac35fe284b3921bb7bf1023/README.md#requirements>, server update command <https://github.com/GustavEikaas/easy-dotnet.nvim/blob/df600029a54754d90ac35fe284b3921bb7bf1023/lua/easy-dotnet/commands.lua#L520-L530>, health check <https://github.com/GustavEikaas/easy-dotnet.nvim/blob/df600029a54754d90ac35fe284b3921bb7bf1023/lua/easy-dotnet/health.lua#L315>
25. Ionide-vim README at `30aac18`. <https://github.com/ionide/Ionide-vim/blob/30aac182c9652f37b5ce65942e5cc32371595995/README.mkd>
26. FsHttp.Studio `src/host/Companion.fs`, companion spawn. <https://github.com/tw0po1nt/FsHttp.Studio/blob/846f7573f845884f8fb7d38bcf2ce1b580bb1f6f/src/host/Companion.fs#L86-L90>
27. FsHttp.Studio `src/host/Extension.fs`, SDK floor and `dotnetPath` override. <https://github.com/tw0po1nt/FsHttp.Studio/blob/846f7573f845884f8fb7d38bcf2ce1b580bb1f6f/src/host/Extension.fs#L27-L147>
28. FsHttp.Studio `.gitignore`. <https://github.com/tw0po1nt/FsHttp.Studio/blob/846f7573f845884f8fb7d38bcf2ce1b580bb1f6f/.gitignore#L15-L18>
29. FsHttp.Studio release v0.2.0 (assets `fshttp-studio-0.2.0.vsix`, 11,054,160 bytes, and `.sha256`). <https://github.com/tw0po1nt/FsHttp.Studio/releases/tag/v0.2.0>
30. FsHttp.Studio README, install and checksum section. <https://github.com/tw0po1nt/FsHttp.Studio/blob/846f7573f845884f8fb7d38bcf2ce1b580bb1f6f/README.md#install>
31. FsHttp.Studio `src/companion/Companion.fsproj`. <https://github.com/tw0po1nt/FsHttp.Studio/blob/846f7573f845884f8fb7d38bcf2ce1b580bb1f6f/src/companion/Companion.fsproj>
32. Measured with `unzip -l` on the v0.2.0 `.vsix` [29]: 27,905,307 bytes under `extension/dist/companion/`, of which `FSharp.Compiler.Service.dll` is 19,412,264 bytes.
33. Neovim releases. <https://github.com/neovim/neovim/releases>
34. lazy.nvim `lua/lazy/pkg/lazy.lua`. <https://github.com/folke/lazy.nvim/blob/306a05526ada86a7b30af95c5cc81ffba93fef97/lua/lazy/pkg/lazy.lua#L1-L15>
35. lazy.nvim `lua/lazy/pkg/rockspec.lua`, `find_rockspec`. <https://github.com/folke/lazy.nvim/blob/306a05526ada86a7b30af95c5cc81ffba93fef97/lua/lazy/pkg/rockspec.lua#L246-L262>
36. Microsoft Learn, ".NET tools". <https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools>
37. merlin README, Vim setup, at `35e0678`. <https://github.com/ocaml/merlin/blob/35e06782e2fe466947a27d57300f63a849123f04/README.md#vim-setup>
38. FsHttp.Studio `global.json`. <https://github.com/tw0po1nt/FsHttp.Studio/blob/846f7573f845884f8fb7d38bcf2ce1b580bb1f6f/global.json>
39. Microsoft Learn, "global.json overview" (`rollForward` values and the search order). <https://learn.microsoft.com/en-us/dotnet/core/tools/global-json>
40. Microsoft Learn, "dotnet-install scripts". <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script>
