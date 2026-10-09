-- Each encoder must write the fields in the order that the companion writes them: alphabetical by wire name.
local json = require("fshttp.json")

local M = {}

local function fail(message)
    error("envelope: " .. message, 0)
end

local checks = {
    object = json.is_object,
    array = json.is_array,
}

local function field(object, key, value_type)
    local value = object[key]
    local check = checks[value_type]
    local ok
    if check then
        ok = check(value)
    else
        ok = type(value) == value_type
    end
    if not ok then
        fail(string.format("'%s' is not a %s", key, value_type))
    end
    return value
end

local function optional(object, key, value_type)
    local value = object[key]
    if value == nil or value == json.null then
        return nil
    end
    return field(object, key, value_type)
end

local function decode_headers(object)
    local headers = {}
    for _, name in ipairs(json.keys(object)) do
        headers[#headers + 1] = { name = name, value = field(object, name, "string") }
    end
    return headers
end

local function encode_headers(headers)
    local fields = {}
    for i, header in ipairs(headers) do
        fields[i] = { header.name, header.value }
    end
    return json.object(fields)
end

local function decode_range(object)
    return {
        start_line = field(object, "startLine", "number"),
        start_col = field(object, "startCol", "number"),
        end_line = field(object, "endLine", "number"),
        end_col = field(object, "endCol", "number"),
        refusal = optional(object, "refusal", "string"),
    }
end

local function encode_range(range)
    return json.object({
        { "endCol", range.end_col },
        { "endLine", range.end_line },
        { "refusal", range.refusal },
        { "startCol", range.start_col },
        { "startLine", range.start_line },
    })
end

local function map(list, fn)
    local result = {}
    for i, item in ipairs(list) do
        result[i] = fn(item)
    end
    return result
end

local function decode_request(object)
    return {
        method = field(object, "method", "string"),
        url = field(object, "url", "string"),
        headers = decode_headers(field(object, "headers", "object")),
        body = {
            state = field(object, "bodyState", "string"),
            base64 = field(object, "bodyBase64", "string"),
            reason = field(object, "bodyReason", "string"),
        },
    }
end

local function encode_request(request)
    return json.object({
        { "bodyBase64", request.body.base64 },
        { "bodyReason", request.body.reason },
        { "bodyState", request.body.state },
        { "headers", encode_headers(request.headers) },
        { "method", request.method },
        { "url", request.url },
    })
end

local codecs = {}

codecs.hello = {
    decode = function()
        return {}
    end,
    encode = function()
        return { { "tag", "hello" } }
    end,
}

-- A companion that is older than the version check sends no `version`.
codecs.ready = {
    decode = function(object)
        return { version = optional(object, "version", "string") }
    end,
    encode = function(envelope)
        return { { "tag", "ready" }, { "version", envelope.version } }
    end,
}

codecs.locate = {
    decode = function(object)
        return { source = field(object, "source", "string") }
    end,
    encode = function(envelope)
        return { { "source", envelope.source }, { "tag", "locate" } }
    end,
}

codecs.blocks = {
    decode = function(object)
        return {
            parse_failed = optional(object, "parseFailed", "boolean") or false,
            ranges = map(field(object, "ranges", "array"), decode_range),
        }
    end,
    encode = function(envelope)
        return {
            { "parseFailed", envelope.parse_failed },
            { "ranges", json.array(map(envelope.ranges, encode_range)) },
            { "tag", "blocks" },
        }
    end,
}

codecs.run = {
    decode = function(object)
        return {
            source = field(object, "source", "string"),
            block_index = field(object, "blockIndex", "number"),
            script_file_name = optional(object, "scriptFileName", "string"),
            timeout_ms = optional(object, "timeoutMs", "number"),
        }
    end,
    encode = function(envelope)
        return {
            { "blockIndex", envelope.block_index },
            { "scriptFileName", envelope.script_file_name },
            { "source", envelope.source },
            { "tag", "run" },
            { "timeoutMs", envelope.timeout_ms },
        }
    end,
}

codecs.ok = {
    decode = function(object)
        return {
            status = field(object, "status", "number"),
            reason = field(object, "reason", "string"),
            headers = decode_headers(field(object, "headers", "object")),
            content_type = field(object, "contentType", "string"),
            body_base64 = field(object, "bodyBase64", "string"),
            request_ms = optional(object, "requestMs", "number") or 0,
            request = decode_request(field(object, "request", "object")),
        }
    end,
    encode = function(envelope)
        return {
            { "bodyBase64", envelope.body_base64 },
            { "contentType", envelope.content_type },
            { "headers", encode_headers(envelope.headers) },
            { "reason", envelope.reason },
            { "request", encode_request(envelope.request) },
            { "requestMs", envelope.request_ms },
            { "status", envelope.status },
            { "tag", "ok" },
        }
    end,
}

codecs.compileError = {
    decode = function(object)
        return {
            diagnostics = map(field(object, "diagnostics", "array"), function(diagnostic)
                if not json.is_object(diagnostic) then
                    fail("a diagnostic is not an object")
                end
                return {
                    message = field(diagnostic, "message", "string"),
                    range = decode_range(field(diagnostic, "range", "object")),
                    loaded_file = optional(diagnostic, "loadedFile", "string"),
                }
            end),
        }
    end,
    encode = function(envelope)
        local diagnostics = map(envelope.diagnostics, function(diagnostic)
            return json.object({
                { "loadedFile", diagnostic.loaded_file },
                { "message", diagnostic.message },
                { "range", encode_range(diagnostic.range) },
            })
        end)
        return { { "diagnostics", json.array(diagnostics) }, { "tag", "compileError" } }
    end,
}

codecs.runtimeError = {
    decode = function(object)
        return { message = field(object, "message", "string") }
    end,
    encode = function(envelope)
        return { { "message", envelope.message }, { "tag", "runtimeError" } }
    end,
}

-- `name` names the blanked binding of an `unboundBlockValue` refusal. Each other code has none.
codecs.refused = {
    decode = function(object)
        return {
            code = field(object, "code", "string"),
            name = optional(object, "name", "string"),
        }
    end,
    encode = function(envelope)
        return { { "code", envelope.code }, { "name", envelope.name }, { "tag", "refused" } }
    end,
}

codecs.error = {
    decode = function(object)
        return { message = field(object, "message", "string") }
    end,
    encode = function(envelope)
        return { { "message", envelope.message }, { "tag", "error" } }
    end,
}

function M.decode(payload)
    local ok, result = pcall(function()
        local object = json.decode(payload)
        if not json.is_object(object) then
            fail("the payload is not a JSON object")
        end
        local tag = field(object, "tag", "string")
        local codec = codecs[tag]
        if not codec then
            fail(string.format("unknown tag '%s'", tag))
        end
        local envelope = codec.decode(object)
        envelope.tag = tag
        return envelope
    end)
    if ok then
        return result
    end
    return nil, result
end

function M.encode(envelope)
    local codec = codecs[envelope.tag]
    if not codec then
        fail(string.format("unknown tag '%s'", tostring(envelope.tag)))
    end
    return json.encode(json.object(codec.encode(envelope)))
end

return M
