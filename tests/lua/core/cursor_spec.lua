local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

describe("fshttp.cursor", function()
    local cursor = core_env.load("fshttp.cursor")
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/cursor/cursor-rule.json")
    local golden_fixture = json.decode(bytes)

    local ranges = {}
    for i, range in ipairs(golden_fixture.ranges) do
        ranges[i] = {
            start_line = range.startLine,
            start_col = range.startCol,
            end_line = range.endLine,
            end_col = range.endCol,
        }
    end

    local function block_index_of(case)
        local block_index = cursor.block_index(ranges, case.cursorLine)
        if block_index == nil then
            return json.null
        end
        return block_index
    end

    it("gives each cursor case the Block index of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.cases) do
            assert.equal(case.blockIndex, block_index_of(case), case.name)
        end
    end)

    it("writes the cursor rule Golden fixture byte for byte", function()
        local cases = {}
        for i, case in ipairs(golden_fixture.cases) do
            cases[i] = json.object({
                { "blockIndex", block_index_of(case) },
                { "cursorLine", case.cursorLine },
                { "name", case.name },
            })
        end
        local written = json.object({
            { "cases", json.array(cases) },
            { "ranges", golden_fixture.ranges },
            { "source", golden_fixture.source },
        })
        assert.equal(bytes, json.encode(written))
    end)

    it("gives nil for a script with no Block", function()
        assert.is_nil(cursor.block_index({}, 1))
    end)
end)
