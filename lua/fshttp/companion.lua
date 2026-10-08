-- The start sequence and the stop of the one companion of this Neovim instance.
local envelope = require("fshttp.envelope")
local frame = require("fshttp.frame")
local sdk = require("fshttp.sdk")
local version_check = require("fshttp.version_check")

local M = {}

-- Only Neovim has the download states and "companionNotFound".
---@alias fshttp.CompanionState "starting"|"ready"|"sdkNotFound"|"stopped"|"downloading"|"downloadFailed"|"noRelease"|"companionNotFound"

---@type vim.SystemObj?
local process
local sequence_ran = false
---@type fshttp.CompanionState?
local state
---@type fun(state: fshttp.CompanionState)[]
local listeners = {}
-- The companion answers one frame at a time, so each answer belongs to the oldest request.
---@type fun(answer: table?, decode_error: string?)[]
local pending = {}

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

---@param new_state fshttp.CompanionState
local function set_state(new_state)
    if state == new_state then
        return
    end
    state = new_state
    for _, listener in ipairs(listeners) do
        listener(new_state)
    end
end

-- The callback of `dotnet --list-sdks` is a fast event, so the state change waits for the main loop.
---@param floor integer
---@param dotnet_path string?
local function report_no_sdk(floor, dotnet_path)
    notify(sdk.not_found_notice(floor, dotnet_path), vim.log.levels.WARN)
    vim.schedule(function()
        set_state("sdkNotFound")
    end)
end

-- On a version mismatch, the companion stays up and each Run goes ahead.
---@param companion_version string?
local function check_version(companion_version)
    local client_version = require("fshttp.version")
    if not version_check.matches(client_version, companion_version) then
        notify(version_check.mismatch_notice(client_version, companion_version), vim.log.levels.WARN)
    end
end

-- An answer that does not decode still ends the oldest request, so the next answer stays matched.
---@param encoded string
local function receive(encoded)
    local answer, decode_error = envelope.decode(encoded)
    if answer and answer.tag == "ready" then
        check_version(answer.version)
        set_state("ready")
        return
    end
    local callback = table.remove(pending, 1)
    if callback then
        callback(answer, decode_error)
    end
end

local function on_exit()
    process = nil
    local abandoned = pending
    pending = {}
    set_state("stopped")
    for _, callback in ipairs(abandoned) do
        callback(nil)
    end
end

---@param dotnet string
---@param companion_dll string
local function spawn(dotnet, companion_dll)
    local parser = frame.parser()
    local ok, started = pcall(vim.system, { dotnet, companion_dll }, {
        stdin = true,
        stdout = function(_, chunk)
            local encoded_envelopes = chunk and parser:push(chunk) or {}
            if #encoded_envelopes > 0 then
                vim.schedule(function()
                    for _, encoded in ipairs(encoded_envelopes) do
                        receive(encoded)
                    end
                end)
            end
        end,
        -- The companion writes its own log to stderr. The client shows no part of it.
        stderr = function() end,
    }, vim.schedule_wrap(on_exit))
    if not ok then
        set_state("stopped")
        return
    end
    process = started
    started:write(frame.encode(envelope.encode({ tag = "hello" })))
end

---@return fshttp.CompanionState? state nil before the start sequence starts a companion
function M.state()
    return state
end

---@param listener fun(state: fshttp.CompanionState)
function M.on_state_change(listener)
    listeners[#listeners + 1] = listener
end

---@param request table an envelope
---@param callback fun(answer: table?, decode_error: string?)
---@return boolean sent
local function send(request, callback)
    if state ~= "ready" or not process then
        return false
    end
    pending[#pending + 1] = callback
    -- A write to a companion that just exited fails. The exit then abandons the callback.
    pcall(process.write, process, frame.encode(envelope.encode(request)))
    return true
end

-- The callback gets nil when the companion stops before it answers.
---@param source string
---@param callback fun(blocks: table?)
---@return boolean sent false when the companion is not ready, and then the callback never runs
function M.locate(source, callback)
    return send({ tag = "locate", source = source }, callback)
end

-- The callback gets nil and no decode error when the companion stops before it answers.
---@param run_envelope table
---@param callback fun(outcome: table?, decode_error: string?)
---@return boolean sent false when the companion is not ready, and then the callback never runs
function M.run(run_envelope, callback)
    return send(run_envelope, callback)
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

    set_state("starting")
    vim.api.nvim_create_autocmd("VimLeavePre", {
        group = vim.api.nvim_create_augroup("fshttp.companion", { clear = true }),
        callback = M.stop,
    })

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

return M
