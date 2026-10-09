-- The rules of :checkhealth fshttp that need no Neovim API: the level of each companion state, the
-- download tools, and the tree-sitter parsers with what each one gives.
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
    return { "curl", "tar", download_rule.checksum_command(sysname, "")[1] }
end

---@param tool string
---@return string
function M.tool_missing(tool)
    return string.format("%s is not on PATH. The client needs %s to download the companion.", tool, tool)
end

---@param tool string
---@return string
function M.tool_fix(tool)
    return string.format("Install %s, or set companion_path to a folder that holds a build of the companion.", tool)
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
function M.parser_missing(parser)
    return string.format("No tree-sitter parser for %s. %s", parser.language, parser.degraded)
end

---@param parser fshttp.HealthParser
---@return string
function M.parser_fix(parser)
    return string.format("Install the %s parser, for example with :TSInstall %s.", parser.language, parser.language)
end

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

return M
