-- `:FsHttp run`: a Run of the Block at the cursor, with its result in the Response buffer.
local companion = require("fshttp.companion")
local locator = require("fshttp.locator")
local refusals = require("fshttp.refusals")
local picker = require("fshttp.picker")
local response_buffer = require("fshttp.response_buffer")
local response_view = require("fshttp.response_view")
local run_target = require("fshttp.run_target")

local M = {}

local not_a_script_notice =
    ":FsHttp run runs a request from an F# script (.fsx). Open a script and put the cursor in a request."
local wait_notice = "The FsHttp.Studio companion is starting. This Run starts when it is ready."

-- Only the result of the latest Run reaches the Response buffer.
local generation = 0

---@param message string
---@param level integer
local function notify(message, level)
    vim.notify(message, level, { title = "FsHttp.Studio" })
end

---@param ok_envelope table a decoded ok envelope
---@param total_ms number
---@return fshttp.RunResult
local function to_result(ok_envelope, total_ms)
    local request = ok_envelope.request
    return {
        status = ok_envelope.status,
        reason = ok_envelope.reason,
        headers = ok_envelope.headers,
        content_type = ok_envelope.content_type,
        body = vim.base64.decode(ok_envelope.body_base64),
        request_ms = ok_envelope.request_ms,
        total_ms = total_ms,
        request = {
            method = request.method,
            url = request.url,
            headers = request.headers,
            body = {
                state = request.body.state,
                bytes = vim.base64.decode(request.body.base64),
                reason = request.body.reason,
            },
        },
    }
end

-- A nil outcome with no decode error means that the companion stopped before it answered.
---@param outcome table?
---@param decode_error string?
---@param total_ms number
---@return fshttp.ResponseView
local function view_for(outcome, decode_error, total_ms)
    if outcome == nil then
        return response_view.message(decode_error or refusals.companion_stopped.detail)
    elseif outcome.tag == "ok" then
        return response_view.result(to_result(outcome, total_ms))
    elseif outcome.tag == "compileError" then
        -- TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/276): move to a Compile error position with <CR>.
        return response_view.compile_error(outcome.diagnostics)
    elseif outcome.tag == "runtimeError" then
        return response_view.runtime_error(outcome.message)
    elseif outcome.tag == "refused" then
        return response_view.refused(outcome.code, outcome.name)
    elseif outcome.tag == "error" then
        return response_view.message(outcome.message)
    end
    return response_view.message("The companion gave a " .. outcome.tag .. " envelope in place of a Run outcome.")
end

---@param buf integer
---@param source string the text of the locate that gave the Block index
---@param block_index integer
local function start_run(buf, source, block_index)
    generation = generation + 1
    local this_run = generation
    response_buffer.show_running()
    local options = require("fshttp").config()
    local run_envelope =
        run_target.run_envelope(source, block_index, vim.api.nvim_buf_get_name(buf), options.request_timeout_ms)
    local started = vim.uv.hrtime()
    local sent = companion.run(run_envelope, function(outcome, decode_error)
        if this_run ~= generation then
            return
        end
        local total_ms = (vim.uv.hrtime() - started) / 1e6
        local ok, view = pcall(view_for, outcome, decode_error, total_ms)
        response_buffer.show(ok and view or response_view.message(tostring(view)))
    end)
    if not sent then
        response_buffer.show(response_view.message(refusals.companion_stopped.detail))
    end
end

---@param blocks table? the answer to the locate
---@param buf integer
---@param source string
---@param cursor_line integer
local function on_located(blocks, buf, source, cursor_line)
    if blocks == nil then
        notify(refusals.companion_stopped.detail, vim.log.levels.WARN)
        return
    elseif blocks.tag ~= "blocks" then
        notify(blocks.message or ("The companion gave a " .. blocks.tag .. " envelope."), vim.log.levels.ERROR)
        return
    end
    local target = run_target.for_cursor(blocks, cursor_line)
    if target.kind == "run" then
        start_run(buf, source, target.block_index)
    elseif target.kind == "notice" then
        notify(target.message, vim.log.levels[target.level])
    else
        picker.open(blocks, buf, function(block_index, refused)
            if refused then
                local code = blocks.ranges[block_index + 1].refusal
                local entry = refusals.codes[code] or refusals.codes[refusals.fallback_code]
                notify(entry.detail, vim.log.levels.WARN)
            else
                start_run(buf, source, block_index)
            end
        end)
    end
end

---@param buf integer
local function buffer_source(buf)
    return table.concat(vim.api.nvim_buf_get_lines(buf, 0, -1, false), "\n") .. "\n"
end

-- The text and the cursor that a Run records while the companion starts. At most one Run waits.
---@type { buf: integer, source: string, cursor_line: integer }?
local pending_wait

-- Locates the buffer text again, so the Block index matches the text of the run envelope.
local function locate_and_run(buf)
    local source = buffer_source(buf)
    local cursor_line = vim.api.nvim_win_get_cursor(0)[1]
    local sent = companion.locate(source, function(blocks)
        on_located(blocks, buf, source, cursor_line)
    end)
    if not sent then
        notify(refusals.companion_stopped.detail, vim.log.levels.WARN)
    end
end

-- Runs the recorded text and cursor once the companion is ready.
---@param wait { buf: integer, source: string, cursor_line: integer }
local function resume_wait(wait)
    local sent = companion.locate(wait.source, function(blocks)
        on_located(blocks, wait.buf, wait.source, wait.cursor_line)
    end)
    if not sent then
        notify(refusals.companion_stopped.detail, vim.log.levels.WARN)
    end
end

-- The one listener that acts on a Run that waits for the companion. It no-ops while no Run waits.
companion.on_state_change(function(state)
    if pending_wait == nil then
        return
    end
    local wait = pending_wait
    pending_wait = nil
    if state == "ready" then
        resume_wait(wait)
    elseif state == "stopped" then
        notify(refusals.companion_stopped.detail, vim.log.levels.WARN)
    else
        -- A state that never becomes ready, for example no SDK: show its notice again.
        local notice = companion.state_notice()
        if notice then
            notify(notice.message, notice.level)
        end
    end
end)

-- Records the text and cursor of a Run while the companion starts, so it runs when the companion
-- is ready. A new :FsHttp run replaces the wait.
---@param buf integer
local function begin_wait(buf)
    local source = buffer_source(buf)
    local cursor_line = vim.api.nvim_win_get_cursor(0)[1]
    pending_wait = { buf = buf, source = source, cursor_line = cursor_line }
    notify(wait_notice, vim.log.levels.INFO)
end

-- A Run in a companion state that is not ready and not stopped.
---@param buf integer
local function handle_not_ready(buf)
    local notice = companion.state_notice()
    if notice then
        -- A state that never becomes ready shows its notice again. No Run starts.
        notify(notice.message, notice.level)
    else
        begin_wait(buf)
    end
end

-- Each Run locates the buffer text again, so the Block index matches the text of the run envelope.
function M.at_cursor()
    local buf = vim.api.nvim_get_current_buf()
    if not locator.is_script(buf) then
        notify(not_a_script_notice, vim.log.levels.INFO)
        return
    end
    require("fshttp").start()

    local state = companion.state()
    if state == "stopped" then
        notify(refusals.companion_stopped.detail, vim.log.levels.WARN)
        return
    elseif state ~= "ready" then
        handle_not_ready(buf)
        return
    end

    locate_and_run(buf)
end

return M
