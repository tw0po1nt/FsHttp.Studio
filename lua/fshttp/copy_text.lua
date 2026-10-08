-- The Copy text that a yank puts in a register. It must match the Golden fixture of the renderer core.
local binary_body = require("fshttp.binary_body")

local M = {}

local replacement_character = "\239\191\189"

-- The UTF-8 decoder of .NET puts one U+FFFD in place of each maximal invalid subsequence, and the
-- Copy text must keep the same bytes.
---@param bytes string
---@return string
local function decode_text(bytes)
    local parts = {}
    local kept_from = 1
    local i = 1
    while i <= #bytes do
        local lead = bytes:byte(i)
        local needed, lower, upper = -1, 0x80, 0xBF
        if lead < 0x80 then
            needed = 0
        elseif lead >= 0xC2 and lead <= 0xDF then
            needed = 1
        elseif lead >= 0xE0 and lead <= 0xEF then
            needed = 2
            lower = lead == 0xE0 and 0xA0 or 0x80
            upper = lead == 0xED and 0x9F or 0xBF
        elseif lead >= 0xF0 and lead <= 0xF4 then
            needed = 3
            lower = lead == 0xF0 and 0x90 or 0x80
            upper = lead == 0xF4 and 0x8F or 0xBF
        end
        local length = 1
        while length <= needed do
            local byte = bytes:byte(i + length)
            if byte == nil or byte < lower or byte > upper then
                break
            end
            lower, upper = 0x80, 0xBF
            length = length + 1
        end
        if needed >= 0 and length > needed then
            i = i + length
        else
            parts[#parts + 1] = bytes:sub(kept_from, i - 1)
            parts[#parts + 1] = replacement_character
            i = i + length
            kept_from = i
        end
    end
    if kept_from == 1 then
        return bytes
    end
    parts[#parts + 1] = bytes:sub(kept_from)
    return table.concat(parts)
end

---@param bytes string
---@return string
local function body_text(bytes)
    if binary_body.looks_binary(bytes) then
        return binary_body.hex_dump(bytes)
    end
    return decode_text(bytes)
end

---@param first_line string
---@param headers fshttp.Header[]
---@param body string?
---@return string
local function message_text(first_line, headers, body)
    local lines = { first_line }
    for _, header in ipairs(headers) do
        lines[#lines + 1] = header.name .. ": " .. header.value
    end
    local head = table.concat(lines, "\n")
    if body == nil then
        return head
    end
    return head .. "\n\n" .. body
end

---@param result fshttp.RunResult
---@return string
function M.request(result)
    local request = result.request
    local body
    if request.body.state == "captured" then
        body = body_text(request.body.bytes)
    elseif request.body.state == "notCaptured" then
        body = request.body.reason
    end
    return message_text(request.method .. " " .. request.url, request.headers, body)
end

---@param result fshttp.RunResult
---@return string
function M.headers(result)
    return message_text(string.format("%d %s", result.status, result.reason), result.headers)
end

-- Gives nil for a body of zero bytes, because there is nothing to copy.
---@param result fshttp.RunResult
---@return string?
function M.body(result)
    if #result.body == 0 then
        return nil
    end
    return body_text(result.body)
end

return M
