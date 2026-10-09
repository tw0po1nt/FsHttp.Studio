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

-- A Block mark proves a locate, so the companion is ready when this returns.
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
            and has_line(snapshot.lines, harness.json_probe_body_line)
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
        return only_window(snapshot) ~= nil and has_line(snapshot.lines, harness.json_probe_body_line)
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

T["a Compile error shows as text, <CR> and <Plug>(FsHttpJump) move to its position, and the Script gets no mark"] = function()
    local child = harness.harness_setup_child()
    local fixture = harness.ui_fixture("compile-error.fsx")
    open_script(child, fixture, { 14 })
    harness.lua_get(child, [[vim.api.nvim_buf_set_lines(0, 11, 12, false, { 'let probe : int = "not an int"' })]])

    -- The Block mark of the Block is the one sign that the Script has. A Compile error adds no more.
    local function script_marks()
        return harness.lua_get(
            child,
            [[(function(path)
                local buf = vim.fn.bufnr(path)
                return {
                    diagnostics = #vim.diagnostic.get(buf),
                    signs = #vim.fn.sign_getplaced(buf, { group = "*" })[1].signs,
                    quickfix = #vim.fn.getqflist(),
                    location_list = #vim.fn.getloclist(0),
                }
            end)(...)]],
            { fixture }
        )
    end
    local marks_before = script_marks()
    assert.equal(0, marks_before.diagnostics + marks_before.quickfix + marks_before.location_list)

    harness.run_at(child, 14)

    local snapshot = eventually_response(child, "the Compile error text at (12,19)", function(shown)
        local window = only_window(shown)
        return window ~= nil and (shown.lines[1] or "") == "Compile error:" and has_text(shown.lines, "(12,19) ")
    end)
    local window = assert(only_window(snapshot))
    assert.equal("Compile error  <CR> on a (line,col) moves to it", window.winbar)
    assert.equal(true, has_text(snapshot.lines, "expected to have type"), vim.inspect(snapshot.lines))
    for _, line in ipairs(snapshot.lines or {}) do
        assert.equal(false, line:match(" $") ~= nil, "a trailing space on: " .. line)
    end
    assert.equal(false, has_text(snapshot.lines, "Runtime error"))

    local position_line
    for i, line in ipairs(snapshot.lines or {}) do
        if line:sub(1, #"(12,19) ") == "(12,19) " then
            position_line = i
        end
    end
    local function move_from_response(keys)
        harness.lua_get(child, "vim.api.nvim_set_current_win(...)", { window.id })
        harness.cmd(child, string.format("call cursor(%d, 1)", position_line))
        harness.type_keys(child, keys)
        harness.eventually(harness.notice_deadline_ms, "the cursor in the Script at (12,19)", function()
            return harness.lua_get(child, "vim.api.nvim_buf_get_name(0)") == fixture
                and vim.deep_equal(harness.lua_get(child, "vim.api.nvim_win_get_cursor(0)"), { 12, 18 })
        end)
        harness.cmd(child, "call cursor(1, 1)")
    end
    move_from_response("<CR>")
    move_from_response("<Plug>(FsHttpJump)")

    assert.same(marks_before, script_marks())
end

T["a Compile error position past the end of the Script gives a WARN notice, and <CR> keeps the cursor"] = function()
    local child = harness.harness_setup_child()
    local fixture = harness.ui_fixture("compile-error.fsx")
    -- The Check above leaves the fixture changed. The reload gives the text on disk.
    harness.cmd(child, "silent edit! " .. vim.fn.fnameescape(fixture))
    harness.await_block_mark(child, 14)
    -- A char in place of a string makes this text different from the Compile error of the Check above.
    harness.lua_get(child, [[vim.api.nvim_buf_set_lines(0, 11, 12, false, { "let probe : int = 'c'" })]])
    harness.run_at(child, 14)

    local snapshot = eventually_response(child, "the Compile error text at (12,19) for a char", function(shown)
        return only_window(shown) ~= nil and has_text(shown.lines, "(12,19) ") and has_text(shown.lines, "'char'")
    end)
    local window = assert(only_window(snapshot))
    local position_line
    for i, line in ipairs(snapshot.lines or {}) do
        if line:sub(1, #"(12,19) ") == "(12,19) " then
            position_line = i
        end
    end

    -- A Script of five lines puts line 12 past its end.
    harness.lua_get(child, "vim.api.nvim_buf_set_lines(vim.fn.bufnr(...), 5, -1, false, {})", { fixture })
    local count = #harness.notices(child)
    harness.lua_get(child, "vim.api.nvim_set_current_win(...)", { window.id })
    harness.cmd(child, string.format("call cursor(%d, 1)", position_line))
    harness.type_keys(child, "<CR>")

    expect_notice(child, count, vim.log.levels.WARN, "The position is past the end of the script.")
    assert.equal(window.id, harness.lua_get(child, "vim.api.nvim_get_current_win()"))
    assert.same({ position_line, 0 }, harness.lua_get(child, "vim.api.nvim_win_get_cursor(0)"))

    harness.lua_get(
        child,
        [[vim.api.nvim_buf_call(vim.fn.bufnr(...), function() vim.cmd("silent edit!") end)]],
        { fixture }
    )
end

local loaded_file_block_line = 11
local loaded_file_position = "loaded/broken.fsx(3,19) "

---@param snapshot nvim_suite.ResponseBuffer
---@return integer?
local function loaded_file_position_line(snapshot)
    for i, line in ipairs(snapshot.lines or {}) do
        if line:sub(1, #loaded_file_position) == loaded_file_position then
            return i
        end
    end
end

---@param child nvim_suite.Child
---@param path string
---@return integer window, integer position_line
local function run_loaded_file_error(child, path)
    open_script(child, path, { loaded_file_block_line })
    harness.run_at(child, loaded_file_block_line)
    local snapshot = eventually_response(child, "the Compile error text at " .. loaded_file_position, function(shown)
        return only_window(shown) ~= nil
            and (shown.lines[1] or "") == "Compile error:"
            and loaded_file_position_line(shown) ~= nil
    end)
    assert.equal(true, has_text(snapshot.lines, "expected to have type"), vim.inspect(snapshot.lines))
    return assert(only_window(snapshot)).id, assert(loaded_file_position_line(snapshot))
end

-- On Windows, FCS gives the path of a Loaded file with backslashes.
---@param child nvim_suite.Child
---@param subject string
---@param predicate fun(state: { name: string, cursor: integer[], window: integer }): boolean
---@return { name: string, cursor: integer[], window: integer, buf: integer }
local function eventually_in_loaded_file(child, subject, predicate)
    local state
    local ok, err = pcall(harness.eventually, harness.notice_deadline_ms, subject, function()
        state = harness.lua_get(
            child,
            [[{ name = vim.fs.normalize(vim.api.nvim_buf_get_name(0)), cursor = vim.api.nvim_win_get_cursor(0),
                window = vim.api.nvim_get_current_win(), buf = vim.api.nvim_get_current_buf() }]]
        )
        return predicate(state)
    end)
    if not ok then
        local notices = harness.notices(child)
        error(string.format("%s\nlast state: %s\nnotices: %s", err, vim.inspect(state), vim.inspect(notices)), 0)
    end
    return state
end

---@param child nvim_suite.Child
---@param count integer the number of notices before the command
---@param prefix string
---@param suffix string
local function expect_warn_notice_around(child, count, prefix, suffix)
    local ok, err = pcall(
        harness.eventually,
        harness.notice_deadline_ms,
        "the notice: " .. prefix .. "…" .. suffix,
        function()
            for _, notice in ipairs(harness.notices_after(child, count)) do
                if
                    notice.level == vim.log.levels.WARN
                    and vim.startswith(notice.message, prefix)
                    and ends_with(notice.message, suffix)
                then
                    return true
                end
            end
            return false
        end
    )
    if not ok then
        error(string.format("%s\nnotices: %s", err, vim.inspect(harness.notices_after(child, count))), 0)
    end
end

---@param child nvim_suite.Child
---@param window integer
---@param position_line integer
local function press_enter_on(child, window, position_line)
    harness.lua_get(child, "vim.api.nvim_set_current_win(...)", { window })
    harness.cmd(child, string.format("call cursor(%d, 1)", position_line))
    harness.type_keys(child, "<CR>")
end

T["a Compile error in a Loaded file names its path, and <CR> opens the Loaded file at its position"] = function()
    local child = harness.harness_setup_child()
    local loaded_file = vim.fs.normalize(harness.ui_fixture("loaded/broken.fsx"))
    local window, position_line = run_loaded_file_error(child, harness.ui_fixture("loaded-file-error.fsx"))

    press_enter_on(child, window, position_line)
    local loaded = eventually_in_loaded_file(child, "the cursor in the Loaded file at (3,19)", function(state)
        return state.name == loaded_file and vim.deep_equal(state.cursor, { 3, 18 })
    end)

    harness.cmd(child, "call cursor(1, 1)")
    press_enter_on(child, window, position_line)
    eventually_in_loaded_file(child, "the cursor back in the same Loaded file window", function(state)
        return state.window == loaded.window and vim.deep_equal(state.cursor, { 3, 18 })
    end)

    -- A Loaded file of one line puts line 3 past its end.
    harness.lua_get(child, "vim.api.nvim_buf_set_lines(..., 1, -1, false, {})", { loaded.buf })
    local count = #harness.notices(child)
    press_enter_on(child, window, position_line)
    expect_notice(
        child,
        count,
        vim.log.levels.WARN,
        string.format("The position is past the end of the loaded file %s.", loaded_file)
    )
    assert.equal(window, harness.lua_get(child, "vim.api.nvim_get_current_win()"))
    assert.same({ position_line, 0 }, harness.lua_get(child, "vim.api.nvim_win_get_cursor(0)"))

    harness.lua_get(child, [[vim.api.nvim_buf_call(..., function() vim.cmd("silent edit!") end)]], { loaded.buf })
    harness.lua_get(child, "vim.api.nvim_win_close(..., true)", { loaded.window })
end

T["a Compile error in a Loaded file that no longer exists gives a WARN notice, and <CR> keeps the cursor"] = function()
    local child = harness.harness_setup_child()
    -- The Windows temp path has an 8.3 short name, which FSI expands in the path of a Loaded file.
    local directory = string.format("%s/out/nvim-tests/loaded-file-%d", vim.uv.cwd(), vim.uv.hrtime())
    vim.fn.mkdir(directory .. "/loaded", "p")
    for _, name in ipairs({ "loaded-file-error.fsx", "loaded/broken.fsx" }) do
        vim.fn.writefile(vim.fn.readfile(harness.ui_fixture(name), "b"), directory .. "/" .. name, "b")
    end
    local script
    local ok, err = pcall(function()
        local window, position_line = run_loaded_file_error(child, directory .. "/loaded-file-error.fsx")
        script = harness.lua_get(child, "vim.api.nvim_get_current_buf()")

        vim.fn.delete(directory .. "/loaded/broken.fsx")
        local count = #harness.notices(child)
        press_enter_on(child, window, position_line)
        expect_warn_notice_around(child, count, "The loaded file ", "/loaded/broken.fsx does not exist.")
        assert.equal(window, harness.lua_get(child, "vim.api.nvim_get_current_win()"))
        assert.same({ position_line, 0 }, harness.lua_get(child, "vim.api.nvim_win_get_cursor(0)"))
    end)
    if script then
        harness.lua_get(child, "vim.api.nvim_buf_delete(..., { force = true })", { script })
    end
    vim.fn.delete(directory, "rf")
    if not ok then
        error(err, 0)
    end
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
                and has_line(snapshot.lines, '  "echoed": "ui-test-server"')
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

T["a binary body shows as the hex view, and a Captured body shows its hex view or its reason"] = function()
    local child = harness.harness_setup_child()
    open_script(child, harness.fixture("binary-body.fsx"), { 27, 29, 35 })

    harness.run_at(child, 27)

    local hex_view = {
        "▾ Body  application/octet-stream · 20 B",
        "Binary body: 20 B",
        "00000000  00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f  ................",
        "00000010  10 11 12 13                                      ....",
    }
    eventually_response(child, "the hex view of the /binary body in the open Body fold", function(snapshot)
        local window = only_window(snapshot)
        local lines = snapshot.lines or {}
        return window ~= nil
            and ends_with(window.winbar, "/binary")
            and #window.closed_folds == 2
            and vim.deep_equal(hex_view, { unpack(lines, #lines - #hex_view + 1) })
    end)

    harness.run_at(child, 29)

    local captured = eventually_response(child, "the hex view of the sent bytes in the Request fold", function(snapshot)
        local window = only_window(snapshot)
        return window ~= nil
            and has_line(snapshot.lines, '  "echoed": "ui-test-server"')
            and has_line(snapshot.lines, "  Binary body: 6 B")
    end)
    local fold = assert(only_window(captured)).closed_folds[1]
    assert.equal("▸ Request  (6 B)  " .. (fold.last - fold.first) .. " lines", fold.text)
    assert.same({
        "  Binary body: 6 B",
        "  00000000  00 01 02 ff 00 80                                ......",
    }, { unpack(captured.lines, fold.last - 1, fold.last) })

    harness.run_at(child, 35)

    local not_captured = eventually_response(child, "the reason for the stream in the Request fold", function(snapshot)
        return has_line(snapshot.lines, "  streamed body: not captured, so that the upload is unchanged")
    end)
    local request = assert(only_window(not_captured)).closed_folds[1]
    assert.equal("▸ Request  " .. (request.last - request.first) .. " lines", request.text)
    assert.equal("  streamed body: not captured, so that the upload is unchanged", not_captured.lines[request.last])
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
        return only_window(snapshot) ~= nil and has_line(snapshot.lines, harness.json_probe_body_line)
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

    expect_notice(child, count, vim.log.levels.WARN, refusals.codes.loopBody.detail)
    harness.holds_for_settle("no Response buffer window", function()
        return #harness.response_buffer(child).windows == 0
    end)
    restore_select(child)
end

local wait_notice = "The FsHttp.Studio companion is starting. This Run starts when it is ready."

-- Only the latest Run reaches the Response buffer, so the buffer alone cannot show that an earlier Run never started.
---@param child nvim_suite.Child
local function install_run_spy(child)
    harness.lua_get(
        child,
        [[(function()
            local companion = require("fshttp.companion")
            local real_run = companion.run
            _G.fshttp_suite_run_indexes = {}
            companion.run = function(run_envelope, callback)
                table.insert(_G.fshttp_suite_run_indexes, run_envelope.block_index)
                return real_run(run_envelope, callback)
            end
        end)()]]
    )
end

T["a Run that starts while the companion starts runs when it is ready, and a second Run replaces it"] = function()
    local opts = { companion_path = harness.companion_path(), dotnet_path = harness.slow_dotnet(2) }
    local child = harness.start_child(opts)
    harness.edit(child, harness.fixture("core-path.fsx"))
    install_run_spy(child)

    -- Run while the companion is still starting. The slow dotnet keeps it in that state.
    harness.run_at(child, 26)
    expect_notice(child, 0, vim.log.levels.INFO, wait_notice)
    harness.run_at(child, 28)

    -- When the companion becomes ready, the second recorded Run starts and fills the Response buffer.
    eventually_response(child, "the /status keys after the wait", function(snapshot)
        return only_window(snapshot) ~= nil and has_text(snapshot.lines, '"slowSeen"')
    end)
    -- The locate of a replaced Run would be answered first, so its Run would show in the spy by now.
    assert.same({ 1 }, harness.lua_get(child, "_G.fshttp_suite_run_indexes"))
end

T["a Run that waits ends with the stopped notice when the companion stops"] = function()
    local opts = { companion_path = harness.companion_path(), dotnet_path = harness.slow_dotnet(2, "stopped") }
    local child = harness.start_child(opts)
    harness.edit(child, harness.fixture("core-path.fsx"))

    harness.run_at(child, 26)

    expect_notice(child, 0, vim.log.levels.INFO, wait_notice)
    expect_notice(child, 0, vim.log.levels.WARN, refusals.companion_stopped.detail)
    assert.equal(0, harness.response_buffer(child).count, "no Response buffer")
end

T["a Run that waits ends with one SDK notice when no SDK is found"] = function()
    local opts = { companion_path = harness.companion_path(), dotnet_path = harness.slow_dotnet(2, "sdkNotFound") }
    local child = harness.start_child(opts)
    harness.edit(child, harness.fixture("core-path.fsx"))

    harness.run_at(child, 26)

    expect_notice(child, 0, vim.log.levels.INFO, wait_notice)
    harness.expect_status(child, "the .NET SDK not found row", "FsHttp.Studio: .NET SDK not found")
    -- The listener of the wait runs when the state changes, so its notice is in the list by now.
    assert.equal(1, #harness.notices_at(child, vim.log.levels.WARN), vim.inspect(harness.notices(child)))
    assert.equal(0, harness.response_buffer(child).count, "no Response buffer")
end

T["a Run that waits starts nothing when its Script closes"] = function()
    local opts = { companion_path = harness.companion_path(), dotnet_path = harness.slow_dotnet(2) }
    local child = harness.start_child(opts)
    harness.edit(child, harness.fixture("core-path.fsx"))

    harness.run_at(child, 26)
    expect_notice(child, 0, vim.log.levels.INFO, wait_notice)
    harness.cmd(child, "bwipeout!")

    harness.eventually(harness.notice_deadline_ms, "the companion to become ready", function()
        return harness.lua_get(child, [[require("fshttp.companion").state()]]) == "ready"
    end)
    harness.holds_for_settle("no Response buffer and no error", function()
        return harness.response_buffer(child).count == 0 and harness.lua_get(child, "vim.v.errmsg") == ""
    end)
end

T[":FsHttp yank and yr, yh, and yb put the Copy text in the register, and g? lists the keys"] = function()
    local child = harness.harness_setup_child()
    local base_url = harness.base_url()
    open_script(child, harness.fixture("request-section.fsx"), { 27 })
    harness.run_at(child, 28)
    local snapshot = eventually_response(child, "the echoed body in the Response buffer", function(shown)
        return only_window(shown) ~= nil and has_line(shown.lines, '  "echoed": "ui-test-server"')
    end)

    local function register(name)
        return harness.lua_get(child, "vim.fn.getreg(...)", { name })
    end

    local function expect_info(count, message)
        expect_notice(child, count, vim.log.levels.INFO, message)
    end

    local count = #harness.notices(child)
    harness.cmd(child, "FsHttp yank body")
    expect_info(count, 'Yanked the Body to register ".')
    local body = register('"')
    assert.equal('{"echoed":"ui-test-server"}', (body:gsub("%s+", "")), "the raw body text")

    count = #harness.notices(child)
    harness.cmd(child, "FsHttp yank body d")
    expect_info(count, "Yanked the Body to register d.")
    assert.equal(body, register("d"), "the register argument gets the same Body text")

    harness.lua_get(child, "vim.api.nvim_set_current_win(...)", { assert(only_window(snapshot)).id })

    count = #harness.notices(child)
    harness.type_keys(child, '"ayr')
    expect_info(count, "Yanked the Request to register a.")
    local request = register("a")
    local tail = '\n\n{"posted":"request-section-fixture"}'
    assert.equal("POST " .. base_url .. "/echo", request:match("^[^\n]*"), request)
    assert.equal(true, request:find("\nX-Fixture: request-section\n", 1, true) ~= nil, request)
    assert.equal(tail, request:sub(-#tail), request)

    count = #harness.notices(child)
    harness.type_keys(child, '"byh')
    expect_info(count, "Yanked the Response headers to register b.")
    local headers = register("b")
    assert.equal("200 OK", headers:match("^[^\n]*"), headers)
    assert.equal(true, headers:find("\nContent-Type: ", 1, true) ~= nil, headers)

    count = #harness.notices(child)
    harness.type_keys(child, '"cyb')
    expect_info(count, "Yanked the Body to register c.")
    assert.equal(true, register("c"):find('"echoed"', 1, true) ~= nil, register("c"))

    count = #harness.notices(child)
    harness.type_keys(child, "g?")
    expect_info(
        count,
        table.concat({
            "Keys of the Response buffer:",
            "yr  Yank the Request",
            "yh  Yank the Response headers",
            "yb  Yank the Body",
            "<CR>  Move to a Compile error position",
            "g?  List the active keys",
        }, "\n")
    )
end

T["a yank to + with no clipboard provider gives the ERROR notice that names the failure"] = function()
    local child = harness.harness_setup_child()
    open_script(child, harness.fixture("request-section.fsx"), { 27 })
    harness.run_at(child, 28)
    local snapshot = eventually_response(child, "the echoed body in the Response buffer", function(shown)
        return only_window(shown) ~= nil and has_line(shown.lines, '  "echoed": "ui-test-server"')
    end)
    harness.lua_get(child, "vim.api.nvim_set_current_win(...)", { assert(only_window(snapshot)).id })
    -- A runner with pbcopy, xclip, or wl-clipboard has a provider, so the Check turns it off.
    harness.lua_get(
        child,
        [[(function()
        _G.fshttp_suite_clipboard_provider = vim.g.loaded_clipboard_provider
        vim.g.loaded_clipboard_provider = 1
    end)()]]
    )

    local count = #harness.notices(child)
    harness.type_keys(child, '"+yr')
    expect_notice(
        child,
        count,
        vim.log.levels.ERROR,
        "Could not yank the Request to register +. "
            .. "Neovim has no clipboard provider. Install one, such as pbcopy, xclip, or wl-clipboard."
    )
    harness.lua_get(child, "(function() vim.g.loaded_clipboard_provider = _G.fshttp_suite_clipboard_provider end)()")
end

T["a yank before any Run gives a WARN notice, and an unknown name or register gives an ERROR notice"] = function()
    local child = harness.start_child({})

    harness.cmd(child, "FsHttp yank request")
    expect_notice(child, 0, vim.log.levels.WARN, "The latest Run gave no response. Run a Block first.")

    harness.cmd(child, "FsHttp yank nothing")
    expect_notice(child, 1, vim.log.levels.ERROR, ":FsHttp yank takes one of: request, headers, body.")

    harness.cmd(child, "FsHttp yank body ab")
    expect_notice(child, 2, vim.log.levels.ERROR, ":FsHttp yank takes one register name, such as + or a. It got ab.")
end

return T
