local core_env = require("lua.core_env")

describe("fshttp.picker", function()
    local picker = core_env.load("fshttp.picker")

    local function range(start_line, refusal)
        return { start_line = start_line, start_col = 0, end_line = start_line, end_col = 1, refusal = refusal }
    end

    it("takes the first line of each Block from the located text", function()
        local source = 'let a = 1\n  http { GET "/one" }  \n\nhttp { GET "/two" }\n'
        local blocks = { parse_failed = false, ranges = { range(2), range(4, "loopBody") } }
        assert.same({ 'http { GET "/one" }', 'http { GET "/two" }' }, picker.first_lines(blocks, source))
    end)

    it("gives each item the glyph, the line number, and the first line, in source order", function()
        local source = 'http { GET "/one" }\nhttp { GET "/two" }\n'
        local blocks = { parse_failed = false, ranges = { range(1), range(2, "loopBody") } }
        assert.same(
            { '▶ 1: http { GET "/one" }', '⊘ 2: http { GET "/two" }' },
            picker.items(blocks, picker.first_lines(blocks, source))
        )
    end)
end)
