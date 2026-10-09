-- The rows of the Status line text. The VSCode extension host has the same rows.
local M = {}

M.prefix = "FsHttp.Studio: "

---@class fshttp.ScriptView
---@field kind "noFSharpDocument"|"notAScript"|"scriptPending"|"script"
---@field blocks? integer the number of located Blocks, for the kind "script"
---@field parse_failed? boolean true when the locate found a Parse failure, for the kind "script"

-- The rows of the states other than ready. Only Neovim has the download states and "companionNotFound".
local state_rows = {
    starting = "starting…",
    sdkNotFound = ".NET SDK not found",
    stopped = "companion stopped",
    downloading = "downloading companion…",
    downloadFailed = "companion download failed",
    companionNotFound = "companion not found",
}

---@param state fshttp.CompanionState? nil before the start sequence runs
---@param client_version string
---@return string
local function state_text(state, client_version)
    if state == nil then
        return "companion not started"
    elseif state == "ready" then
        return "companion ready"
    elseif state == "noRelease" then
        return "no companion for v" .. client_version
    end
    return state_rows[state]
end

---@param view fshttp.ScriptView
---@return string
local function script_text(view)
    if view.kind == "notAScript" then
        return "not an .fsx script"
    elseif view.kind == "scriptPending" then
        return "looking for requests…"
    end
    local blocks = view.blocks or 0
    local hidden = view.parse_failed and ": a syntax error can hide others" or ""
    if blocks == 1 then
        return "1 request" .. hidden
    elseif blocks > 1 then
        return string.format("%d requests", blocks) .. hidden
    elseif view.parse_failed then
        return "no requests found: syntax error"
    end
    return "no requests found"
end

-- Returns the text after the prefix, or nil to hide the Status line text. Each state other than
-- ready outranks the script view.
---@param state fshttp.CompanionState? nil before the start sequence runs
---@param view fshttp.ScriptView
---@param client_version string
---@return string?
function M.text(state, view, client_version)
    if view.kind == "noFSharpDocument" then
        return nil
    elseif state ~= "ready" then
        return state_text(state, client_version)
    end
    return script_text(view)
end

---@param state fshttp.CompanionState?
---@param view fshttp.ScriptView
---@param client_version string
---@return string?
function M.row(state, view, client_version)
    local text = M.text(state, view, client_version)
    return text and M.prefix .. text
end

-- The row of the companion state alone, for a buffer that is not F#.
---@param state fshttp.CompanionState?
---@param client_version string
---@return string
function M.state_row(state, client_version)
    return M.prefix .. state_text(state, client_version)
end

return M
