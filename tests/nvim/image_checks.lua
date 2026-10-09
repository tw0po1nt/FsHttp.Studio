local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local fixture = harness.fixture("image-body.fsx")
local block_line = 25

-- The PNG that `GET /image` gives. It must match `UiTestServer.Server.imageBody`.
local png_signature = "\137PNG\r\n\26\n"
local png_size = 218

-- Replaces snacks.nvim in the child with a stub. The stub records each call of the image placement
-- in `_G.fshttp_image_calls`, and reads the file at the time of the call, because the client removes
-- the file on the next paint.
---@param child nvim_suite.Child
---@param terminal_supported boolean
local function stub_snacks(child, terminal_supported)
    harness.lua_get(
        child,
        [[(function(terminal_supported)
            _G.fshttp_image_calls = {}
            package.loaded.snacks = {
                image = {
                    supports_terminal = function()
                        return terminal_supported
                    end,
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
        { terminal_supported }
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

-- Opens the fixture, runs its Block, and waits until the first line below the Body title is `expected`.
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

T["an image body shows its pixel size, and calls the image placement of snacks.nvim with a temporary file"] = function()
    local child = harness.harness_setup_child()
    stub_snacks(child, true)

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
    assert.equal(false, call.opts.inline)
end

T["an image body in a terminal with no image support shows its pixel size and the reason"] = function()
    local child = harness.harness_setup_child()
    stub_snacks(child, false)

    run_image_block(child, "100×100 px  the terminal does not support images")

    assert.same({}, harness.lua_get(child, [[_G.fshttp_image_calls]]))
end

return T
