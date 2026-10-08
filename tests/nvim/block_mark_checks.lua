local MiniTest = require("mini.test")
local harness = require("nvim.harness")
local refusals = require("fshttp.refusals")

local run_title = refusals.run_block_mark_title
local stopped_title = refusals.companion_stopped_block_mark_title

---@param line integer
---@param title string
---@return string
local function mark(line, title)
    return string.format("%d: %s [%s]", line, title, title:match("^(%S+)"))
end

---@param ... string
---@return string
local function marks(...)
    return table.concat({ ... }, "\n")
end

---@param child nvim_suite.Child
---@param subject string
---@param expected string
local function expect_block_marks(child, subject, expected)
    harness.eventually_equal(harness.block_mark_deadline_ms, subject, expected, function()
        return harness.block_marks(child)
    end)
end

---@param child nvim_suite.Child
---@param subject string
local function expect_no_block_mark_through_settle(child, subject)
    harness.holds_for_settle(subject, function()
        return harness.block_marks(child) == ""
    end)
end

local T = MiniTest.new_set()

T["no-requests Block marks"] = MiniTest.new_set()

T["no-requests Block marks"]["a syntax error above the Blocks gives the line-1 mark only"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, harness.ui_fixture("no-requests-above.fsx"))

    expect_block_marks(
        child,
        "exactly one no-requests Block mark on line 1",
        mark(1, refusals.no_blocks_parse_failure_block_mark_title)
    )
end

T["no-requests Block marks"]["a syntax error below the last Block keeps a Block mark with the run title on each Block"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, harness.ui_fixture("no-requests-below.fsx"))

    expect_block_marks(
        child,
        "a Block mark with the run title on each Block",
        marks(mark(10, run_title), mark(14, run_title))
    )
end

T["no-requests Block marks"]["a syntax error between two Blocks keeps one Block mark with the run title"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, harness.ui_fixture("no-requests-between.fsx"))

    expect_block_marks(child, "exactly one Block mark with the run title", mark(10, run_title))
end

T["no-requests Block marks"]["a clean script with no Block gets no Block mark"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, harness.ui_fixture("no-requests-above.fsx"))
    expect_block_marks(
        child,
        "the no-requests Block mark on the broken fixture first",
        mark(1, refusals.no_blocks_parse_failure_block_mark_title)
    )

    harness.edit(child, harness.ui_fixture("no-requests-empty.fsx"))

    expect_no_block_mark_through_settle(child, "no Block mark on a clean script with no Block")
end

T["the Block mark of a loop Block shows the loopBody refusal title"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, harness.ui_fixture("loop-lens.fsx"))

    expect_block_marks(
        child,
        "the refusal title on the Block in the loop",
        mark(10, refusals.codes.loopBody.block_mark_title)
    )
end

T["an edit locates the Script again after the pause"] = function()
    local child = harness.harness_setup_child()
    harness.edit(child, harness.fixture("block-marks.fsx"))
    expect_block_marks(
        child,
        "a Block mark with the run title on each of the two Blocks",
        marks(mark(8, run_title), mark(10, run_title))
    )

    harness.type_keys(child, "G", "o", "<CR>", 'http { GET "http://127.0.0.1:9/three" }', "<Esc>")

    expect_block_marks(
        child,
        "a Block mark with the run title on the new Block too",
        marks(mark(8, run_title), mark(10, run_title), mark(12, run_title))
    )
    harness.cmd(child, "bwipeout!")
end

T["a stopped companion keeps each Block mark with the stopped title"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, harness.fixture("block-marks.fsx"))
    expect_block_marks(
        child,
        "a Block mark with the run title on each of the two Blocks",
        marks(mark(8, run_title), mark(10, run_title))
    )
    local companions = harness.new_companion_pids(known)
    assert.equal(1, #companions, "the new child Neovim started one companion")

    vim.uv.kill(companions[1], "sigkill")

    expect_block_marks(
        child,
        "the stopped title on each Block mark",
        marks(mark(8, stopped_title), mark(10, stopped_title))
    )

    harness.lua_get(child, [[vim.api.nvim_buf_set_lines(0, 0, 0, false, { "// one", "// two" })]])
    expect_block_marks(
        child,
        "each Block mark two lines lower, with no locate",
        marks(mark(10, stopped_title), mark(12, stopped_title))
    )

    harness.edit(child, harness.ui_fixture("no-requests-below.fsx"))
    expect_no_block_mark_through_settle(child, "no Block mark on a Script that no locate covered")
end

return T
