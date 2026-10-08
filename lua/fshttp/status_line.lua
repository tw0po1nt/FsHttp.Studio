-- Gives the Status line text of a buffer. Each change of the text fires a User autocmd.
local companion = require("fshttp.companion")
local locator = require("fshttp.locator")
local status_line_text = require("fshttp.status_line_text")

local M = {}

M.event = "FsHttpStatusLineTextChanged"

local watching = false

---@param buf integer
---@return fshttp.ScriptView
local function script_view(buf)
    if vim.bo[buf].filetype ~= "fsharp" then
        return { kind = "noFSharpDocument" }
    elseif not vim.api.nvim_buf_get_name(buf):match("%.fsx$") then
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

-- Echoes the row of the Active document. A buffer that is not F# gets the companion state row.
function M.echo()
    local row = M.row(vim.api.nvim_get_current_buf())
        or status_line_text.state_row(companion.state(), require("fshttp.version"))
    vim.api.nvim_echo({ { row } }, false, {})
end

local function changed()
    vim.api.nvim_exec_autocmds("User", { pattern = M.event, modeline = false })
    vim.cmd.redrawstatus({ bang = true })
end

-- Only the first call has an effect.
function M.watch()
    if watching then
        return
    end
    watching = true
    companion.on_state_change(changed)
    locator.on_answer_change(changed)
end

return M
