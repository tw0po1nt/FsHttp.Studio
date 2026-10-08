-- The start sequence and the stop of the one companion of this Neovim instance.
local envelope = require("fshttp.envelope")
local frame = require("fshttp.frame")
local sdk = require("fshttp.sdk")

local M = {}

---@alias fshttp.CompanionState "starting" | "ready" | "sdk_not_found" | "stopped"

---@type vim.SystemObj?
local process
---@type fshttp.CompanionState?
local state
local sequence_ran = false

local list_sdks_timeout_ms = 10000

---@param message string
---@param level integer
local function notify(message, level)
    vim.schedule(function()
        vim.notify(message, level, { title = "FsHttp.Studio" })
    end)
end

---@param path string
---@return string?
local function read_file(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end
    local text = file:read("*a")
    file:close()
    return text
end

---@param floor integer
---@param dotnet_path string?
local function report_no_sdk(floor, dotnet_path)
    state = "sdk_not_found"
    notify(sdk.not_found_notice(floor, dotnet_path), vim.log.levels.WARN)
end

---@param dotnet string
---@param companion_dll string
local function spawn(dotnet, companion_dll)
    local parser = frame.parser()
    local ok, started = pcall(vim.system, { dotnet, companion_dll }, {
        stdin = true,
        stdout = function(_, chunk)
            if not chunk then
                return
            end
            for _, payload in ipairs(parser:push(chunk)) do
                local decoded = envelope.decode(payload)
                if decoded and decoded.tag == "ready" then
                    state = "ready"
                end
            end
        end,
        -- The companion writes its own log to stderr. The client shows no part of it.
        stderr = function() end,
    }, function()
        state = "stopped"
        process = nil
    end)
    if not ok then
        state = "stopped"
        return
    end
    process = started
    started:write(frame.encode(envelope.encode({ tag = "hello" })))
end

-- Runs the start sequence once for each Neovim instance: get the companion, check the SDK floor,
-- and start the companion.
---@param config fshttp.Config
function M.start(config)
    if sequence_ran then
        return
    end
    sequence_ran = true

    local folder = config.companion_path
    if folder == nil then
        -- TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/279): download the Companion archive.
        return
    end

    vim.api.nvim_create_autocmd("VimLeavePre", {
        group = vim.api.nvim_create_augroup("fshttp.companion", { clear = true }),
        callback = M.stop,
    })

    state = "starting"
    local floor = sdk.floor(read_file(vim.fs.joinpath(folder, "Companion.runtimeconfig.json")))
    local dotnet = sdk.dotnet_command(config.dotnet_path)
    local companion_dll = vim.fs.joinpath(folder, "Companion.dll")

    ---@param result vim.SystemCompleted
    local function on_list_sdks(result)
        if result.code == 0 and sdk.has_sdk_at_floor(floor, result.stdout or "") then
            vim.schedule(function()
                spawn(dotnet, companion_dll)
            end)
        else
            report_no_sdk(floor, config.dotnet_path)
        end
    end

    -- vim.system raises an error at once when it cannot start the executable.
    local list_sdks = { dotnet, "--list-sdks" }
    local ok = pcall(vim.system, list_sdks, { text = true, timeout = list_sdks_timeout_ms }, on_list_sdks)
    if not ok then
        report_no_sdk(floor, config.dotnet_path)
    end
end

-- Sends SIGTERM to the companion and returns at once, so a hung companion cannot hold the exit of
-- Neovim.
function M.stop()
    if process then
        process:kill("sigterm")
        process = nil
    end
end

---@return fshttp.CompanionState?
function M.state()
    return state
end

return M
