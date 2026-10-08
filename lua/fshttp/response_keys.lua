-- The local keys of the Response buffer, and the list that g? shows.
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
    { lhs = "<CR>", plug = "<Plug>(FsHttpJump)", description = "Move to a Compile error position" },
    { lhs = "g?", plug = "<Plug>(FsHttpHelp)", description = "List the active keys" },
}

---@param buf integer
function M.attach(buf)
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
        vim.notify("The current buffer is not the Response buffer.", vim.log.levels.INFO, { title = "FsHttp.Studio" })
        return
    end
    table.insert(lines, 1, "Keys of the Response buffer:")
    vim.notify(table.concat(lines, "\n"), vim.log.levels.INFO, { title = "FsHttp.Studio" })
end

return M
