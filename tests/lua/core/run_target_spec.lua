local core_env = require("lua.core_env")

describe("fshttp.run_target", function()
    local run_target = core_env.load("fshttp.run_target")
    local refusals = core_env.load("fshttp.refusals")
    local envelope = core_env.load("fshttp.envelope")

    local function range(start_line, end_line, refusal)
        return { start_line = start_line, start_col = 0, end_line = end_line, end_col = 1, refusal = refusal }
    end

    local function blocks(ranges, parse_failed)
        return { tag = "blocks", parse_failed = parse_failed or false, ranges = ranges }
    end

    it("gives a WARN notice for a Parse failure with no Block", function()
        assert.same(
            { kind = "notice", level = "WARN", message = "No requests found: this script has a syntax error." },
            run_target.for_cursor(blocks({}, true), 1)
        )
    end)

    it("gives an INFO notice for a script with no Block and no Parse failure", function()
        assert.same({
            kind = "notice",
            level = "INFO",
            message = "This script has no request. Write an http { } block to run one.",
        }, run_target.for_cursor(blocks({}), 1))
    end)

    it("gives a Run of the Block that contains the cursor", function()
        local target = run_target.for_cursor(blocks({ range(3, 5), range(8, 10) }), 9)
        assert.same({ kind = "run", block_index = 1 }, target)
    end)

    it("follows the cursor rule in a script with a Parse failure and a Block", function()
        local target = run_target.for_cursor(blocks({ range(3, 5) }, true), 4)
        assert.same({ kind = "run", block_index = 0 }, target)
    end)

    it("gives a refused Block a WARN notice with the detail of its Refusal code", function()
        local target = run_target.for_cursor(blocks({ range(9, 10, "loopBody") }), 10)
        assert.same({ kind = "notice", level = "WARN", message = refusals.codes.loopBody.detail }, target)
    end)

    it("gives an unknown Refusal code the detail of the fallback code", function()
        local target = run_target.for_cursor(blocks({ range(9, 10, "aCodeFromLater") }), 9)
        assert.equal(refusals.codes[refusals.fallback_code].detail, target.message)
    end)

    it("gives a cursor outside every Block the outside target", function()
        assert.same({ kind = "outside" }, run_target.for_cursor(blocks({ range(3, 5) }), 7))
    end)

    it("bounds a Run by 30000 ms by default", function()
        assert.equal(30000, run_target.request_timeout_ms(nil))
    end)

    it("keeps 0, which sets no bound", function()
        assert.equal(0, run_target.request_timeout_ms(0))
    end)

    it("keeps a number at or above 0, with no fraction", function()
        assert.equal(1500, run_target.request_timeout_ms(1500))
        assert.equal(1500, run_target.request_timeout_ms(1500.7))
    end)

    it("gives the default for a value that is not a finite number at or above 0", function()
        for _, value in ipairs({ -1, "1000", true, math.huge, 0 / 0 }) do
            assert.equal(30000, run_target.request_timeout_ms(value), tostring(value))
        end
    end)

    it("writes the run envelope with the source text, the Block index, the Script path, and the bound", function()
        local run = run_target.run_envelope("http { GET url }\n", 1, "/scripts/a.fsx", nil)
        assert.equal(
            '{"blockIndex":1,"scriptFileName":"/scripts/a.fsx","source":"http { GET url }\\n","tag":"run","timeoutMs":30000}',
            envelope.encode(run)
        )
    end)

    it("leaves out the Script path of a buffer with no name", function()
        local run = run_target.run_envelope("x\n", 0, "", 0)
        assert.equal('{"blockIndex":0,"source":"x\\n","tag":"run","timeoutMs":0}', envelope.encode(run))
    end)
end)
