local core_env = require("lua.core_env")

describe("fshttp.block_mark", function()
    local block_mark = core_env.load("fshttp.block_mark")
    local refusals = core_env.load("fshttp.refusals")

    local function range(start_line, refusal)
        return { start_line = start_line, start_col = 0, end_line = start_line + 2, end_col = 1, refusal = refusal }
    end

    it("gives a Block that a Run can reach the run title on its first line", function()
        local marks = block_mark.for_blocks({ parse_failed = false, ranges = { range(10) } })
        assert.same({ { line = 10, title = "▶ Run request", on_block = true, runnable = true } }, marks)
    end)

    it("gives a refused Block the Block mark title of its Refusal code", function()
        local marks = block_mark.for_blocks({ parse_failed = false, ranges = { range(9, "loopBody") } })
        assert.same({ { line = 9, title = "⊘ Cannot run: inside a loop", on_block = true, runnable = false } }, marks)
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
            {
                line = 1,
                title = "⊘ No requests found: this script has a syntax error",
                on_block = false,
                runnable = false,
            },
        }, marks)
    end)

    it("gives a Parse failure with a Block only the Block mark", function()
        local marks = block_mark.for_blocks({ parse_failed = true, ranges = { range(12) } })
        assert.same({ { line = 12, title = "▶ Run request", on_block = true, runnable = true } }, marks)
    end)

    it("gives a script with no Block and no Parse failure no mark", function()
        assert.same({}, block_mark.for_blocks({ parse_failed = false, ranges = {} }))
    end)

    it("gives a stopped companion the stopped title", function()
        assert.equal("⊘ Cannot run: the companion stopped", refusals.companion_stopped_block_mark_title)
    end)

    it("takes the glyph of a title from the text before its first space", function()
        assert.equal("▶", block_mark.glyph("▶ Run request"))
        assert.equal("⊘", block_mark.glyph(refusals.companion_stopped_block_mark_title))
        for code, refusal in pairs(refusals.codes) do
            assert.equal("⊘", block_mark.glyph(refusal.block_mark_title), code)
        end
    end)

    it("gives each remembered Block the starting title while the companion starts", function()
        local shown = { { id = 1, row = 7, on_block = true }, { id = 2, row = 9, on_block = true } }
        assert.same({
            { id = 1, row = 7, title = "⊘ Cannot run: the companion is starting" },
            { id = 2, row = 9, title = "⊘ Cannot run: the companion is starting" },
        }, block_mark.not_ready_changes(shown, "starting"))
    end)

    it("paints no Block mark for a first start, which remembers no Block", function()
        assert.same({}, block_mark.not_ready_changes({}, "starting"))
    end)

    it("removes a mark on no Block while the companion starts", function()
        local shown = { { id = 3, row = 0, on_block = false } }
        assert.same({ { id = 3, row = 0, title = nil } }, block_mark.not_ready_changes(shown, "starting"))
    end)

    it("gives each remembered Block the stopped title in the stopped state and in each failed start state", function()
        local shown = { { id = 1, row = 7, on_block = true } }
        for _, state in ipairs({ "stopped", "sdkNotFound", "downloadFailed", "noRelease", "companionNotFound" }) do
            assert.same(
                { { id = 1, row = 7, title = refusals.companion_stopped_block_mark_title } },
                block_mark.not_ready_changes(shown, state),
                state
            )
        end
    end)

    it("keeps each title while the Companion archive downloads", function()
        assert.is_nil(block_mark.not_ready_changes({ { id = 1, row = 7, on_block = true } }, "downloading"))
    end)
end)
