local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

-- The companion version that the companion of this run sends in its ready envelope.
---@return string
local function companion_version()
    local file = assert(io.open("package.json", "rb"))
    local version = vim.json.decode(file:read("*a")).version
    file:close()
    return version
end

---@param child nvim_suite.Child
---@return nvim_suite.Notice[]
local function warn_notices(child)
    return harness.notices_at(child, vim.log.levels.WARN)
end

T["a companion of the client version gives no version WARN notice"] = function()
    local child = harness.harness_setup_child()

    harness.holds_for_settle("no version WARN notice in the child Neovim of Harness setup", function()
        for _, notice in ipairs(warn_notices(child)) do
            if notice.message:find("FsHttp.Studio is version", 1, true) then
                return false
            end
        end
        return true
    end)
end

T["a companion of a different version gives one WARN notice and the WARN health line, the companion stays up, and a Run succeeds"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() }, "9.9.9")
    harness.edit(child, harness.fixture("harness-setup.fsx"))
    harness.eventually(harness.companion_exists_deadline_ms, "the companion of the new child Neovim", function()
        return #harness.new_companion_pids(known) == 1
    end)
    local companion = harness.new_companion_pids(known)[1]

    harness.eventually(harness.notice_deadline_ms, "a WARN notice", function()
        return #warn_notices(child) > 0
    end)
    local message = warn_notices(child)[1].message

    assert.equal(true, message:find("FsHttp.Studio is version 9.9.9", 1, true) ~= nil, message)
    assert.equal(true, message:find("is version " .. companion_version(), 1, true) ~= nil, message)
    assert.equal(true, message:find("Set companion_path to a build of version 9.9.9", 1, true) ~= nil, message)
    assert.equal(true, message:find("remove companion_path", 1, true) ~= nil, message)

    harness.holds_for_settle("one WARN notice and a live companion", function()
        return #warn_notices(child) == 1 and harness.process_exists(companion)
    end)

    local version_lines = vim.tbl_filter(function(item)
        return item.text == message
    end, harness.checkhealth(child))
    assert.same({ { section = "Companion", level = "WARNING", text = message, advice = {} } }, version_lines)

    harness.edit(child, harness.fixture("core-path.fsx"))
    harness.await_block_mark(child, 26)
    harness.run_at(child, 26)

    harness.eventually(harness.response_deadline_ms, "a successful Run in the Response buffer", function()
        return vim.tbl_contains(harness.response_buffer(child).lines or {}, harness.json_probe_body_line)
    end)
end

return T
