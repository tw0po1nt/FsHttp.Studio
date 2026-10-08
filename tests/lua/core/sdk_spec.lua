local core_env = require("lua.core_env")

local runtimeconfig = [[
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "rollForward": "LatestMajor",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "10.0.0"
    }
  }
}]]

local list_sdks = "8.0.404 [/usr/local/share/dotnet/sdk]\n"
    .. "10.0.201 [/usr/local/share/dotnet/sdk]\n"
    .. "11.0.100-rc.1.26425.128 [/usr/local/share/dotnet/sdk]\n"

describe("fshttp.sdk", function()
    local sdk = core_env.load("fshttp.sdk")

    it("uses dotnet on PATH when dotnet_path is nil or blank", function()
        assert.equal("dotnet", sdk.dotnet_command(nil))
        assert.equal("dotnet", sdk.dotnet_command(""))
        assert.equal("dotnet", sdk.dotnet_command("  \t"))
    end)

    it("uses dotnet_path when it is set", function()
        assert.equal("/opt/dotnet/dotnet", sdk.dotnet_command("/opt/dotnet/dotnet"))
    end)

    it("reads the floor from the framework version of Companion.runtimeconfig.json", function()
        assert.equal(10, sdk.floor(runtimeconfig))
        assert.equal(11, sdk.floor((runtimeconfig:gsub('"10%.0%.0"', '"11.0.0"'))))
    end)

    it("uses the fallback floor when Companion.runtimeconfig.json is missing or bad", function()
        assert.equal(10, sdk.fallback_floor)
        assert.equal(sdk.fallback_floor, sdk.floor(nil))
        assert.equal(sdk.fallback_floor, sdk.floor("{"))
        assert.equal(sdk.fallback_floor, sdk.floor('{"runtimeOptions":{}}'))
        assert.equal(sdk.fallback_floor, sdk.floor('{"runtimeOptions":{"framework":{"version":"x.0"}}}'))
    end)

    it("finds an SDK at the floor or above in the dotnet --list-sdks output", function()
        assert.is_true(sdk.has_sdk_at_floor(10, list_sdks))
        assert.is_true(sdk.has_sdk_at_floor(11, list_sdks))
        assert.is_true(sdk.has_sdk_at_floor(10, "10.0.100 [C:\\Program Files\\dotnet\\sdk]\r\n"))
    end)

    it("finds no SDK when each SDK is below the floor, or when the output is empty", function()
        assert.is_false(sdk.has_sdk_at_floor(12, list_sdks))
        assert.is_false(sdk.has_sdk_at_floor(10, "8.0.404 [/sdk]\n9.0.100 [/sdk]\n"))
        assert.is_false(sdk.has_sdk_at_floor(10, ""))
        assert.is_false(sdk.has_sdk_at_floor(10, "10abc.0.1 [/sdk]\n"))
    end)

    it("names the floor and the download URL in each WARN notice", function()
        for _, dotnet_path in ipairs({ false, "/missing/dotnet" }) do
            local notice = sdk.not_found_notice(10, dotnet_path or nil)
            assert.is_truthy(notice:find(".NET 10 SDK", 1, true))
            assert.is_truthy(notice:find("https://aka.ms/dotnet/download", 1, true))
        end
    end)

    it("names dotnet_path in the WARN notice when dotnet_path is set", function()
        local notice = sdk.not_found_notice(10, "/missing/dotnet")
        assert.is_truthy(notice:find("dotnet_path (/missing/dotnet)", 1, true))
        assert.is_falsy(sdk.not_found_notice(10, nil):find("/missing/dotnet", 1, true))
    end)
end)
