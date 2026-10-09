-- The VSCode extension host uses the same SDK rule.
local json = require("fshttp.json")

local M = {}

M.download_url = "https://aka.ms/dotnet/download"

-- The floor when Companion.runtimeconfig.json is missing or does not parse.
M.fallback_floor = 10

local function major_of(version)
    local head = version:match("^([^.]*)")
    if head:match("^%d+$") then
        return tonumber(head)
    end
    return nil
end

---@param dotnet_path string?
---@return boolean
local function is_set(dotnet_path)
    return type(dotnet_path) == "string" and dotnet_path:find("%S") ~= nil
end

---@param dotnet_path string?
---@return string
function M.dotnet_command(dotnet_path)
    if is_set(dotnet_path) then
        ---@cast dotnet_path string
        return dotnet_path
    end
    return "dotnet"
end

-- The major version of runtimeOptions.framework.version is the SDK floor.
---@param runtimeconfig_text string? the text of Companion.runtimeconfig.json, or nil when the file is missing
---@return integer
function M.floor(runtimeconfig_text)
    if runtimeconfig_text == nil then
        return M.fallback_floor
    end
    local ok, config = pcall(json.decode, runtimeconfig_text)
    local version = ok
        and type(config) == "table"
        and type(config.runtimeOptions) == "table"
        and type(config.runtimeOptions.framework) == "table"
        and config.runtimeOptions.framework.version
    if type(version) ~= "string" then
        return M.fallback_floor
    end
    return major_of(version) or M.fallback_floor
end

-- The companion rolls forward onto a newer major version, so a newer SDK also runs it.
---@param floor integer
---@param list_sdks_output string
---@return boolean
function M.has_sdk_at_floor(floor, list_sdks_output)
    for line in list_sdks_output:gmatch("[^\n]+") do
        local version = line:match("^%s*(%S+)")
        local major = version and major_of(version)
        if major and major >= floor then
            return true
        end
    end
    return false
end

---@param floor integer
---@param dotnet_path string?
---@return string
function M.not_found_notice(floor, dotnet_path)
    if is_set(dotnet_path) then
        return string.format(
            "FsHttp.Studio needs a .NET %d SDK or newer, and found none at dotnet_path (%s). "
                .. "Correct the path, or remove dotnet_path to use dotnet on PATH. Get the SDK from %s.",
            floor,
            dotnet_path,
            M.download_url
        )
    end
    return string.format(
        "FsHttp.Studio needs a .NET %d SDK or newer, and found none on PATH. "
            .. "Install the SDK from %s, or set dotnet_path to a dotnet executable that has it.",
        floor,
        M.download_url
    )
end

return M
