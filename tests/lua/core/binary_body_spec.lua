local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

local function to_bytes(numbers)
    local chars = {}
    for i, number in ipairs(numbers) do
        chars[i] = string.char(number)
    end
    return table.concat(chars)
end

describe("fshttp.binary_body", function()
    local binary_body = core_env.load("fshttp.binary_body")
    local json = core_env.load("fshttp.json")

    describe("the binary test", function()
        local bytes = read("tests/golden/binary/binary-test.json")
        local golden_fixture = json.decode(bytes)

        it("gives each case the result of the Golden fixture", function()
            for _, case in ipairs(golden_fixture.cases) do
                assert.equal(case.looksBinary, binary_body.looks_binary(to_bytes(case.bytes)), case.name)
            end
        end)

        it("writes the binary test Golden fixture byte for byte", function()
            local cases = {}
            for i, case in ipairs(golden_fixture.cases) do
                cases[i] = json.object({
                    { "bytes", case.bytes },
                    { "looksBinary", binary_body.looks_binary(to_bytes(case.bytes)) },
                    { "name", case.name },
                })
            end
            assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
        end)
    end)

    describe("the hex dump", function()
        local bytes = read("tests/golden/binary/hex-dump.json")
        local golden_fixture = json.decode(bytes)

        it("gives each case the hex dump of the Golden fixture", function()
            for _, case in ipairs(golden_fixture.cases) do
                assert.equal(case.hexDump, binary_body.hex_dump(to_bytes(case.bytes)), case.name)
            end
        end)

        it("writes the hex dump Golden fixture byte for byte", function()
            local cases = {}
            for i, case in ipairs(golden_fixture.cases) do
                cases[i] = json.object({
                    { "bytes", case.bytes },
                    { "hexDump", binary_body.hex_dump(to_bytes(case.bytes)) },
                    { "name", case.name },
                })
            end
            assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
        end)
    end)
end)
