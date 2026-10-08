local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local image_fixture = harness.fixture("image-body.fsx")
local image_block_line = 25

T["a bad option value gives the ERROR notice, and its key keeps the default"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path(), request_timeout_ms = -5 })

    local errors = harness.notices_at(child, vim.log.levels.ERROR)
    assert.equal(1, #errors, vim.inspect(errors))
    assert.equal(
        "FsHttp.Studio: the option request_timeout_ms is -5. It takes a number of 0 or more (0 sets no bound). "
            .. "The option keeps its default (30000).",
        errors[1].message
    )
    assert.equal(30000, harness.lua_get(child, [[require("fshttp").config().request_timeout_ms]]))
end

T["an unknown key gives a WARN notice, and each other key applies"] = function()
    local child = harness.start_child({
        companion_path = harness.companion_path(),
        theme = "red",
        response_buffer = { split = "below" },
    })

    local warnings = harness.notices_at(child, vim.log.levels.WARN)
    assert.equal(1, #warnings, vim.inspect(warnings))
    assert.is_truthy(warnings[1].message:find("theme", 1, true))
    assert.equal("below", harness.lua_get(child, [[require("fshttp").config().response_buffer.split]]))
end

T["response_buffer.images = false gives the fallback line with the option as the reason"] = function()
    local child = harness.start_child({
        companion_path = harness.companion_path(),
        response_buffer = { images = false },
    })
    harness.edit(child, image_fixture)
    harness.eventually(harness.block_mark_deadline_ms, "a Block mark on line " .. image_block_line, function()
        return harness.block_marks(child):find("^" .. image_block_line .. ": ") ~= nil
    end)

    harness.run_at(child, image_block_line)

    local expected = "100×100 px  images are off (response_buffer.images)"
    harness.eventually(harness.response_deadline_ms, "the fallback line below the Body title", function()
        return table.concat(harness.response_buffer(child).lines or {}, "\n"):find(expected, 1, true) ~= nil
    end)
end

T["block_mark.virtual_line = false removes the virtual line at once, and the sign stays"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, harness.fixture("block-marks.fsx"))
    harness.eventually(harness.block_mark_deadline_ms, "a Block mark with a virtual line", function()
        return harness.block_marks(child):find("^%d+: %S.- %[%S+%]") ~= nil
    end)

    harness.lua_get(child, [[require("fshttp").setup({ block_mark = { virtual_line = false } })]])

    local marks = harness.block_marks(child)
    assert.equal(true, marks:find("^%d+:  %[%S+%]") ~= nil, marks)
end

return T
