# The Neovim client

Spec for the main feature of v0.3: a Neovim client beside the VSCode extension. The map
[FsHttp.Studio v0.3: Neovim support and shared features](https://github.com/tw0po1nt/FsHttp.Studio/issues/239)
holds the decisions. Each decision below names the ticket that holds its detail.

v0.3 also ships three features in both Clients, each with its own spec:
[0016 Copy as curl](0016-copy-as-curl.md), [0017 Run request at cursor](0017-run-request-at-cursor.md),
and [0018 Restart companion](0018-restart-companion.md). The Neovim half of each one depends on this
spec.

## Problem Statement

**A Neovim user cannot use FsHttp.Studio.** FsHttp.Studio is a VSCode extension only. A user who
writes F# scripts in Neovim, and LazyVim in particular, can run a request only through FSI. FSI
flattens the response to text. The user gets no status line, no Request section, no folds for the
headers, no image, and no copy of the request.

**A second editor can drift from the first.** Each Client shows the same refusal text, the same
Status line text, and the same copy payload. A second copy of each rule can fall behind the F#
source with no error.

## Solution

**v0.3 ships a Neovim client beside the VSCode extension.** The Neovim client is plain Lua. It
starts the same companion and exchanges the same envelopes, as ADR-0002 states. It matches VSCode
v0.2 in full: a Block mark on each located block, a Response buffer with the status line, the
Request, the Response headers, and the Body, images through snacks.nvim, and a yank for each copy
button.

**One source of truth holds the two Clients together.** An F# script generates the user-facing text
for the Lua client from `Refusals.fs`. F# tests write Golden fixtures that the Lua core suite must
match byte for byte.

One version, one tag, and one GitHub Release cover both Clients and the companion. The UI suite,
the Neovim suite, and the Lua core suite together are the Release gate.

## User Stories

### Install and start (Neovim)

1. As a LazyVim user, I want to install the client with `{ "tw0po1nt/FsHttp.Studio" }`, so that I add no runtimepath code.
2. As a lazy.nvim user, I want the companion to download when lazy.nvim installs or updates the plugin, so that my first script opens with no wait.
3. As a user of another plugin manager, I want the companion to download at first use, so that the client works with no build hook.
4. As a Neovim user, I want the client to verify the checksum of the companion before it unpacks it, so that a damaged download never runs.
5. As a Neovim user, I want the client to download the companion of its own version, so that the Lua client and the companion agree on the envelope.
6. As a user on a checkout of `main` after a version bump, I want an ERROR notice that tells me to pin a release or set `companion_path`, so that I know why no companion starts.
7. As a contributor, I want to point `companion_path` at my local build, so that I test a companion change with no release.
8. As a user on a machine with no network access, I want `companion_path` to stop each download, so that `:Lazy update` does not fail at each run.
9. As a Neovim user, I want the client to find `dotnet` on PATH, or at `dotnet_path`, by the same rule as VSCode, so that both editors use the same SDK.
10. As a Neovim user with no .NET 10 SDK, I want a WARN notice that names the SDK floor and the download URL, so that I know what to install.
11. As a Neovim user, I want `:checkhealth fshttp` to report each required and each optional item with its fix, so that I can repair my setup alone.
12. As a user who reports a bug, I want `:checkhealth fshttp` to show my options, so that I can paste the full configuration.
13. As a user with a companion from `companion_path` of a different version, I want one WARN notice that names both versions, so that I can explain a strange result.

### Start a Run (Neovim)

14. As a Neovim user, I want `:FsHttp run` to run the innermost block at the cursor, so that I start a Run from the keyboard.
15. As a Neovim user with the cursor outside every block, I want a picker that lists each located block, so that I can choose one.
16. As a Neovim user, I want a Block mark on each located block, so that I see which blocks can run.
17. As a Neovim user, I want a refused block to show its refusal title in its Block mark, so that I know why it cannot run before I try.
18. As a Neovim user who runs a refused block, I want a WARN notice with the reason and the workaround, so that I can fix the script.
19. As a Neovim user, I want `<Plug>(FsHttpRun)` and no default key, so that I choose my own key.
20. As a Neovim user who runs a block while the companion starts, I want the Run to start when the companion is ready, so that I do not run the command again.
21. As a Neovim user with a syntax error that hides every block, I want a mark on line 1 that says so, so that I do not think FsHttp.Studio is broken.

### The Response buffer (Neovim)

22. As a Neovim user, I want the result in one Response buffer in a split, so that the script stays in view.
23. As a Neovim user, I want each Run to reuse the window of the Response buffer, so that my layout stays the same.
24. As a Neovim user, I want the cursor to stay in the script after a Run, so that I can edit and run again.
25. As a Neovim user, I want the status, the timings, the size, the method, and the URL in the winbar, so that they stay on screen while I scroll the body.
26. As a Neovim user in a narrow split, I want the winbar to cut the start of the URL, so that the status and the path stay visible.
27. As a Neovim user, I want the Request and the Response headers in closed folds, so that the body is the first thing I see.
28. As a Neovim user, I want a JSON, XML, or HTML body with highlights and folds, so that I can read a large body.
29. As a Neovim user with no tree-sitter parser, I want a pretty-printed JSON body with folds from its structure, so that the body still reads well.
30. As a Neovim user, I want an image body drawn in the buffer through snacks.nvim, so that I see the image in the editor.
31. As a Neovim user whose terminal cannot draw an image, I want the pixel size and the reason, so that I know why no image shows.
32. As a Neovim user with an HTML body, I want `:FsHttp open` to show the rendered page in the browser with scripts blocked, so that I see the page safely.
33. As a Neovim user with an image body, I want `:FsHttp open` to open the image in the system viewer, so that I see an image that my terminal cannot draw.
34. As a Neovim user, I want a hint line that names `:FsHttp open`, so that I learn the command where I need it.
35. As a Neovim user, I want a binary body as a hex view, so that I can inspect it.
36. As a Neovim user, I want a minified body to wrap in the window, so that I can read it with no horizontal scroll.
37. As a Neovim user, I want a Compile error, a Runtime error, and a Refused Run as text in the Response buffer, so that each outcome has one place.
38. As a Neovim user with a Compile error, I want `<CR>` on a `(line,col)` line to move the cursor to that position in the script, so that I reach the error fast.
39. As a Neovim user with an F# language server, I want FsHttp.Studio to add no diagnostic, so that one error does not show two times.
40. As a Neovim user, I want "Running… Ns" while a Run is in progress, so that I know the Run is alive.

### Copy (Neovim)

41. As a Neovim user, I want `:FsHttp yank request`, `headers`, and `body` to put the payload of the VSCode copy buttons in a register, so that I can paste it anywhere.
42. As a Neovim user, I want `yr`, `yh`, `yb`, and `yc` in the Response buffer, so that a yank needs no command.
43. As a Neovim user, I want `g?` to list the keys of the Response buffer, so that I can learn them.
44. As a Neovim user, I want a notice that confirms each yank, or names the clipboard failure, so that I do not paste old text.
45. As a Neovim user, I want a `<Plug>` map for each key of the Response buffer, so that I can choose my own keys.

### Companion state (Neovim)

46. As a LazyVim user, I want the Status line text in lualine with no setup, so that I see the companion state.
47. As a user of a different statusline, I want `require("fshttp").status()`, so that I can add the text myself.
48. As a Neovim user, I want `:FsHttp status`, so that I can read the state with no statusline.
49. As a Neovim user, I want a notice only for a state that needs a fix, so that the client stays quiet when all is well.
50. As a Neovim user, I want the same Status line text rows as VSCode, so that the documentation covers both editors.

### Settings (Neovim)

51. As a LazyVim user, I want to set options through `opts`, so that I configure the client the LazyVim way.
52. As a Neovim user, I want to turn off the virtual line or the sign of each Block mark, so that the script looks as I want.
53. As a Neovim user, I want to choose the split direction of the Response buffer, so that it fits my layout.
54. As a Neovim user, I want to turn off images and the default keys, so that the client fits my setup.
55. As a Neovim user, I want to remove the automatic lualine entry, so that I can put the text where I want.
56. As a Neovim user with a typo in an option, I want an ERROR or a WARN notice that names the key, so that I can fix it, and each other option still applies.
57. As a Neovim user who changes a path option, I want an INFO notice that names `:FsHttp restart`, so that I know how to apply it.

### Maintainer

58. As the maintainer, I want one version, one tag, and one release for both Clients, so that I ship v0.3 in one step.
59. As the maintainer, I want the user-facing text of both Clients to come from `Refusals.fs`, so that the two Clients cannot drift.
60. As the maintainer, I want Golden fixtures that F# writes and Lua reads, so that a change to the envelope or a pure rule fails in both suites.
61. As the maintainer, I want the Release gate to drive the files that the release ships, so that a green gate proves the shipped bytes.
62. As the maintainer, I want the `.vsix` to hold no Lua file, so that the VSCode package stays the same size.
63. As the maintainer, I want the Neovim suite on Linux, macOS, and Windows, so that `curl`, `tar`, the checksum tool, and `dotnet` discovery work on each platform.

## Implementation Decisions

### Part A: the Neovim client

#### A1. Language and runtime

Ticket: [Which language and runtime does the Neovim client use?](https://github.com/tw0po1nt/FsHttp.Studio/issues/245)

- The Neovim client is plain Lua. No F# code runs in Neovim. The user needs Neovim and the .NET
  SDK, and no other runtime.
- The floor is Neovim 0.11, which is the LazyVim floor.
- The client follows the SageFs model: a pure core that tests run with no Neovim, and a thin layer
  on the Neovim API.
- The pure core sits behind a module seam. A core module never reads the `vim` global. Generated
  code can replace the core in the future.
- The pure core holds: the envelope decode and encode, the frame parser, the cursor rule, the
  Status line text rows, the copy payload, the Curl command, the binary test, the hex dump, the
  JSON pretty-printer, the version match rule, and the option validation.
- `vim.json.decode` loses key order. Thus the Lua JSON pretty-printer reformats the body token by
  token, as the F# pretty-printer does.
- A new ADR records the choice of plain Lua and the rejected routes (a Lua shim with Fable JS, an
  F# sidecar, a companion that talks to Neovim, and a typed Lua dialect).

#### A2. Where the client lives, and how it ships

Ticket: [Where does the Neovim client live, and how does it ship?](https://github.com/tw0po1nt/FsHttp.Studio/issues/247)

- The client lives at the root of this repo: `lua/`, `plugin/`, `doc/`, `build.lua`, and
  `lazy.lua` sit beside `package.json`. lazy.nvim loads the repo root, and reads `build.lua` from
  the root only.
- `.vscodeignore` excludes each of these paths. A guardrail checks that the `.vsix` holds no Lua
  file.
- `package.json` holds one version. The VSCode extension, the Neovim client, and the companion
  carry that version.
- Each version gets one tag `v<version>` and one GitHub Release. The release carries the `.vsix`,
  the Companion archive `fshttp-studio-companion-<version>.tar.gz`, and a `.sha256` file for each.
- A fix to one Client ships as a new version of both Clients. The release notes state which Client
  changed.
- With `version = "*"`, lazy.nvim takes the highest semver tag and skips each pre-release tag.
- `beta.yml` attaches the Companion archive and its `.sha256` file to the Beta pre-release. A
  Branch build uploads the Companion archive as a workflow artifact.
- A tester of a Beta in Neovim pins the Beta tag, downloads the Companion archive by hand, and sets
  `companion_path`. The client downloads only from the release that `version.lua` names.
- One F# script, `generate-lua.fsx`, writes `lua/fshttp/version.lua` from `package.json` and
  `lua/fshttp/refusals.lua` from `Refusals.fs`. Both files are committed, because a plugin manager
  runs no build step. CI runs the script with `--check`.
- The project tests lazy.nvim only. The README also gives a `vim.pack` snippet for Neovim 0.12 and
  later. The client has no luarocks package.
- README.md gets a short Neovim section: the requirements, the LazyVim and `vim.pack` snippets, and
  a pointer to `:help fshttp`. `doc/fshttp.txt` holds the commands, the options, and the health
  check.
- A new ADR records the location at the repo root and the single version.

#### A3. How the client gets the companion and the .NET SDK

Ticket: [How does the Neovim client get the companion and the .NET SDK?](https://github.com/tw0po1nt/FsHttp.Studio/issues/246)

- The client downloads the Companion archive at first use, from the release that `version.lua`
  names. It runs `curl` and `tar` through `vim.system`.
- The client verifies the `.sha256` file before it unpacks the archive. The checksum tool is
  `sha256sum` on Linux, `shasum -a 256` on macOS, and `certutil -hashfile <file> SHA256` on
  Windows.
- The client writes the download under a temporary name and then renames it. Thus two Neovim
  instances can download at the same time.
- Each version goes in its own folder under `stdpath("data")`. After a successful download, the
  client deletes the older versions.
- In lazy.nvim, `build.lua` calls the download routine at install and at each update. When
  `companion_path` is set, the routine downloads nothing and reports that `companion_path` is in
  use.
- In other plugin managers, the first `.fsx` buffer starts the download in the background, with an
  INFO notice.
- When no release has the plugin version, no companion starts. An ERROR notice gives two fixes: pin
  the plugin to a release, or set `companion_path`.
- The client downloads only the release that `version.lua` names. It builds no companion from
  source.
- The client uses `dotnet_path`, or `dotnet` on PATH when that option is not set. It reads the SDK
  floor from `Companion.runtimeconfig.json` in the companion folder, and compares it with the
  majors that `dotnet --list-sdks` reports.
- The client installs no .NET SDK. It names the floor and `https://aka.ms/dotnet/download`.
- A test-only environment variable points the download base URL at the test server of the Neovim
  suite.

#### A4. Lifecycle

- Each Neovim instance runs one companion.
- The first `.fsx` buffer runs the start sequence: get the companion (a download or
  `companion_path`), check the SDK floor, and start the companion.
- `VimLeavePre` stops the companion with the stop of spec 0018.
- `:FsHttp restart` runs the full start sequence again, as spec 0018 states.

#### A5. The version check

Ticket: [Does the Neovim client check the version of a companion from companion_path?](https://github.com/tw0po1nt/FsHttp.Studio/issues/252)

- `Companion.fsproj` reads the version from `package.json` at build time and sets
  `InformationalVersion` with no source revision.
- The companion sends that value as `version` in the `ready` envelope. This is the only envelope
  change in v0.3. A Client that does not know the field ignores it, and the VSCode host ignores it.
- The client removes the suffix from the companion version (all text from the first `-`) and
  compares `major.minor.patch` with `version.lua`. A `ready` with no `version` is a mismatch.
- On a mismatch, the companion stays up and each Run goes ahead. One WARN notice names both
  versions. It tells the user to set `companion_path` to a build of the plugin version, or to
  remove the option. `:checkhealth fshttp` shows a WARN line with the same two versions.

#### A6. How a user starts a Run

Tickets: [How does a user start a Run in Neovim?](https://github.com/tw0po1nt/FsHttp.Studio/issues/243),
[How does Run request at cursor work in VSCode?](https://github.com/tw0po1nt/FsHttp.Studio/issues/257)

- `:FsHttp` is one top-level command with subcommands and completion: `run`, `restart`, `open`,
  `yank`, and `status`.
- The plugin exposes `<Plug>(FsHttpRun)` and binds no key. The README shows a lazy.nvim `keys`
  entry for `fsharp` buffers.
- **The cursor rule binds both Clients.** Spec 0017 defines it, and its Golden fixture pins it. A
  block contains the cursor when the cursor line is between the start line and the end line of the
  block range. The column has no effect. The innermost block is the block with the latest start.
- A cursor in a block inside another block targets the inner block, which gets the
  `insideAnotherRequest` refusal.
- Each Run locates the buffer text again, maps the cursor to a range, and sends the run envelope
  with the same text.
- When the cursor is outside every block, `vim.ui.select` opens. It lists each located block in
  source order, refused blocks too. Each item shows the glyph of the lens title, the line number,
  and the first source line. A pick on a refused block shows its refusal, and no Run starts.
- While the companion starts, `:FsHttp run` shows an INFO notice and records the buffer text and
  the cursor. The Run starts when the companion is ready. One Run at most waits, and a new command
  replaces it. If the companion stops, or no SDK is found, the client abandons the wait and shows
  the sentence for that state.

The outcome for each state:

| State | Result |
|---|---|
| A refused target | A WARN notice with the detail from `Refusals`. No Run starts, and the Response buffer stays closed. |
| A Parse failure and no block | A WARN notice: "No requests found: this script has a syntax error." |
| No block and no Parse failure | An INFO notice: "This script has no request. Write an http { } block to run one." |
| Companion stopped | The stopped sentence as a WARN notice. The client maps no cursor and opens no picker. |
| A Refused Run from the run envelope | The Response buffer shows it, as spec 0003 Decision 6 states. |

#### A7. Block marks

- Each located block in an `.fsx` buffer gets a **Block mark**: a virtual line above the first line
  of the block, and a sign. The virtual line shows the lens title from `Refusals`. The sign shows
  the glyph of that title.
- The client locates a script again on `BufEnter`, and on `TextChanged` and `TextChangedI` after a
  pause of about 300 ms. Extmarks move with the edits of the user between two locates.
- A Parse failure with no block gives a virtual line above line 1:
  `⊘ No requests found: this script has a syntax error`, with the `⊘` sign on line 1.
- A script with no block and no Parse failure gets no Block mark.
- A stopped companion keeps each Block mark, with the title `⊘ Cannot run: the companion stopped`.
  A buffer that no locate covered gets no mark.
- A starting companion keeps each remembered Block mark, with the title
  `⊘ Cannot run: the companion is starting`, as spec 0018 states.
- Highlight groups set the colors. No option sets the text or the glyphs.

#### A8. The Response buffer

Tickets: [Where does the Neovim client show a Run's result?](https://github.com/tw0po1nt/FsHttp.Studio/issues/244),
[What does the Response buffer look like with a real response?](https://github.com/tw0po1nt/FsHttp.Studio/issues/250)

- The client keeps one Response buffer at most. It is a scratch buffer with the filetype
  `fshttp_response`.
- A Run opens the Response buffer in a split (by default on the right). If a window already shows
  the buffer, the Run reuses that window. The cursor stays in the script. A closed window opens
  again on the next Run.
- The window sets `wrap`, `linebreak`, and `breakindent`. The buffer keeps the exact bytes of the
  body. The client has no HTML formatter.
- While a Run is in progress, the buffer shows "Running… Ns".

The layout came from the prototype in the tag `archive/prototype/response-buffer` (variant D):

```
winbar:  200 OK  294 ms · 336 ms total  6.2 KB  GET https://api.github.com/repos/fsprojects/FsHttp
▸ Request  3 lines
▸ Response headers  (26)  26 lines
▾ Body  application/json · 6.2 KB
{
  "id": 145749918,
  ...
```

- **The winbar holds the status line.** The order is the status code and reason, the request time,
  the total time, the size, the method, and the URL. When the split is too narrow, the winbar cuts
  the start of the URL (`%<`).
- **The buffer holds three folds:** Request, Response headers, and Body. Request and Response
  headers start closed. Body starts open. A closed fold shows its title and its line count.
- **Body text.** For a JSON, XML, or HTML body, the client parses the body region with the
  tree-sitter parser for that language, for highlights and folds. With no parser, a JSON body shows
  as pretty-printed text, with folds from the JSON structure.
- **Binary body.** The buffer ports the hex view and the reason for a Captured body.
- **Image body.** The client writes the bytes to a temporary file and calls the image placement of
  snacks.nvim, when snacks.nvim is present and the terminal supports it. The line below the Body
  header gives the pixel size, for example `100×100 px`. When no image can show, the same line adds
  the reason, for example `100×100 px  snacks.nvim is not installed`.
- **Hint lines.** For an HTML body or an image body, a virtual line directly below the Body header
  names `:FsHttp open`:
  - HTML: `:FsHttp open  shows the rendered page in the browser, with scripts blocked`
  - Image: `:FsHttp open  shows the image in the system viewer`
- **Compile error.** The buffer shows the Compile error text of the VSCode viewer, with the trailing
  spaces removed from each line. `<CR>` on a `(line,col)` line moves the cursor to that position in
  the script, or opens the loaded file that holds the error. The winbar shows
  `Compile error  <CR> on a (line,col) moves to it in the script`. The script gets no diagnostic,
  no sign, and no quickfix entry. This keeps the rule of spec 0009.
- **Runtime error and Refused Run.** Each one shows as text in the Response buffer, as in VSCode.

#### A9. `:FsHttp open`

- `:FsHttp open` writes the body of the last Run to a temporary file, with the extension from the
  Content-Type, and calls `vim.ui.open`.
- An HTML body opens in the browser. An image body opens in the system viewer.
- For an HTML body, the client first adds a `<meta http-equiv="Content-Security-Policy">` that
  blocks each script and allows styles and images. Thus the scripts of a response never run, as in
  the VSCode renderer.
- The file is static. The command starts no server, and the page does not change on the next Run.
- ADR-0001 gets the update in Part E.

#### A10. Yank

- `:FsHttp yank request|headers|body|curl` puts the payload in `v:register`. Thus `"+` reaches the
  system clipboard.
- The `request`, `headers`, and `body` payloads are the payloads of spec 0013. The `curl` payload
  is the Curl command of spec 0016.
- The Response buffer maps these local keys:

| Key | Action | `<Plug>` map |
|---|---|---|
| `yr` | Yank the Request | `<Plug>(FsHttpYankRequest)` |
| `yh` | Yank the Response headers | `<Plug>(FsHttpYankHeaders)` |
| `yb` | Yank the Body | `<Plug>(FsHttpYankBody)` |
| `yc` | Yank the Curl command | `<Plug>(FsHttpYankCurl)` |
| `<CR>` | Move to a Compile error position | `<Plug>(FsHttpJump)` |
| `g?` | List the active keys | `<Plug>(FsHttpHelp)` |

- A notice confirms each yank, or names the clipboard failure.

#### A11. Companion state

Ticket: [How does the Neovim client report the companion state?](https://github.com/tw0po1nt/FsHttp.Studio/issues/249)

- `require("fshttp").status()` returns the Status line text for the current buffer, or nil. It
  returns nil when the current buffer does not have the `fsharp` filetype.
- The `lazy.lua` spec adds an `optional = true` spec for lualine.nvim. That spec adds `status()` to
  `lualine_x`, with a `cond` function that reads `status_line.lualine` at each draw.
- Each change of the companion state or the script view fires a `User` autocmd and redraws the
  statusline.
- `:FsHttp status` echoes the same text. In a buffer that is not F#, it echoes the companion state
  row only.
- `:checkhealth fshttp` adds a line for the live companion state: OK when ready, and an ERROR with
  the fix for a failed start state or a stopped companion.
- Each row starts with `FsHttp.Studio: `. The rows for the VSCode states and the script views are
  the VSCode rows. The Neovim client adds these rows:

| Start state | Row |
|---|---|
| The download is in progress | `downloading companion…` |
| The download failed | `companion download failed` |
| No release has the plugin version | `no companion for v<version>` |
| `companion_path` has no companion | `companion not found` |

- `Protocol.State` keeps its four cases. VSCode never reaches a download state.
- Only a state that needs a fix raises a notice:

| State change | Level | Text |
|---|---|---|
| The download starts | INFO | The download is in progress. |
| The download failed | ERROR | The cause (curl, tar, or the checksum), and "Run :FsHttp restart to try again." |
| No release has the plugin version | ERROR | The two fixes of A3. |
| The .NET SDK is not found | WARN | The SDK floor and the download URL. |
| `companion_path` has no companion | ERROR | The path, the missing file, the two fixes, and `:FsHttp restart`. |
| A version mismatch | WARN | The two versions and the two fixes of A5. |

- Starting, ready, and a companion death raise no notice.

#### A12. Settings

Ticket: [Which settings does the Neovim client have, and how does a user set them?](https://github.com/tw0po1nt/FsHttp.Studio/issues/254)

- `require("fshttp").setup(opts)` is the one entry point. lazy.nvim calls it from `opts`. With no
  `setup()` call, the client uses the defaults. The client reads no `vim.g` variable.

```lua
{
  dotnet_path = nil,            -- nil: dotnet on PATH
  companion_path = nil,         -- nil: download the Companion archive
  request_timeout_ms = 30000,   -- 0: no bound
  block_mark = { virtual_line = true, sign = true },
  response_buffer = {
    split = "right",            -- right | left | below | above
    images = true,
    keys = true,
  },
  status_line = { lualine = true },
}
```

- `request_timeout_ms` mirrors `fshttpStudio.requestTimeoutMs`.
- `response_buffer.images = false` gives the fallback line with the option as the reason:
  `100×100 px  images are off (response_buffer.images)`.
- `response_buffer.keys = false` turns off the six local keys of A10. Each `<Plug>` map stays.
- `vim.fs.normalize` expands each path option.
- `setup()` checks the type and the value set of each key. A bad value gives one ERROR notice that
  names the key, the value, and the accepted values, and the key keeps its default. An unknown key
  gives a WARN notice. Each other key applies.
- `setup()` checks only the type of the two path options. The start sequence checks the files.
- Each `setup()` call replaces the configuration with the defaults plus the new `opts`.

| Option | Takes effect |
|---|---|
| `request_timeout_ms` | On the next Run |
| `block_mark.*` | At once, and the client paints each Block mark again |
| `response_buffer.*` | On the next Run |
| `status_line.lualine` | On the next draw |
| `dotnet_path`, `companion_path` | On the next start sequence |

- A change to a path option while a companion runs gives an INFO notice that names
  `:FsHttp restart`.
- `:checkhealth fshttp` gets an Options section: OK for the `setup()` call or the defaults, one
  INFO line for each value that differs from its default, one ERROR line for each bad value, and
  one WARN line for each unknown key.

#### A13. Health check

`:checkhealth fshttp` reports:

- **Required items** as an ERROR with the fix when missing: `dotnet` with an SDK at the floor, the
  companion (its version and its path), `curl`, `tar`, and a checksum tool.
- **Optional items** as a WARN that names what degrades: snacks.nvim with a terminal that can show
  images, and the `json`, `xml`, and `html` tree-sitter parsers. With `response_buffer.images =
  false`, the image item is OK "turned off". A missing optional item stops no Run.
- **The live companion state** (A11), **the version check** (A5), and **the options** (A12).

### Part C: one source of truth for both Clients

#### C1. Text from `Refusals.fs`

- `generate-lua.fsx` writes each user-facing sentence of `Refusals.fs` into `refusals.lua`.
- `staleBlockIndex` names the lens in VSCode. `Refusals.fs` holds a variant for each Client. The
  Neovim variant ends "To run this request, run :FsHttp run again."
- `companionStopped` becomes the template of spec 0018. The generator fills in `:FsHttp restart`.
- `Refusals.fs` gets the three new entries of spec 0018: the starting lens title, the restart
  sentence, and the overlap notice.
- Each other sentence that both Clients show lives in `Refusals.fs` too: the no-request sentences of
  A6 and of spec 0017.

#### C2. Golden fixtures

- F# tests write each Golden fixture to one shared folder. The Lua core suite reads the same files
  and must match each one byte for byte.
- An F# test compares its output with the committed fixture and fails on a difference. An update
  flag rewrites the fixtures.
- The Golden fixtures cover:
  - each envelope tag and each Run outcome, with a fixed `version` in `ready`
  - the copy payload of spec 0013
  - the Curl command, one fixture for each rule of spec 0016
  - the binary test and the hex dump
  - the JSON pretty-printer
  - `Protocol.statusText` for each `State` and `ScriptView` case, with the counts 0, 1, 2, and -1
  - the cursor rule of spec 0017
  - the SDK floor rule of A3: the floor from `Companion.runtimeconfig.json`, and the test of the
    `dotnet --list-sdks` output against the floor

### Part D: the Release gate and CI

Ticket: [What gates a release of the Neovim client?](https://github.com/tw0po1nt/FsHttp.Studio/issues/248)

- The Release gate is the UI suite, the Neovim suite, and the Lua core suite.
- Both Neovim suites use mini.test, started through lazy.minit. lazy.minit loads the client as a
  lazy.nvim spec, so each run also loads the tested install route.
- The Neovim suite drives a child Neovim over RPC against a real companion, the test HTTP server,
  and the Sidecar of the UI suite. A hung companion stops the child, and the runner continues.
- The Lua core suite loads each core module in an environment with no `vim` global.
- `rhysd/action-setup-vim` installs Neovim. The Neovim suites run on four legs: Linux with Neovim
  0.11, and Linux, macOS, and Windows with the pinned stable version.
- A file pins the stable version. The weekly pin workflow opens a pull request when a newer Neovim
  ships.
- The Neovim suite has no retry. A flaky Check is a defect to fix.
- The Neovim suite asserts Budgets for Harness setup, for each Check, and for the suite. The build
  sets the values from measured runs on each operating system.
- `ci.yml` runs the Lua core suite once on each pull request. A Beta runs the Lua core suite.
- A new workflow, `nvim-tests.yml`, runs both suites on the four legs. It runs on each push to
  `main`, and on a pull request that changes the Lua client, the Neovim tests, the Golden fixtures,
  `src/**`, `package.json`, or the workflow files.
- A composite action, `run-nvim-suite`, holds the suite steps for `nvim-tests.yml` and
  `release.yml`.
- `ci.yml` and the build job of `release.yml` run these guardrails:
  - `generate-lua.fsx --check`
  - a check that the `.vsix` holds no Lua file
  - StyLua `--check`, with a `stylua.toml` at the repo root
  - `lua-language-server --check` over the LuaCATS annotations, with the Neovim 0.11 API types
  - `check-banned-patterns.sh`, extended to read Lua `--` comments, Lua string literals, and
    `doc/*.txt`
- `release.yml` splits into three jobs:
  1. A **build job** runs the guardrails, packages the `.vsix` and the Companion archive, writes a
     `.sha256` file for each, and uploads the four files as workflow artifacts.
  2. A **UI suite job** and the **four Neovim legs** download those artifacts and drive them.
  3. A **publish job** needs each gate to be green, and attaches the same four files to the draft
     Release.
- One `force` input skips each suite, and the run logs the skip. The guardrails always run.

### Part E: ADRs and the glossary

- **ADR-0001 gets this update** (Neovim client):

  > **Update (Neovim client):** The Neovim client shows each Run's result in the Response buffer,
  > inside the editor. A terminal cannot draw a rendered HTML page, and some terminals cannot draw
  > an image. For these bodies only, `:FsHttp open` writes the body to a static file and opens it
  > with the system handler. The user starts this action on demand. The Response buffer stays the
  > result surface for each Run.

- **ADR-0009 gets this update** (Neovim client):

  > **Update (Neovim client):** Each release ships the VSCode extension and the Neovim client. The
  > Release gate is the UI suite, the Neovim suite, and the Lua core suite. Each suite drives the
  > artifacts that the release attaches. The one `force` input skips each suite. A fix to one Client
  > waits on the gates of both Clients.

- **ADR-0012** records the plain Lua client (A1).
- **ADR-0013** records the client at the repo root with one version (A2).
- **`GLOSSARY.md`** gets these terms: Client, Status line text, Block mark, Response buffer,
  Companion archive, Restart, Curl command, Golden fixture, Release gate, Neovim suite, Lua core
  suite, and UI suite. Active document, Check, Harness, Harness setup, Proven-live, Budget, Sidecar,
  Dead port, Beta, and Branch build become editor-neutral. The ticket
  [Which glossary terms become editor-neutral, and which get a Neovim sibling?](https://github.com/tw0po1nt/FsHttp.Studio/issues/253)
  holds the detail.

## Testing Decisions

### What makes a good test

- A test drives the behavior that a user or a Client sees. It asserts the text on screen, the
  register, the clipboard, the bytes that a server receives, or the process that exists.
- A test does not assert the inner state of a module.
- A pure rule that both Clients hold gets one Golden fixture. The F# side writes it, and the Lua
  side reads it. Thus one fixture proves both Clients.
- A Check in the Neovim suite asserts the layout through the child Neovim: the buffer lines, the
  closed folds, the winbar text, and the virtual lines.

### The seams

1. **The UI suite** (exists). It drives a real VSCode through ExTester. This spec adds no Check to
   it, and it stays in the Release gate.
2. **The Neovim suite** (new). It drives a child Neovim over RPC, with the Companion archive that
   the job built.
3. **The Golden fixtures** (new). They are the one seam between the two Clients.
4. **The Lua core suite** (new). It runs the specs of the pure core with no `vim` global.
5. **host.Tests, renderer.Tests, and companion.Tests** (exist).

### Neovim suite: ported Checks

| UI suite Check | Neovim suite Check |
|---|---|
| Harness self-check, Proven-live | The child Neovim answers, the Sidecar parses, the fixture is open, and a companion exists. |
| Core path: one Run, then the next | `:FsHttp run` fills the Response buffer. The next Run replaces it in the same window. |
| A 404 as a response, a Dead port as a Runtime error | The Response buffer shows the same two outcomes. |
| Loop refusal | The Block mark shows the refusal. A Run gives a WARN notice and opens no Response buffer. |
| Cross-block Refused Run | The Response buffer shows the refused text. The script gets no diagnostic. |
| Compile error names its source | The Response buffer shows the same text, `<CR>` moves to the range, and the script gets no diagnostic. |
| Companion death | The Response buffer shows the stopped text, each Block mark shows the stopped title, and `:FsHttp restart` recovers a Run. |
| Copy buttons | `:FsHttp yank` and `yr`, `yh`, `yb` put the spec 0013 payload in the register. |
| Request section shows what a POST sent | The Request fold shows it. |
| No-requests lens on syntax errors | The Block marks match the same four cases. |
| Clean scripts report one, many, and zero requests | `status()` returns the same three rows. |
| An `.fs` module | `status()` returns `not an .fsx script`. |
| Syntax-error scripts | `status()` returns the same two rows. |
| The item hides outside F# | `status()` returns nil in a buffer that is not F#, and the row again on the script. |
| A document switch | A buffer switch returns `looking for requests…` until the locate response arrives. |
| The count of the active script | The same, for the current buffer. |
| Companion death and the status | `status()` returns nil while the Response buffer has focus, and `companion stopped` on the script. |

### Neovim suite: Checks for Neovim surfaces only

- A clean first start downloads the Companion archive, verifies it, unpacks it, checks the SDK
  floor, and starts the companion.
- A bad checksum starts no companion, gives the ERROR notice, and shows `companion download failed`.
- The download in progress shows `downloading companion…`.
- No release: the test server returns 404, and the client shows the no-release row and the ERROR
  notice.
- A `dotnet_path` that names a missing file gives `.NET SDK not found` and the WARN notice.
- A companion of a different version (`version.lua` set to `9.9.9`) gives the WARN notice, the WARN
  health line, and a successful Run.
- `:FsHttp open` with a stubbed `vim.ui.open` writes a file with the right extension and the CSP
  meta.
- An image body without snacks.nvim shows the fallback line. With a stubbed snacks.nvim, the client
  calls the image placement with the temporary file.
- A JSON body gets tree-sitter folds with the parser, and structure folds without it.
- The lualine entry: `nvim_eval_statusline` shows the Status line text.
- `:FsHttp status` echoes the text in a script, and the state row in a buffer that is not F#.
- `:checkhealth fshttp` reports no ERROR for a valid setup, an ERROR for a missing `dotnet_path`,
  a WARN with no snacks.nvim, the live state line, and a value that differs from its default.
- A bad option value gives the ERROR notice, and its key keeps the default.
- A `companion_path` with no `Companion.dll` gives `companion not found` and the ERROR notice.
- `response_buffer.images = false` gives the fallback line with the option as the reason.
- Each of the four Response buffer cases (JSON, image, HTML, Compile error) has one reference
  screenshot. Only the Linux leg with the pinned stable version compares the screenshots.

### Lua core suite

- Reads each Golden fixture and matches it byte for byte.
- Specs for the rows that only Neovim has, the version match rule (`0.3.0-beta.2` matches `0.3.0`,
  `0.3.1` and a missing version give a mismatch), and the option validation.

### F# suites

- **host.Tests:** the Golden fixtures for `Protocol.statusText` and the envelope.
- **renderer.Tests:** the Golden fixtures for the copy payload, the binary test, the hex dump, and
  the JSON pretty-printer.
- **A check of `generate-lua.fsx`:** CI runs the script with `--check` against the committed
  `version.lua` and `refusals.lua`.
- **companion.Tests:** `hello` gets a `ready` that carries the `package.json` version.

### Prior art

- The UI suite Checks in `tests/ui.Tests`, in particular the companion death Check and the copy
  button Checks.
- The renderer core tests of spec 0013 for `copyText`.
- The Harness, Sidecar, and Budget pattern of specs 0005 to 0011.
- The SageFs Neovim plugin for the pure core and the thin API layer.

### Gaps for `release-gate.md`

The build adds these entries to the honest gaps when the Neovim suite ships:

- No Check compares the pixels of an image.
- The suites test lazy.nvim only. The `vim.pack` route gets no check.
- No Check downloads from a real GitHub Release URL.
- A Beta runs the Lua core suite only.
- A user with no lualine sees no companion state until the user runs a command.

## Out of Scope

- **Other editors.** Emacs, Helix, Zed, and Rider.
- **A Fable Lua target.** The spike compiles none of the pure host files. It is a separate, long
  effort.
- **The session model.** Session reuse, request chaining, and cancel change the Run and Setup terms
  and ADR-0007. They need a map of their own.
- **Other candidate features:** save the body to a file, a copy button on the error render, the
  request tree, a first-run walkthrough, an inline card, and `.fs` files.
- **A diagnostic for a Compile error** in either Client.
- **A luarocks package**, and tests for plugin managers other than lazy.nvim.
- **An HTML formatter** and HTML rendering in the terminal.
- **A size option** for the Response buffer window.

## Further Notes

### Research and the prototype

- [How can a Neovim plugin show images and rich text?](https://github.com/tw0po1nt/FsHttp.Studio/issues/240)
- [Which routes can a Neovim plugin be written in, F# included?](https://github.com/tw0po1nt/FsHttp.Studio/issues/241)
- [How do Neovim plugin managers install a plugin that needs a .NET binary?](https://github.com/tw0po1nt/FsHttp.Studio/issues/242)
- [What does it take to upstream a Lua target to Fable?](https://github.com/tw0po1nt/FsHttp.Studio/issues/251)
- The Response buffer prototype and its screenshots are in the tag `archive/prototype/response-buffer`.

### Sequencing

1. The shared source of truth first: `generate-lua.fsx`, the `Refusals.fs` changes, the `version`
   field in `ready`, and the Golden fixtures.
2. The Lua pure core and the Lua core suite.
3. The start sequence, the Block marks, and `:FsHttp run`, with the Neovim suite Harness.
4. The Response buffer, `:FsHttp open`, and the yanks.
5. The companion state, the settings, and the health check.
6. The Neovim halves of specs 0016, 0017, and 0018.
7. The release workflow split, the Companion archive, and the README and vimdoc.

### The other v0.3 specs

- [0016 Copy as curl](0016-copy-as-curl.md)
- [0017 Run request at cursor](0017-run-request-at-cursor.md), which also defines the cursor rule
  of A6
- [0018 Restart companion](0018-restart-companion.md), which also defines the stop of A4 and the
  stopped template of C1

Each of these specs records its own changes to earlier specs.
