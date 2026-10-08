-- The version match rule between the plugin and the companion that it runs.
local M = {}

-- A Beta build carries a suffix such as `-beta.2`, and the rule ignores it.
---@param version string
---@return string
local function without_suffix(version)
    return (version:match("^([^-]*)"))
end

-- True when the companion version has the same major.minor.patch as the plugin version. A companion
-- that is older than the version check sends no version, and that is a mismatch.
---@param plugin_version string the version that version.lua holds
---@param companion_version string? the version of the ready envelope
---@return boolean
function M.matches(plugin_version, companion_version)
    if type(companion_version) ~= "string" then
        return false
    end
    return without_suffix(companion_version) == without_suffix(plugin_version)
end

-- The WARN notice for a version mismatch.
---@param plugin_version string
---@param companion_version string?
---@return string
function M.mismatch_notice(plugin_version, companion_version)
    local companion_text = "reports no version"
    if type(companion_version) == "string" then
        companion_text = "is version " .. companion_version
    end
    return string.format(
        "FsHttp.Studio is version %s, and the companion at companion_path %s. "
            .. "Set companion_path to a build of version %s, or remove companion_path.",
        plugin_version,
        companion_text,
        plugin_version
    )
end

return M
