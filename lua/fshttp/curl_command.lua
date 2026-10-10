-- Each Curl command must match its Golden fixture.
local M = {}

-- Linux limits each argument to 128 KB, and Git Bash limits the command line to 32,767 characters.
local max_inline_body_bytes = 16384

local base64_alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"

---@param index integer 0 to 63
---@return string
local function base64_digit(index)
    return base64_alphabet:sub(index + 1, index + 1)
end

-- A core module cannot read vim.base64, and LuaJIT and Lua 5.1 have no common bit operator.
---@param bytes string
---@return string
local function base64(bytes)
    local parts = {}
    for i = 1, #bytes, 3 do
        local first, second, third = bytes:byte(i, i + 2)
        local group = first * 65536 + (second or 0) * 256 + (third or 0)
        parts[#parts + 1] = base64_digit(math.floor(group / 262144))
            .. base64_digit(math.floor(group / 4096) % 64)
            .. (second and base64_digit(math.floor(group / 64) % 64) or "=")
            .. (third and base64_digit(group % 64) or "=")
    end
    return table.concat(parts)
end

-- Strict UTF-8 with no C0, DEL, or C1 control character except tab and LF. The F# renderer must agree.
---@param bytes string
---@return boolean
local function is_safe_to_paste(bytes)
    local function in_range(i, lower, upper)
        local byte = bytes:byte(i)
        return byte ~= nil and byte >= lower and byte <= upper
    end
    local function continues(i)
        return in_range(i, 0x80, 0xBF)
    end
    local i = 1
    while i <= #bytes do
        local lead = bytes:byte(i)
        local width = 0
        if lead == 9 or lead == 10 or (lead >= 0x20 and lead < 0x7F) then
            width = 1
        elseif lead == 0xC2 and in_range(i + 1, 0xA0, 0xBF) then
            width = 2
        elseif lead >= 0xC3 and lead <= 0xDF and continues(i + 1) then
            width = 2
        elseif lead == 0xE0 and in_range(i + 1, 0xA0, 0xBF) and continues(i + 2) then
            width = 3
        elseif
            ((lead >= 0xE1 and lead <= 0xEC) or lead == 0xEE or lead == 0xEF)
            and continues(i + 1)
            and continues(i + 2)
        then
            width = 3
        elseif lead == 0xED and in_range(i + 1, 0x80, 0x9F) and continues(i + 2) then
            width = 3
        elseif lead == 0xF0 and in_range(i + 1, 0x90, 0xBF) and continues(i + 2) and continues(i + 3) then
            width = 4
        elseif lead >= 0xF1 and lead <= 0xF3 and continues(i + 1) and continues(i + 2) and continues(i + 3) then
            width = 4
        elseif lead == 0xF4 and in_range(i + 1, 0x80, 0x8F) and continues(i + 2) and continues(i + 3) then
            width = 4
        end
        if width == 0 then
            return false
        end
        i = i + width
    end
    return true
end

---@param text string
---@return string
local function shell_quote(text)
    return "'" .. (text:gsub("'", "'\\''")) .. "'"
end

---@param headers fshttp.Header[]
---@param name string
---@return boolean
local function has_header(headers, name)
    for _, header in ipairs(headers) do
        if header.name:lower() == name:lower() then
            return true
        end
    end
    return false
end

-- curl sends GET with no data flag and POST with one. `-X HEAD` makes curl wait for a body.
---@param method string
---@param has_body boolean
---@return string[]
local function method_flag(method, has_body)
    if (method == "POST" and has_body) or (method == "GET" and not has_body) then
        return {}
    elseif method == "HEAD" then
        return { "--head" }
    elseif method:match("^[A-Za-z0-9%-]*$") then
        return { "-X " .. method }
    end
    return { "-X " .. shell_quote(method) }
end

---@param list string[]
---@param items string[]
local function append(list, items)
    for _, item in ipairs(items) do
        list[#list + 1] = item
    end
end

---@alias fshttp.CurlBody { inline: string }|{ base64: string }

-- `--data-raw` reads no file at a leading `@`. curl removes a `Name:` header and adds its own default headers.
---@param request fshttp.SentRequest
---@param body fshttp.CurlBody? nil for no data flag
---@return string
local function command_text(request, body)
    local first_line = { "curl" }
    append(first_line, method_flag(request.method, body ~= nil))
    first_line[#first_line + 1] = shell_quote(request.url)

    local arguments = { table.concat(first_line, " ") }
    if request.url:find("[%[%]{}]") then
        arguments[#arguments + 1] = "--globoff"
    end
    for _, header in ipairs(request.headers) do
        if header.name:lower() ~= "content-length" then
            local value = header.value:match("^[ \t]*$") and ";" or (": " .. header.value)
            arguments[#arguments + 1] = "-H " .. shell_quote(header.name .. value)
        end
    end
    local defaults = { "User-Agent", "Accept" }
    if body ~= nil then
        defaults[#defaults + 1] = "Content-Type"
    end
    for _, name in ipairs(defaults) do
        if not has_header(request.headers, name) then
            arguments[#arguments + 1] = "-H " .. shell_quote(name .. ":")
        end
    end

    if body == nil then
        return table.concat(arguments, " \\\n  ")
    elseif body.inline then
        arguments[#arguments + 1] = "--data-raw " .. shell_quote(body.inline)
        return table.concat(arguments, " \\\n  ")
    end
    -- `--data @-` removes each CR and LF from the body.
    arguments[#arguments + 1] = "--data-binary @-"
    return "printf '%s' "
        .. shell_quote(body.base64)
        .. " \\\n  | base64 -d \\\n  | "
        .. table.concat(arguments, " \\\n    ")
end

---@param result fshttp.RunResult
---@return string? command
---@return string? reason nil when there is a command
function M.build(result)
    local request = result.request
    local body = request.body
    if request.method == "HEAD" and body.state ~= "none" then
        return nil, "curl cannot send a HEAD request with a body."
    elseif body.state == "notCaptured" then
        return nil,
            "The companion did not read the body of the Request, so a Curl command would send a different request."
    elseif body.state == "none" then
        return command_text(request, nil)
    elseif #body.bytes <= max_inline_body_bytes and is_safe_to_paste(body.bytes) then
        return command_text(request, { inline = body.bytes })
    end
    return command_text(request, { base64 = base64(body.bytes) })
end

return M
