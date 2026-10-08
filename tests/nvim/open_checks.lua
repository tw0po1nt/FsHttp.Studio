local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local block_line = 25
local policy_meta =
    "<meta http-equiv=\"Content-Security-Policy\" content=\"script-src 'none'; object-src 'none'; frame-src 'none'; style-src * 'unsafe-inline'; img-src * data:\">"

-- Cross-process contract for `GET /html`. It must match `UiTestServer.Server.htmlBody`.
local html_body =
    "<!doctype html><html><head><title>probe</title></head><body><h1>ui-test-server</h1><script>document.title = 'ran'</script></body></html>"

-- Replaces vim.ui.open in the child with a stub. The stub records the path and reads the file at
-- the time of the call, so a Check can later tell that the file did not change.
---@param child nvim_suite.Child
local function stub_open(child)
    harness.lua_get(
        child,
        [[(function()
            _G.fshttp_open_calls = {}
            vim.ui.open = function(path)
                local file = assert(io.open(path, "rb"))
                local content = file:read("*a")
                file:close()
                table.insert(_G.fshttp_open_calls, { path = path, content = content })
                return {}, nil
            end
            return true
        end)()]]
    )
end

---@param child nvim_suite.Child
---@return { path: string, content: string }[]
local function open_calls(child)
    return harness.lua_get(child, [[_G.fshttp_open_calls]])
end

-- The virtual lines on the Body header, and the real line below it.
---@param child nvim_suite.Child
---@return { virtual: string[], next_line: string? }
local function body_header(child)
    return harness.lua_get(
        child,
        [[(function()
            local buf
            for _, candidate in ipairs(vim.api.nvim_list_bufs()) do
                if vim.bo[candidate].filetype == "fshttp_response" then
                    buf = candidate
                end
            end
            local lines = vim.api.nvim_buf_get_lines(buf, 0, -1, false)
            for i, line in ipairs(lines) do
                if line:sub(1, #"▾ Body") == "▾ Body" then
                    local virtual = {}
                    local marks = vim.api.nvim_buf_get_extmarks(buf, -1, { i - 1, 0 }, { i - 1, -1 }, { details = true })
                    for _, mark in ipairs(marks) do
                        for _, virt_line in ipairs(mark[4].virt_lines or {}) do
                            local text = ""
                            for _, chunk in ipairs(virt_line) do
                                text = text .. chunk[1]
                            end
                            table.insert(virtual, text)
                        end
                    end
                    return { virtual = virtual, next_line = lines[i + 1] }
                end
            end
            return { virtual = {}, next_line = nil }
        end)()]]
    )
end

-- Opens the fixture, runs its Block, and waits until the first line below the Body header starts
-- with `expected`. An earlier Check can leave a Response buffer in the child, so any body would not do.
---@param child nvim_suite.Child
---@param fixture_name string
---@param expected string
local function run_block(child, fixture_name, expected)
    harness.edit(child, harness.fixture(fixture_name))
    harness.eventually(harness.block_mark_deadline_ms, "a Block mark on line " .. block_line, function()
        return harness.block_marks(child):find("^" .. block_line .. ": ") ~= nil
    end)
    harness.run_at(child, block_line)
    harness.eventually(harness.response_deadline_ms, "the body of this Run below the Body header", function()
        local next_line = body_header(child).next_line
        return next_line ~= nil and next_line:sub(1, #expected) == expected
    end)
end

T["writes an HTML body with the policy to a static file, and calls vim.ui.open"] = function()
    local child = harness.harness_setup_child()
    stub_open(child)
    run_block(child, "open-body.fsx", html_body)

    harness.cmd(child, "FsHttp open")

    local calls = open_calls(child)
    assert.equal(1, #calls, vim.inspect(calls))
    assert.equal(".html", calls[1].path:sub(-5))
    assert.equal(
        html_body:gsub("<head>", function()
            return "<head>" .. policy_meta
        end, 1),
        calls[1].content
    )

    -- The next Run, and the next open, leave the first file as it was.
    harness.run_at(child, block_line)
    harness.cmd(child, "FsHttp open")
    local later = open_calls(child)
    assert.equal(2, #later, vim.inspect(later))
    assert.is_true(later[1].path ~= later[2].path)
    local file = assert(io.open(later[1].path, "rb"))
    local still = file:read("*a")
    file:close()
    assert.equal(calls[1].content, still)
end

T["an HTML body shows the open hint directly below the Body header"] = function()
    local child = harness.harness_setup_child()
    run_block(child, "open-body.fsx", html_body)

    local header = body_header(child)

    assert.same({ ":FsHttp open  shows the rendered page in the browser, with scripts blocked" }, header.virtual)
    assert.equal(html_body, header.next_line)
end

T["an image body shows the open hint directly below the Body header, and opens as a png"] = function()
    local child = harness.harness_setup_child()
    stub_open(child)
    run_block(child, "image-body.fsx", "100×100 px")

    local header = body_header(child)
    harness.cmd(child, "FsHttp open")

    assert.same({ ":FsHttp open  shows the image in the system viewer" }, header.virtual)
    local calls = open_calls(child)
    assert.equal(1, #calls, vim.inspect(calls))
    assert.equal(".png", calls[1].path:sub(-4))
    assert.equal("\137PNG\r\n\26\n", calls[1].content:sub(1, 8))
    assert.equal(218, #calls[1].content)
end

return T
