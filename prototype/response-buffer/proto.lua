-- PROTOTYPE, throwaway. It answers one question: what does the Response buffer look like with a
-- real response? It draws four variants of the Response buffer from saved results in fixtures/.
-- It needs no companion and no client.
--
-- Run it from the repo root, in your own LazyVim:
--   nvim -c "luafile prototype/response-buffer/proto.lua"
--
--   <Tab>     next variant           <S-Tab>   next case
--   :Proto A|B|C|D                   :Proto json|image|html|compile
--   :Proto noimage                   shows the image fallback text (toggle)
--   :Proto noparser                  hides the tree-sitter parsers (toggle)

local dir = vim.fn.fnamemodify(debug.getinfo(1, "S").source:sub(2), ":p:h")
local ns = vim.api.nvim_create_namespace("fshttp_proto")
local mark_ns = vim.api.nvim_create_namespace("fshttp_proto_marks")
local diag_ns = vim.api.nvim_create_namespace("fshttp")

local VARIANTS = { "A", "B", "C", "D" }
local VNAME = {
  A = "Port: status line in the buffer, sections as folds",
  B = "Pinned: status line in the winbar, sections open",
  C = "Body only: real filetype, metadata in a float",
  D = "Decided: B, status first, headers closed, wrap",
}
local CASES = { "json", "image", "html", "compile" }
local BLOCK_LINE = { json = 8, image = 10, html = 12, compile = 16 }

local S = { variant = "A", case = "json", no_image = false, no_parser = false }
local LEVELS = {}

---------------------------------------------------------------------------------------------------
-- Pure helpers, ported loosely from Renderer.fs

local function human(n)
  if n < 1024 then
    return n .. " B"
  elseif n < 1024 * 1024 then
    return string.format("%.1f KB", n / 1024)
  end
  return string.format("%.1f MB", n / 1048576)
end

local function bare_ct(ct)
  local s = (ct or ""):match("^[^;]*") or ""
  return (s:gsub("%s", "")):lower()
end

local function status_hl(s)
  if s >= 200 and s < 300 then
    return "DiagnosticOk"
  elseif s >= 300 and s < 400 then
    return "DiagnosticInfo"
  elseif s >= 400 and s < 500 then
    return "DiagnosticWarn"
  end
  return "DiagnosticError"
end

local function png_size(bytes)
  if bytes:sub(2, 4) ~= "PNG" then
    return nil
  end
  local function be(i)
    local a, b, c, d = bytes:byte(i, i + 3)
    return ((a * 256 + b) * 256 + c) * 256 + d
  end
  return be(17), be(21)
end

