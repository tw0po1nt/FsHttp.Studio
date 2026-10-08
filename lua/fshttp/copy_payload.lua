-- The text that a yank puts in a register. It must match the Golden fixture of the renderer core.
local binary_body = require("fshttp.binary_body")

local M = {}

-- A body with invalid UTF-8 copies byte for byte, where the renderer core copies U+FFFD.
---@param bytes string
---@return string
local function body_text(bytes)
    if binary_body.looks_binary(bytes) then
        return binary_body.hex_dump(bytes)
    end
    return bytes
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
