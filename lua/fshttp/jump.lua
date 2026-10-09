-- <CR> in the Response buffer: moves the cursor to the position of a Compile error in the script.
local M = {}

---@type integer?
local script_buf

-- The script of the latest Run, which is where a Compile error position points.
---@param buf integer
function M.remember(buf)
    script_buf = buf
end

---@param buf integer
---@return integer
local function script_window(buf)
    for _, win in ipairs(vim.api.nvim_tabpage_list_wins(0)) do
        if vim.api.nvim_win_get_buf(win) == buf then
            return win
        end
    end
    vim.cmd("topleft split")
    vim.api.nvim_win_set_buf(0, buf)
    return vim.api.nvim_get_current_win()
end

function M.jump()
    local response_buffer = require("fshttp.response_buffer")
    local position = response_buffer.position_at(vim.api.nvim_win_get_cursor(0)[1])
    if not position then
        return
    end
    if not script_buf or not vim.api.nvim_buf_is_valid(script_buf) then
        vim.notify("The script of the Run is closed.", vim.log.levels.WARN, { title = "FsHttp.Studio" })
        return
    end
    -- The envelope has no file name, so a position from a loaded file looks like a script position.
    -- A line past the end of the script cannot be a script position, so the cursor stays.
    if position.line > vim.api.nvim_buf_line_count(script_buf) then
        vim.notify(
            "The position is past the end of the script. The Compile error can be in a loaded file.",
            vim.log.levels.WARN,
            { title = "FsHttp.Studio" }
        )
        return
    end
    local win = script_window(script_buf)
    local text = vim.api.nvim_buf_get_lines(script_buf, position.line - 1, position.line, false)[1] or ""
    vim.api.nvim_set_current_win(win)
    vim.api.nvim_win_set_cursor(win, { position.line, math.min(position.col, math.max(#text - 1, 0)) })
end

return M
