local core_env = require("lua.core_env")

describe("fshttp.block_mark", function()
    local block_mark = core_env.load("fshttp.block_mark")
    local refusals = core_env.load("fshttp.refusals")

    local function range(start_line, refusal)
        return { start_line = start_line, start_col = 0, end_line = start_line + 2, end_col = 1, refusal = refusal }
    end

    it("gives a Block that a Run can reach the run title on its first line", function()
        local marks = block_mark.for_blocks({ parse_failed = false, ranges = { range(10) } })
        assert.same({ { line = 10, title = "▶ Run request", on_block = true } }, marks)
    end)

    it("gives a refused Block the Block mark title of its Refusal code", function()
        local marks = block_mark.for_blocks({ parse_failed = false, ranges = { range(9, "loopBody") } })
        assert.same({ { line = 9, title = "⊘ Cannot run: inside a loop", on_block = true } }, marks)
    end)

    it("gives an unknown Refusal code the title of the fallback code", function()
        local marks = block_mark.for_blocks({ parse_failed = false, ranges = { range(4, "aCodeFromLater") } })
        assert.equal(refusals.codes[refusals.fallback_code].block_mark_title, marks[1].title)
    end)

    it("keeps the source order of the Blocks", function()
        local marks = block_mark.for_blocks({ parse_failed = false, ranges = { range(3), range(8, "ifBranch") } })
        assert.same({ 3, 8 }, { marks[1].line, marks[2].line })
    end)

    it("gives a Parse failure with no Block one mark on line 1", function()
        local marks = block_mark.for_blocks({ parse_failed = true, ranges = {} })
        assert.same({
            { line = 1, title = "⊘ No requests found: this script has a syntax error", on_block = false },
        }, marks)
    end)

    it("gives a Parse failure with a Block only the Block mark", function()
        local marks = block_mark.for_blocks({ parse_failed = true, ranges = { range(12) } })
        assert.same({ { line = 12, title = "▶ Run request", on_block = true } }, marks)
    end)

    it("gives a script with no Block and no Parse failure no mark", function()
        assert.same({}, block_mark.for_blocks({ parse_failed = false, ranges = {} }))
    end)

    it("gives a stopped companion the stopped title", function()
        assert.equal("⊘ Cannot run: the companion stopped", block_mark.stopped_title)
    end)

    it("takes the glyph of a title from the text before its first space", function()
        assert.equal("▶", block_mark.glyph("▶ Run request"))
        assert.equal("⊘", block_mark.glyph(block_mark.stopped_title))
        for code, refusal in pairs(refusals.codes) do
            assert.equal("⊘", block_mark.glyph(refusal.block_mark_title), code)
        end
    end)
end)
