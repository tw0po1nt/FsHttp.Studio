-- The binary test and the hex dump of the VSCode renderer core. Each must match its Golden fixture.
local M = {}

local max_bytes = 256
local bytes_per_line = 16

-- A NUL byte, or more than 30 percent control bytes other than tab, newline, and carriage return.
---@param bytes string
---@return boolean
function M.looks_binary(bytes)
    if #bytes == 0 then
        return false
    end
    if bytes:find("\0", 1, true) then
        return true
    end
    local _, control_count = bytes:gsub("[\1-\8\11\12\14-\31]", "")
    return control_count / #bytes > 0.30
end

-- The first 256 bytes, 16 bytes on each line, and a line that counts the bytes that it does not show.
---@param bytes string
---@return string
function M.hex_dump(bytes)
    local shown = math.min(#bytes, max_bytes)
    local lines = {}
    for offset = 0, shown - 1, bytes_per_line do
        local line_length = math.min(bytes_per_line, shown - offset)
        local hex = {}
        local ascii = {}
        for j = 1, bytes_per_line do
            if j <= line_length then
                local byte = bytes:byte(offset + j)
                hex[j] = string.format("%02x ", byte)
                ascii[j] = (byte >= 32 and byte < 127) and string.char(byte) or "."
            else
                hex[j] = "   "
            end
        end
        lines[#lines + 1] = string.format("%08x  %s %s", offset, table.concat(hex), table.concat(ascii))
    end
    local dumped = table.concat(lines, "\n")
    if #bytes > shown then
        return dumped .. string.format("\n… (%d more bytes)", #bytes - shown)
    end
    return dumped
end

return M
