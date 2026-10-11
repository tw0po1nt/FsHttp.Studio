local MiniTest = require("mini.test")
local harness = require("nvim.harness")
local refusals = require("fshttp.refusals")

local T = MiniTest.new_set()

---@param child nvim_suite.Child
---@param subject string
---@param line string
local function expect_response_line(child, subject, line)
    harness.eventually(harness.response_deadline_ms, subject, function()
        return vim.tbl_contains(harness.response_buffer(child).lines or {}, line)
    end)
end

---@param pids integer[]
---@return boolean
local function each_gone(pids)
    for _, pid in ipairs(pids) do
        if harness.process_exists(pid) then
            return false
        end
    end
    return true
end

T["Restart under a hang"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    local ok, err = pcall(function()
        harness.edit(child, harness.fixture("slow.fsx"))
        harness.await_block_mark(child, 26)
        local old = harness.new_companion_pids(known)
        assert.equal(1, #old, "the new child Neovim started one companion")
        harness.run_at(child, 26)
        harness.eventually(harness.response_deadline_ms, "the Response buffer of the hang Run", function()
            return harness.response_buffer(child).count > 0
        end)

        harness.cmd(child, "FsHttp restart")
        local count = #harness.notices(child)
        harness.cmd(child, "FsHttp restart")

        expect_response_line(child, "the Run-ended sentence", "Runtime error: " .. refusals.restart_ended_run)
        local restarting = vim.tbl_filter(function(notice)
            return notice.message == refusals.companion_restarting and notice.level == vim.log.levels.INFO
        end, harness.notices_after(child, count))
        assert.equal(1, #restarting, "one restarting notice for the command during the stop")
        harness.eventually(harness.companion_gone_deadline_ms, "the old companion to stop", function()
            return each_gone(old)
        end)
        harness.eventually(harness.block_mark_deadline_ms, "the run title on the Block mark again", function()
            return harness.block_marks(child):find("^26: " .. refusals.run_block_mark_title) ~= nil
        end)

        harness.edit(child, harness.fixture("core-path.fsx"))
        harness.await_block_mark(child, 26)
        harness.run_at(child, 26)
        expect_response_line(child, "the recovery Run", harness.json_probe_body_line)
    end)
    harness.release_slow()
    if not ok then
        error(err, 0)
    end
end

T["A worker under a hang"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    local ok, err = pcall(function()
        harness.edit(child, harness.fixture("core-path.fsx"))
        harness.await_block_mark(child, 26)
        harness.run_at(child, 26)
        expect_response_line(child, "the warm Run", harness.json_probe_body_line)

        harness.edit(child, harness.fixture("worker-hang.fsx"))
        harness.await_block_mark(child, 27)
        harness.run_at(child, 27)
        harness.eventually(harness.response_deadline_ms, "the worker beside the companion", function()
            return #harness.new_companion_pids(known) >= 2
        end)
        local processes = harness.new_companion_pids(known)

        harness.cmd(child, "FsHttp restart")

        harness.eventually(harness.companion_gone_deadline_ms, "each process of the old companion to stop", function()
            return each_gone(processes)
        end)
        assert.equal(2, harness.lua_get(child, "1 + 1"), "the child Neovim still answers")
    end)
    harness.release_slow()
    if not ok then
        error(err, 0)
    end
end

return T
