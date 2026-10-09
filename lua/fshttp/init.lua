local M = {}

---@class fshttp.Options
---@field companion_path? string The folder that contains Companion.dll and Companion.runtimeconfig.json.
---@field dotnet_path? string A dotnet executable. With no value, the client uses dotnet on PATH.
---@field request_timeout_ms? number The bound of each Run in milliseconds. 0 sets no bound.
---@field block_mark? { virtual_line?: boolean, sign?: boolean }
---@field response_buffer? { split?: "right"|"left"|"below"|"above", images?: boolean, keys?: boolean }
---@field status_line? { lualine?: boolean } lualine = false removes the lualine entry.

local notify = require("fshttp.notify").notify
local options = require("fshttp.options")

---@type fshttp.Config
local config = options.defaults()
---@type fshttp.OptionProblem[]?
local setup_problems

local levels = { ERROR = vim.log.levels.ERROR, WARN = vim.log.levels.WARN }

---@param opts? fshttp.Options
function M.setup(opts)
    local new, problems = options.resolve(opts)
    for _, name in ipairs(options.path_options) do
        if new[name] then
            new[name] = vim.fs.normalize(new[name])
        end
    end
    for _, problem in ipairs(problems) do
        notify(problem.message, levels[problem.level])
    end
    local changed = options.changed_paths(config, new)
    config = new
    setup_problems = problems
    local companion = package.loaded["fshttp.companion"]
    local state = companion and companion.state()
    if #changed > 0 and (state == "starting" or state == "ready") then
        notify(
            string.format(
                "FsHttp.Studio: a companion runs, and %s changed. Run :FsHttp restart to use the new value.",
                table.concat(changed, " and ")
            ),
            vim.log.levels.INFO
        )
    end
    if package.loaded["fshttp.locator"] then
        require("fshttp.locator").repaint()
    end
end

---@return fshttp.Config
function M.config()
    return config
end

---@return fshttp.OptionProblem[]? problems nil before the first setup() call
function M.setup_problems()
    return setup_problems
end

-- Only the first call has an effect.
function M.start()
    require("fshttp.locator").watch()
    require("fshttp.status_line").watch()
    require("fshttp.companion").start(config)
end

---@return string? text the Status line text, or nil when the Active document does not have the fsharp filetype
function M.status()
    return require("fshttp.status_line").row(vim.api.nvim_get_current_buf())
end

return M
