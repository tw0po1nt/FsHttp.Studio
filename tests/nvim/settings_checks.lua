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
    harness.await_block_mark(child, image_block_line)

    harness.run_at(child, image_block_line)

    local expected = "100×100 px  images are off (response_buffer.images)"
    harness.eventually(harness.response_deadline_ms, "the fallback line below the Body title", function()
        return table.concat(harness.response_buffer(child).lines or {}, "\n"):find(expected, 1, true) ~= nil
    end)
end

T["response_buffer.keys = false leaves l and h their Neovim motions, and each <Plug> map stays"] = function()
    local child = harness.start_child({
        companion_path = harness.companion_path(),
        response_buffer = { keys = false },
    })
    harness.edit(child, harness.fixture("request-section.fsx"))
    harness.await_block_mark(child, 27)

    harness.run_at(child, 28)

    local headers
    harness.eventually(harness.response_deadline_ms, "a closed Response headers fold", function()
        local window = harness.response_buffer(child).windows[1]
        for _, fold in ipairs(window and window.closed_folds or {}) do
            if fold.text:match("^▸ Response headers") then
                headers = fold
                harness.lua_get(child, "vim.api.nvim_set_current_win(...)", { window.id })
                return true
            end
        end
        return false
    end)
    for _, lhs in ipairs({ "l", "h" }) do
        assert.equal(0, harness.lua_get(child, "vim.fn.maparg(..., 'n', false, true).buffer or 0", { lhs }), lhs)
    end
    for _, plug in ipairs({ "<Plug>(FsHttpOpenFold)", "<Plug>(FsHttpCloseFold)" }) do
        assert.equal(1, harness.lua_get(child, "vim.fn.maparg(..., 'n', false, true).expr", { plug }), plug)
    end

    harness.cmd(child, string.format("call cursor(%d, 1)", headers.first))
    harness.cmd(child, "normal! zo")
    harness.cmd(child, string.format("call cursor(%d, 1)", headers.first + 1))
    harness.type_keys(child, "l")
    harness.eventually(harness.notice_deadline_ms, "l moves one column right", function()
        return vim.deep_equal(harness.lua_get(child, "vim.api.nvim_win_get_cursor(0)"), { headers.first + 1, 1 })
    end)
    harness.type_keys(child, "h")
    harness.eventually(harness.notice_deadline_ms, "h moves one column left", function()
        return vim.deep_equal(harness.lua_get(child, "vim.api.nvim_win_get_cursor(0)"), { headers.first + 1, 0 })
    end)
    harness.type_keys(child, "h")
    harness.holds_for_settle("h in column 1 keeps the Response headers fold open", function()
        return harness.lua_get(child, "vim.fn.foldclosed(...)", { headers.first }) == -1
    end)
end

T["block_mark.sign is an unknown key: one WARN names it, and each other key applies"] = function()
    local child = harness.start_child({
        companion_path = harness.companion_path(),
        block_mark = { sign = false },
        response_buffer = { split = "below" },
    })

    local warnings = harness.notices_at(child, vim.log.levels.WARN)
    assert.equal(1, #warnings, vim.inspect(warnings))
    assert.equal("FsHttp.Studio: setup() has no option block_mark.sign. The client ignores it.", warnings[1].message)
    assert.equal("below", harness.lua_get(child, [[require("fshttp").config().response_buffer.split]]))
end

T["block_mark.virtual_line = false removes each Block mark at once"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, harness.fixture("block-marks.fsx"))
    harness.eventually(harness.block_mark_deadline_ms, "a Block mark with a virtual line", function()
        return harness.block_marks(child):find("^%d+: %S") ~= nil
    end)

    harness.lua_get(child, [[require("fshttp").setup({ block_mark = { virtual_line = false } })]])

    local marks = harness.block_marks(child)
    assert.equal("8: \n10: ", marks)
    assert.equal(0, harness.sign_count(child))
end

T["a change to the dotnet path gives the INFO notice that names :FsHttp restart"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.edit(child, harness.fixture("core-path.fsx"))
    harness.await_block_mark(child, 26)
    local count = #harness.notices(child)

    harness.lua_get(
        child,
        string.format([[require("fshttp").setup({ dotnet_path = %q })]], harness.fixture("missing/dotnet"))
    )

    local infos = vim.tbl_filter(function(notice)
        return notice.level == vim.log.levels.INFO
    end, harness.notices_after(child, count))
    assert.equal(1, #infos, vim.inspect(infos))
    assert.is_truthy(infos[1].message:find("dotnet_path", 1, true))
    assert.is_truthy(infos[1].message:find(":FsHttp restart", 1, true))
end

return T
