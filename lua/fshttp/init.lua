local M = {}

---@class fshttp.Options
---@field companion_path? string The folder that holds Companion.dll and Companion.runtimeconfig.json.
---@field dotnet_path? string A dotnet executable. With no value, the client uses dotnet on PATH.
---@field status_line? { lualine?: boolean } lualine = false removes the lualine entry.
---@field request_timeout_ms? number The bound of each Run in milliseconds. 0 sets no bound.

---@class fshttp.Config
---@field companion_path? string
---@field dotnet_path? string
---@field status_line { lualine: boolean }
---@field request_timeout_ms? number

---@type fshttp.Config
local config = { status_line = { lualine = true } }

---@param opts? fshttp.Options
function M.setup(opts)
    opts = opts or {}
    local status_line = type(opts.status_line) == "table" and opts.status_line or {}
    config = {
        companion_path = opts.companion_path,
        dotnet_path = opts.dotnet_path,
        status_line = { lualine = status_line.lualine ~= false },
        request_timeout_ms = opts.request_timeout_ms,
    }
end

---@return fshttp.Config
function M.config()
    return config
end

-- Only the first call has an effect.
function M.start()
    require("fshttp.locator").watch()
    require("fshttp.status_line").watch()
    require("fshttp.companion").start(config)
end

---@return string? text the Status line text of the Active document, or nil when it does not have the fsharp filetype
function M.status()
    return require("fshttp.status_line").row(vim.api.nvim_get_current_buf())
end

return M
