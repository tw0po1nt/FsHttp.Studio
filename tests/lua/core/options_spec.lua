local core_env = require("lua.core_env")

describe("fshttp.options", function()
    local options = core_env.load("fshttp.options")

    it("gives the defaults of the spec for no opts", function()
        local config, problems = options.resolve(nil)
        assert.same({}, problems)
        assert.same({
            request_timeout_ms = 30000,
            block_mark = { virtual_line = true, sign = true },
            response_buffer = { split = "right", images = true, keys = true },
            status_line = { lualine = true },
        }, config)
    end)

    it("applies each good key over the defaults", function()
        local config, problems = options.resolve({
            dotnet_path = "/opt/dotnet",
            companion_path = "~/companion",
            request_timeout_ms = 0,
            block_mark = { sign = false },
            response_buffer = { split = "below", images = false, keys = false },
            status_line = { lualine = false },
        })
        assert.same({}, problems)
        assert.equal("/opt/dotnet", config.dotnet_path)
        assert.equal("~/companion", config.companion_path)
        assert.equal(0, config.request_timeout_ms)
        assert.same({ virtual_line = true, sign = false }, config.block_mark)
        assert.same({ split = "below", images = false, keys = false }, config.response_buffer)
        assert.same({ lualine = false }, config.status_line)
    end)

    it("gives one ERROR that names the key, the value, and the accepted values, and keeps the default", function()
        local config, problems = options.resolve({ response_buffer = { split = "up", images = false } })
        assert.equal(1, #problems)
        assert.equal("ERROR", problems[1].level)
        assert.equal("response_buffer.split", problems[1].key)
        assert.equal(
            'FsHttp.Studio: the option response_buffer.split is "up". It takes right, left, below, or above. '
                .. "The option keeps its default (right).",
            problems[1].message
        )
        assert.equal("right", config.response_buffer.split)
        assert.equal(false, config.response_buffer.images)
    end)

    it("checks the type of each boolean key", function()
        local config, problems = options.resolve({ status_line = { lualine = "no" }, block_mark = { sign = 1 } })
        assert.equal(2, #problems)
        assert.equal("block_mark.sign", problems[1].key)
        assert.equal("status_line.lualine", problems[2].key)
        assert.equal(true, config.status_line.lualine)
        assert.equal(true, config.block_mark.sign)
    end)

    it("rejects a timeout that is negative, a string, a boolean, infinite, or NaN", function()
        for _, bad in ipairs({ -1, "30", true, math.huge, 0 / 0 }) do
            local config, problems = options.resolve({ request_timeout_ms = bad })
            assert.equal(1, #problems)
            assert.equal("request_timeout_ms", problems[1].key)
            assert.equal(30000, config.request_timeout_ms)
        end
    end)

    it("checks only the type of a path option", function()
        local config, problems = options.resolve({ dotnet_path = "", companion_path = "/no/such/folder" })
        assert.same({}, problems)
        assert.equal("/no/such/folder", config.companion_path)
        config, problems = options.resolve({ dotnet_path = 5 })
        assert.equal("dotnet_path", problems[1].key)
        assert.is_nil(config.dotnet_path)
    end)

    it("gives an ERROR for a group that is not a table", function()
        local config, problems = options.resolve({ block_mark = true })
        assert.equal("block_mark", problems[1].key)
        assert.equal("ERROR", problems[1].level)
        assert.same({ virtual_line = true, sign = true }, config.block_mark)
    end)

    it("gives a WARN for an unknown key, and applies each other key", function()
        local config, problems = options.resolve({
            theme = "red",
            block_mark = { glow = true, sign = false },
            request_timeout_ms = 5,
        })
        assert.equal(2, #problems)
        assert.equal("WARN", problems[1].level)
        assert.equal("block_mark.glow", problems[1].key)
        assert.equal("theme", problems[2].key)
        assert.equal(5, config.request_timeout_ms)
        assert.equal(false, config.block_mark.sign)
    end)

    it("gives an ERROR for opts that is a string, and uses the defaults", function()
        local config, problems = options.resolve("x")
        assert.equal("opts", problems[1].key)
        assert.same(options.defaults(), config)
    end)

    it("gives a fresh configuration on each call", function()
        local first = options.resolve({ block_mark = { sign = false } })
        local second = options.resolve(nil)
        assert.equal(true, second.block_mark.sign)
        assert.equal(false, first.block_mark.sign)
    end)

    it("names the path options that differ", function()
        local a = options.resolve({ companion_path = "/a" })
        local b = options.resolve({ companion_path = "/b", dotnet_path = "/d" })
        assert.same({ "dotnet_path", "companion_path" }, options.changed_paths(a, b))
        assert.same({}, options.changed_paths(a, a))
    end)

    it("gives no changed value for the defaults", function()
        assert.same({}, options.changed_values(options.defaults()))
    end)

    it("gives each value that differs from its default, in the order of the key names", function()
        local config = options.resolve({
            response_buffer = { split = "below", keys = true },
            dotnet_path = "/opt/dotnet",
            request_timeout_ms = 0,
        })
        assert.same({
            { key = "dotnet_path", value = "/opt/dotnet", default = nil },
            { key = "request_timeout_ms", value = 0, default = 30000 },
            { key = "response_buffer.split", value = "below", default = "right" },
        }, options.changed_values(config))
    end)

    it("states the key, the value, and the default of a changed value", function()
        local config = options.resolve({ response_buffer = { images = false }, companion_path = "/c" })
        local changes = options.changed_values(config)
        assert.equal('companion_path = "/c" (default nil)', options.change_text(changes[1]))
        assert.equal("response_buffer.images = false (default true)", options.change_text(changes[2]))
    end)
end)
