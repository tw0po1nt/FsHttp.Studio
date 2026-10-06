# Research: How a Neovim plugin can show images and rich text

Issue: [#240](https://github.com/tw0po1nt/FsHttp.Studio/issues/240), "How can a Neovim plugin show
images and rich text?" The map is [#239](https://github.com/tw0po1nt/FsHttp.Studio/issues/239).

> **Scope.** This document collects facts. The HITL ticket
> [#244](https://github.com/tw0po1nt/FsHttp.Studio/issues/244) makes the viewer decision.

The sources were read on 2026-10-06. Each claim cites a numbered source, and the list at the end
gives each URL. The latest stable Neovim release at that date is v0.12.5, published 2026-08-23 [27].

## What the VSCode client shows today

The renderer core dispatches a response body on its `Content-Type` [42]:

- An `image/*` body goes to an `<img>` element with a `data:` URI. The browser engine of the
  webview decodes the format, so the renderer core converts no image.
- A JSON body goes to a collapsible, highlighted tree.
- An HTML body goes to a sandboxed iframe, which shows the rendered page.
- XML, plain text, and unknown types go to a monospace fallback, with a hex view for binary data.

A Neovim client with full parity must give an equivalent for each of these four paths.

## 1. Terminal graphics protocols

Three protocols put pixels in a terminal cell grid.

### 1.1 The kitty graphics protocol

- The protocol sends pixel data in an APC escape sequence (`ESC _G ... ESC \`) [1].
- The terminal accepts three formats: 24-bit RGB, 32-bit RGBA, and PNG. The data can be
  zlib-compressed [1].
- The client can send the data in the escape code itself, as a path to a file, as a path to a
  temporary file, or as a shared memory object [1]. Only the direct method works over SSH, because
  the terminal cannot read a file on the remote host. image.nvim and snacks.nvim switch to the
  direct method when they detect SSH [16][18].
- **Unicode placeholders** came in kitty 0.28.0. The client sends the image with `U=1` to make a
  virtual placement. Then it prints the character `U+10EEEE` in each cell where the image must
  show [1].
- A placeholder encodes the image ID in its foreground color, and diacritics encode the row and
  column [1].
- The kitty documentation states the reason for placeholders: they let an image live "inside any
  host application that supports Unicode, foreground colors (tmux, vim, weechat, etc.)". The host
  moves the placeholder text as it redraws, and the image moves with it [1].

Other terminals implement the protocol too. The kitty documentation lists Ghostty, Konsole,
WezTerm, iTerm2, Warp, st (with a patch), wayst, xterm.js, AbsoluteTelnet/SSH, and Mobile SSH [1].

### 1.2 Sixel

Sixel is a bitmap graphics format that many terminals decode [10]. A Neovim plugin must encode the
image to sixel itself. image.nvim uses ImageMagick for this step [15].

### 1.3 The iTerm2 inline images protocol

- The sequence is `ESC ] 1337 ; File = [arguments] : <base64 data> ^G` [2].
- The arguments are `name`, `size`, `width`, `height`, `preserveAspectRatio`, and `inline` [2].
- iTerm2 displays "any image format that macOS supports", which includes PNG, GIF, and PDF [2].
- iTerm2 3.5 added a multipart form (`MultipartFile`, `FilePart`, `FileEnd`) for large files. The
  iTerm2 page gives part-size limits for tmux [2].
- WezTerm implements this protocol [6].

### 1.4 Terminal support

| Terminal | kitty graphics | Unicode placeholders | Sixel | iTerm2 inline images |
| --- | --- | --- | --- | --- |
| kitty | Yes [1] | Yes, from 0.28.0 [1] | No [10] | No data |
| Ghostty | Yes [1][5] | Yes [19][28] | No [10] | No data |
| WezTerm | Yes, with limits [6][15][18] | No [19][28] | Yes, from 20200620 [6] | Yes [6] |
| iTerm2 | Yes [1][2] | Yes [4] | Yes, from 3.3.0 [3] | Yes [2] |
| Konsole | Yes [1] | No data | Yes, from 22.04 [10] | No data |
| foot | No data | No data | Yes, from 1.2.0 [10] | No data |
| xterm | No data | No data | Yes, on by default from patch #359 [9] | No data |
| Windows Terminal | No data | No data | Yes, from 1.22.10352.0 [8] | No data |
| VSCode terminal | No mention [7] | No mention [7] | Yes, opt-in [7] | Yes, opt-in [7] |
| Alacritty | No data | No data | No [10] | No data |

Notes on the table:

- "No data" means that no source read for this note states the support.
- The VSCode integrated terminal shows images only when `terminal.integrated.enableImages` is on.
  The setting is off by default [7].
- image.nvim states that WezTerm "implements it, but the performance is bad and it's not fully
  compliant" [15]. snacks.nvim states that WezTerm cannot show an inline image [18].
- iTerm2 3.7.3 (2026-09-22) fixed a bug in its kitty placeholder cells. This proves that iTerm2
  draws placeholder cells [4].
- Source [10] is a community tracker. It is the only source here for the kitty, Ghostty, Konsole,
  foot, and Alacritty sixel cells.

### 1.5 Behavior inside tmux

- tmux passes an escape sequence through to the outer terminal only in a wrapper:
  `ESC Ptmux; ... ESC \`. The pane option `allow-passthrough` controls this [11].
- `allow-passthrough on` permits passthrough only for a visible pane. The value `all` permits it
  for a pane that is not visible too [11].
- tmux 3.3 added the option, with a default of off. tmux 3.4 added the value `all` [12].
- tmux does not decode the kitty protocol or the iTerm2 protocol. A plugin must wrap each sequence
  in the passthrough form. snacks.nvim and kulala.nvim double each `ESC` byte inside the wrapper,
  and they run `tmux set -p allow-passthrough all` [19][28].
- A kitty image that a plugin places at a screen position stays at that position in the outer
  terminal. tmux does not move it when the pane scrolls or changes. Unicode placeholders solve
  this, because tmux redraws them as text [1].
- image.nvim asks for tmux 3.3 or later, `allow-passthrough on`, `visual-activity off`, and
  `focus-events on` [15]. Its option `tmux_show_only_in_active_window` hides an image when its tmux
  window is not active [15].
- tmux 3.4 added sixel support when the build uses `--enable-sixel` [12][13]. The option is off by
  default in the configure script [13]. The Homebrew formula turns it on [14]. tmux 3.7 raised the
  limit to 20 sixel images [12].
- The format variable `sixel_support` reports whether the tmux server has sixel support [11].
- snacks.nvim marks Zellij as not supported, "since they don't have any support for passthrough"
  [18].

## 2. Neovim plugins and APIs that show images

### 2.1 image.nvim (3rd/image.nvim)

- **Backends.** `kitty` (the default and the recommended one), `ueberzug`, and `sixel` [15]. The
  `ueberzug` backend needs the external ueberzugpp program [15].
- **kitty mode.** The option `kitty_method` is `"normal"` by default. The other value is
  `"unicode-placeholders"` [16][17]. The normal method transmits by file path, and it switches to
  direct transmission over SSH [16].
- **Dependencies.** ImageMagick is required, through its CLI (`magick_cli`, the default) or through
  the `magick` Lua rock (`magick_rock`). cURL is required for a remote image [15].
- **Formats.** The default file hijack list holds PNG, JPG, JPEG, GIF, WebP, and AVIF [15].
- **Terminals.** Kitty 0.28 or later is recommended. Ghostty support is marked "SUBJECT TO
  CHANGE" [15].
- **API for other plugins** [15]:

```lua
local api = require("image")
local image = api.from_file("/path/to/image.png", {
  id = "my_image_id",          -- optional
  window = 1000,               -- binds the image to a window and its bounds
  buffer = 1000,               -- binds the image to a buffer
  with_virtual_padding = true, -- pads lines with extmarks
  inline = true,               -- the image follows an extmark
  x = 1, y = 1, width = 10, height = 10,
})
api.from_url("https://...", { --[[ same options ]] }, function(img) end)
image:render()          -- or image:render(geometry)
image:clear()
image:move(x, y)
image:brightness(value) -- also saturation and hue
```

`from_file` takes a path, so a client with image bytes in memory must write them to a file first.

### 2.2 snacks.nvim image module (folke/snacks.nvim)

- **Protocol.** The kitty graphics protocol only [18]. `Snacks.image.supports_terminal()` checks
  for it [18].
- **Terminals.** kitty, Ghostty, and WezTerm, with tmux passthrough [18]. The source table marks
  placeholders as on for kitty and Ghostty, and off for WezTerm [19].
- **Two modes.** Inline mode draws a grid of `U+10EEEE` placeholders in the buffer [20]. On a
  terminal without placeholders, the module falls back to a floating window [18][20].
- **Formats.** PDF, PNG, JPG, JPEG, GIF, BMP, WebP, TIFF, HEIC, AVIF, and five video formats. Every
  format except PNG goes through ImageMagick [18].
- **Neovim version.** snacks.nvim needs Neovim 0.9.4 or later [22].
- **API for other plugins.** The documented functions are `Snacks.image.hover()`,
  `Snacks.image.supports(file)`, `Snacks.image.supports_file(file)`,
  `Snacks.image.supports_terminal()`, and `Snacks.image.langs()` [18]. The module also exposes
  `Snacks.image.placement` and `Snacks.image.buf` [18]. Their sources give these signatures [20][21]:

```lua
-- Places the image file `src` in buffer `buf`.
Snacks.image.placement.new(buf, src, opts)
-- opts: pos, range, conceal, inline, width, min_width, max_width,
--       height, min_height, max_height, on_update, on_update_pre,
--       type, auto_resize  (snacks.image.Opts)

-- Turns `buf` into an image buffer for opts.src, or for the buffer name.
Snacks.image.buf.attach(buf, opts)
```

`placement.new` asserts that `src` is a string, and the module reads it as a file [20].

### 2.3 vim.ui.img in Neovim core (nightly only)

- Neovim HEAD adds `vim.ui.img`. The news file states that it "can display images" [24]. Version
  v0.12.5 does not have it [27].
- The help marks the module as experimental, and it states that the semantics are not final. The
  module supports PNG images through the kitty graphics protocol [23].
- The API is `vim.ui.img.set(data_or_id, opts)`, `vim.ui.img.get(id)`, and `vim.ui.img.del(id)`.
  `set` takes the image bytes as a string, so no file is necessary [23].
- `opts` holds `row`, `col`, `width`, `height`, and `zindex` in screen cells [23]. These positions
  are grid positions, with no binding to a buffer line.
- A plugin can replace `vim.ui.img` with its own backend that provides `set`, `get`, and `del` [23].
- The kitty backend sends each sequence with `nvim_ui_send()`. It uses direct transmission and a
  placement at a screen position. Its source contains no tmux passthrough wrapper [25].
- `nvim_ui_send()` arrived in Neovim 0.12. It "writes arbitrary data to a UI's stdout" when Neovim
  runs in the TUI [26].

### 2.4 Prior art: kulala.nvim, an HTTP client for Neovim

- kulala.nvim shows an image response body inline, through the kitty protocol or the iTerm2
  protocol. The option `ui.show_images` is on by default [29].
- It ships its own image module, with a terminal table for kitty, Ghostty, WezTerm, iTerm2, and
  tmux. It does not call image.nvim or snacks.nvim [28].
- It writes the iTerm2 form as OSC 1337 for iTerm2 and WezTerm [28].

## 3. Highlighted, folded text in a Neovim buffer

### 3.1 Tree-sitter highlights and folds

- `vim.treesitter.start(buf, lang)` turns on tree-sitter highlights for a buffer [30].
- `vim.treesitter.foldexpr()` gives fold levels. A window uses it with
  `vim.wo.foldexpr = 'v:lua.vim.treesitter.foldexpr()'`. It exists from Neovim 0.9.0 [30].
- Neovim 0.12.5 bundles parsers for C, Lua, Markdown, Vimscript, Vimdoc, and tree-sitter queries
  only [30]. A JSON, XML, or HTML parser must come from somewhere else.
- nvim-treesitter installs extra parsers. Its `json`, `xml`, and `html` parsers each ship
  highlight and fold queries [32].
- The current nvim-treesitter is "a full, incompatible, rewrite". It needs Neovim 0.12.0 or later,
  `tree-sitter-cli` 0.26.1 or later, and a C compiler [31].

### 3.2 Regex syntax and syntax folds

Neovim ships regex syntax files for JSON, XML, and HTML. They work with no extra install.

- `syntax/json.vim` marks `{ }` and `[ ]` regions with `fold`. `foldmethod=syntax` folds them [33].
- XML folds between tags after `let g:xml_syntax_folding = 1` and `set foldmethod=syntax` [34].
- HTML folds between tags after `let g:html_syntax_folding = 1` and `set foldmethod=syntax` [34].
- The help warns that syntax folding can make syntax highlighting slow, mostly for large files [34].

### 3.3 Formatting the body

- Neovim 0.12 added an `indent` option and a `sort_keys` option to `vim.json.encode()` [26].
- rest.nvim formats a response body with the native `gq` command [35]. `gq` uses the formatter
  that the buffer's filetype configures.
- kulala.nvim pretty-prints in its own core program, with `indent`, `expand_tabs`, and `sort_keys`
  options [29].

### 3.4 HTML in a buffer

A buffer shows text in cells. A Neovim buffer can show HTML source with highlights and folds. It
cannot show the rendered page that the VSCode iframe shows [42]. A rendered page needs a browser
engine, which section 4 covers.

## 4. Live browser pages from Neovim

### 4.1 markdown-preview.nvim

- The plugin runs a Node.js process. That process attaches to Neovim as an RPC client over its
  stdin and stdout [38].
- The process starts an HTTP server with a socket.io WebSocket server [37].
- The server listens on `127.0.0.1` by default. `g:mkdp_open_to_the_world = 1` makes it listen on
  `0.0.0.0` [36][37].
- The port comes from `g:mkdp_port`, or else from 8080 plus a number from the clock [37].
- The server pushes new content to the page with a `refresh_content` event, and closes the page
  with `close_page` [37].
- The process opens the URL in the system browser. `g:mkdp_browser` names a browser, and
  `g:mkdp_browserfunc` names a Vim function that receives the URL [36].
- `g:mkdp_open_ip` sets the host in the URL. The README gives it for "remote Vim and preview on
  local browser" [36].
- The build step needs Node.js, with yarn or npm, or a prebuilt bundle from
  `mkdp#util#install()` [36].
- The last push to the repository was 2024-07-23 [41].

### 4.2 peek.nvim

- peek.nvim runs on Deno [39].
- By default it opens a webview window through `webview_deno`. The option `app = 'browser'`
  opens the default browser, and `app` can name a browser with arguments [39].
- The last push to the repository was 2024-08-20 [41].

### 4.3 Opening a URL from core Neovim

`vim.ui.open(path)` opens a path or a URL with the system handler: `open` on macOS,
`explorer.exe` on Windows, and `xdg-open` on Linux [40].

### 4.4 The pattern

The common pattern has four parts:

1. A helper process outside Neovim serves an HTML page on a local port.
2. Neovim sends content to the helper. markdown-preview.nvim uses msgpack-RPC on stdio [38].
3. The helper pushes each update to the open page over a WebSocket [37].
4. Neovim, or the helper, opens the URL in a browser or a webview window [36][39][40].

## 5. Facts that the viewer decision depends on

These facts bear on #244. This note does not choose a viewer.

1. **The kitty protocol has the widest reach in Neovim.** image.nvim, snacks.nvim, kulala.nvim,
   and `vim.ui.img` all use it [15][18][23][28]. kitty, Ghostty, iTerm2, WezTerm, and Konsole
   implement it [1].
2. **No single protocol covers every terminal.** WezTerm has only partial kitty support [15][18].
   Windows Terminal, foot, and xterm have sixel in the sources read here [8][9][10]. Alacritty and
   Apple Terminal have no sixel [10], and no source read here lists them for the other two
   protocols.
3. **tmux needs `allow-passthrough`** for the kitty and iTerm2 protocols [11][15][19]. Unicode
   placeholders let tmux move an image with its text, and kitty, Ghostty, and iTerm2 draw them
   [1][4][19]. image.nvim states that all its backends render inside tmux [15].
4. **The kitty protocol decodes PNG only.** A JPEG, GIF, WebP, or SVG body needs conversion to PNG
   first [1]. image.nvim and snacks.nvim do this with ImageMagick, an external install [15][18].
   The VSCode client needs no conversion, because the webview decodes each format [42].
5. **Both image plugins take a file path.** The client must write the body bytes to a file before
   it calls image.nvim or snacks.nvim [15][20]. `vim.ui.img` takes bytes, but it is experimental
   and absent from stable Neovim [23][27].
6. **A plugin dependency is optional.** kulala.nvim shows that an HTTP client can ship its own
   kitty and iTerm2 code with no image plugin [28].
7. **JSON, XML, and HTML text work in every terminal.** Regex syntax files give highlights and
   folds with no install [33][34]. Tree-sitter folds need a parser from nvim-treesitter or another
   source [30][32].
8. **A rendered HTML page needs a browser engine.** No terminal method shows the rendered page that
   the VSCode iframe shows [42]. The preview plugins use a local server and a browser or webview
   window [36][39].
9. **A browser page is outside the editor.**
   [ADR-0001](../adr/0001-in-editor-webview-renderer.md) rejected a browser-file approach because
   "the in-editor experience is the point of the product". A browser-page viewer for Neovim would
   contradict ADR-0001 as it is written. #244 must record a resolution if it picks that surface.
10. **A browser page can reuse the renderer core.** The renderer core and the webview are
    F#/Fable code that produce DOM [42]. A browser page would run the same code. This is an
    inference from the repo layout. No prototype has tested it.

## Sources

1. kitty graphics protocol: https://sw.kovidgoyal.net/kitty/graphics-protocol/
2. iTerm2 inline images protocol: https://iterm2.com/documentation-images.html
3. iTerm2 3.3.0 change log: https://iterm2.com/downloads/stable/iTerm2-3_3_0.changelog
4. iTerm2 3.7.3 change log: https://iterm2.com/appcasts/full_changes.txt
5. Ghostty features: https://ghostty.org/docs/features
6. WezTerm features: https://wezterm.org/features.html
7. VSCode terminal advanced docs: https://code.visualstudio.com/docs/terminal/advanced
8. Windows Terminal v1.22.10352.0 release: https://github.com/microsoft/terminal/releases/tag/v1.22.10352.0
9. xterm change log: https://invisible-island.net/xterm/xterm.log.html
10. Are We Sixel Yet (community tracker): https://www.arewesixelyet.com/
11. tmux manual page: https://github.com/tmux/tmux/blob/master/tmux.1
12. tmux CHANGES: https://github.com/tmux/tmux/blob/master/CHANGES
13. tmux configure script: https://github.com/tmux/tmux/blob/master/configure.ac
14. Homebrew tmux formula: https://github.com/Homebrew/homebrew-core/blob/main/Formula/t/tmux.rb
15. image.nvim README: https://github.com/3rd/image.nvim/blob/master/README.md
16. image.nvim kitty backend: https://github.com/3rd/image.nvim/blob/master/lua/image/backends/kitty/init.lua
17. image.nvim defaults: https://github.com/3rd/image.nvim/blob/master/lua/image/init.lua
18. snacks.nvim image docs: https://github.com/folke/snacks.nvim/blob/main/docs/image.md
19. snacks.nvim terminal table: https://github.com/folke/snacks.nvim/blob/main/lua/snacks/image/terminal.lua
20. snacks.nvim placement: https://github.com/folke/snacks.nvim/blob/main/lua/snacks/image/placement.lua
21. snacks.nvim image buffer: https://github.com/folke/snacks.nvim/blob/main/lua/snacks/image/buf.lua
22. snacks.nvim README: https://github.com/folke/snacks.nvim/blob/main/README.md
23. Neovim Lua reference, `vim.ui.img` (HEAD): https://github.com/neovim/neovim/blob/master/runtime/doc/lua.txt
24. Neovim news (HEAD): https://github.com/neovim/neovim/blob/master/runtime/doc/news.txt
25. `vim.ui.img` kitty backend: https://github.com/neovim/neovim/blob/master/runtime/lua/vim/ui/img/_kitty.lua
26. Neovim 0.12 news: https://github.com/neovim/neovim/blob/master/runtime/doc/news-0.12.txt
27. Neovim releases: https://github.com/neovim/neovim/releases
28. kulala.nvim image terminal table: https://github.com/mistweaverco/kulala.nvim/blob/main/lua/kulala/ui/image/terminal.lua
29. kulala.nvim defaults: https://github.com/mistweaverco/kulala.nvim/blob/main/lua/kulala/config/defaults.lua
30. Neovim 0.12.5 tree-sitter reference: https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/treesitter.txt
31. nvim-treesitter README: https://github.com/nvim-treesitter/nvim-treesitter/blob/main/README.md
32. nvim-treesitter supported languages: https://github.com/nvim-treesitter/nvim-treesitter/blob/main/SUPPORTED_LANGUAGES.md
33. Neovim 0.12.5 JSON syntax file: https://github.com/neovim/neovim/blob/v0.12.5/runtime/syntax/json.vim
34. Neovim 0.12.5 syntax reference (`xml-folding`, `html-folding`): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/syntax.txt
35. rest.nvim README: https://github.com/rest-nvim/rest.nvim/blob/main/README.md
36. markdown-preview.nvim README: https://github.com/iamcco/markdown-preview.nvim/blob/master/README.md
37. markdown-preview.nvim server: https://github.com/iamcco/markdown-preview.nvim/blob/master/app/server.js
38. markdown-preview.nvim RPC attach: https://github.com/iamcco/markdown-preview.nvim/blob/master/app/nvim.js
39. peek.nvim README: https://github.com/toppair/peek.nvim/blob/master/README.md
40. Neovim 0.12.5 Lua reference, `vim.ui.open()`: https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua.txt
41. GitHub repository metadata (`pushed_at`): https://api.github.com/repos/iamcco/markdown-preview.nvim and https://api.github.com/repos/toppair/peek.nvim
42. FsHttp.Studio renderer core: [`src/renderer/Renderer.fs`](../../src/renderer/Renderer.fs)