-- Pretty-prints JSON text in key order. Returns the lines and the structural fold ranges
-- (0-based rows), which is the fallback when no json parser is installed.
local function pretty_json(text)
  local out, depth, row, stack, folds = {}, 0, 0, {}, {}
  local in_str, esc = false, false
  local function nl()
    out[#out + 1] = "\n" .. string.rep("  ", depth)
    row = row + 1
  end
  local i = 1
  while i <= #text do
    local c = text:sub(i, i)
    if in_str then
      out[#out + 1] = c
      if esc then
        esc = false
      elseif c == "\\" then
        esc = true
      elseif c == '"' then
        in_str = false
      end
    elseif c == '"' then
      in_str = true
      out[#out + 1] = c
    elseif c == "{" or c == "[" then
      local close = c == "{" and "}" or "]"
      local j = i + 1
      while text:sub(j, j):match("%s") do
        j = j + 1
      end
      if text:sub(j, j) == close then
        out[#out + 1] = c .. close
        i = j
      else
        out[#out + 1] = c
        stack[#stack + 1] = row
        depth = depth + 1
        nl()
      end
    elseif c == "}" or c == "]" then
      depth = depth - 1
      nl()
      out[#out + 1] = c
      folds[#folds + 1] = { table.remove(stack), row }
    elseif c == "," then
      out[#out + 1] = ","
      nl()
    elseif c == ":" then
      out[#out + 1] = ": "
    elseif not c:match("%s") then
      out[#out + 1] = c
    end
    i = i + 1
  end
  return vim.split(table.concat(out), "\n"), folds
end

-- Parses the body lines with a tree-sitter string parser, and adds the highlights and the fold
-- ranges at the body's row offset. Returns false when the parser is missing.
local function ts_body(lang, lines, row0, view)
  if S.no_parser then
    return false
  end
  local ok, added = pcall(vim.treesitter.language.add, lang)
  if not ok or not added then
    return false
  end
  local text = table.concat(lines, "\n")
  local parser = vim.treesitter.get_string_parser(text, lang)
  parser:parse(true)
  parser:for_each_tree(function(tree, ltree)
    local l = ltree:lang()
    local q = vim.treesitter.query.get(l, "highlights")
    if q then
      for id, node in q:iter_captures(tree:root(), text) do
        local sr, sc, er, ec = node:range()
        view.hls[#view.hls + 1] = { row0 + sr, sc, row0 + er, ec, "@" .. q.captures[id] .. "." .. l }
      end
    end
  end)
  local root = parser:trees()[1]:root()
  local fq = vim.treesitter.query.get(lang, "folds")
  if fq then
    local seen = {}
    for _, node in fq:iter_captures(root, text) do
      local sr, _, er = node:range()
      local key = sr .. ":" .. er
      if er > sr and not seen[key] then
        seen[key] = true
        view.folds[#view.folds + 1] = { row0 + sr, row0 + er }
      end
    end
  end
  return true
end

---------------------------------------------------------------------------------------------------
-- View building

local function new_view()
  return { lines = {}, hls = {}, folds = {}, closed = {}, virt = {} }
end

-- Appends one line built from { text, hl_group } chunks. Returns its 0-based row.
local function add(view, chunks)
  if type(chunks) == "string" then
    chunks = { { chunks } }
  end
  local row, col, parts = #view.lines, 0, {}
  for _, ch in ipairs(chunks) do
    parts[#parts + 1] = ch[1]
    if ch[2] then
      view.hls[#view.hls + 1] = { row, col, row, col + #ch[1], ch[2] }
    end
    col = col + #ch[1]
  end
  view.lines[#view.lines + 1] = table.concat(parts)
  return row
end

local function winbar(chunks)
  local s = {}
  for _, ch in ipairs(chunks) do
    s[#s + 1] = (ch[2] and ("%#" .. ch[2] .. "#") or "%#WinBar#") .. ch[1]:gsub("%%", "%%%%")
  end
  return table.concat(s)
end

local function status_chunks(env, body)
  return {
    { env.request.method, "Keyword" },
    { " " },
    { env.request.url, "Underlined" },
    { "  " },
    { env.status .. " " .. env.reason, status_hl(env.status) },
    { "  " },
    { string.format("%d ms", math.floor(env.requestMs + 0.5)), "Number" },
    { string.format(" · %d ms total", math.floor(env.totalMs + 0.5)), "Comment" },
    { "  " },
    { human(#body), "Comment" },
  }
end

-- The decided order: status, timings, and size always show. The winbar cuts the start of the
-- URL (the %< item) when the split is narrow.
local function decided_winbar(env, body)
  local s = winbar({
    { " " .. env.status .. " " .. env.reason, status_hl(env.status) },
    { "  " },
    { string.format("%d ms", math.floor(env.requestMs + 0.5)), "Number" },
    { string.format(" · %d ms total", math.floor(env.totalMs + 0.5)), "Comment" },
    { "  " .. human(#body), "Comment" },
    { "  " .. env.request.method .. " ", "Keyword" },
  })
  return s .. "%<" .. winbar({ { env.request.url, "Underlined" } })
end

local function hint_text(ct)
  if ct == "text/html" then
    return ":FsHttp open  shows the rendered page in the browser, with scripts blocked"
  end
  return ":FsHttp open  shows the image in the system viewer"
end

-- Each header row is indented two columns, so the section title stands out.
local function header_rows(view, headers)
  for _, h in ipairs(headers) do
    add(view, { { "  " }, { h[1], "Identifier" }, { ": ", "Delimiter" }, { h[2] } })
  end
end

local function section(view, title, extra, fill, closed)
  local start = add(view, { { "▾ " .. title, "Title" }, extra and { "  " .. extra, "Comment" } or { "" } })
  fill()
  if #view.lines - 1 > start then
    view.folds[#view.folds + 1] = { start, #view.lines - 1 }
    if closed then
      view.closed[#view.closed + 1] = start
    end
  end
  return start
end

-- Adds the body lines at the current end of the view. Returns the row for an inline image, if any.
local function body_lines(view, env, body, ct, opts)
  if ct:match("json$") then
    local lines, sfolds = pretty_json(body)
    local row0 = #view.lines
    for _, l in ipairs(lines) do
      view.lines[#view.lines + 1] = l
    end
    if not ts_body("json", lines, row0, view) then
      for _, f in ipairs(sfolds) do
        view.folds[#view.folds + 1] = { row0 + f[1], row0 + f[2] }
      end
    end
  elseif ct == "text/html" then
    if opts.hint_line then
      add(view, { { hint_text(ct), "DiagnosticHint" } })
    end
    local lines = vim.split(body, "\n", { trimempty = true })
    local row0 = #view.lines
    for _, l in ipairs(lines) do
      view.lines[#view.lines + 1] = l
    end
    ts_body("html", lines, row0, view)
  elseif ct:match("^image/") then
    local w, h = png_size(body)
    local dims = w and string.format("%d×%d px", w, h) or "unknown size"
    local reason
    if S.no_image then
      reason = "images are off (:Proto noimage)"
    elseif not package.loaded["snacks"] or not Snacks.image then
      reason = "snacks.nvim is not installed"
    elseif not Snacks.image.supports_terminal() then
      reason = "this terminal has no kitty graphics protocol"
    end
    local row = add(view, {
      { opts.dims_only and dims or (ct .. " · " .. human(#body) .. " · " .. dims), "Comment" },
      reason and { "  " .. reason, "DiagnosticWarn" } or { "" },
    })
    if opts.hint_line then
      add(view, { { hint_text(ct), "DiagnosticHint" } })
    end
    if not reason then
      return row
    end
  end
end

local function compile_view(res)
  local view = new_view()
  add(view, { { "Compile error:", "DiagnosticError" } })
  for _, d in ipairs(res.diagnostics) do
    local msg = vim.split(d.message, "\n")
    add(view, { { string.format("(%d,%d) ", d.startLine, d.startCol + 1), "LineNr" }, { msg[1] } })
    for k = 2, #msg do
      add(view, msg[k])
    end
  end
  return view
end

local function build(res)
  if res.kind == "compileError" then
    local view = compile_view(res)
    if S.variant == "D" then
      for i, l in ipairs(view.lines) do
        view.lines[i] = (l:gsub("%s+$", ""))
      end
      view.wrap = true
      view.winbar = winbar({
        { " Compile error", "DiagnosticError" },
        { "  <CR> on a (line,col) moves to it in the script", "Comment" },
      })
    elseif S.variant ~= "A" then
      local n = #res.diagnostics
      view.winbar = winbar({
        { " Compile error", "DiagnosticError" },
        { string.format("  %d diagnostic%s in demo.fsx  ]d jumps to it", n, n == 1 and "" or "s"), "Comment" },
      })
    end
    return view
  end

  local body = vim.base64.decode(res.bodyBase64)
  local ct = bare_ct(res.contentType)
  local view = new_view()

  if S.variant == "A" then
    add(view, status_chunks(res, body))
    add(view, "")
    section(view, "Request", nil, function()
      add(view, { { "  " }, { res.request.method .. " " .. res.request.url, "Keyword" } })
      header_rows(view, res.request.headers)
    end, true)
    section(view, "Response headers", "(" .. #res.headers .. ")", function()
      header_rows(view, res.headers)
    end, true)
    section(view, "Body", ct .. " · " .. human(#body), function()
      view.image_row = body_lines(view, res, body, ct, { hint_line = true })
    end, false)
  elseif S.variant == "D" then
    view.winbar = decided_winbar(res, body)
    view.wrap = true
    section(view, "Request", nil, function()
      add(view, { { "  " }, { res.request.method .. " " .. res.request.url, "Keyword" } })
      header_rows(view, res.request.headers)
    end, true)
    section(view, "Response headers", "(" .. #res.headers .. ")", function()
      header_rows(view, res.headers)
    end, true)
    local hdr = section(view, "Body", ct .. " · " .. human(#body), function()
      view.image_row = body_lines(view, res, body, ct, { hint_line = false, dims_only = true })
    end, false)
    if ct == "text/html" or ct:match("^image/") then
      view.vlines = { { hdr, { { "  " .. hint_text(ct), "DiagnosticHint" } } } }
    end
  elseif S.variant == "B" then
    view.winbar = winbar(status_chunks(res, body))
    section(view, "Request", nil, function()
      add(view, { { "  " }, { res.request.method .. " " .. res.request.url, "Keyword" } })
      header_rows(view, res.request.headers)
    end, true)
    section(view, "Response headers", "(" .. #res.headers .. ")", function()
      header_rows(view, res.headers)
    end, false)
    local hdr = section(view, "Body", ct .. " · " .. human(#body), function()
      view.image_row = body_lines(view, res, body, ct, { hint_line = false })
    end, false)
    if ct == "text/html" or ct:match("^image/") then
      view.virt[#view.virt + 1] = { hdr, { { "   " .. hint_text(ct), "DiagnosticHint" } } }
    end
  else
    local chunks = status_chunks(res, body)
    chunks[#chunks + 1] = { "   gR request · gH headers", "Comment" }
    if ct == "text/html" or ct:match("^image/") then
      chunks[#chunks + 1] = { " · :FsHttp open", "DiagnosticHint" }
    end
    view.winbar = winbar(chunks)
    if ct:match("json$") then
      view.ft = "json"
      view.lines = (pretty_json(body))
      if S.no_parser then
        view.ft = nil
        local _, sfolds = pretty_json(body)
        view.folds = sfolds
      end
    elseif ct == "text/html" then
      view.ft = S.no_parser and nil or "html"
      view.lines = vim.split(body, "\n", { trimempty = true })
    else
      view.image_row = body_lines(view, res, body, ct, { hint_line = false })
    end
    view.request = { res.request.method .. " " .. res.request.url }
    for _, h in ipairs(res.request.headers) do
      view.request[#view.request + 1] = h[1] .. ": " .. h[2]
    end
    view.headers = { res.status .. " " .. res.reason }
    for _, h in ipairs(res.headers) do
      view.headers[#view.headers + 1] = h[1] .. ": " .. h[2]
    end
  end
  view.body = body
  view.ct = ct
  return view
end

---------------------------------------------------------------------------------------------------
-- Neovim side

local function fold_levels(n, ranges)
  local depth, starts, lv = {}, {}, {}
  for l = 0, n - 1 do
    depth[l] = 0
  end
  for _, r in ipairs(ranges) do
    starts[r[1]] = true
    for l = r[1], math.min(r[2], n - 1) do
      depth[l] = depth[l] + 1
    end
  end
  for l = 0, n - 1 do
    lv[l + 1] = (starts[l] and ">" or "") .. depth[l]
  end
  return lv
end

_G.FsHttpProtoFold = function(lnum)
  local lv = LEVELS[vim.api.nvim_get_current_buf()]
  return lv and lv[lnum] or "0"
end

_G.FsHttpProtoFoldText = function()
  local line = vim.fn.getline(vim.v.foldstart)
  local n = vim.v.foldend - vim.v.foldstart
  if line:sub(1, #"▾") == "▾" then
    return { { "▸" .. line:sub(#"▾" + 1), "Title" }, { "  " .. n .. " lines", "Comment" } }
  end
  return { { line, "Normal" }, { " ⋯ " .. n .. " lines", "Comment" } }
end

local function script_buf()
  return vim.fn.bufnr(dir .. "/demo.fsx")
end

local function response_buf()
  if S.buf and vim.api.nvim_buf_is_valid(S.buf) then
    return S.buf
  end
  S.buf = vim.api.nvim_create_buf(false, true)
  vim.api.nvim_buf_set_name(S.buf, "FsHttp://response")
  vim.bo[S.buf].bufhidden = "hide"
  vim.keymap.set("n", "gH", function()
    if S.view and S.view.headers then
      vim.lsp.util.open_floating_preview(S.view.headers, "", { border = "rounded", title = " Response headers " })
    end
  end, { buffer = S.buf })
  vim.keymap.set("n", "gR", function()
    if S.view and S.view.request then
      vim.lsp.util.open_floating_preview(S.view.request, "", { border = "rounded", title = " Request " })
    end
  end, { buffer = S.buf })
  vim.keymap.set("n", "<CR>", function()
    local l, c = vim.api.nvim_get_current_line():match("^%((%d+),(%d+)%)")
    if not l then
      return
    end
    for _, w in ipairs(vim.api.nvim_list_wins()) do
      if vim.api.nvim_win_get_buf(w) == script_buf() then
        vim.api.nvim_set_current_win(w)
        vim.api.nvim_win_set_cursor(w, { tonumber(l), tonumber(c) - 1 })
      end
    end
  end, { buffer = S.buf })
  return S.buf
end

-- Reuses a window that shows the Response buffer, or opens a vertical split on the right.
local function response_win(buf)
  for _, w in ipairs(vim.api.nvim_list_wins()) do
    if vim.api.nvim_win_get_buf(w) == buf then
      return w
    end
  end
  return vim.api.nvim_open_win(buf, false, { split = "right", win = 0 })
end

local function block_marks()
  local sb = script_buf()
  vim.api.nvim_buf_clear_namespace(sb, mark_ns, 0, -1)
  for i, l in ipairs(vim.api.nvim_buf_get_lines(sb, 0, -1, false)) do
    if l:match("^http {") then
      vim.api.nvim_buf_set_extmark(sb, mark_ns, i - 1, 0, {
        virt_lines = { { { "▶ Run request", "Comment" } } },
        virt_lines_above = true,
        sign_text = "▶",
        sign_hl_group = "DiagnosticOk",
      })
    end
  end
end

local function switcher()
  local text = string.format(
    " PROTOTYPE │ variant %s: %s │ case: %s%s%s │ <Tab> variant  <S-Tab> case ",
    S.variant,
    VNAME[S.variant],
    S.case,
    S.no_image and " · noimage" or "",
    S.no_parser and " · noparser" or ""
  )
  if not (S.sbuf and vim.api.nvim_buf_is_valid(S.sbuf)) then
    S.sbuf = vim.api.nvim_create_buf(false, true)
  end
  vim.api.nvim_buf_set_lines(S.sbuf, 0, -1, false, { text })
  local width = vim.fn.strdisplaywidth(text)
  local cfg = {
    relative = "editor",
    row = vim.o.lines - 5,
    col = math.max(0, math.floor((vim.o.columns - width) / 2)),
    width = width,
    height = 1,
    style = "minimal",
    border = "rounded",
    focusable = false,
    zindex = 250,
  }
  if S.swin and vim.api.nvim_win_is_valid(S.swin) then
    vim.api.nvim_win_set_config(S.swin, cfg)
  else
    S.swin = vim.api.nvim_open_win(S.sbuf, false, cfg)
    vim.wo[S.swin].winhighlight = "Normal:PmenuSel,FloatBorder:PmenuSel"
  end
end

local function render()
  local res = vim.json.decode(table.concat(vim.fn.readfile(dir .. "/fixtures/" .. S.case .. ".json"), "\n"))
  local view = build(res)
  S.view = view

  local sb = script_buf()
  vim.diagnostic.reset(diag_ns, sb)
  if res.kind == "compileError" and S.variant ~= "D" then
    local diags = {}
    for _, d in ipairs(res.diagnostics) do
      diags[#diags + 1] = {
        lnum = d.startLine - 1,
        col = d.startCol,
        end_lnum = d.endLine - 1,
        end_col = d.endCol,
        severity = vim.diagnostic.severity.ERROR,
        source = "FsHttp",
        message = d.message,
      }
    end
    vim.diagnostic.set(diag_ns, sb, diags)
  end

  local buf = response_buf()
  if S.placement then
    pcall(function()
      S.placement:close()
    end)
    S.placement = nil
  end
  pcall(vim.treesitter.stop, buf)
  vim.bo[buf].modifiable = true
  vim.api.nvim_buf_set_lines(buf, 0, -1, false, view.lines)
  vim.bo[buf].modifiable = false
  vim.bo[buf].filetype = view.ft or "fshttp_response"
  if view.ft then
    pcall(vim.treesitter.start, buf, view.ft)
  end
  vim.api.nvim_buf_clear_namespace(buf, ns, 0, -1)
  for _, h in ipairs(view.hls) do
    vim.api.nvim_buf_set_extmark(buf, ns, h[1], h[2], {
      end_row = h[3],
      end_col = h[4],
      hl_group = h[5],
      priority = 110,
      strict = false,
    })
  end
  for _, v in ipairs(view.virt) do
    vim.api.nvim_buf_set_extmark(buf, ns, v[1], 0, { virt_text = v[2], virt_text_pos = "eol" })
  end
  for _, v in ipairs(view.vlines or {}) do
    vim.api.nvim_buf_set_extmark(buf, ns, v[1], 0, { virt_lines = { v[2] } })
  end
  LEVELS[buf] = fold_levels(#view.lines, view.folds)

  local win = response_win(buf)
  vim.wo[win].winbar = view.winbar or ""
  vim.wo[win].wrap = view.wrap or false
  vim.wo[win].linebreak = view.wrap or false
  vim.wo[win].breakindent = view.wrap or false
  vim.wo[win].number = false
  vim.wo[win].relativenumber = false
  vim.wo[win].signcolumn = "no"
  vim.wo[win].foldcolumn = "1"
  vim.wo[win].foldmethod = "expr"
  if view.ft then
    vim.wo[win].foldexpr = "v:lua.vim.treesitter.foldexpr()"
    vim.wo[win].foldtext = ""
  else
    vim.wo[win].foldexpr = "v:lua.FsHttpProtoFold(v:lnum)"
    vim.wo[win].foldtext = "v:lua.FsHttpProtoFoldText()"
  end
  vim.wo[win].foldlevel = 99
  vim.api.nvim_win_call(win, function()
    vim.cmd("normal! zx")
    for _, r in ipairs(view.closed) do
      pcall(vim.cmd, (r + 1) .. "foldclose")
    end
    vim.cmd("normal! gg")
  end)

  if view.image_row then
    local file = vim.fn.tempname() .. ".png"
    local f = assert(io.open(file, "wb"))
    f:write(view.body)
    f:close()
    S.placement = Snacks.image.placement.new(buf, file, {
      inline = true,
      pos = { view.image_row + 1, 0 },
      max_height = 20,
    })
  end

  for _, w in ipairs(vim.api.nvim_list_wins()) do
    if vim.api.nvim_win_get_buf(w) == sb then
      pcall(vim.api.nvim_win_set_cursor, w, { BLOCK_LINE[S.case], 0 })
    end
  end
  switcher()
end

local function cycle(list, cur)
  for i, v in ipairs(list) do
    if v == cur then
      return list[i % #list + 1]
    end
  end
  return list[1]
end

vim.cmd.edit(dir .. "/demo.fsx")
block_marks()

vim.keymap.set("n", "<Tab>", function()
  S.variant = cycle(VARIANTS, S.variant)
  render()
end)
vim.keymap.set("n", "<S-Tab>", function()
  S.case = cycle(CASES, S.case)
  render()
end)
vim.api.nvim_create_user_command("Proto", function(o)
  local a = o.args
  if vim.tbl_contains(VARIANTS, a) then
    S.variant = a
  elseif vim.tbl_contains(CASES, a) then
    S.case = a
  elseif a == "noimage" then
    S.no_image = not S.no_image
  elseif a == "noparser" then
    S.no_parser = not S.no_parser
  end
  render()
end, {
  nargs = 1,
  complete = function()
    return { "A", "B", "C", "D", "json", "image", "html", "compile", "noimage", "noparser" }
  end,
})
vim.api.nvim_create_autocmd("VimResized", { callback = switcher })

render()
