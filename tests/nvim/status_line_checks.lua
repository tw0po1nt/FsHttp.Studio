local MiniTest = require("mini.test")
local harness = require("nvim.harness")
local refusals = require("fshttp.refusals")

local one_fixture = harness.ui_fixture("status-bar-one.fsx")
local many_fixture = harness.ui_fixture("status-bar-many.fsx")
local empty_fixture = harness.ui_fixture("no-requests-empty.fsx")
local module_fixture = harness.ui_fixture("status-bar-module.fs")
local other_fixture = harness.ui_fixture("status-bar-other.md")
local above_fixture = harness.ui_fixture("no-requests-above.fsx")
local between_fixture = harness.ui_fixture("no-requests-between.fsx")
local slow_fixture = harness.fixture("slow.fsx")
-- The line of the one Block in the slow fixture.
local slow_block_line = 26

---@param text string
---@return string
local function row(text)
    return "FsHttp.Studio: " .. text
end

local hidden = "nil"
local pending_row = row("looking for requests…")
local one_row = row("1 request")
local many_row = row("2 requests")
local empty_row = row("no requests found")
local stopped_row = row("companion stopped")

local T = MiniTest.new_set()

T["clean scripts report one, many, and zero requests"] = function()
    local child = harness.harness_setup_child()

    harness.edit(child, one_fixture)
    harness.expect_status(child, "1 request on a script with one Block", one_row)

    harness.edit(child, many_fixture)
    harness.expect_status(child, "2 requests on a script with two Blocks", many_row)

    harness.edit(child, empty_fixture)
    harness.expect_status(child, "no requests found on a clean script with no Block", empty_row)
end

T["an .fs module gives not an .fsx script"] = function()
    local child = harness.harness_setup_child()

    harness.edit(child, module_fixture)

    harness.expect_status(child, "not an .fsx script on an .fs module", row("not an .fsx script"))
end

T["syntax-error scripts report total loss and partial loss"] = function()
    local child = harness.harness_setup_child()

    harness.edit(child, above_fixture)
    harness.expect_status(child, "the total loss row", row("no requests found: syntax error"))

    harness.edit(child, between_fixture)
    harness.expect_status(child, "the partial loss row", row("1 request: a syntax error can hide others"))
end

T["status() gives nil outside F#, and the row again on the script"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, one_fixture)
    harness.expect_status(child, "1 request before the buffer that is not F#", one_row)

    harness.edit(child, other_fixture)
    harness.expect_status(child, "nil in a Markdown buffer", hidden)

    harness.edit(child, one_fixture)
    harness.expect_status(child, "1 request after the return to the script", one_row)
end

T["a buffer switch gives looking for requests… until the locate answer arrives"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, empty_fixture)
    harness.expect_status(child, "no requests found on the empty script first", empty_row)
    harness.edit(child, many_fixture)
    harness.expect_status(child, "2 requests before the switch back", many_row)

    local before_answer = harness.edit_and_read_status(child, empty_fixture)

    assert.equal(pending_row, before_answer, "the row before the locate answer arrives")
    harness.expect_status(child, "no requests found after the locate answer", empty_row)
end

T["the count follows the Active document with a second script open"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, empty_fixture)
    harness.expect_status(child, "no requests found on the empty script first", empty_row)

    harness.edit(child, many_fixture)
    harness.expect_status(child, "2 requests while the empty script stays open", many_row)

    harness.holds_for_settle("2 requests through the settle window", function()
        return harness.status(child) == many_row
    end)
    harness.edit(child, empty_fixture)
    harness.expect_status(child, "no requests found on the empty script again", empty_row)
end

