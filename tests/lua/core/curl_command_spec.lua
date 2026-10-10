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

local function result_for(method, headers, body)
    return {
        status = 200,
        reason = "OK",
        headers = {},
        body = "",
        request = { method = method, url = "http://api.example.com/notes", headers = headers, body = body },
    }
end

local function captured(bytes)
    return { state = "captured", bytes = bytes, reason = "" }
end

local function notes_pipe_command(bytes)
    return "printf '%s' '"
        .. vim.base64.encode(bytes)
        .. "' \\\n"
        .. "  | base64 -d \\\n"
        .. "  | curl 'http://api.example.com/notes' \\\n"
        .. "    -H 'Content-Type: text/plain' \\\n"
        .. "    -H 'User-Agent:' \\\n"
        .. "    -H 'Accept:' \\\n"
        .. "    --data-binary @-"
end

local text_plain = { { name = "Content-Type", value = "text/plain" } }

describe("fshttp.curl_command", function()
    local curl_command = core_env.load("fshttp.curl_command")
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/curl/curl-command.json")
    local golden_fixture = json.decode(bytes)

    local function result_of(case)
        local headers = {}
        for i, pair in ipairs(case.request.headers) do
            headers[i] = { name = pair[1], value = pair[2] }
        end
        local result = result_for(case.request.method, headers, {
            state = case.request.bodyState,
            bytes = to_bytes(case.request.bodyBytes),
            reason = case.request.bodyReason,
        })
        result.request.url = case.request.url
        return result
    end

    local function nullable(text)
        if text == nil then
            return json.null
        end
        return text
    end

    it("gives each case the Curl command of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.cases) do
            assert.equal(case.curl, nullable((curl_command.build(result_of(case)))), case.name)
        end
    end)

    it("writes the Curl command Golden fixture byte for byte", function()
        local cases = {}
        for i, case in ipairs(golden_fixture.cases) do
            cases[i] = json.object({
                { "curl", nullable((curl_command.build(result_of(case)))) },
                { "name", case.name },
                { "request", case.request },
            })
        end
        assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
    end)

    it("gives a reason for a body that the companion did not read", function()
        local body = { state = "notCaptured", bytes = "", reason = "streamed body: not captured" }
        local command, reason = curl_command.build(result_for("POST", {}, body))
        assert.is_nil(command)
        assert.equal(
            "The companion did not read the body of the Request, so a Curl command would send a different request.",
            reason
        )
    end)

    it("gives a reason for a HEAD with a body", function()
        local command, reason = curl_command.build(result_for("HEAD", {}, captured("hello")))
        assert.is_nil(command)
        assert.equal("curl cannot send a HEAD request with a body.", reason)
    end)

    it("gives no reason when there is a Curl command", function()
        local _, reason = curl_command.build(result_for("GET", {}, { state = "none", bytes = "", reason = "" }))
        assert.is_nil(reason)
    end)

    it("gives the base64 pipe for each Captured body that is not safe to paste, or above 16,384 bytes", function()
        local unsafe_bodies = {
            { "a CR", "a\r\nb" },
            { "a NUL", "a\0b" },
            { "a control byte", "a\27b" },
            { "a DEL byte", "a\127b" },
            { "the first C1 control character", "a\194\128b" },
            { "the last C1 control character", "a\194\159b" },
            { "invalid UTF-8", "n\228i" },
            { "an overlong form", "\192\175" },
            { "a surrogate", "\237\160\128" },
            { "a sequence that the body ends inside", "a\240\159\152" },
            { "16,385 bytes", string.rep("abcdefghijklmno\n", 1024) .. "a" },
        }
        for _, case in ipairs(unsafe_bodies) do
            local command = curl_command.build(result_for("POST", text_plain, captured(case[2])))
            assert.equal(notes_pipe_command(case[2]), command, case[1])
        end
    end)

    it("puts a body with characters outside ASCII, U+00A0, a tab, and an LF inline", function()
        local body = "café\194\160→ 日本 😀\tsecond line\n"
        assert.equal(
            "curl 'http://api.example.com/notes' \\\n"
                .. "  -H 'Content-Type: text/plain' \\\n"
                .. "  -H 'User-Agent:' \\\n"
                .. "  -H 'Accept:' \\\n"
                .. "  --data-raw '"
                .. body
                .. "'",
            (curl_command.build(result_for("POST", text_plain, captured(body))))
        )
    end)

    it("puts a method that is not a plain token in single quotes", function()
        local command = curl_command.build(result_for("BAD METHOD", {}, { state = "none", bytes = "", reason = "" }))
        assert.equal(true, vim.startswith(command, "curl -X 'BAD METHOD' 'http://api.example.com/notes' \\\n"), command)
    end)
end)
