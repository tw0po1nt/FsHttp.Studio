-- The one Response buffer, and the window that shows it.
local image_placement = require("fshttp.image_placement")
local response_view = require("fshttp.response_view")

local M = {}

M.filetype = "fshttp_response"

local namespace = vim.api.nvim_create_namespace("fshttp.response_buffer")
local running_interval_ms = 1000

---@type integer?
local buf
---@type fshttp.ResponseView?
local current_view
---@type string[]
local levels = {}
---@type uv.uv_timer_t?
local running_timer
local highlights_defined = false

local function define_highlights()
    local links = {
        FsHttpResponseSection = "Title",
        FsHttpResponseDetail = "Comment",
        FsHttpResponseHeaderName = "Identifier",
        FsHttpResponseMethod = "Keyword",
        FsHttpResponseUrl = "Underlined",
        FsHttpResponseTime = "Number",
        FsHttpResponseError = "DiagnosticError",
        FsHttpResponseRefused = "Title",
        FsHttpResponseStatus2xx = "DiagnosticOk",
        FsHttpResponseStatus3xx = "DiagnosticInfo",
        FsHttpResponseStatus4xx = "DiagnosticWarn",
        FsHttpResponseStatus5xx = "DiagnosticError",
        FsHttpResponseStatusOther = "Comment",
    }
    for group, link in pairs(links) do
        vim.api.nvim_set_hl(0, group, { link = link, default = true })
    end
end

local function ensure_highlights()
    if highlights_defined then
        return
    end
    highlights_defined = true
    define_highlights()
    vim.api.nvim_create_autocmd("ColorScheme", {
        group = vim.api.nvim_create_augroup("fshttp.response_buffer", { clear = true }),
        callback = define_highlights,
    })
end

---@return integer
local function get_buf()
    if buf and vim.api.nvim_buf_is_valid(buf) then
        if not vim.api.nvim_buf_is_loaded(buf) then
            vim.fn.bufload(buf)
        end
        return buf
    end
    buf = vim.api.nvim_create_buf(false, true)
    vim.api.nvim_buf_set_name(buf, "fshttp://response")
    vim.bo[buf].modifiable = false
    vim.bo[buf].filetype = M.filetype
    require("fshttp.response_keys").attach(buf)
    return buf
end

---@return integer[]
local function windows()
    if not buf or not vim.api.nvim_buf_is_valid(buf) then
        return {}
    end
    return vim.fn.win_findbuf(buf)
end

---@param win integer
---@param name string
---@param value any
local function set_local(win, name, value)
    vim.api.nvim_set_option_value(name, value, { scope = "local", win = win })
end

---@param win integer
local function apply_to_window(win)
    local view = current_view
    if not view then
        return
    end
    set_local(win, "winbar", view.winbar)
    set_local(win, "wrap", true)
    set_local(win, "linebreak", true)
    set_local(win, "breakindent", true)
    set_local(win, "number", false)
    set_local(win, "relativenumber", false)
    set_local(win, "signcolumn", "no")
    set_local(win, "foldmethod", "expr")
    set_local(win, "foldexpr", "v:lua.require'fshttp.response_buffer'.fold_level(v:lnum)")
    set_local(win, "foldtext", "v:lua.require'fshttp.response_buffer'.fold_text()")
    set_local(win, "foldlevel", 99)
    vim.api.nvim_win_set_cursor(win, { 1, 0 })
    vim.api.nvim_win_call(win, function()
        vim.cmd("normal! zX")
        for _, fold in ipairs(view.folds) do
            if fold.closed then
                pcall(vim.api.nvim_command, fold.first .. "foldclose")
            end
        end
    end)
end

---@param view fshttp.ResponseView
local function paint(view)
    ensure_highlights()
    image_placement.clear()
    local target = get_buf()
    current_view = view
    levels = response_view.fold_levels(#view.lines, view.folds)
    vim.bo[target].modifiable = true
    vim.api.nvim_buf_set_lines(target, 0, -1, false, view.lines)
    vim.bo[target].modifiable = false
    vim.bo[target].modified = false
    vim.api.nvim_buf_clear_namespace(target, namespace, 0, -1)
    for _, highlight in ipairs(view.highlights) do
        vim.api.nvim_buf_set_extmark(target, namespace, highlight.line - 1, highlight.first_col, {
            end_col = highlight.last_col,
            hl_group = highlight.group,
        })
    end
    for _, win in ipairs(windows()) do
        apply_to_window(win)
    end
    if view.image then
        image_placement.place(target, view.image)
    end
end

local function stop_running()
    if running_timer then
        running_timer:stop()
        running_timer:close()
        running_timer = nil
    end
end

-- Opens a split for the buffer when no window of the current tab page shows it. The cursor stays
-- in the current window.
local function open_window()
    local target = get_buf()
    for _, win in ipairs(vim.api.nvim_tabpage_list_wins(0)) do
        if vim.api.nvim_win_get_buf(win) == target then
            return
        end
    end
    local win = vim.api.nvim_open_win(target, false, { split = "right", win = vim.api.nvim_get_current_win() })
    apply_to_window(win)
end

-- Shows "Running… Ns" in the buffer, and opens its window, until the next call of M.show.
function M.show_running()
    stop_running()
    paint(response_view.running(0))
    open_window()
    local started = vim.uv.now()
    local timer = assert(vim.uv.new_timer())
    running_timer = timer
    timer:start(
        running_interval_ms,
        running_interval_ms,
        vim.schedule_wrap(function()
            if running_timer == timer then
                paint(response_view.running(math.floor((vim.uv.now() - started) / 1000)))
            end
        end)
    )
end

-- A closed window stays closed until the next Run.
---@param view fshttp.ResponseView
function M.show(view)
    stop_running()
    paint(view)
end

-- The script position of a line of a Compile error.
---@param lnum integer
---@return fshttp.ScriptPosition?
function M.position_at(lnum)
    return current_view and current_view.positions and current_view.positions[lnum]
end

---@param lnum integer
---@return string
function M.fold_level(lnum)
    if vim.api.nvim_get_current_buf() ~= buf then
        return "0"
    end
    return levels[lnum] or "0"
end

---@return string
function M.fold_text()
    if vim.api.nvim_get_current_buf() ~= buf then
        return vim.fn.foldtext()
    end
    local first = vim.v.foldstart
    local line = vim.api.nvim_buf_get_lines(0, first - 1, first, false)[1] or ""
    return response_view.fold_text(line, vim.v.foldend - first)
end

return M
