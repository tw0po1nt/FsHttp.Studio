local core_env = require("lua.core_env")

local folder = "tests/golden/envelope/"

local function read(name)
    local file = assert(io.open(folder .. name, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

local function golden_fixture_names()
    local names = {}
    for name, kind in vim.fs.dir(folder) do
        if kind == "file" and name:match("%.json$") then
            names[#names + 1] = name
        end
    end
    table.sort(names)
    return names
end

local escapes = "\"quoted\" \\ <tag> & 'single' + `tick` \t\n\r\b\f\1\127 é → 😀"

describe("fshttp.envelope", function()
    local envelope = core_env.load("fshttp.envelope")

    it("decodes and encodes each envelope Golden fixture byte for byte", function()
        for _, name in ipairs(golden_fixture_names()) do
            local bytes = read(name)
            local decoded, err = envelope.decode(bytes)
            assert.equal(nil, err, name)
            assert.equal(bytes, envelope.encode(decoded), name)
        end
    end)

    it("encodes the three Client envelopes as the Golden fixtures contain them", function()
        local source = envelope.decode(read("locate.json")).source
        assert.equal(read("hello.json"), envelope.encode({ tag = "hello" }))
        assert.equal(read("locate.json"), envelope.encode({ tag = "locate", source = source }))
        assert.equal(
            read("run.json"),
            envelope.encode({
                tag = "run",
                source = source,
                block_index = 1,
                script_file_name = "/scripts/golden.fsx",
                timeout_ms = 30000,
            })
        )
    end)

    it("reads the companion version from ready", function()
        assert.equal("1.2.3-beta.4", envelope.decode(read("ready.json")).version)
    end)

    it("reads a ready with no version as a ready with a nil version", function()
        local ready = envelope.decode('{"tag":"ready"}')
        assert.equal("ready", ready.tag)
        assert.is_nil(ready.version)
    end)

    it("reads the Block ranges and the refusal code from blocks", function()
        local blocks = envelope.decode(read("blocks.json"))
        assert.is_false(blocks.parse_failed)
        assert.same({ start_line = 4, start_col = 0, end_line = 6, end_col = 1 }, blocks.ranges[1])
        assert.equal("loopBody", blocks.ranges[2].refusal)
    end)

    it("reads each escape and each UTF-8 width back to the same text", function()
        assert.equal(escapes, envelope.decode(read("runtime-error.json")).message)
        assert.equal(escapes, envelope.decode(read("compile-error.json")).diagnostics[2].message)
    end)

    it("reads the Loaded file of a diagnostic, and no Loaded file for a Script diagnostic", function()
        local loaded = envelope.decode(read("compile-error-loaded-file.json")).diagnostics
        assert.equal("/scripts/lib/helpers.fsx", loaded[1].loaded_file)
        assert.equal("/shared/inner.fsx", loaded[2].loaded_file)
        assert.is_nil(envelope.decode(read("compile-error.json")).diagnostics[1].loaded_file)
    end)

    it("keeps the header order of an ok envelope", function()
        local ok = envelope.decode(read("ok.json"))
        assert.same({
            { name = "Content-Type", value = "application/json; charset=utf-8" },
            { name = "X-Escapes", value = escapes },
        }, ok.headers)
        assert.same({
            { name = "Content-Type", value = "application/json; charset=utf-8" },
            { name = "Accept", value = "application/json" },
        }, ok.request.headers)
        assert.equal(201, ok.status)
        assert.equal(12.5, ok.request_ms)
        assert.equal("captured", ok.request.body.state)
    end)

    it("reads the three Captured body states", function()
        assert.equal("captured", envelope.decode(read("ok.json")).request.body.state)
        assert.equal("none", envelope.decode(read("ok-http-error-response.json")).request.body.state)
        local not_captured = envelope.decode(read("ok-not-captured.json")).request.body
        assert.equal("notCaptured", not_captured.state)
        assert.is_true(#not_captured.reason > 0)
    end)

    it("reads the binding name of a refusal only when the companion sends one", function()
        assert.is_nil(envelope.decode(read("refused.json")).name)
        assert.equal("dexId", envelope.decode(read("refused-unbound-block-value.json")).name)
    end)

    it("returns nil and a reason for an unknown tag or a payload that is not JSON", function()
        local decoded, err = envelope.decode('{"tag":"notATag"}')
        assert.is_nil(decoded)
        assert.matches("unknown tag 'notATag'", err, 1, true)
        decoded, err = envelope.decode("{")
        assert.is_nil(decoded)
        assert.is_string(err)
    end)
end)

describe("fshttp.frame", function()
    local frame = core_env.load("fshttp.frame")
    local stream = read("frames.bin")
    local payloads = frame.parser():push(stream)

    it("finds the frame of each envelope Golden fixture in frames.bin", function()
        local golden_fixtures = {}
        for i, name in ipairs(golden_fixture_names()) do
            golden_fixtures[i] = read(name)
        end
        local framed = vim.deepcopy(payloads)
        table.sort(golden_fixtures)
        table.sort(framed)
        assert.same(golden_fixtures, framed)
    end)

    it("encodes each envelope Golden fixture to the frames that the companion writes", function()
        local frames = {}
        for i, payload in ipairs(payloads) do
            frames[i] = frame.encode(payload)
        end
        assert.equal(stream, table.concat(frames))
    end)

    it("parses the frames whatever the chunk size", function()
        for _, size in ipairs({ 1, 2, 3, 4, 5, 7, 64, #stream }) do
            local parser = frame.parser()
            local parsed = {}
            for i = 1, #stream, size do
                for _, payload in ipairs(parser:push(stream:sub(i, i + size - 1))) do
                    parsed[#parsed + 1] = payload
                end
            end
            assert.same(payloads, parsed, "chunk size " .. size)
        end
    end)

    it("keeps a partial frame until a later chunk completes it", function()
        local parser = frame.parser()
        local framed = frame.encode("{}")
        assert.same({}, parser:push(framed:sub(1, 5)))
        assert.same({ "{}" }, parser:push(framed:sub(6)))
    end)

    it("parses an empty payload", function()
        assert.same({ "" }, frame.parser():push(frame.encode("")))
    end)
end)
