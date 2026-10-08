local M = {}

---@class fshttp.Config
---@field companion_path? string The folder that holds Companion.dll and Companion.runtimeconfig.json.
---@field dotnet_path? string A dotnet executable. With no value, the client uses dotnet on PATH.

---@type fshttp.Config
local config = {}

---@param opts? fshttp.Config
function M.setup(opts)
    opts = opts or {}
    config = { companion_path = opts.companion_path, dotnet_path = opts.dotnet_path }
end

-- Runs the start sequence of the companion. Only the first call has an effect.
function M.start()
    require("fshttp.companion").start(config)
end

return M
