-- lazy.nvim runs this file when it installs or updates the plugin. It downloads the Companion
-- archive of the client version. When companion_path is set, it downloads nothing.
local plugin_dir = vim.fs.dirname(debug.getinfo(1, "S").source:sub(2))
vim.opt.rtp:append(plugin_dir)

-- lazy.nvim has not called setup() at build time, so the option comes from the opts of the plugin spec.
---@return string?
local function companion_path_option()
    local ok, path = pcall(function()
        for _, plugin in pairs(require("lazy.core.config").plugins) do
            if plugin.dir == plugin_dir then
                local opts = require("lazy.core.plugin").values(plugin, "opts", false)
                return type(opts) == "table" and opts.companion_path or nil
            end
        end
    end)
    if ok and type(path) == "string" then
        return path
    end
    return require("fshttp").config().companion_path
end

local ok, message = require("fshttp.download").build(companion_path_option())
if not ok then
    error(message, 0)
end
vim.notify(message, vim.log.levels.INFO, { title = "FsHttp.Studio" })
