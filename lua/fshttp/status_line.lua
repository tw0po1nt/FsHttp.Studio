-- Each change of the Status line text fires a User autocmd.
local companion = require("fshttp.companion")
local locator = require("fshttp.locator")
local status_line_text = require("fshttp.status_line_text")

local M = {}

M.event = "FsHttpStatusLineTextChanged"

local watching = false
---@type string?
local last_row

---@param buf integer
---@return fshttp.ScriptView
local function script_view(buf)
    if vim.bo[buf].filetype ~= "fsharp" then
        return { kind = "noFSharpDocument" }
    elseif not locator.is_script(buf) then
        return { kind = "notAScript" }
    end
    local answer = locator.answer(buf)
    if not answer then
        return { kind = "scriptPending" }
    end
    return { kind = "script", blocks = answer.blocks, parse_failed = answer.parse_failed }
end

---@param buf integer
---@return string? row nil when the buffer does not have the fsharp filetype
function M.row(buf)
    return status_line_text.row(companion.state(), script_view(buf), require("fshttp.version"))
end

function M.echo()
    local row = M.row(vim.api.nvim_get_current_buf())
        or status_line_text.state_row(companion.state(), require("fshttp.version"))
    vim.api.nvim_echo({ { row } }, false, {})
end

local function changed()
    last_row = M.row(vim.api.nvim_get_current_buf())
    vim.api.nvim_exec_autocmds("User", { pattern = M.event, modeline = false })
    vim.cmd.redrawstatus({ bang = true })
end

-- A buffer switch, such as to a buffer that is not a Script, can change the script view with no new locate answer.
local function current_buffer_changed()
    if M.row(vim.api.nvim_get_current_buf()) ~= last_row then
        changed()
    end
end

function M.watch()
    if watching then
        return
    end
    watching = true
    companion.on_state_change(changed)
    locator.on_answer_change(changed)
    vim.api.nvim_create_autocmd({ "BufEnter", "WinEnter", "FileType" }, {
        group = vim.api.nvim_create_augroup("fshttp.status_line", { clear = true }),
        callback = current_buffer_changed,
    })
end

return M
