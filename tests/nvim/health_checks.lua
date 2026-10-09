local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

---@param items nvim_suite.HealthItem[]
---@param section? string
---@param level? string
---@return nvim_suite.HealthItem[]
local function items_in(items, section, level)
    return vim.tbl_filter(function(item)
        return (section == nil or item.section == section) and (level == nil or item.level == level)
    end, items)
end

---@param items nvim_suite.HealthItem[]
---@param section string
---@param text string
---@return nvim_suite.HealthItem
local function item_with(items, section, text)
    for _, item in ipairs(items) do
        if item.section == section and item.text == text then
            return item
        end
    end
    error(string.format("no item %q in the section %s:\n%s", text, section, vim.inspect(items)), 0)
end

-- The child Neovim of Harness setup, with a ready companion.
---@return nvim_suite.Child
local function ready_child()
    local child = harness.harness_setup_child()
    harness.eventually(harness.companion_exists_deadline_ms, "a ready companion", function()
        return harness.lua_get(child, [[require("fshttp.companion").state()]]) == "ready"
    end)
    return child
end

T["a valid setup reports no ERROR"] = function()
    local items = harness.checkhealth(ready_child())

    assert.same({}, items_in(items, nil, "ERROR"))
    assert.equal(true, #items_in(items, "Required items", "OK") >= 5, vim.inspect(items))
end

T["the live state line is OK when the companion is ready"] = function()
    local items = harness.checkhealth(ready_child())

    assert.equal("OK", item_with(items, "Companion", "FsHttp.Studio: companion ready").level)
end

-- The child of Harness setup can have the snacks.nvim stub of an image Check, so this Check starts its own child.
T["no snacks.nvim gives a WARN that names what degrades"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    assert.equal(false, harness.lua_get(child, [[(pcall(require, "snacks"))]]), "the child Neovim has no snacks.nvim")

    local items = harness.checkhealth(child)

    local images = item_with(
        items,
        "Optional items",
        "No image can show: snacks.nvim is not installed. An image body shows its pixel size and no image."
    )
    assert.equal("WARNING", images.level)
    assert.equal(true, #images.advice > 0, vim.inspect(images))
end

T["a dotnet_path that names a missing file gives an ERROR with the fix, and the live state line is an ERROR"] = function()
    local missing = harness.fixture("missing/dotnet")
    local child = harness.start_child({ companion_path = harness.companion_path(), dotnet_path = missing })
    harness.edit(child, harness.fixture("harness-setup.fsx"))
    harness.expect_status(child, "the .NET SDK not found row", "FsHttp.Studio: .NET SDK not found")

    local items = harness.checkhealth(child)

    local required_errors = items_in(items, "Required items", "ERROR")
    assert.equal(1, #required_errors, vim.inspect(items))
    local dotnet = required_errors[1].text
    -- setup() normalizes each path option, so the item shows the normalized path.
    local shown = "dotnet_path (" .. vim.fs.normalize(missing) .. ")"
    assert.equal(true, dotnet:find(shown, 1, true) ~= nil, dotnet)
    assert.equal(true, dotnet:find("https://aka.ms/dotnet/download", 1, true) ~= nil, dotnet)

    local state = item_with(items, "Companion", "FsHttp.Studio: .NET SDK not found")
    assert.equal("ERROR", state.level)
    assert.same({ dotnet }, state.advice)
end

T["a value that differs from its default gives one INFO line in the Options section"] = function()
    local companion_path = harness.companion_path()
    local child = harness.start_child({ companion_path = companion_path, response_buffer = { split = "below" } })

    local options = items_in(harness.checkhealth(child), "Options")

    assert.same({
        { section = "Options", level = "OK", text = "setup() applied the options.", advice = {} },
        {
            section = "Options",
            level = "INFO",
            text = string.format("companion_path = %q (default nil)", vim.fs.normalize(companion_path)),
            advice = {},
        },
        {
            section = "Options",
            level = "INFO",
            text = 'response_buffer.split = "below" (default "right")',
            advice = {},
        },
    }, options)
end

T["a bad value gives an ERROR line, and an unknown key gives a WARN line, in the Options section"] = function()
    local child =
        harness.start_child({ companion_path = harness.companion_path(), request_timeout_ms = -5, theme = "red" })

    local options = items_in(harness.checkhealth(child), "Options")

    local errors = items_in(options, nil, "ERROR")
    assert.equal(1, #errors, vim.inspect(options))
    assert.equal(
        "FsHttp.Studio: the option request_timeout_ms is -5. It takes a number of 0 or more (0 sets no bound). "
            .. "The option keeps its default (30000).",
        errors[1].text
    )
    local warnings = items_in(options, nil, "WARNING")
    assert.equal(1, #warnings, vim.inspect(options))
    assert.equal("FsHttp.Studio: setup() has no option theme. The client ignores it.", warnings[1].text)
end

T["response_buffer.images = false gives an OK image item that states images are turned off"] = function()
    local child =
        harness.start_child({ companion_path = harness.companion_path(), response_buffer = { images = false } })

    local items = harness.checkhealth(child)

    local images = item_with(items, "Optional items", "Images are turned off (response_buffer.images = false).")
    assert.equal("OK", images.level)
end

return T
