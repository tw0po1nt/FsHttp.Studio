local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local doc_dir = vim.fs.joinpath(vim.uv.cwd(), "doc")

-- :helptags fails on a duplicate tag, so each Check also proves that each tag is unique.
---@return nvim_suite.Child
local function child_with_help_tags()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.cmd(child, "helptags " .. vim.fn.fnameescape(doc_dir))
    return child
end

---@return table<string, true>
local function help_tags()
    local tags = {}
    for _, line in ipairs(vim.fn.readfile(vim.fs.joinpath(doc_dir, "tags"))) do
        tags[line:match("^[^\t]+")] = true
    end
    return tags
end

---@return string[]
local function help_lines()
    return vim.fn.readfile(vim.fs.joinpath(doc_dir, "fshttp.txt"))
end

---@param value any
---@return string
local function default_text(value)
    if type(value) == "string" then
        return string.format("%q", value)
    end
    return tostring(value)
end

---@param defaults table
---@param key string the dotted name of the key
---@return any
local function default_of(defaults, key)
    local name, sub = key:match("^([^.]+)%.(.+)$")
    if name then
        return defaults[name][sub]
    end
    return defaults[key]
end

T[":help fshttp opens doc/fshttp.txt"] = function()
    local child = child_with_help_tags()

    harness.cmd(child, "help fshttp")

    assert.equal("help", harness.lua_get(child, "vim.bo.buftype"))
    local name = harness.lua_get(child, "vim.fs.normalize(vim.api.nvim_buf_get_name(0))")
    assert.equal(true, vim.endswith(name, "/doc/fshttp.txt"), name)
end

T["each subcommand, option key, and <Plug> map has a help tag"] = function()
    local child = child_with_help_tags()
    local expected = {}
    for _, name in ipairs(harness.lua_get(child, [[vim.tbl_keys(require("fshttp.command").subcommands)]])) do
        expected[#expected + 1] = ":FsHttp-" .. name
    end
    for _, key in ipairs(harness.lua_get(child, [[require("fshttp.options").keys()]])) do
        expected[#expected + 1] = "fshttp-" .. key
    end
    local plug_maps = harness.lua_get(
        child,
        [[vim.tbl_map(function(map) return map.lhs end, vim.tbl_filter(function(map)
            return vim.startswith(map.lhs, "<Plug>(FsHttp")
        end, vim.api.nvim_get_keymap("n")))]]
    )
    vim.list_extend(expected, plug_maps)
    assert.equal(true, vim.tbl_contains(expected, "<Plug>(FsHttpRun)"), vim.inspect(expected))

    local tags = help_tags()

    local missing = vim.tbl_filter(function(tag)
        return not tags[tag]
    end, expected)
    table.sort(missing)
    assert.same({}, missing)
end

T["the defaults block of the help file gives the option defaults"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    local lines = help_lines()
    local first = assert(
        vim.iter(ipairs(lines)):find(function(_, line)
            return vim.endswith(line, "The defaults: >lua")
        end),
        "the help file has no defaults block"
    )
    local block = {}
    for i = first + 1, #lines do
        if vim.startswith(lines[i], "<") then
            break
        end
        block[#block + 1] = lines[i]
    end

    local shown
    local chunk = assert(loadstring(table.concat(block, "\n"), "the defaults block"))
    setfenv(chunk, {
        require = function()
            return {
                setup = function(opts)
                    shown = opts
                end,
            }
        end,
    })
    chunk()

    assert.same(harness.lua_get(child, [[require("fshttp.options").defaults()]]), shown)
end

T["the default line of each option gives its default"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    local defaults = harness.lua_get(child, [[require("fshttp.options").defaults()]])
    local lines = help_lines()

    local wrong = {}
    for _, key in ipairs(harness.lua_get(child, [[require("fshttp.options").keys()]])) do
        local tag = "*fshttp-" .. key .. "*"
        local at = vim.iter(ipairs(lines)):find(function(_, line)
            return vim.trim(line) == tag
        end)
        local shown
        for i = (at or #lines) + 1, math.min((at or #lines) + 3, #lines) do
            shown = shown or lines[i]:match("%(default: (.-)%)$")
        end
        local expected = default_text(default_of(defaults, key))
        if shown ~= expected then
            wrong[#wrong + 1] =
                string.format("%s: the help file gives %s, the default is %s", key, tostring(shown), expected)
        end
    end
    assert.same({}, wrong)
end

T["each highlight group has a line with its default link"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    local links = harness.lua_get(
        child,
        [[vim.tbl_extend("error", require("fshttp.locator").highlight_links, require("fshttp.response_buffer").highlight_links)]]
    )
    local lines = help_lines()

    local missing = {}
    for group, link in pairs(links) do
        local found = vim.iter(lines):any(function(line)
            local shown_group, shown_link = line:match("^%s+(%S+)%s+(%S+)$")
            return shown_group == group and shown_link == link
        end)
        if not found then
            missing[#missing + 1] = group .. " " .. link
        end
    end
    table.sort(missing)
    assert.same({}, missing)
end

return T
