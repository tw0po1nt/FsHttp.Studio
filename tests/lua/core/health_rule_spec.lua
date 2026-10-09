local core_env = require("lua.core_env")

describe("fshttp.health_rule", function()
    local rule = core_env.load("fshttp.health_rule")

    it("gives OK for the ready state", function()
        assert.equal("ok", rule.state_level("ready"))
    end)

    it("gives ERROR for each state that cannot become ready", function()
        for _, state in ipairs({ "sdkNotFound", "downloadFailed", "noRelease", "companionNotFound", "stopped" }) do
            assert.equal("error", rule.state_level(state), state)
        end
    end)

    it("gives INFO for a state that can still become ready, and before the start sequence", function()
        for _, state in ipairs({ "starting", "downloading" }) do
            assert.equal("info", rule.state_level(state), state)
        end
        assert.equal("info", rule.state_level(nil))
    end)

    it("names curl, tar, and the checksum tool of each operating system", function()
        assert.same({ "curl", "tar", "sha256sum" }, rule.download_tools("Linux"))
        assert.same({ "curl", "tar", "shasum" }, rule.download_tools("Darwin"))
        assert.same({ "curl", "tar", "certutil" }, rule.download_tools("Windows_NT"))
    end)

    it("names the json, xml, and html parsers, and what each missing parser changes", function()
        local languages = {}
        for _, parser in ipairs(rule.parsers) do
            languages[#languages + 1] = parser.language
            local missing = rule.parser_missing(parser)
            assert.equal(true, missing:find("No tree-sitter parser for " .. parser.language, 1, true) ~= nil, missing)
            assert.equal(true, missing:find("body shows", 1, true) ~= nil, missing)
        end
        assert.same({ "json", "xml", "html" }, languages)
    end)

    it("states the reason and what degrades when no image can show", function()
        assert.equal(
            "No image can show: snacks.nvim is not installed. An image body shows its pixel size and no image.",
            rule.images_missing("snacks.nvim is not installed")
        )
    end)
end)
