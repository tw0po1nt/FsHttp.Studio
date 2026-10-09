local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

describe("fshttp.response_view Compile error text", function()
    local response_view = core_env.load("fshttp.response_view")
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/compile-error/compile-error-text.json")
    local golden_fixture = json.decode(bytes)

    local function present(value)
        if value == json.null then
            return nil
        end
        return value
    end

    local function text_of(case)
        local diagnostics = {}
        for i, d in ipairs(case.diagnostics) do
            diagnostics[i] = {
                message = d.message,
                range = { start_line = d.startLine, start_col = d.startCol },
                loaded_file = present(d.loadedFile),
            }
        end
        local view = response_view.compile_error(diagnostics, present(case.scriptFileName))
        return table.concat(view.lines, "\n")
    end

    it("gives each case the text of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.cases) do
            assert.equal(case.text, text_of(case), tostring(present(case.scriptFileName)))
        end
    end)

    it("writes the Compile error text Golden fixture byte for byte", function()
        local cases = {}
        for i, case in ipairs(golden_fixture.cases) do
            local diagnostics = {}
            for j, d in ipairs(case.diagnostics) do
                diagnostics[j] = json.object({
                    { "loadedFile", d.loadedFile },
                    { "message", d.message },
                    { "startCol", d.startCol },
                    { "startLine", d.startLine },
                })
            end
            cases[i] = json.object({
                { "diagnostics", json.array(diagnostics) },
                { "scriptFileName", case.scriptFileName },
                { "text", text_of(case) },
            })
        end
        assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
    end)
end)
