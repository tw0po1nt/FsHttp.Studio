-- Each frame is a 4-byte big-endian length and then that many bytes of payload.
local M = {}

function M.encode(payload)
    local length = #payload
    return string.char(
        math.floor(length / 0x1000000) % 0x100,
        math.floor(length / 0x10000) % 0x100,
        math.floor(length / 0x100) % 0x100,
        length % 0x100
    ) .. payload
end

local Parser = {}
Parser.__index = Parser

-- Joins the held chunks only when a whole length or a whole frame is present. Thus the parser copies
-- a large frame once, whatever the number of chunks.
local function flatten(parser)
    if #parser.chunks > 1 then
        parser.chunks = { table.concat(parser.chunks) }
    end
    return parser.chunks[1] or ""
end

-- Takes the next chunk of the stream, and returns a list of each payload that the chunk completes.
-- A partial frame stays in the parser until a later chunk completes it.
function Parser:push(chunk)
    local payloads = {}
    if #chunk > 0 then
        self.chunks[#self.chunks + 1] = chunk
        self.size = self.size + #chunk
    end
    while true do
        if not self.length then
            if self.size < 4 then
                break
            end
            local b1, b2, b3, b4 = flatten(self):byte(1, 4)
            self.length = ((b1 * 0x100 + b2) * 0x100 + b3) * 0x100 + b4
        end
        if self.size < 4 + self.length then
            break
        end
        local buffer = flatten(self)
        payloads[#payloads + 1] = buffer:sub(5, 4 + self.length)
        local rest = buffer:sub(5 + self.length)
        self.chunks = { rest }
        self.size = #rest
        self.length = nil
    end
    return payloads
end

function M.parser()
    return setmetatable({ chunks = {}, size = 0, length = nil }, Parser)
end

return M
