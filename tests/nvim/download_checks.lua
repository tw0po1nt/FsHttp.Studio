local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local fixture = harness.fixture("harness-setup.fsx")

-- Starts a child Neovim for the client version `version`, with an empty download folder. The
-- download folder of the runner is private to the suite, so the Check may delete it.
---@param version string
---@param opts? table
---@return nvim_suite.Child child, string root
local function child_with_empty_root(version, opts)
    local child = harness.start_child(opts or {}, version)
    local root = harness.download_root(child)
    vim.fn.delete(root, "rf")
    return child, root
end

---@param child nvim_suite.Child
---@param level integer
---@param subject string
---@return nvim_suite.Notice
local function notice_at(child, level, subject)
    harness.eventually(harness.notice_deadline_ms, subject, function()
        return #harness.notices_at(child, level) > 0
    end)
    return harness.notices_at(child, level)[1]
end

---@param message string
---@param text string
local function assert_contains(message, text)
    assert.equal(true, message:find(text, 1, true) ~= nil, string.format("%q lacks %q", message, text))
end

T["a clean first start downloads, verifies, and unpacks the archive, checks the SDK floor, and starts the companion"] = function()
    harness.publish_release("9.9.1")
    local child, root = child_with_empty_root("9.9.1")
    -- A version folder of an older release goes after the download succeeds.
    vim.fn.mkdir(root .. "/0.0.1", "p")

    local status = harness.edit_and_read_status(child, fixture)
    assert.equal("FsHttp.Studio: downloading companion…", status)

    harness.eventually_equal(harness.companion_exists_deadline_ms, "the companion state", "ready", function()
        return harness.lua_get(child, [[require("fshttp.companion").state()]])
    end)
    assert.equal(1, vim.fn.filereadable(root .. "/9.9.1/Companion.dll"))
    assert.equal(0, vim.fn.isdirectory(root .. "/0.0.1"), "the older version folder is gone")
    assert.equal(0, #vim.fn.glob(root .. "/.work-*", false, true), "no work folder is left")

    local info = harness.notices_at(child, vim.log.levels.INFO)[1]
    assert_contains(info.message, "downloading the companion for v9.9.1")
    assert.equal(0, #harness.notices_at(child, vim.log.levels.WARN), "a downloaded companion gets no version check")
end

T["a rename that fails because another Neovim installed the same version first still starts the companion"] = function()
    harness.publish_release("9.9.6")
    local child, root = child_with_empty_root("9.9.6")
    -- The stub stands in for the other instance: it installs the version folder, and then the rename fails.
    harness.lua_get(
        child,
        [[(function()
            local target = require("fshttp.download").folder(require("fshttp.version"))
            local rename = vim.uv.fs_rename
            vim.uv.fs_rename = function(from, to)
                if to ~= target then
                    return rename(from, to)
                end
                assert(rename(from, to))
                return nil, "ENOTEMPTY: directory not empty"
            end
        end)()]]
    )
    harness.edit(child, fixture)

    harness.eventually_equal(harness.companion_exists_deadline_ms, "the companion state", "ready", function()
        return harness.lua_get(child, [[require("fshttp.companion").state()]])
    end)
    assert.equal(1, vim.fn.filereadable(root .. "/9.9.6/Companion.dll"))
    assert.equal(0, #harness.notices_at(child, vim.log.levels.ERROR), "no ERROR notice")
end

T["a bad checksum starts no companion, gives the ERROR notice, and shows companion download failed"] = function()
    harness.publish_release("9.9.2", true)
    local child, root = child_with_empty_root("9.9.2")
    harness.edit(child, fixture)

    harness.expect_status(child, "the download failed row", "FsHttp.Studio: companion download failed")
    local error_notice = notice_at(child, vim.log.levels.ERROR, "an ERROR notice")
    assert_contains(error_notice.message, "checksum")
    assert_contains(error_notice.message, "Run :FsHttp restart to try again.")
    assert.equal(0, vim.fn.isdirectory(root .. "/9.9.2"), "the archive was not unpacked")
    harness.holds_for_settle("no ready companion", function()
        return harness.lua_get(child, [[require("fshttp.companion").state()]]) == "downloadFailed"
    end)
end

T["a 404 from the test server shows the no-release row and the ERROR notice"] = function()
    local child = child_with_empty_root("9.9.3")
    harness.edit(child, fixture)

    harness.expect_status(child, "the no-release row", "FsHttp.Studio: no companion for v9.9.3")
    local error_notice = notice_at(child, vim.log.levels.ERROR, "an ERROR notice")
    assert_contains(error_notice.message, "v9.9.3")
    assert_contains(error_notice.message, "Pin the plugin to a release")
    assert_contains(error_notice.message, "companion_path")
end

T["a companion_path with no Companion.dll gives companion not found and the ERROR notice"] = function()
    local empty = vim.fn.tempname() .. "-no-companion"
    vim.fn.mkdir(empty, "p")
    local child = harness.start_child({ companion_path = empty })
    harness.edit(child, fixture)

    harness.expect_status(child, "the companion not found row", "FsHttp.Studio: companion not found")
    local error_notice = notice_at(child, vim.log.levels.ERROR, "an ERROR notice")
    assert_contains(error_notice.message, empty)
    assert_contains(error_notice.message, "Companion.dll")
    assert_contains(error_notice.message, "remove companion_path")
    assert_contains(error_notice.message, ":FsHttp restart")
    vim.fn.delete(empty, "rf")
end

T["build.lua downloads the archive, and with companion_path set it downloads nothing"] = function()
    harness.publish_release("9.9.4")
    local child, root = child_with_empty_root("9.9.4")
    harness.lua_get(child, [[dofile(vim.uv.cwd() .. "/build.lua")]])
    assert.equal(1, vim.fn.filereadable(root .. "/9.9.4/Companion.dll"))

    local with_path = harness.start_child({ companion_path = harness.companion_path() }, "9.9.5")
    local root_with_path = harness.download_root(with_path)
    vim.fn.delete(root_with_path .. "/9.9.5", "rf")
    harness.lua_get(with_path, [[dofile(vim.uv.cwd() .. "/build.lua")]])
    assert.equal(0, vim.fn.isdirectory(root_with_path .. "/9.9.5"), "no download")
    local info = harness.notices_at(with_path, vim.log.levels.INFO)[1]
    assert_contains(info.message, "companion_path is in use")
end

return T
