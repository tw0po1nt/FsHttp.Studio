local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

---@type integer?
local frozen_companion

T["a hung child Neovim stops, and a new child Neovim answers"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, harness.fixture("harness-setup.fsx"))
    harness.eventually(harness.companion_exists_deadline_ms, "the companion of the new child Neovim", function()
        return #harness.new_companion_pids(known) == 1
    end)
    frozen_companion = harness.new_companion_pids(known)[1]
    harness.freeze_process(frozen_companion)

    child.mini.lua_notify("while true do end")
    local answered, err = pcall(harness.lua_get, child, "1 + 1")

    assert.equal(false, answered, "the hung child Neovim gave an answer")
    assert.equal(true, tostring(err):find("so the Harness stopped it", 1, true) ~= nil, tostring(err))
    harness.eventually(harness.companion_gone_deadline_ms, "the hung child Neovim to stop", function()
        return not harness.process_exists(child.pid)
    end)
    assert.equal(2, harness.lua_get(harness.start_child({}), "1 + 1"), "a new child Neovim answers")
end

T["the next Check finds no frozen companion"] = function()
    local companion = frozen_companion
    assert.equal(true, companion ~= nil, "the hung child Neovim Check ran first")
    ---@cast companion integer
    harness.eventually(harness.companion_gone_deadline_ms, "the frozen companion to stop", function()
        return not harness.process_exists(companion)
    end)
end

return T
