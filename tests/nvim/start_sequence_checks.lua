local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

---@param pids integer[]
---@return string
local function pid_list(pids)
    return "[" .. table.concat(pids, ", ") .. "]"
end

-- The SDK floor that the published companion states.
---@return integer
local function companion_floor()
    local file = assert(io.open(harness.companion_path() .. "/Companion.runtimeconfig.json", "rb"))
    local config = vim.json.decode(file:read("*a"))
    file:close()
    return assert(tonumber(config.runtimeOptions.framework.version:match("^(%d+)%.")))
end

T["a second Script buffer starts no second companion"] = function()
    local child = harness.harness_setup_child()
    local expected = pid_list(harness.harness_setup_companion_pids())

    harness.edit(child, harness.fixture("second.fsx"))
    assert.equal(harness.fixture("second.fsx"), harness.lua_get(child, "vim.api.nvim_buf_get_name(0)"))

    harness.holds_for_settle("the companion pids " .. expected, function()
        return pid_list(harness.new_companion_pids({})) == expected
    end)
end

T["VimLeavePre stops the companion"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, harness.fixture("harness-setup.fsx"))
    harness.eventually(harness.companion_exists_deadline_ms, "the companion of the new child Neovim", function()
        return #harness.new_companion_pids(known) == 1
    end)
    local companion = harness.new_companion_pids(known)[1]

    harness.cmd(child, "doautocmd VimLeavePre")

    harness.eventually(harness.companion_gone_deadline_ms, "the companion to stop", function()
        return not harness.process_exists(companion)
    end)
    assert.equal(2, harness.lua_get(child, "1 + 1"), "the child Neovim still answers")
end

T["a dotnet_path that names a missing file gives the WARN notice, and no companion starts"] = function()
    local known = harness.companion_pids()
    local missing = harness.fixture("missing/dotnet")
    local child = harness.start_child({ companion_path = harness.companion_path(), dotnet_path = missing })
    harness.edit(child, harness.fixture("harness-setup.fsx"))

    ---@type nvim_suite.Notice?
    local warning
    harness.eventually(harness.notice_deadline_ms, "a WARN notice", function()
        for _, notice in ipairs(harness.notices(child)) do
            if notice.level == vim.log.levels.WARN then
                warning = notice
                return true
            end
        end
        return false
    end)
    ---@cast warning nvim_suite.Notice

    local floor = companion_floor()
    assert.equal(true, warning.message:find(string.format(".NET %d SDK", floor), 1, true) ~= nil, warning.message)
    assert.equal(true, warning.message:find("https://aka.ms/dotnet/download", 1, true) ~= nil, warning.message)
    assert.equal(true, warning.message:find("dotnet_path (" .. missing .. ")", 1, true) ~= nil, warning.message)

    harness.holds_for_settle("no new companion", function()
        return #harness.new_companion_pids(known) == 0
    end)
end

return T
