-- JSON that keeps the key order of each object. The encoder writes the same bytes as the default
-- System.Text.Json encoder of the companion.
local M = {}

local array_meta = { kind = "array" }

M.null = setmetatable({}, { kind = "null" })

-- Makes an object from a list of { key, value } pairs, in order. The object omits a pair with a
-- nil value.
function M.object(fields)
    local object = {}
    local keys = {}
    for _, field in ipairs(fields) do
        local key, value = field[1], field[2]
        if value ~= nil then
            keys[#keys + 1] = key
            object[key] = value
        end
    end
    return setmetatable(object, { kind = "object", keys = keys })
end

function M.array(items)
    return setmetatable(items, array_meta)
end

local function kind(value)
    local mt = getmetatable(value)
    return mt and mt.kind
end

function M.is_object(value)
    return type(value) == "table" and kind(value) == "object"
end

function M.is_array(value)
    return type(value) == "table" and kind(value) == "array"
end

-- Returns the keys of an object in the order that the text or the pair list gave them.
function M.keys(object)
    return getmetatable(object).keys
end

local function decode_error(pos, message)
    error(string.format("json: %s at byte %d", message, pos), 0)
end

local function skip_space(text, pos)
    return (text:find("[^ \t\r\n]", pos)) or (#text + 1)
end

local function utf8_char(code)
    if code < 0x80 then
        return string.char(code)
    elseif code < 0x800 then
        return string.char(0xC0 + math.floor(code / 0x40), 0x80 + code % 0x40)
    elseif code < 0x10000 then
        return string.char(0xE0 + math.floor(code / 0x1000), 0x80 + math.floor(code / 0x40) % 0x40, 0x80 + code % 0x40)
    end
    return string.char(
        0xF0 + math.floor(code / 0x40000),
        0x80 + math.floor(code / 0x1000) % 0x40,
        0x80 + math.floor(code / 0x40) % 0x40,
        0x80 + code % 0x40
    )
end

local short_escapes = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }

local decode_value

local function decode_string(text, pos)
    local parts = {}
    local i = pos + 1
    while true do
        local stop = text:find('["\\]', i)
        if not stop then
            decode_error(pos, "unterminated string")
        end
        parts[#parts + 1] = text:sub(i, stop - 1)
        if text:sub(stop, stop) == '"' then
            return table.concat(parts), stop + 1
        end
        local escape = text:sub(stop + 1, stop + 1)
        if escape == "u" then
            local code = tonumber(text:sub(stop + 2, stop + 5), 16)
            if not code then
                decode_error(stop, "bad \\u escape")
            end
            i = stop + 6
            if code >= 0xD800 and code <= 0xDBFF and text:sub(i, i + 1) == "\\u" then
                local low = tonumber(text:sub(i + 2, i + 5), 16)
                if low and low >= 0xDC00 and low <= 0xDFFF then
                    code = 0x10000 + (code - 0xD800) * 0x400 + (low - 0xDC00)
                    i = i + 6
                end
            end
            parts[#parts + 1] = utf8_char(code)
        else
            local char = short_escapes[escape]
            if not char then
                decode_error(stop, "bad escape")
            end
            parts[#parts + 1] = char
            i = stop + 2
        end
    end
end

local function decode_number(text, pos)
    local stop = select(2, text:find("^-?%d+", pos))
    if not stop then
        decode_error(pos, "unexpected character")
    end
    stop = select(2, text:find("^%.%d+", stop + 1)) or stop
    stop = select(2, text:find("^[eE][-+]?%d+", stop + 1)) or stop
    return tonumber(text:sub(pos, stop)), stop + 1
end

local function decode_object(text, pos)
    local fields = {}
    pos = skip_space(text, pos + 1)
    if text:sub(pos, pos) == "}" then
        return M.object(fields), pos + 1
    end
    while true do
        if text:sub(pos, pos) ~= '"' then
            decode_error(pos, "expected a key")
        end
        local key, value
        key, pos = decode_string(text, pos)
        pos = skip_space(text, pos)
        if text:sub(pos, pos) ~= ":" then
            decode_error(pos, "expected ':'")
        end
        value, pos = decode_value(text, skip_space(text, pos + 1))
        fields[#fields + 1] = { key, value }
        pos = skip_space(text, pos)
        local char = text:sub(pos, pos)
        if char == "}" then
            return M.object(fields), pos + 1
        elseif char ~= "," then
            decode_error(pos, "expected ',' or '}'")
        end
        pos = skip_space(text, pos + 1)
    end
end

local function decode_array(text, pos)
    local items = {}
    pos = skip_space(text, pos + 1)
    if text:sub(pos, pos) == "]" then
        return M.array(items), pos + 1
    end
    while true do
        local value
        value, pos = decode_value(text, pos)
        items[#items + 1] = value
        pos = skip_space(text, pos)
        local char = text:sub(pos, pos)
        if char == "]" then
            return M.array(items), pos + 1
        elseif char ~= "," then
            decode_error(pos, "expected ',' or ']'")
        end
        pos = skip_space(text, pos + 1)
    end
end

local literals = { ["true"] = true, ["false"] = false, ["null"] = M.null }

decode_value = function(text, pos)
    local char = text:sub(pos, pos)
    if char == "{" then
        return decode_object(text, pos)
    elseif char == "[" then
        return decode_array(text, pos)
    elseif char == '"' then
        return decode_string(text, pos)
    end
    for word, value in pairs(literals) do
        if text:sub(pos, pos + #word - 1) == word then
            return value, pos + #word
        end
    end
    return decode_number(text, pos)
end

-- Raises an error on text that is not one JSON value.
function M.decode(text)
    local value, pos = decode_value(text, skip_space(text, 1))
    pos = skip_space(text, pos)
    if pos <= #text then
        decode_error(pos, "trailing text")
    end
    return value
end

-- The companion's encoder escapes these ASCII characters, and each character that is not ASCII.
local ascii_escapes = {
    ["\b"] = "\\b",
    ["\t"] = "\\t",
    ["\n"] = "\\n",
    ["\f"] = "\\f",
    ["\r"] = "\\r",
    ["\\"] = "\\\\",
}
for byte = 0, 0x1F do
    local char = string.char(byte)
    ascii_escapes[char] = ascii_escapes[char] or string.format("\\u%04X", byte)
end
for _, char in ipairs({ '"', "&", "'", "+", "<", ">", "`", "\127" }) do
    ascii_escapes[char] = string.format("\\u%04X", char:byte())
end

local function utf16_escape(code)
    if code < 0x10000 then
        return string.format("\\u%04X", code)
    end
    code = code - 0x10000
    return string.format("\\u%04X\\u%04X", 0xD800 + math.floor(code / 0x400), 0xDC00 + code % 0x400)
end

-- Escapes a run of UTF-8 bytes. A byte that does not start a whole sequence becomes U+FFFD.
local function escape_utf8(run)
    local parts = {}
    local i = 1
    while i <= #run do
        local lead = run:byte(i)
        local code, width = 0xFFFD, 1
        if lead >= 0xF0 then
            code, width = lead - 0xF0, 4
        elseif lead >= 0xE0 then
            code, width = lead - 0xE0, 3
        elseif lead >= 0xC0 then
            code, width = lead - 0xC0, 2
        end
        if i + width - 1 > #run then
            code, width = 0xFFFD, #run - i + 1
        else
            for j = i + 1, i + width - 1 do
                code = code * 0x40 + (run:byte(j) - 0x80)
            end
        end
        parts[#parts + 1] = utf16_escape(code)
        i = i + width
    end
    return table.concat(parts)
end

local function encode_string(text)
    local escaped = text:gsub("[%z\1-\31\"&'+<>\\`\127]", ascii_escapes)
    escaped = escaped:gsub("[\128-\255]+", escape_utf8)
    return '"' .. escaped .. '"'
end

local function encode_number(number)
    if number ~= number or number == math.huge or number == -math.huge then
        error("json: cannot encode " .. tostring(number), 0)
    end
    local text
    for _, precision in ipairs({ 15, 16, 17 }) do
        text = string.format("%." .. precision .. "g", number)
        if tonumber(text) == number then
            break
        end
    end
    return (text:gsub("e", "E"))
end

local encode_value

local function encode_object(object)
    local parts = {}
    for _, key in ipairs(M.keys(object)) do
        parts[#parts + 1] = encode_string(key) .. ":" .. encode_value(object[key])
    end
    return "{" .. table.concat(parts, ",") .. "}"
end

local function encode_array(array)
    local parts = {}
    for i, value in ipairs(array) do
        parts[i] = encode_value(value)
    end
    return "[" .. table.concat(parts, ",") .. "]"
end

encode_value = function(value)
    local value_type = type(value)
    if value_type == "string" then
        return encode_string(value)
    elseif value_type == "number" then
        return encode_number(value)
    elseif value_type == "boolean" then
        return tostring(value)
    elseif value == M.null then
        return "null"
    elseif M.is_object(value) then
        return encode_object(value)
    elseif M.is_array(value) then
        return encode_array(value)
    end
    error("json: cannot encode a " .. value_type .. " that is not from json.object or json.array", 0)
end

function M.encode(value)
    return encode_value(value)
end

local not_one_value = {}

local function fail_pretty()
    error(not_one_value, 0)
end

-- Gives the position after the closing quote of the string at `pos`. It accepts only the strings
-- that the renderer's JSON parser accepts, so the pretty-printed text matches the Golden fixture.
local function string_end(text, pos)
    local i = pos + 1
    while true do
        local stop = text:find('["\\]', i)
        if not stop then
            fail_pretty()
        end
        if text:sub(stop, stop) == '"' then
            return stop + 1
        end
        local escape = text:sub(stop + 1, stop + 1)
        if escape == "u" then
            if not text:sub(stop + 2, stop + 5):match("^%x%x%x%x$") then
                fail_pretty()
            end
            i = stop + 6
        elseif short_escapes[escape] then
            i = stop + 2
        else
            fail_pretty()
        end
    end
end

---@class fshttp.PrettyPrint
---@field text string
---@field parts string[]
---@field line integer
---@field folds { first: integer, last: integer }[]

---@param state fshttp.PrettyPrint
---@param piece string
local function emit(state, piece)
    state.parts[#state.parts + 1] = piece
    state.line = state.line + select(2, piece:gsub("\n", ""))
end

---@param state fshttp.PrettyPrint
---@param first integer
---@param stop integer the position after the token
---@return integer
local function emit_token(state, first, stop)
    emit(state, state.text:sub(first, stop - 1))
    return stop
end

local pretty_value

---@param state fshttp.PrettyPrint
---@param pos integer the position of the opening bracket
---@param indent string
---@param closing string
---@param is_object boolean
---@return integer
local function pretty_container(state, pos, indent, closing, is_object)
    local text = state.text
    local opening = text:sub(pos, pos)
    pos = skip_space(text, pos + 1)
    if text:sub(pos, pos) == closing then
        emit(state, opening .. closing)
        return pos + 1
    end
    local first_line = state.line
    local inner = indent .. "  "
    emit(state, opening .. "\n")
    while true do
        emit(state, inner)
        if is_object then
            pos = skip_space(text, pos)
            if text:sub(pos, pos) ~= '"' then
                fail_pretty()
            end
            pos = skip_space(text, emit_token(state, pos, string_end(text, pos)))
            if text:sub(pos, pos) ~= ":" then
                fail_pretty()
            end
            emit(state, ": ")
            pos = pos + 1
        end
        pos = skip_space(text, pretty_value(state, pos, inner))
        local char = text:sub(pos, pos)
        if char == closing then
            emit(state, "\n" .. indent .. closing)
            state.folds[#state.folds + 1] = { first = first_line, last = state.line }
            return pos + 1
        elseif char ~= "," then
            fail_pretty()
        end
        emit(state, ",\n")
        pos = pos + 1
    end
end

---@param state fshttp.PrettyPrint
---@param pos integer
---@param indent string
---@return integer
pretty_value = function(state, pos, indent)
    local text = state.text
    pos = skip_space(text, pos)
    local char = text:sub(pos, pos)
    if char == "{" then
        return pretty_container(state, pos, indent, "}", true)
    elseif char == "[" then
        return pretty_container(state, pos, indent, "]", false)
    elseif char == '"' then
        return emit_token(state, pos, string_end(text, pos))
    end
    local word = ({ t = "true", f = "false", n = "null" })[char]
    if word then
        if text:sub(pos, pos + #word - 1) ~= word then
            fail_pretty()
        end
        return emit_token(state, pos, pos + #word)
    elseif char == "-" or char:match("^%d$") then
        local stop = select(2, text:find("^[%d%-%+%.eE]+", pos))
        return emit_token(state, pos, stop + 1)
    end
    fail_pretty()
    return pos
end

-- Copies each token as the text gives it, so each escape, each number, and the key order stay the
-- same. Only the space between the tokens changes, to a two-space indent. Gives nil when `text` is
-- not one JSON value. The folds give the lines of each object and each array that spans more than
-- one line of the output.
---@param text string
---@return string? pretty
---@return { first: integer, last: integer }[]? folds
function M.pretty_print(text)
    local state = { text = text, parts = {}, line = 1, folds = {} }
    local ok, pos = pcall(pretty_value, state, 1, "")
    if not ok then
        if pos ~= not_one_value then
            error(pos, 0)
        end
        return nil, nil
    end
    if skip_space(text, pos) <= #text then
        return nil, nil
    end
    return table.concat(state.parts), state.folds
end

return M
