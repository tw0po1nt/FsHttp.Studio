-- `:FsHttp run`: a Run of the Block at the cursor, with its result in the Response buffer.
local companion = require("fshttp.companion")
local locator = require("fshttp.locator")
local refusals = require("fshttp.refusals")
local response_buffer = require("fshttp.response_buffer")
local response_view = require("fshttp.response_view")
local run_target = require("fshttp.run_target")

local M = {}

local not_a_script_notice =
    ":FsHttp run runs a request from an F# script (.fsx). Open a script and put the cursor in a request."
local not_ready_notice = "The FsHttp.Studio companion is not ready. Run :FsHttp run again when it is ready."
local outside_notice = "Put the cursor in a request, then run :FsHttp run again."

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
        return response_view.result(to_result(outcome, total_ms), require("fshttp.body_syntax").parse)
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
        -- TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/271): open the picker of the located Blocks.
        notify(outside_notice, vim.log.levels.INFO)
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
        local notice = companion.state_notice()
        if notice then
            notify(notice.message, notice.level)
        else
            -- TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/271): wait for the companion, then start the Run.
            notify(not_ready_notice, vim.log.levels.INFO)
        end
        return
    end

    local source = table.concat(vim.api.nvim_buf_get_lines(buf, 0, -1, false), "\n") .. "\n"
    local cursor_line = vim.api.nvim_win_get_cursor(0)[1]
    local sent = companion.locate(source, function(blocks)
        on_located(blocks, buf, source, cursor_line)
    end)
    if not sent then
        notify(refusals.companion_stopped.detail, vim.log.levels.WARN)
    end
end

return M
