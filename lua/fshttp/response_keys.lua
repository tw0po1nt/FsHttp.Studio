local notify = require("fshttp.notify").notify

local M = {}

---@class fshttp.ResponseKey
---@field lhs string
---@field plug string
---@field description string

---@type fshttp.ResponseKey[]
M.keys = {
    { lhs = "yr", plug = "<Plug>(FsHttpYankRequest)", description = "Yank the Request" },
    { lhs = "yh", plug = "<Plug>(FsHttpYankHeaders)", description = "Yank the Response headers" },
    { lhs = "yb", plug = "<Plug>(FsHttpYankBody)", description = "Yank the Body" },
    { lhs = "yc", plug = "<Plug>(FsHttpYankCurl)", description = "Yank the Curl command" },
    { lhs = "<CR>", plug = "<Plug>(FsHttpJump)", description = "Move to a Compile error position" },
    { lhs = "g?", plug = "<Plug>(FsHttpHelp)", description = "List the active keys" },
    { lhs = "l", plug = "<Plug>(FsHttpOpenFold)", description = "Open the fold or move right" },
    { lhs = "h", plug = "<Plug>(FsHttpCloseFold)", description = "Close the fold or move left" },
}

---@return string
function M.open_fold()
    return vim.fn.foldclosed(".") ~= -1 and "zo" or "l"
end

---@return string
function M.close_fold()
    if vim.fn.foldclosed(".") ~= -1 then
        return "zc"
    end
    local first_non_blank = vim.fn.getline("."):find("%S") or math.huge
    return (vim.fn.foldlevel(".") > 0 and vim.fn.col(".") <= first_non_blank) and "zc" or "h"
end

---@param buf integer
function M.attach(buf)
    if not require("fshttp").config().response_buffer.keys then
        for _, key in ipairs(M.keys) do
            pcall(vim.keymap.del, "n", key.lhs, { buffer = buf })
        end
        return
    end
    for _, key in ipairs(M.keys) do
        vim.keymap.set(
            "n",
            key.lhs,
            key.plug,
            { buffer = buf, remap = true, desc = "FsHttp.Studio: " .. key.description }
        )
    end
end

-- A key is active when the buffer still has its local map.
---@param buf integer
---@return string[]
function M.active_lines(buf)
    local lines = {}
    for _, key in ipairs(M.keys) do
        local map = vim.fn.maparg(key.lhs, "n", false, true)
        if type(map) == "table" and map.buffer == 1 and map.rhs == key.plug then
            lines[#lines + 1] = string.format("%s  %s", key.lhs, key.description)
        end
    end
    return lines
end

function M.help()
    local lines = M.active_lines(vim.api.nvim_get_current_buf())
    if #lines == 0 then
        notify("The current buffer is not the Response buffer.", vim.log.levels.INFO)
        return
    end
    table.insert(lines, 1, "Keys of the Response buffer:")
    notify(table.concat(lines, "\n"), vim.log.levels.INFO)
end

return M
