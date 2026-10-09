-- The start sequence and the stop of the one companion of this Neovim instance.
local download = require("fshttp.download")
local download_rule = require("fshttp.download_rule")
local envelope = require("fshttp.envelope")
local frame = require("fshttp.frame")
local sdk = require("fshttp.sdk")
local version_check = require("fshttp.version_check")

local M = {}

-- Only Neovim has the download states and "companionNotFound".
---@alias fshttp.CompanionState "starting"|"ready"|"sdkNotFound"|"stopped"|"downloading"|"downloadFailed"|"noRelease"|"companionNotFound"

---@class fshttp.StateNotice
---@field message string
---@field level integer

---@type vim.SystemObj?
local process
local sequence_ran = false
-- Only a companion from companion_path gets the version check, because a downloaded companion is the version of the client.
local check_companion_version = false
---@type fshttp.CompanionState?
local state
-- A state that never becomes ready keeps its notice, so that a Run in that state can show the fix again.
---@type fshttp.StateNotice?
local state_notice
-- The version that the companion sent in its ready envelope.
---@type string?
local companion_version
-- The WARN notice of a version mismatch, which :checkhealth fshttp shows again.
---@type string?
local version_mismatch_notice
---@type fun(state: fshttp.CompanionState)[]
local listeners = {}
-- The companion answers one frame at a time, so each answer belongs to the oldest sent envelope.
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
---@param notice fshttp.StateNotice? the notice that the change to `new_state` raised
local function set_state(new_state, notice)
    if state == new_state then
        return
    end
    state = new_state
    state_notice = notice
    for _, listener in ipairs(listeners) do
        listener(new_state)
    end
end

-- The callback of `dotnet --list-sdks` is a fast event, so the state change waits for the main loop.
---@param floor integer
---@param dotnet_path string?
local function report_no_sdk(floor, dotnet_path)
    local notice = { message = sdk.not_found_notice(floor, dotnet_path), level = vim.log.levels.WARN }
    notify(notice.message, notice.level)
    vim.schedule(function()
        set_state("sdkNotFound", notice)
    end)
end

-- On a version mismatch, the companion stays up and each Run goes ahead.
---@param ready_version string?
local function check_version(ready_version)
    local client_version = require("fshttp.version")
    if not version_check.matches(client_version, ready_version) then
        version_mismatch_notice = version_check.mismatch_notice(client_version, ready_version)
        notify(version_mismatch_notice, vim.log.levels.WARN)
    end
end

-- An answer that does not decode still ends the oldest sent envelope, so the next answer stays matched.
---@param encoded string
local function receive(encoded)
    local answer, decode_error = envelope.decode(encoded)
    if answer and answer.tag == "ready" then
        companion_version = answer.version
        if check_companion_version then
            check_version(answer.version)
        end
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

---@return fshttp.StateNotice? notice nil when the current state raised no notice
function M.state_notice()
    return state_notice
end

---@return string? version the version of the ready envelope, or nil before the companion is ready
function M.version()
    return companion_version
end

---@return string? notice the WARN notice of a version mismatch, or nil when the versions match
function M.version_mismatch_notice()
    return version_mismatch_notice
end

-- The folder of the companion: companion_path, or the download folder of the client version.
---@param config fshttp.Config
---@return string
function M.folder(config)
    return config.companion_path or download.folder(require("fshttp.version"))
end

-- The SDK floor that Companion.runtimeconfig.json in `folder` states.
---@param folder string
---@return integer
function M.sdk_floor(folder)
    return sdk.floor(read_file(vim.fs.joinpath(folder, "Companion.runtimeconfig.json")))
end

