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

return T
