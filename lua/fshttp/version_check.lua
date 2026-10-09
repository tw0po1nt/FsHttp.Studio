local M = {}

-- A Beta build carries a suffix such as `-beta.2`, and the rule ignores it.
---@param version string
---@return string
local function without_suffix(version)
    return (version:match("^([^-]*)"))
end

-- A companion that is older than the version check sends no version, and that is a mismatch.
---@param client_version string
---@param companion_version string? the version of the ready envelope
---@return boolean
function M.matches(client_version, companion_version)
    if type(companion_version) ~= "string" then
        return false
    end
    return without_suffix(companion_version) == without_suffix(client_version)
end

---@param client_version string
---@param companion_version string?
---@return string
function M.mismatch_notice(client_version, companion_version)
    local companion_text = "reports no version"
    if type(companion_version) == "string" then
        companion_text = "is version " .. companion_version
    end
    return string.format(
        "FsHttp.Studio is version %s, and the companion at companion_path %s. "
            .. "Set companion_path to a build of version %s, or remove companion_path.",
        client_version,
        companion_text,
        client_version
    )
end

return M
