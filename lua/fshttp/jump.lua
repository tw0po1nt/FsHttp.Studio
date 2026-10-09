local notify = require("fshttp.notify").notify

local M = {}

---@type integer?
local script_buf

---@param buf integer
function M.remember(buf)
    script_buf = buf
end

---@param buf integer
---@return integer?
local function window_in_tab(buf)
    for _, win in ipairs(vim.api.nvim_tabpage_list_wins(0)) do
        if vim.api.nvim_win_get_buf(win) == buf then
            return win
        end
    end
end

---@param buf integer
---@return integer
local function script_window(buf)
    local win = window_in_tab(buf)
    if win then
        return win
    end
    vim.cmd("topleft split")
    vim.api.nvim_win_set_buf(0, buf)
    return vim.api.nvim_get_current_win()
end

---@param win integer
---@param buf integer
---@param position fshttp.CompileErrorPosition
local function move_to(win, buf, position)
    local text = vim.api.nvim_buf_get_lines(buf, position.line - 1, position.line, false)[1] or ""
    vim.api.nvim_set_current_win(win)
    vim.api.nvim_win_set_cursor(win, { position.line, math.min(position.col, math.max(#text - 1, 0)) })
end

---@param path string
---@return integer? buf the loaded buffer that edits the file at `path`
local function loaded_buffer(path)
    local wanted = vim.fs.normalize(path)
    for _, buf in ipairs(vim.api.nvim_list_bufs()) do
        if vim.api.nvim_buf_is_loaded(buf) and vim.fs.normalize(vim.api.nvim_buf_get_name(buf)) == wanted then
            return buf
        end
    end
end

---@param position fshttp.CompileErrorPosition
---@param loaded_file string
local function jump_to_loaded_file(position, loaded_file)
    if not vim.uv.fs_stat(loaded_file) then
        notify(string.format("The loaded file %s does not exist.", loaded_file), vim.log.levels.WARN)
        return
    end
    local buf = loaded_buffer(loaded_file)
    local line_count = buf and vim.api.nvim_buf_line_count(buf) or #vim.fn.readfile(loaded_file)
    if position.line > line_count then
        notify(string.format("The position is past the end of the loaded file %s.", loaded_file), vim.log.levels.WARN)
        return
    end
    local win = buf and window_in_tab(buf)
    if not win then
        vim.cmd("topleft split")
        vim.cmd.edit(vim.fn.fnameescape(loaded_file))
        win = vim.api.nvim_get_current_win()
    end
    move_to(win, vim.api.nvim_win_get_buf(win), position)
end

function M.jump()
    local response_buffer = require("fshttp.response_buffer")
    local position = response_buffer.position_at(vim.api.nvim_win_get_cursor(0)[1])
    if not position then
        return
    end
    if position.loaded_file then
        jump_to_loaded_file(position, position.loaded_file)
        return
    end
    if not script_buf or not vim.api.nvim_buf_is_valid(script_buf) then
        notify("The script of the Run is closed.", vim.log.levels.WARN)
        return
    end
    if position.line > vim.api.nvim_buf_line_count(script_buf) then
        notify("The position is past the end of the script.", vim.log.levels.WARN)
        return
    end
    move_to(script_window(script_buf), script_buf, position)
end

return M
