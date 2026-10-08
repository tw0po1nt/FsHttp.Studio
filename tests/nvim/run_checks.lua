local MiniTest = require("mini.test")
local harness = require("nvim.harness")
local refusals = require("fshttp.refusals")

local T = MiniTest.new_set()

---@param lines string[]?
---@param text string
---@return boolean
local function has_line(lines, text)
    return vim.tbl_contains(lines or {}, text)
end

---@param lines string[]?
---@param text string
---@return boolean
local function has_text(lines, text)
    return table.concat(lines or {}, "\n"):find(text, 1, true) ~= nil
end

---@param text string
---@param suffix string
---@return boolean
local function ends_with(text, suffix)
    return suffix == "" or text:sub(-#suffix) == suffix
end

---@param snapshot nvim_suite.ResponseBuffer
---@return nvim_suite.ResponseWindow?
local function only_window(snapshot)
    if #snapshot.windows ~= 1 then
        return nil
    end
    return snapshot.windows[1]
end

-- Waits until the Response buffer satisfies `predicate`, and returns the snapshot that satisfied it.
---@param child nvim_suite.Child
---@param subject string
---@param predicate fun(snapshot: nvim_suite.ResponseBuffer): boolean
---@return nvim_suite.ResponseBuffer
local function eventually_response(child, subject, predicate)
    local snapshot
    local ok, err = pcall(harness.eventually, harness.response_deadline_ms, subject, function()
        snapshot = harness.response_buffer(child)
        return predicate(snapshot)
    end)
    if not ok then
        error(string.format("%s\nlast Response buffer:\n%s", err, vim.inspect(snapshot)), 0)
    end
    return snapshot
end

-- Opens `path`, and waits for a Block mark on each line of `lines`. A Block mark proves a locate,
-- so the companion is ready when this returns.
---@param child nvim_suite.Child
---@param path string
---@param lines integer[]
local function open_script(child, path, lines)
    harness.edit(child, path)
    harness.eventually(harness.block_mark_deadline_ms, "a Block mark on each Block of " .. path, function()
        local marks = harness.block_marks(child)
        for _, line in ipairs(lines) do
            if not marks:find("^" .. line .. ": ") and not marks:find("\n" .. line .. ": ") then
                return false
            end
        end
        return true
    end)
end

---@param child nvim_suite.Child
---@param count integer the number of notices before the command
---@param level integer
---@param message string
local function expect_notice(child, count, level, message)
    harness.eventually(harness.notice_deadline_ms, "the notice: " .. message, function()
        for _, notice in ipairs(harness.notices_after(child, count)) do
            if notice.level == level and notice.message == message then
                return true
            end
        end
        return false
    end)
end

-- The child screen is 80 columns wide, and the winbar of a split of that screen has no room for the URL.
---@param child nvim_suite.Child
local function widen_screen(child)
    harness.cmd(child, "set columns=200")
    harness.cmd(child, "wincmd =")
end

-- The count of the lines below the title of the section whose title starts with `title`.
---@param lines string[]
---@param title string
---@return integer
local function section_line_count(lines, title)
    local first, last
    for i, line in ipairs(lines) do
        if first and line:sub(1, #"▾ ") == "▾ " then
            last = i - 1
            break
        end
        if line:sub(1, #title) == title then
            first = i
        end
    end
    assert(first, "the Response buffer has no section " .. title)
    return (last or #lines) - first
end

T["the command completes run, and <Plug>(FsHttpRun) has no key"] = function()
    local child = harness.harness_setup_child()

    local completion = harness.lua_get(child, [[vim.fn.getcompletion("FsHttp ", "cmdline")]])
    assert.equal(true, vim.tbl_contains(completion, "run"), vim.inspect(completion))
    assert.same({ "run" }, harness.lua_get(child, [[vim.fn.getcompletion("FsHttp ru", "cmdline")]]))

    local mapped = harness.lua_get(child, [[vim.fn.maparg("<Plug>(FsHttpRun)", "n", false, true).callback ~= nil]])
    assert.equal(true, mapped, "<Plug>(FsHttpRun) is mapped")
    assert.equal(0, harness.lua_get(child, [[vim.fn.hasmapto("<Plug>(FsHttpRun)")]]), "a key is bound to the Run")
end

T[":FsHttp run fills the Response buffer, and the next Run replaces it in the same window"] = function()
    local child = harness.harness_setup_child()
    local fixture = harness.fixture("core-path.fsx")
    local base_url = harness.base_url()
    widen_screen(child)
    open_script(child, fixture, { 26, 28 })

    harness.run_at(child, 26)

    eventually_response(child, "Running… in the Response buffer", function(snapshot)
        return snapshot.lines ~= nil and (snapshot.lines[1] or ""):match("^Running… %d+s$") ~= nil
    end)
    local first = eventually_response(child, "status 200, the /json URL, and the probe body", function(snapshot)
        local window = only_window(snapshot)
        return window ~= nil
            and has_line(snapshot.lines, harness.json_probe_body)
            and window.winbar:match("^ 200 OK  %d+ ms · %d+ ms total  26 B  GET ") ~= nil
            and ends_with(window.winbar, base_url .. "/json")
    end)
    local window = assert(only_window(first))

    assert.equal(1, first.count, "one Response buffer")
    assert.equal("nofile", first.buftype, "a scratch buffer")
    assert.equal(fixture, first.current_buffer_name, "the cursor stays in the Script")
    assert.equal(true, window.col > first.current_col, "the split is on the right of the Script")
    assert.same({ true, true, true }, { window.wrap, window.linebreak, window.breakindent })
    assert.equal(2, #window.closed_folds, vim.inspect(window.closed_folds))
    assert.equal(1, window.closed_folds[1].first, "the Request fold starts on line 1")
    assert.equal(
        string.format("▸ Request  %d lines", section_line_count(first.lines, "▾ Request")),
        window.closed_folds[1].text
    )
    local headers_count = section_line_count(first.lines, "▾ Response headers")
    assert.equal(
        string.format("▸ Response headers  (%d)  %d lines", headers_count, headers_count),
        window.closed_folds[2].text
    )
    assert.equal(true, has_line(first.lines, "▾ Body  application/json · 26 B"), "an open Body fold")

    harness.run_at(child, 28)

    local second = eventually_response(
        child,
        "the /status URL and its keys, with the probe body gone",
        function(snapshot)
            local next_window = only_window(snapshot)
            return next_window ~= nil
                and has_text(snapshot.lines, '"slowSeen"')
                and has_text(snapshot.lines, '"slowWaiting"')
                and not has_text(snapshot.lines, '"probe"')
                and ends_with(next_window.winbar, base_url .. "/status")
        end
    )
    assert.equal(1, second.count, "one Response buffer")
    assert.equal(window.id, assert(only_window(second)).id, "the same window")
    assert.equal(fixture, second.current_buffer_name, "the cursor stays in the Script")

    harness.lua_get(child, "vim.api.nvim_win_set_width(...)", { window.id, 60 })
    local narrow = assert(only_window(harness.response_buffer(child)))
    assert.equal(true, narrow.winbar:match("^ 200 OK  %d+ ms") ~= nil, narrow.winbar)
    assert.equal(true, ends_with(narrow.winbar, "/status"), narrow.winbar)
    assert.equal(false, narrow.winbar:find(base_url, 1, true) ~= nil, "the start of the URL is cut: " .. narrow.winbar)
    harness.cmd(child, "wincmd =")
end

T["a closed Response window opens again on the next Run"] = function()
    local child = harness.harness_setup_child()
    local fixture = harness.fixture("core-path.fsx")
    open_script(child, fixture, { 26, 28 })
    harness.run_at(child, 26)
    eventually_response(child, "the probe body in one Response window", function(snapshot)
        return only_window(snapshot) ~= nil and has_line(snapshot.lines, harness.json_probe_body)
    end)

    harness.close_response_windows(child)
    assert.equal(0, #harness.response_buffer(child).windows, "the Response window is closed")
    harness.run_at(child, 28)

    local reopened = eventually_response(child, "the /status keys in one Response window", function(snapshot)
        return only_window(snapshot) ~= nil and has_text(snapshot.lines, '"slowSeen"')
    end)
    assert.equal(1, reopened.count, "one Response buffer")
    assert.equal(fixture, reopened.current_buffer_name, "the cursor stays in the Script")
    assert.equal(
        true,
        assert(only_window(reopened)).col > reopened.current_col,
        "the split is on the right of the Script"
    )
end

T["a 404 shows as a response, and a Dead port shows as a Runtime error"] = function()
    local child = harness.harness_setup_child()
    widen_screen(child)
    open_script(child, harness.fixture("run-outcomes.fsx"), { 32, 34 })

    harness.run_at(child, 32)

    eventually_response(child, "status 404, the /notfound URL and body, and no Runtime error", function(snapshot)
        local window = only_window(snapshot)
        return window ~= nil
            and window.winbar:match("^ 404 Not Found  ") ~= nil
            and ends_with(window.winbar, "/notfound")
            and window.winbar_expression:find("FsHttpResponseStatus4xx", 1, true) ~= nil
            and has_line(snapshot.lines, "ui-test-server:notfound")
            and has_text(snapshot.lines, "▾ Response headers")
            and not has_text(snapshot.lines, "Runtime error")
    end)

    harness.run_at(child, 34)

    eventually_response(child, "Runtime error text, with no status and no 404 body", function(snapshot)
        local window = only_window(snapshot)
        return window ~= nil
            and (snapshot.lines[1] or ""):match("^Runtime error: ") ~= nil
            and window.winbar == ""
            and not has_text(snapshot.lines, "ui-test-server:notfound")
            and not has_text(snapshot.lines, "▾ Response headers")
    end)
end

T["a Run of the loop Block gives a WARN notice and opens no Response buffer"] = function()
    local child = harness.harness_setup_child()
    harness.close_response_windows(child)
    open_script(child, harness.ui_fixture("loop-lens.fsx"), { 10 })
    local before = harness.response_buffer(child)
    local count = #harness.notices(child)

    harness.run_at(child, 10)

    expect_notice(child, count, vim.log.levels.WARN, refusals.codes.loopBody.detail)
    harness.holds_for_settle("no Response buffer window and no new Run", function()
        local snapshot = harness.response_buffer(child)
        return #snapshot.windows == 0 and vim.deep_equal(snapshot.lines, before.lines)
    end)
end

T["a cross-block Refused Run shows the refused text, and the Script gets no diagnostic"] = function()
    local child = harness.harness_setup_child()
    local fixture = harness.ui_fixture("cross-block.fsx")
    open_script(child, fixture, { 11, 13 })

    harness.run_at(child, 14)

    local detail = refusals.unbound_block_value.detail:gsub("{name}", "dexId")
    eventually_response(child, "the unboundBlockValue refusal in the Response buffer", function(snapshot)
        local window = only_window(snapshot)
        return window ~= nil
            and window.winbar == ""
            and vim.deep_equal(snapshot.lines, { refusals.unbound_block_value.title, "", detail })
    end)
    assert.equal(fixture, harness.lua_get(child, "vim.api.nvim_buf_get_name(0)"), "the cursor stays in the Script")
    harness.holds_for_settle("no diagnostic in the Script", function()
        return harness.lua_get(child, "#vim.diagnostic.get(0)") == 0
    end)
end

T["the Request fold shows what a POST sent"] = function()
    local child = harness.harness_setup_child()
    local base_url = harness.base_url()
    open_script(child, harness.fixture("request-section.fsx"), { 27 })

    harness.run_at(child, 28)

    local snapshot = eventually_response(
        child,
        "a closed Request fold with the size of the sent body",
        function(snapshot)
            local window = only_window(snapshot)
            return window ~= nil
                and has_line(snapshot.lines, '{"echoed":"ui-test-server"}')
                and window.closed_folds[1] ~= nil
                and window.closed_folds[1].text:match("^▸ Request  %(%d+ B%)  %d+ lines$") ~= nil
        end
    )
    local fold = assert(only_window(snapshot)).closed_folds[1]
    local request = { unpack(snapshot.lines, fold.first, fold.last) }
    assert.equal("  POST " .. base_url .. "/echo", request[2])
    assert.equal(true, has_line(request, "  X-Fixture: request-section"), vim.inspect(request))
    assert.equal(true, has_line(request, '  {"posted":"request-section-fixture"}'), vim.inspect(request))
end

T["request_timeout_ms bounds the Run"] = function()
    local child = harness.harness_setup_child()
    local opts = { companion_path = harness.companion_path() }
    harness.lua_get(child, [[require("fshttp").setup(vim.tbl_extend("force", ..., { request_timeout_ms = 1000 }))]], {
        opts,
    })
    local ok, err = pcall(function()
        open_script(child, harness.fixture("slow.fsx"), { 26 })

        harness.run_at(child, 26)

        eventually_response(child, "the Runtime error of the bound", function(snapshot)
            return (snapshot.lines or {})[1]
                == "Runtime error: No response within 1000 ms. FsHttp.Studio stopped waiting."
        end)
    end)
    harness.lua_get(child, [[require("fshttp").setup(...)]], { opts })
    harness.release_slow()
    if not ok then
        error(err, 0)
    end
end

T["the no-Block notices"] = function()
    local child = harness.harness_setup_child()
    harness.close_response_windows(child)

    open_script(child, harness.ui_fixture("no-requests-above.fsx"), { 1 })
    local count = #harness.notices(child)
    harness.run_at(child, 1)
    expect_notice(child, count, vim.log.levels.WARN, refusals.no_blocks_parse_failure)

    harness.edit(child, harness.ui_fixture("no-requests-empty.fsx"))
    count = #harness.notices(child)
    harness.run_at(child, 1)
    expect_notice(child, count, vim.log.levels.INFO, refusals.no_blocks_empty)

    harness.holds_for_settle("no Response buffer window", function()
        return #harness.response_buffer(child).windows == 0
    end)
end

T["a stopped companion gives the stopped WARN notice and no Run"] = function()
    local known = harness.companion_pids()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    open_script(child, harness.fixture("block-marks.fsx"), { 8, 10 })
    local companions = harness.new_companion_pids(known)
    assert.equal(1, #companions, "the new child Neovim started one companion")
    vim.uv.kill(companions[1], "sigkill")
    harness.eventually(harness.block_mark_deadline_ms, "the stopped title on the Block marks", function()
        return harness.block_marks(child):find(refusals.companion_stopped_block_mark_title, 1, true) ~= nil
    end)

    harness.run_at(child, 8)

    expect_notice(child, 0, vim.log.levels.WARN, refusals.companion_stopped.detail)
    harness.holds_for_settle("no Response buffer", function()
        return harness.response_buffer(child).count == 0
    end)
end

T["a missing .NET SDK gives the SDK WARN notice again and no Run"] = function()
    local missing = harness.fixture("missing/dotnet")
    local child = harness.start_child({ companion_path = harness.companion_path(), dotnet_path = missing })
    harness.edit(child, harness.fixture("block-marks.fsx"))
    harness.expect_status(child, "the .NET SDK not found row", "FsHttp.Studio: .NET SDK not found")
    local warning = assert(harness.notices_at(child, vim.log.levels.WARN)[1], "the WARN notice of the SDK")
    local count = #harness.notices(child)

    harness.run_at(child, 8)

    expect_notice(child, count, vim.log.levels.WARN, warning.message)
    harness.holds_for_settle("no Response buffer", function()
        return harness.response_buffer(child).count == 0
    end)
end

-- Installs a vim.ui.select stub in the child. It records the items it is given and picks the item
-- at _G.fshttp_suite_pick_index (default the first). A Check reads _G.fshttp_suite_select.items.
local function install_select_stub(child)
    harness.lua_get(
        child,
        [[(function()
            _G.fshttp_suite_real_select = vim.ui.select
            _G.fshttp_suite_select = { items = nil }
            _G.fshttp_suite_pick_index = 1
            vim.ui.select = function(items, opts, callback)
                _G.fshttp_suite_select = { items = items, opts = opts }
                local idx = _G.fshttp_suite_pick_index
                if idx < 1 or idx > #items then return end
                callback(items[idx], idx)
            end
        end)()]]
    )
end

local function restore_select(child)
    harness.lua_get(child, "(function() vim.ui.select = _G.fshttp_suite_real_select end)()")
end

---@param child nvim_suite.Child
---@return string[]? items the items that vim.ui.select was given, nil when it was not called
local function select_items(child)
    local select = harness.lua_get(child, "_G.fshttp_suite_select")
    return select and select.items or nil
end

T["the picker lists each located Block in source order, and a pick starts a Run"] = function()
    local child = harness.harness_setup_child()
    widen_screen(child)
    local fixture = harness.fixture("core-path.fsx")
    open_script(child, fixture, { 26, 28 })
    install_select_stub(child)

    -- The cursor is outside every Block, so the picker opens.
    harness.run_at(child, 1)

    harness.eventually(harness.notice_deadline_ms, "the picker to open", function()
        return select_items(child) ~= nil
    end)
    local json_line = 'http { GET $"' .. "{baseUrl}" .. '/json" }'
    local status_line = 'http { GET $"' .. "{baseUrl}" .. '/status" }'
    assert.same({
        "▶ 26: " .. json_line,
        "▶ 28: " .. status_line,
    }, select_items(child))

    -- The stub picked the first Block, so its Run starts and fills the Response buffer.
    eventually_response(child, "the probe body after the picker pick", function(snapshot)
        return only_window(snapshot) ~= nil and has_line(snapshot.lines, harness.json_probe_body)
    end)
    restore_select(child)
end

T["a pick on a refused Block shows its refusal, and no Run starts"] = function()
    local child = harness.harness_setup_child()
    harness.close_response_windows(child)
    open_script(child, harness.ui_fixture("loop-lens.fsx"), { 10 })
    install_select_stub(child)

    local count = #harness.notices(child)

    -- The cursor is outside the one Block, so the picker opens with it.
    harness.run_at(child, 1)

    harness.eventually(harness.notice_deadline_ms, "the picker to open", function()
        return select_items(child) ~= nil
    end)
    assert.same({ '⊘ 10: http { GET "http://127.0.0.1:9/" }' }, select_items(child))

    -- Picking the refused Block shows its refusal and starts no Run.
    expect_notice(child, count, vim.log.levels.WARN, refusals.codes.loopBody.detail)
    harness.holds_for_settle("no Response buffer window", function()
        return #harness.response_buffer(child).windows == 0
    end)
    restore_select(child)
end

T["a Run that starts while the companion starts runs when it is ready"] = function()
    local opts = { companion_path = harness.companion_path(), dotnet_path = harness.slow_dotnet(2) }
    local child = harness.start_child(opts)
    local fixture = harness.fixture("core-path.fsx")
    harness.edit(child, fixture)

    -- Run while the companion is still starting. The slow dotnet keeps it in that state.
    harness.run_at(child, 26)

    expect_notice(
        child,
        0,
        vim.log.levels.INFO,
        "The FsHttp.Studio companion is starting. This Run starts when it is ready."
    )

    -- When the companion becomes ready, the recorded Run starts and fills the Response buffer.
    eventually_response(child, "the probe body after the wait", function(snapshot)
        return only_window(snapshot) ~= nil and has_line(snapshot.lines, harness.json_probe_body)
    end)
end

return T
