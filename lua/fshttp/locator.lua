-- Locates each Script again after a change, and paints the Block marks that the locate gives.
local block_mark = require("fshttp.block_mark")
local companion = require("fshttp.companion")
local refusals = require("fshttp.refusals")

local M = {}

local namespace = vim.api.nvim_create_namespace("fshttp.block_mark")
local relocate_delay_ms = 300

-- The extmark ids of the marks on a Block, for each Script that a locate covered.
---@type table<integer, table<integer, true>>
local located = {}
---@type table<integer, uv.uv_timer_t>
local timers = {}
-- The changedtick check of an answer covers each edit that comes while a locate waits.
---@type table<integer, true>
local waiting = {}
local watching = false

local function define_highlights()
    vim.api.nvim_set_hl(0, "FsHttpBlockMark", { link = "LspCodeLens", default = true })
    vim.api.nvim_set_hl(0, "FsHttpBlockMarkRun", { link = "DiagnosticOk", default = true })
    vim.api.nvim_set_hl(0, "FsHttpBlockMarkRefused", { link = "DiagnosticWarn", default = true })
end

---@param buf integer
---@return boolean
local function is_script(buf)
    return vim.api.nvim_buf_is_loaded(buf) and vim.api.nvim_buf_get_name(buf):match("%.fsx$") ~= nil
end

---@param buf integer
---@param row integer 0-based
---@param title string
---@param runnable boolean
---@param id integer?
---@return integer id
local function set_mark(buf, row, title, runnable, id)
    local indent = vim.api.nvim_buf_call(buf, function()
        return vim.fn.indent(row + 1)
    end)
    local sign_highlight = runnable and "FsHttpBlockMarkRun" or "FsHttpBlockMarkRefused"
    return vim.api.nvim_buf_set_extmark(buf, namespace, row, 0, {
        id = id,
        virt_lines = { { { string.rep(" ", math.max(indent, 0)) .. title, "FsHttpBlockMark" } } },
        virt_lines_above = true,
        sign_text = block_mark.glyph(title),
        sign_hl_group = sign_highlight,
        invalidate = true,
        strict = false,
    })
end

-- A window that shows line 1 at its top shows no virtual line above line 1 until it scrolls up.
---@param buf integer
---@param count integer the number of virtual lines above line 1
local function show_line_one_marks(buf, count)
    for _, win in ipairs(vim.fn.win_findbuf(buf)) do
        vim.api.nvim_win_call(win, function()
            if vim.fn.line("w0") == 1 then
                vim.fn.winrestview({ topfill = count })
            end
        end)
    end
end

---@param buf integer
---@param marks fshttp.BlockMark[]
local function paint(buf, marks)
    vim.api.nvim_buf_clear_namespace(buf, namespace, 0, -1)
    local on_block = {}
    local line_one_count = 0
    for _, mark in ipairs(marks) do
        local id = set_mark(buf, mark.line - 1, mark.title, mark.runnable)
        if mark.on_block then
            on_block[id] = true
        end
        if mark.line == 1 then
            line_one_count = line_one_count + 1
        end
    end
    located[buf] = on_block
    if line_one_count > 0 then
        show_line_one_marks(buf, line_one_count)
    end
end

-- The line-1 mark of a Parse failure is on no Block, so the stopped paint removes it.
---@param buf integer
local function paint_stopped(buf)
    local on_block = located[buf]
    for _, extmark in ipairs(vim.api.nvim_buf_get_extmarks(buf, namespace, 0, -1, { details = true })) do
        local id, row, details = extmark[1], extmark[2], extmark[4]
        if on_block[id] and details and not details.invalid then
            set_mark(buf, row, refusals.companion_stopped_block_mark_title, false, id)
        else
            vim.api.nvim_buf_del_extmark(buf, namespace, id)
        end
    end
end

local schedule_locate

---@param buf integer
local function locate(buf)
    if not is_script(buf) or waiting[buf] then
        return
    end
    local changedtick = vim.api.nvim_buf_get_changedtick(buf)
    local source = table.concat(vim.api.nvim_buf_get_lines(buf, 0, -1, false), "\n") .. "\n"
    waiting[buf] = true
    local sent = companion.locate(source, function(blocks)
        waiting[buf] = nil
        if not blocks or blocks.tag ~= "blocks" or not is_script(buf) then
            return
        end
        -- The ranges belong to older text, and their lines can be wrong for the text now.
        if vim.api.nvim_buf_get_changedtick(buf) ~= changedtick then
            schedule_locate(buf)
            return
        end
        paint(buf, block_mark.for_blocks(blocks))
    end)
    if not sent then
        waiting[buf] = nil
    end
end

---@param buf integer
schedule_locate = function(buf)
    local timer = timers[buf]
    if not timer then
        timer = assert(vim.uv.new_timer())
        timers[buf] = timer
    end
    timer:start(
        relocate_delay_ms,
        0,
        vim.schedule_wrap(function()
            locate(buf)
        end)
    )
end

---@param buf integer
local function forget(buf)
    local timer = timers[buf]
    if timer then
        timer:stop()
        timer:close()
        timers[buf] = nil
    end
    located[buf] = nil
end

---@param state fshttp.CompanionState
local function on_state_change(state)
    if state == "ready" then
        for _, buf in ipairs(vim.api.nvim_list_bufs()) do
            locate(buf)
        end
    elseif state == "stopped" then
        for buf in pairs(located) do
            if vim.api.nvim_buf_is_loaded(buf) then
                paint_stopped(buf)
            end
        end
    end
end

-- Only the first call has an effect.
function M.watch()
    if watching then
        return
    end
    watching = true
    define_highlights()

    local group = vim.api.nvim_create_augroup("fshttp.block_mark", { clear = true })
    vim.api.nvim_create_autocmd("ColorScheme", { group = group, callback = define_highlights })
    vim.api.nvim_create_autocmd("BufEnter", {
        group = group,
        pattern = "*.fsx",
        callback = function(args)
            locate(args.buf)
        end,
    })
    vim.api.nvim_create_autocmd({ "TextChanged", "TextChangedI" }, {
        group = group,
        pattern = "*.fsx",
        callback = function(args)
            schedule_locate(args.buf)
        end,
    })
    vim.api.nvim_create_autocmd("BufUnload", {
        group = group,
        pattern = "*.fsx",
        callback = function(args)
            forget(args.buf)
        end,
    })
    companion.on_state_change(on_state_change)
end

return M
