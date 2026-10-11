local download = require("fshttp.download")
local download_rule = require("fshttp.download_rule")
local envelope = require("fshttp.envelope")
local frame = require("fshttp.frame")
local notify = require("fshttp.notify").notify
local notify_scheduled = require("fshttp.notify").notify_scheduled
local process_stop = require("fshttp.process_stop")
local refusals = require("fshttp.refusals")
local restart = require("fshttp.restart")
local sdk = require("fshttp.sdk")
local version_check = require("fshttp.version_check")

local M = {}

-- Only Neovim has the download states and "companionNotFound".
---@alias fshttp.CompanionState "starting"|"ready"|"sdkNotFound"|"stopped"|"downloading"|"downloadFailed"|"noRelease"|"companionNotFound"

---@class fshttp.StateNotice
---@field message string
---@field level integer

---@alias fshttp.AbandonReason "stopped"|"restarted"

---@type vim.SystemObj?
local process
local sequence_ran = false
local session = restart.initial()
-- Only a companion from companion_path gets the version check, because a downloaded companion is the version of the client.
local check_companion_version = false
---@type fshttp.CompanionState?
local state
-- A state that never becomes ready keeps its notice, so that a Run in that state can show the fix again.
---@type fshttp.StateNotice?
local state_notice
---@type string?
local companion_version
---@type string?
local version_mismatch_notice
---@type fun(state: fshttp.CompanionState)[]
local listeners = {}
-- The companion answers one frame at a time, so each answer belongs to the oldest sent envelope.
---@type fun(answer: table?, decode_error: string?, reason: fshttp.AbandonReason?)[]
local pending = {}
---@type fun()?
local cancel_download
---@type fun()[]? nil while no stop is in progress
local stop_waiters

local list_sdks_timeout_ms = 10000
local poll_ms = 10

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
---@param notice fshttp.StateNotice?
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
---@param epoch integer
local function report_no_sdk(floor, dotnet_path, epoch)
    vim.schedule(function()
        if not restart.is_current(session, epoch) then
            return
        end
        local notice = { message = sdk.not_found_notice(floor, dotnet_path), level = vim.log.levels.WARN }
        notify(notice.message, notice.level)
        set_state("sdkNotFound", notice)
    end)
end

---@param ready_version string?
local function check_version(ready_version)
    local client_version = require("fshttp.version")
    if not version_check.matches(client_version, ready_version) then
        version_mismatch_notice = version_check.mismatch_notice(client_version, ready_version)
        notify_scheduled(version_mismatch_notice, vim.log.levels.WARN)
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

-- The exit of a retired process goes to no listener, so the stop reads this table.
---@type table<vim.SystemObj, boolean>
local exited = setmetatable({}, { __mode = "k" })

---@param reason fshttp.AbandonReason
local function abandon_pending(reason)
    local abandoned = pending
    pending = {}
    for _, callback in ipairs(abandoned) do
        callback(nil, nil, reason)
    end
end

---@param exiting vim.SystemObj?
---@param epoch integer
local function on_exit(exiting, epoch)
    if exiting then
        exited[exiting] = true
    end
    if not restart.is_current(session, epoch) then
        return
    end
    process = nil
    set_state("stopped")
    abandon_pending("stopped")
end

---@param dotnet string
---@param companion_dll string
---@param epoch integer
local function spawn(dotnet, companion_dll, epoch)
    local parser = frame.parser()
    ---@type vim.SystemObj?
    local spawned
    local ok, started = pcall(
        vim.system,
        { dotnet, companion_dll },
        {
            stdin = true,
            detach = process_stop.detach_on_spawn(vim.uv.os_uname().sysname),
            stdout = function(_, chunk)
                local encoded_envelopes = chunk and parser:push(chunk) or {}
                if #encoded_envelopes > 0 then
                    vim.schedule(function()
                        if not restart.is_current(session, epoch) then
                            return
                        end
                        for _, encoded in ipairs(encoded_envelopes) do
                            receive(encoded)
                        end
                    end)
                end
            end,
            -- The companion writes its own log to stderr.
            stderr = function() end,
        },
        vim.schedule_wrap(function()
            on_exit(spawned, epoch)
        end)
    )
    if not ok then
        set_state("stopped")
        return
    end
    spawned = started
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

---@return string? version nil before the companion is ready
function M.version()
    return companion_version
end

---@return string? notice nil when the versions match
function M.version_mismatch_notice()
    return version_mismatch_notice
end

---@param config fshttp.Config
---@return string
function M.folder(config)
    return config.companion_path or download.folder(require("fshttp.version"))
end

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
---@param callback fun(answer: table?, decode_error: string?, reason: fshttp.AbandonReason?)
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

-- The callback gets nil when the companion stops or restarts before it answers.
---@param source string
---@param callback fun(blocks: table?, decode_error: string?, reason: fshttp.AbandonReason?)
---@return boolean sent false when the companion is not ready, and then the callback never runs
function M.locate(source, callback)
    return send({ tag = "locate", source = source }, callback)
end

-- The callback gets nil and no decode error when the companion stops or restarts before it answers.
---@param run_envelope table
---@param callback fun(outcome: table?, decode_error: string?, reason: fshttp.AbandonReason?)
---@return boolean sent false when the companion is not ready, and then the callback never runs
function M.run(run_envelope, callback)
    return send(run_envelope, callback)
