local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local fixture = harness.fixture("image-body.fsx")
local block_line = 25

-- The PNG that `GET /image` gives. It must match `UiTestServer.Server.imageBody`.
local png_signature = "\137PNG\r\n\26\n"
local png_size = 218

-- The stub reads the file at the time of the call, because the client removes the file on the next paint.
---@param child nvim_suite.Child
---@param terminal { images: boolean, placeholders: boolean }
local function stub_snacks(child, terminal)
    harness.lua_get(
        child,
        [[(function(terminal)
            _G.fshttp_image_calls = {}
            package.loaded.snacks = {
                image = {
                    supports_terminal = function()
                        return terminal.images
                    end,
                    terminal = {
                        env = function()
                            return { supported = terminal.images, placeholders = terminal.placeholders }
                        end,
                    },
                    supports_file = function()
                        return true
                    end,
                    placement = {
                        new = function(buf, src, opts)
                            local file = assert(io.open(src, "rb"))
                            local bytes = file:read("*a")
                            file:close()
                            table.insert(_G.fshttp_image_calls, {
                                buf = buf,
                                src = src,
                                opts = opts,
                                size = #bytes,
                                signature = bytes:sub(1, 8),
                                filetype = vim.bo[buf].filetype,
                                line = vim.api.nvim_buf_get_lines(buf, opts.pos[1] - 1, opts.pos[1], false)[1],
                            })
                            return { close = function() end }
                        end,
                    },
                },
            }
            return true
        end)(...)]],
        { terminal }
    )
end

-- The later Checks reuse the child of Harness setup, so a failed Check must restore the PATH of the child.
---@param child nvim_suite.Child
---@param check fun()
local function without_imagemagick(child, check)
    local empty = vim.fn.tempname() .. "-empty"
    vim.fn.mkdir(empty, "p")
    local path = harness.lua_get(
        child,
        [[(function(empty)
            local path = vim.env.PATH
            vim.env.PATH = empty
            return path
        end)(...)]],
        { empty }
    )
    local ok, err = pcall(check)
    harness.lua_get(
        child,
        [[(function(path)
            vim.env.PATH = path
            return true
        end)(...)]],
        { path }
    )
    if not ok then
        error(err, 0)
    end
end

---@class nvim_suite.ImageMark
---@field row integer 0-based
---@field virt_lines integer
---@field virt_lines_above boolean

---@param child nvim_suite.Child
---@return nvim_suite.ImageMark[]
local function image_marks(child)
    return harness.lua_get(
        child,
        [[(function()
            local namespace = vim.api.nvim_get_namespaces()["snacks.image"]
            local marks = {}
            if not namespace then
                return marks
            end
            for _, buf in ipairs(vim.api.nvim_list_bufs()) do
                if vim.bo[buf].filetype == "fshttp_response" then
                    for _, mark in ipairs(vim.api.nvim_buf_get_extmarks(buf, namespace, 0, -1, { details = true })) do
                        marks[#marks + 1] = {
                            row = mark[2],
                            virt_lines = #(mark[4].virt_lines or {}),
                            virt_lines_above = mark[4].virt_lines_above == true,
                        }
                    end
                end
            end
            return marks
        end)()]]
    )
end

---@param child nvim_suite.Child
---@return string[]? lines the lines below the Body title
local function body_lines(child)
    local lines = harness.response_buffer(child).lines or {}
    for i, line in ipairs(lines) do
        if line:sub(1, #"▾ Body") == "▾ Body" then
            return { unpack(lines, i + 1) }
        end
    end
    return nil
end

---@param child nvim_suite.Child
---@param expected string
local function run_image_block(child, expected)
    harness.edit(child, fixture)
    harness.await_block_mark(child, block_line)

    harness.run_at(child, block_line)

    local lines
    local ok, err = pcall(
        harness.eventually,
        harness.response_deadline_ms,
        "the pixel size below the Body title",
        function()
            lines = body_lines(child)
            return lines ~= nil and lines[1] == expected
        end
    )
    if not ok then
        error(string.format("%s\nlast lines below the Body title:\n%s", err, vim.inspect(lines)), 0)
    end
    assert.same({ expected }, lines)
end

T["an image body with no snacks.nvim shows its pixel size and the reason"] = function()
    local child = harness.harness_setup_child()
    assert.equal(false, harness.lua_get(child, [[(pcall(require, "snacks"))]]), "the child Neovim has no snacks.nvim")

    run_image_block(child, "100×100 px  snacks.nvim is not installed")
end

T["an image body shows its pixel size, and calls the inline image placement of snacks.nvim with a temporary file"] = function()
    local child = harness.harness_setup_child()
    stub_snacks(child, { images = true, placeholders = true })

    run_image_block(child, "100×100 px")

    local calls = harness.lua_get(child, [[_G.fshttp_image_calls]])
    assert.equal(1, #calls, vim.inspect(calls))
    local call = calls[1]
    assert.equal("fshttp_response", call.filetype)
    assert.equal(png_signature, call.signature)
    assert.equal(png_size, call.size)
    assert.equal(".png", call.src:sub(-4))
    assert.equal("100×100 px", call.line)
    -- `line` is the text at the row of `pos`, so the placement sits below the pixel size.
    assert.equal(0, call.opts.pos[2])
    assert.equal(true, call.opts.inline)
end

T["an image body with no ImageMagick shows its pixel size and the reason"] = function()
    local child = harness.harness_setup_child()
    stub_snacks(child, { images = true, placeholders = true })

    without_imagemagick(child, function()
        run_image_block(child, "100×100 px  ImageMagick is not installed")
    end)

    assert.same({}, harness.lua_get(child, [[_G.fshttp_image_calls]]))
end

T["an image body in a terminal with no image support shows its pixel size and the reason"] = function()
    local child = harness.harness_setup_child()
    stub_snacks(child, { images = false, placeholders = false })

    run_image_block(child, "100×100 px  the terminal does not support images")

    assert.same({}, harness.lua_get(child, [[_G.fshttp_image_calls]]))
end

T["an image body in a terminal with no Unicode placeholders shows its pixel size and the reason"] = function()
    local child = harness.harness_setup_child()
    stub_snacks(child, { images = true, placeholders = false })

    run_image_block(child, "100×100 px  the terminal does not support Unicode placeholders")

    assert.same({}, harness.lua_get(child, [[_G.fshttp_image_calls]]))
end

T["an image body shows through the real snacks.nvim as virtual lines below the pixel size, and the text stays"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() }, nil, false, true)
    assert.equal(true, harness.lua_get(child, [[(pcall(require, "snacks"))]]), "the child Neovim has snacks.nvim")

    run_image_block(child, "100×100 px")
    local text = assert(harness.response_buffer(child).lines, "the child Neovim has a Response buffer")
    local size_row
    for i, line in ipairs(text) do
        if line == "100×100 px" then
            size_row = i - 1
        end
    end

    local marks
    local ok, err = pcall(
        harness.eventually,
        harness.response_deadline_ms,
        "an image extmark with virtual lines in the Response buffer",
        function()
            marks = image_marks(child)
            return #marks > 0 and marks[1].virt_lines > 0
        end
    )
    if not ok then
        error(string.format("%s\nlast image extmarks:\n%s", err, vim.inspect(marks)), 0)
    end
    assert.equal(1, #marks, vim.inspect(marks))
    assert.equal(size_row, marks[1].row)
    assert.equal(false, marks[1].virt_lines_above)
    assert.same(text, harness.response_buffer(child).lines)
end

return T