T["companion death: the Response buffer shows the stopped text, and status() gives nil there"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, slow_fixture)
    harness.expect_status(child, "1 request before the companion stops", one_row)
    local companions = harness.new_companion_pids(known)
    assert.equal(1, #companions, "the new child Neovim started one companion")

    local ok, err = pcall(function()
        harness.run_at(child, slow_block_line)
        harness.eventually(harness.response_deadline_ms, "Running… in the Response buffer", function()
            local lines = harness.response_buffer(child).lines or {}
            return (lines[1] or ""):find("^Running…") ~= nil
        end)

        vim.uv.kill(companions[1], "sigkill")
        harness.eventually(harness.response_deadline_ms, "the stopped text in the Response buffer", function()
            local lines = harness.response_buffer(child).lines or {}
            return table.concat(lines, "\n"):find(refusals.companion_stopped.detail, 1, true) ~= nil
                and (lines[1] or ""):find("^Running…") == nil
        end)
        harness.expect_status(child, "companion stopped on the script", stopped_row)

        harness.lua_get(
            child,
            [[(function()
            for _, win in ipairs(vim.api.nvim_tabpage_list_wins(0)) do
                if vim.bo[vim.api.nvim_win_get_buf(win)].filetype == "fshttp_response" then
                    vim.api.nvim_set_current_win(win)
                end
            end
        end)()]]
        )
        harness.expect_status(child, "nil while the Response buffer has focus", hidden)

        harness.cmd(child, "wincmd p")
        harness.expect_status(child, "companion stopped on the script again", stopped_row)
        harness.holds_for_settle("no notice for the start, the ready state, the Run, or the companion death", function()
            return #harness.notices(child) == 0
        end)
    end)
    harness.release_slow()
    if not ok then
        error(err, 0)
    end
end

T["each change of the companion state or the script view fires the User autocmd"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.lua_get(
        child,
        [[(function()
        _G.fshttp_suite_rows = {}
        vim.api.nvim_create_autocmd("User", {
            pattern = "FsHttpStatusLineTextChanged",
            callback = function()
                table.insert(_G.fshttp_suite_rows, tostring(require("fshttp").status()))
            end,
        })
    end)()]]
    )
    local function rows()
        return table.concat(harness.lua_get(child, "_G.fshttp_suite_rows"), "\n")
    end

    harness.edit(child, one_fixture)

    local ready_rows = table.concat({ row("starting…"), pending_row, one_row }, "\n")
    harness.eventually_equal(harness.status_line_text_deadline_ms, "one row for each change", ready_rows, rows)
    local companions = harness.new_companion_pids(known)
    assert.equal(1, #companions, "the new child Neovim started one companion")

    vim.uv.kill(companions[1], "sigkill")

    harness.eventually_equal(
        harness.status_line_text_deadline_ms,
        "the stopped row after the companion death",
        ready_rows .. "\n" .. stopped_row,
        rows
    )
end

T[":FsHttp status echoes the row in a script, and the companion state row in a buffer that is not F#"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, many_fixture)
    harness.expect_status(child, "2 requests on the script", many_row)

    assert.equal(many_row, harness.fshttp_status_echo(child), "the echo in the script")

    harness.edit(child, other_fixture)
    assert.equal(row("companion ready"), harness.fshttp_status_echo(child), "the echo in a Markdown buffer")
end

---@param child nvim_suite.Child
---@return string
local function lualine_text(child)
    return harness.lua_get(
        child,
        [[vim.api.nvim_eval_statusline(vim.wo.statusline ~= "" and vim.wo.statusline or vim.o.statusline, { maxwidth = 500 }).str]]
    )
end

T["the lualine entry shows the Status line text, and status_line.lualine = false removes it"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() }, nil, true)
    harness.edit(child, harness.fixture("block-marks.fsx"))

    harness.eventually(harness.status_line_text_deadline_ms, "2 requests in the lualine statusline", function()
        return lualine_text(child):find(many_row, 1, true) ~= nil
    end)

    harness.lua_get(child, [[require("fshttp").setup({ status_line = { lualine = false } })]])
    harness.lua_get(child, [[require("lualine").refresh({ place = { "statusline" } })]])
    harness.eventually(harness.status_line_text_deadline_ms, "no Status line text in the lualine statusline", function()
        return lualine_text(child):find("FsHttp.Studio", 1, true) == nil
    end)
end

return T