---@param listener fun(state: fshttp.CompanionState)
function M.on_state_change(listener)
    listeners[#listeners + 1] = listener
end

---@param outgoing table an envelope
---@param callback fun(answer: table?, decode_error: string?)
---@return boolean sent
local function send(outgoing, callback)
    if state ~= "ready" or not process then
        return false
    end
    pending[#pending + 1] = callback
    -- A write to a companion that just exited fails. The exit then abandons the callback.
    pcall(process.write, process, frame.encode(envelope.encode(outgoing)))
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

-- Runs `dotnet --list-sdks`, and tells `on_result` if an SDK at the floor of the companion in
-- `folder` is installed. The callback can run in a fast event.
---@param config fshttp.Config
---@param folder string
---@param on_result fun(found: boolean, floor: integer, dotnet: string)
local function probe_sdk(config, folder, on_result)
    local floor = M.sdk_floor(folder)
    local dotnet = sdk.dotnet_command(config.dotnet_path)

    ---@param result vim.SystemCompleted
    local function on_list_sdks(result)
        on_result(result.code == 0 and sdk.has_sdk_at_floor(floor, result.stdout or ""), floor, dotnet)
    end

    -- vim.system raises an error at once when it cannot start the executable.
    local list_sdks = { dotnet, "--list-sdks" }
    local ok = pcall(vim.system, list_sdks, { text = true, timeout = list_sdks_timeout_ms }, on_list_sdks)
    if not ok then
        on_result(false, floor, dotnet)
    end
end

-- :checkhealth writes its report in one pass, so it waits for the probe.
---@param config fshttp.Config
---@param folder string
---@return boolean found
---@return integer floor
---@return string dotnet the dotnet command that the probe ran
function M.has_sdk(config, folder)
    local found, floor, dotnet
    probe_sdk(config, folder, function(...)
        found, floor, dotnet = ...
    end)
    -- vim.system stops `dotnet` at its timeout, so the callback runs before this bound.
    vim.wait(list_sdks_timeout_ms * 2, function()
        return found ~= nil
    end)
    return found == true, floor, dotnet
end

-- Checks the SDK floor of the companion in `folder`, and starts the companion.
---@param config fshttp.Config
---@param folder string
local function check_sdk_and_spawn(config, folder)
    set_state("starting")

    local companion_dll = vim.fs.joinpath(folder, "Companion.dll")
    probe_sdk(config, folder, function(found, floor, dotnet)
        if found then
            vim.schedule(function()
                spawn(dotnet, companion_dll)
            end)
        else
            report_no_sdk(floor, config.dotnet_path)
        end
    end)
end

-- A state that needs a fix raises its notice one time and keeps it for a later Run.
---@param new_state fshttp.CompanionState
---@param message string
---@param level integer
local function fail_start(new_state, message, level)
    local notice = { message = message, level = level }
    notify(notice.message, notice.level)
    set_state(new_state, notice)
end

-- Downloads the Companion archive of the client version, and then checks the SDK and starts the companion.
---@param config fshttp.Config
local function download_and_spawn(config)
    local version = require("fshttp.version")
    local folder = M.folder(config)
    if download.is_installed(folder) then
        check_sdk_and_spawn(config, folder)
        return
    end

    -- The state has no kept notice, so a Run in this state waits for the companion.
    set_state("downloading")
    notify(download_rule.downloading_notice(version), vim.log.levels.INFO)
    download.fetch(version, function(result)
        if result.kind == "installed" then
            check_sdk_and_spawn(config, result.folder)
        else
            ---@type fshttp.CompanionState
            local new_state = result.kind == "noRelease" and "noRelease" or "downloadFailed"
            fail_start(new_state, download_rule.failure_notice(result, version), vim.log.levels.ERROR)
        end
    end)
end

-- Runs the start sequence once for each Neovim instance: get the companion, check the SDK floor,
-- and start the companion.
---@param config fshttp.Config
function M.start(config)
    if sequence_ran then
        return
    end
    sequence_ran = true

    vim.api.nvim_create_autocmd("VimLeavePre", {
        group = vim.api.nvim_create_augroup("fshttp.companion", { clear = true }),
        callback = M.stop,
    })

    local folder = config.companion_path
    check_companion_version = folder ~= nil
    if folder == nil then
        download_and_spawn(config)
    elseif download.is_installed(folder) then
        check_sdk_and_spawn(config, folder)
    else
        fail_start("companionNotFound", download_rule.not_found_notice(folder), vim.log.levels.ERROR)
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