end

-- The callback can run in a fast event.
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

---@param config fshttp.Config
---@param folder string
---@param epoch integer
local function check_sdk_and_spawn(config, folder, epoch)
    set_state("starting")

    local companion_dll = vim.fs.joinpath(folder, "Companion.dll")
    probe_sdk(config, folder, function(found, floor, dotnet)
        if found then
            vim.schedule(function()
                if restart.is_current(session, epoch) then
                    spawn(dotnet, companion_dll, epoch)
                end
            end)
        else
            report_no_sdk(floor, config.dotnet_path, epoch)
        end
    end)
end

---@param new_state fshttp.CompanionState
---@param message string
---@param level integer
local function fail_start(new_state, message, level)
    local notice = { message = message, level = level }
    notify_scheduled(notice.message, notice.level)
    set_state(new_state, notice)
end

---@param config fshttp.Config
---@param epoch integer
local function download_and_spawn(config, epoch)
    local version = require("fshttp.version")
    local folder = M.folder(config)
    if download.is_installed(folder) then
        check_sdk_and_spawn(config, folder, epoch)
        return
    end

    -- The state has no kept notice, so a Run in this state waits for the companion.
    set_state("downloading")
    notify_scheduled(download_rule.downloading_notice(version), vim.log.levels.INFO)
    cancel_download = download.fetch(version, function(result)
        if not restart.is_current(session, epoch) then
            return
        end
        cancel_download = nil
        if result.kind == "installed" then
            check_sdk_and_spawn(config, result.folder, epoch)
        else
            ---@type fshttp.CompanionState
            local new_state = result.kind == "noRelease" and "noRelease" or "downloadFailed"
            fail_start(new_state, download_rule.failure_notice(result, version), vim.log.levels.ERROR)
        end
    end)
end

---@param config fshttp.Config
local function run_sequence(config)
    local epoch = session.epoch
    local folder = config.companion_path
    check_companion_version = folder ~= nil
    if folder == nil then
        download_and_spawn(config, epoch)
    elseif download.is_installed(folder) then
        check_sdk_and_spawn(config, folder, epoch)
    else
        fail_start("companionNotFound", download_rule.not_found_notice(folder), vim.log.levels.ERROR)
    end
end

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

    run_sequence(config)
end

-- An idle companion exits when its stdin closes. Each waiting Run gets nil.
---@param reason fshttp.AbandonReason
---@param on_done fun() runs after the process is gone
local function stop_async(reason, on_done)
    if stop_waiters then
        table.insert(stop_waiters, on_done)
        return
    end
    local stopping = process
    if not stopping then
        vim.schedule(on_done)
        return
    end
    process = nil
    stop_waiters = { on_done }
    abandon_pending(reason)

    local settled = false
    local function finish()
        if settled then
            return
        end
        settled = true
        local waiters = stop_waiters or {}
        stop_waiters = nil
        for _, waiter in ipairs(waiters) do
            waiter()
        end
    end

    local function kill()
        local command = process_stop.kill_command(vim.uv.os_uname().sysname, stopping.pid)
        if command.kind == "signal_group" then
            vim.uv.kill(-command.pid, "sigkill")
            finish()
            return
        end
        local ok = pcall(
            vim.system,
            command.command,
            { timeout = process_stop.taskkill_timeout_ms },
            vim.schedule_wrap(finish)
        )
        if not ok then
            finish()
        end
    end

    -- The write can fail when the companion is already exiting.
    pcall(stopping.write, stopping, nil)

    local deadline = vim.uv.now() + process_stop.stop_delay_ms
    local timer = assert(vim.uv.new_timer())
    timer:start(
        poll_ms,
        poll_ms,
        vim.schedule_wrap(function()
            if timer:is_closing() then
                return
            end
            local gone = exited[stopping]
            if not gone and vim.uv.now() < deadline then
                return
            end
            timer:stop()
            timer:close()
            if gone then
                finish()
            else
                kill()
            end
        end)
    )
end

---@param config fshttp.Config
---@return boolean restarted false when a stop is in progress
function M.restart(config)
    if not sequence_ran then
        M.start(config)
        return true
    end
    local decision
    session, decision = restart.on_command(session)
    if decision == "notifyRestarting" then
        notify(refusals.companion_restarting, vim.log.levels.INFO)
        return false
    end
    if cancel_download then
        cancel_download()
        cancel_download = nil
    end
    companion_version = nil
    version_mismatch_notice = nil
    set_state("starting")
    local epoch = session.epoch
    stop_async("restarted", function()
        session = restart.on_stopped(session)
        if restart.is_current(session, epoch) then
            run_sequence(config)
        end
    end)
    return true
end

-- VimLeavePre needs the stop to end before Neovim exits.
function M.stop()
    session = restart.on_quit(session)
    local done = false
    stop_async("stopped", function()
        done = true
    end)
    local margin_ms = 500
    vim.wait(process_stop.stop_delay_ms + process_stop.taskkill_timeout_ms + margin_ms, function()
        return done
    end, poll_ms)
end

return M
