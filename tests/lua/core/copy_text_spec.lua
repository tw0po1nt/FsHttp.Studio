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

describe("fshttp.copy_text", function()
    local copy_text = core_env.load("fshttp.copy_text")
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/copy/copy-text.json")
    local golden_fixture = json.decode(bytes)

    local function headers_of(pairs)
        local headers = {}
        for i, pair in ipairs(pairs) do
            headers[i] = { name = pair[1], value = pair[2] }
        end
        return headers
    end

    local function result_of(case)
        return {
            status = case.status,
            reason = case.reason,
            headers = headers_of(case.headers),
            body = to_bytes(case.body),
            request = {
                method = case.request.method,
                url = case.request.url,
                headers = headers_of(case.request.headers),
                body = {
                    state = case.request.bodyState,
                    bytes = to_bytes(case.request.bodyBytes),
                    reason = case.request.bodyReason,
                },
            },
        }
    end

    local function nullable(text)
        if text == nil then
            return json.null
        end
        return text
    end

    it("gives each case the Copy text of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.cases) do
            local result = result_of(case)
            assert.equal(case.requestText, copy_text.request(result), case.name .. ": request")
            assert.equal(case.responseHeadersText, copy_text.headers(result), case.name .. ": headers")
            assert.equal(case.responseBodyText, nullable(copy_text.body(result)), case.name .. ": body")
        end
    end)

    it("writes the Copy text Golden fixture byte for byte", function()
        local cases = {}
        for i, case in ipairs(golden_fixture.cases) do
            local result = result_of(case)
            cases[i] = json.object({
                { "body", case.body },
                { "headers", case.headers },
                { "name", case.name },
                { "reason", case.reason },
                { "request", case.request },
                { "requestText", copy_text.request(result) },
                { "responseBodyText", nullable(copy_text.body(result)) },
                { "responseHeadersText", copy_text.headers(result) },
                { "status", case.status },
            })
        end
        assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
    end)
end)
