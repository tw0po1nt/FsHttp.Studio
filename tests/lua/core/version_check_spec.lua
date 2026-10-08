local core_env = require("lua.core_env")

describe("fshttp.version_check", function()
    local version_check = core_env.load("fshttp.version_check")

    it("matches a companion version with a suffix to the plugin version", function()
        assert.is_true(version_check.matches("0.3.0", "0.3.0-beta.2"))
    end)

    it("matches a companion version that is equal to the plugin version", function()
        assert.is_true(version_check.matches("0.3.0", "0.3.0"))
    end)

    it("gives a mismatch for a different patch version", function()
        assert.is_false(version_check.matches("0.3.0", "0.3.1"))
    end)

    it("gives a mismatch for a missing companion version", function()
        assert.is_false(version_check.matches("0.3.0", nil))
    end)

    it("gives a mismatch for an empty companion version", function()
        assert.is_false(version_check.matches("0.3.0", ""))
    end)

    it("names both versions and the two fixes in the WARN notice", function()
        local notice = version_check.mismatch_notice("0.3.0", "0.2.0")
        assert.equal(true, notice:find("FsHttp.Studio is version 0.3.0", 1, true) ~= nil, notice)
        assert.equal(true, notice:find("is version 0.2.0", 1, true) ~= nil, notice)
        assert.equal(true, notice:find("Set companion_path to a build of version 0.3.0", 1, true) ~= nil, notice)
        assert.equal(true, notice:find("remove companion_path", 1, true) ~= nil, notice)
    end)

    it("states that the companion reports no version when the version is missing", function()
        local notice = version_check.mismatch_notice("0.3.0", nil)
        assert.equal(true, notice:find("FsHttp.Studio is version 0.3.0", 1, true) ~= nil, notice)
        assert.equal(true, notice:find("reports no version", 1, true) ~= nil, notice)
        assert.equal(true, notice:find("Set companion_path to a build of version 0.3.0", 1, true) ~= nil, notice)
    end)
end)
