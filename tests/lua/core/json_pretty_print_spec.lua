local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

describe("the JSON pretty-printer of fshttp.json", function()
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/json/pretty-print.json")
    local golden_fixture = json.decode(bytes)

    local function pretty_of(case)
        local pretty = json.pretty_print(case.body)
        if pretty == nil then
            return json.null
        end
        return pretty
    end

    it("gives each case the output of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.cases) do
            assert.equal(case.pretty, pretty_of(case), case.name)
        end
    end)

    it("writes the JSON pretty-printer Golden fixture byte for byte", function()
        local cases = {}
        for i, case in ipairs(golden_fixture.cases) do
            cases[i] = json.object({
                { "body", case.body },
                { "name", case.name },
                { "pretty", pretty_of(case) },
            })
        end
        assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
    end)

    it("gives the lines of each object and each array that spans more than one line", function()
        local _, folds = json.pretty_print('{"name":"snorlax","moves":["rest","snore"],"stats":{"hp":160},"tags":[]}')
        assert.same({ { first = 3, last = 6 }, { first = 7, last = 9 }, { first = 1, last = 11 } }, folds)
    end)

    it("counts a line break inside a string in the lines of a fold", function()
        local pretty, folds = json.pretty_print('[["a\nb"]]')
        assert.equal('[\n  [\n    "a\nb"\n  ]\n]', pretty)
        assert.same({ { first = 2, last = 5 }, { first = 1, last = 6 } }, folds)
    end)

    it("gives no folds for a value that is not an object or an array", function()
        assert.same({}, select(2, json.pretty_print("42")))
    end)
end)
