-- The screen changes with the operating system and the Neovim version, so the Checks compare only on
-- Linux with the pinned stable Neovim. NVIM_TEST_SCREENSHOTS overrides that rule.
local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local root = assert(vim.uv.cwd())
local screenshots_dir = root .. "/tests/nvim/screenshots"
local pin_path = root .. "/tests/nvim/neovim-pin.json"
local queries_dir = root .. "/tests/nvim/treesitter"

local screen_lines = 30
local screen_columns = 160
local block_line = 25
local compile_error_block_line = 14

-- The winbar shows these strings in place of the parts that change on each run.
local base_url_mask = "http://test-server"
local time_mask = "N ms"

---@return "compare"|"update"|"skip" mode
---@return string reason
local function screenshot_mode()
    local override = vim.env.NVIM_TEST_SCREENSHOTS
    if override == "1" then
        return "compare", ""
    elseif override == "update" then
        return "update", ""
    elseif override ~= nil and override ~= "" then
        error(string.format("NVIM_TEST_SCREENSHOTS is %q. Set it to 1 or update.", override), 0)
    end
    local file = assert(io.open(pin_path, "rb"), "cannot read " .. pin_path)
    local pin = vim.json.decode(file:read("*a"))
    file:close()
    local sysname = vim.uv.os_uname().sysname
    local version = vim.version()
    local stable = assert(vim.version.parse(pin.stable), "the stable version in neovim-pin.json does not parse")
    if sysname == "Linux" and vim.version.eq(version, stable) then
        return "compare", ""
    end
    return "skip",
        string.format(
            "the screenshots compare only on Linux with Neovim %s, and this leg is %s with Neovim %s. Set NVIM_TEST_SCREENSHOTS=1 to compare.",
            tostring(stable),
            sysname,
            tostring(version)
        )
end

local T = MiniTest.new_set({
    hooks = {
        pre_case = function()
            local mode, reason = screenshot_mode()
            if mode == "skip" then
                MiniTest.skip(reason)
            end
        end,
    },
})

---@return string
local function json_parser_path()
    local path = vim.env.NVIM_TEST_JSON_PARSER
    if path == nil or path == "" then
        error("NVIM_TEST_JSON_PARSER is not set, so the Check cannot find the JSON parser. Run tests/nvim/run.sh.", 0)
    end
    return path
end

-- A new child Neovim with a screen of a fixed size. Each Check starts its own child, so no earlier
-- Check can change the screen.
---@return nvim_suite.Child
local function start_screen_child()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.cmd(child, string.format("set lines=%d columns=%d", screen_lines, screen_columns))
    return child
end

-- Opens `path`, and waits for the Block mark on `line`. The status line of the Script shows the
-- path from the repo root, because `:cd` makes Neovim shorten each buffer name.
---@param child nvim_suite.Child
---@param path string
---@param line integer
local function open_script(child, path, line)
    harness.edit(child, path)
    harness.cmd(child, "cd " .. vim.fn.fnameescape(root))
    harness.await_block_mark(child, line)
end

-- Runs the Block on `line`, and waits until the Response buffer satisfies `predicate`.
---@param child nvim_suite.Child
---@param line integer
---@param subject string
---@param predicate fun(snapshot: nvim_suite.ResponseBuffer): boolean
local function run_block(child, line, subject, predicate)
    harness.run_at(child, line)
    local snapshot
    local ok, err = pcall(harness.eventually, harness.response_deadline_ms, subject, function()
        snapshot = harness.response_buffer(child)
        return #snapshot.windows == 1 and predicate(snapshot)
    end)
    if not ok then
        error(string.format("%s\nlast Response buffer:\n%s", err, vim.inspect(snapshot)), 0)
    end
end

---@param lines string[]?
---@param text string
---@return boolean
local function has_line(lines, text)
    return vim.tbl_contains(lines or {}, text)
end

-- Replaces the parts of the winbar that change on each run: the times, and the URL of the test HTTP
-- server, which keeps its port. Then clears the command line, because it shows the last message.
---@param child nvim_suite.Child
local function mask_changing_text(child)
    harness.lua_get(
        child,
        [[(function(base_url, base_url_mask, time_mask)
            for _, win in ipairs(vim.api.nvim_list_wins()) do
                local buf = vim.api.nvim_win_get_buf(win)
                if vim.bo[buf].filetype == "fshttp_response" then
                    local winbar = vim.wo[win].winbar
                    winbar = winbar:gsub(vim.pesc(base_url), base_url_mask)
                    winbar = winbar:gsub("%d+ ms", time_mask)
                    vim.wo[win].winbar = winbar
                end
            end
        end)(...)]],
        { harness.base_url(), base_url_mask, time_mask }
    )
    harness.cmd(child, "echo ''")
end

-- Compares the screen of the child with the reference file `name`. When the reference file is
-- missing, the Check fails. Without this guard, mini.test writes the file and passes.
---@param child nvim_suite.Child
---@param name string
local function expect_screenshot(child, name)
    mask_changing_text(child)
    local path = screenshots_dir .. "/" .. name
    local mode = screenshot_mode()
    if mode ~= "update" and vim.fn.filereadable(path) == 0 then
        error(string.format("no reference screenshot at %s. Set NVIM_TEST_SCREENSHOTS=update to write it.", path), 0)
    end
    MiniTest.expect.reference_screenshot(harness.screenshot(child), path, { force = mode == "update" })
end

T["a JSON body with the parser"] = function()
    local child = start_screen_child()
    harness.lua_get(
        child,
        [[(function(parser, queries)
            vim.opt.runtimepath:append(queries)
            return vim.treesitter.language.add("json", { path = parser })
        end)(...)]],
        { json_parser_path(), queries_dir }
    )

    open_script(child, harness.fixture("json-body.fsx"), block_line)
    run_block(child, block_line, "the JSON body", function(snapshot)
        return has_line(snapshot.lines, '  "speed": 30}}')
    end)

    expect_screenshot(child, "json-body")
end

T["an image body with no snacks.nvim"] = function()
    local child = start_screen_child()

    open_script(child, harness.fixture("image-body.fsx"), block_line)
    run_block(child, block_line, "the pixel size of the image", function(snapshot)
        return has_line(snapshot.lines, "100×100 px  snacks.nvim is not installed")
    end)

    expect_screenshot(child, "image-body")
end

T["an HTML body"] = function()
    local child = start_screen_child()

    open_script(child, harness.fixture("open-body.fsx"), block_line)
    run_block(child, block_line, "the HTML body", function(snapshot)
        local last = (snapshot.lines or {})[#(snapshot.lines or {})] or ""
        return last:sub(1, #"<!doctype html>") == "<!doctype html>"
    end)

    expect_screenshot(child, "html-body")
end

T["a Compile error"] = function()
    local child = start_screen_child()
    local fixture = harness.ui_fixture("compile-error.fsx")
    open_script(child, fixture, compile_error_block_line)
    harness.lua_get(child, [[vim.api.nvim_buf_set_lines(0, 11, 12, false, { 'let probe : int = "not an int"' })]])

    run_block(child, compile_error_block_line, "the Compile error at (12,19)", function(snapshot)
        local lines = snapshot.lines or {}
        return lines[1] == "Compile error:" and table.concat(lines, "\n"):find("(12,19) ", 1, true) ~= nil
    end)

    expect_screenshot(child, "compile-error")
end

return T
