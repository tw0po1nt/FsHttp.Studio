-- The rules of :checkhealth fshttp that need no Neovim API: the level of each companion state, the
-- download tools, the tree-sitter parsers with what each one gives, and the text of each item.
local download_rule = require("fshttp.download_rule")

local M = {}

---@alias fshttp.HealthLevel "ok"|"info"|"warn"|"error"

-- A state that cannot become ready with no action of the user.
local failed_states = {
    sdkNotFound = true,
    downloadFailed = true,
    noRelease = true,
    companionNotFound = true,
    stopped = true,
}

---@param state fshttp.CompanionState? nil before the start sequence runs
---@return fshttp.HealthLevel
function M.state_level(state)
    if state == "ready" then
        return "ok"
    elseif failed_states[state] then
        return "error"
    end
    return "info"
end

-- The executables that the download of the Companion archive runs.
---@param sysname string the `sysname` field of `vim.uv.os_uname()`
---@return string[]
function M.download_tools(sysname)
    return { "curl", "tar", download_rule.checksum_tool(sysname) }
end

M.companion_path_fix = "Set companion_path to a folder that holds a build of the companion."

---@param dotnet string the path of the dotnet executable, or the command when PATH does not hold it
---@param floor integer
---@return string
function M.sdk_found(dotnet, floor)
    return string.format("%s has a .NET %d SDK or newer.", dotnet, floor)
end

---@param version string the client version
---@param folder string
---@return string
function M.companion_missing(version, folder)
    return string.format("No companion for v%s is at %s.", version, folder)
end

M.companion_missing_fix = { "Open an F# script (.fsx) to download the companion.", M.companion_path_fix }

-- The companion of companion_path tells its version only in its ready envelope.
---@param version string? the version of the ready envelope, or nil before the companion is ready
---@param folder string
---@return string
function M.companion_path_found(version, folder)
    if version == nil then
        return string.format("The companion is at %s (companion_path). It reports its version when it starts.", folder)
    end
    return string.format("The companion v%s is at %s (companion_path).", version, folder)
end

---@param tool string
---@param path string
---@return string
function M.tool_found(tool, path)
    return string.format("%s is at %s.", tool, path)
end

---@param tool string
---@return string
function M.tool_missing(tool)
    return string.format("%s is not on PATH. The client needs %s to download the companion.", tool, tool)
end

---@param tool string
---@return string[]
function M.tool_fix(tool)
    return { string.format("Install %s.", tool), M.companion_path_fix }
end

---@param tool string
---@return string
function M.tool_not_needed(tool)
    return string.format("%s is not on PATH. companion_path is in use, so the client downloads nothing.", tool)
end

---@class fshttp.HealthParser
---@field language string
---@field degraded string what the Response buffer shows with no parser

---@type fshttp.HealthParser[]
M.parsers = {
    {
        language = "json",
        degraded = "A JSON body shows pretty-printed, with folds from its structure and no highlights.",
    },
    { language = "xml", degraded = "An XML body shows as plain text, with no highlights and no folds." },
    { language = "html", degraded = "An HTML body shows as plain text, with no highlights and no folds." },
}

---@param parser fshttp.HealthParser
---@return string
function M.parser_found(parser)
    return string.format("The tree-sitter parser for %s is installed.", parser.language)
end

---@param parser fshttp.HealthParser
---@return string
function M.parser_missing(parser)
    return string.format("No tree-sitter parser for %s. %s", parser.language, parser.degraded)
end

---@param parser fshttp.HealthParser
---@return string
function M.parser_fix(parser)
    return string.format("Install the %s parser, for example with :TSInstall %s.", parser.language, parser.language)
end

M.images_on = "snacks.nvim shows images in this terminal."

-- The WARN line when no image can show. `reason` is the reason that the line below the Body title shows.
---@param reason string
---@return string
function M.images_missing(reason)
    return string.format("No image can show: %s. An image body shows its pixel size and no image.", reason)
end

M.images_fix = {
    "Install snacks.nvim, and use a terminal that its image module supports.",
    "Run :FsHttp open to show an image body in the system viewer.",
}

M.images_off = "Images are turned off (response_buffer.images = false)."

---@param companion_version string
---@param client_version string
---@return string
function M.version_matches(companion_version, client_version)
    return string.format("The companion v%s matches the client v%s.", companion_version, client_version)
end

M.version_pending = "The version check runs when the companion is ready."

M.setup_applied = "setup() applied the options."

M.setup_not_called = "No setup() call: the client uses the defaults."

return M
