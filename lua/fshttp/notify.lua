local M = {}

local title = "FsHttp.Studio"

---@param message string
---@param level integer
function M.notify(message, level)
    vim.notify(message, level, { title = title })
end

-- A callback of vim.system runs in a fast event, where a call to vim.notify causes an error.
---@param message string
---@param level integer
function M.notify_scheduled(message, level)
    vim.schedule(function()
        M.notify(message, level)
    end)
end

return M
