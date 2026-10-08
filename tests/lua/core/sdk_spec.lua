local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

describe("fshttp.sdk", function()
    local sdk = core_env.load("fshttp.sdk")
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/sdk/sdk-floor.json")
    local golden_fixture = json.decode(bytes)

    it("uses dotnet on PATH when dotnet_path is nil or blank", function()
        assert.equal("dotnet", sdk.dotnet_command(nil))
        assert.equal("dotnet", sdk.dotnet_command(""))
        assert.equal("dotnet", sdk.dotnet_command("  \t"))
    end)

    it("uses dotnet_path when it is set", function()
        assert.equal("/opt/dotnet/dotnet", sdk.dotnet_command("/opt/dotnet/dotnet"))
    end)

    local function floor_of(case)
        local version = case.frameworkVersion
        if version == json.null then
            version = nil
        end
        local framework = json.object({ { "version", version } })
        local runtimeconfig = json.object({ { "runtimeOptions", json.object({ { "framework", framework } }) } })
        return sdk.floor(json.encode(runtimeconfig))
    end

    it("uses the fallback floor when Companion.runtimeconfig.json is missing", function()
        assert.equal(sdk.fallback_floor, sdk.floor(nil))
        assert.equal(sdk.fallback_floor, sdk.floor("{"))
    end)

    it("gives each floor case the SDK floor of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.floorCases) do
            assert.equal(case.floor, floor_of(case), case.name)
        end
    end)

    it("gives each dotnet --list-sdks case the result of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.listSdksCases) do
            assert.equal(case.hasSdkAtFloor, sdk.has_sdk_at_floor(case.floor, case.listSdksOutput), case.name)
        end
    end)

    it("writes the SDK floor Golden fixture byte for byte", function()
        local floor_cases = {}
        for i, case in ipairs(golden_fixture.floorCases) do
            floor_cases[i] = json.object({
                { "floor", floor_of(case) },
                { "frameworkVersion", case.frameworkVersion },
                { "name", case.name },
            })
        end
        local list_sdks_cases = {}
        for i, case in ipairs(golden_fixture.listSdksCases) do
            list_sdks_cases[i] = json.object({
                { "floor", case.floor },
                { "hasSdkAtFloor", sdk.has_sdk_at_floor(case.floor, case.listSdksOutput) },
                { "listSdksOutput", case.listSdksOutput },
                { "name", case.name },
            })
        end
        local written = json.object({
            { "floorCases", json.array(floor_cases) },
            { "listSdksCases", json.array(list_sdks_cases) },
        })
        assert.equal(bytes, json.encode(written))
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
