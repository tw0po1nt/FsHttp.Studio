-- The picker that lists each located Block when the cursor is outside every Block.
local block_mark = require("fshttp.block_mark")

local M = {}

-- Each item shows the glyph of the Block mark title, the line number, and the first source line of
-- the Block. The items are in source order, refused Blocks too.
---@param blocks { parse_failed: boolean, ranges: { start_line: integer, refusal: string? }[] } a decoded blocks envelope
---@param first_lines string[] first_lines[i] is the first source line of the i-th located Block
---@return string[] items
function M.items(blocks, first_lines)
    local marks = block_mark.for_blocks(blocks)
    local items = {}
    for i, mark in ipairs(marks) do
        items[i] = string.format("%s %d: %s", block_mark.glyph(mark.title), mark.line, first_lines[i])
    end
    return items
end

---@param blocks { ranges: { start_line: integer }[] } a decoded blocks envelope
---@param source string the text of the locate that gave `blocks`
---@return string[] first_lines first_lines[i] is the first source line of the i-th located Block, trimmed
function M.first_lines(blocks, source)
    local lines = {}
    for line in (source .. "\n"):gmatch("(.-)\n") do
        lines[#lines + 1] = line
    end
    local first_lines = {}
    for i, range in ipairs(blocks.ranges) do
        first_lines[i] = (lines[range.start_line] or ""):match("^%s*(.-)%s*$")
    end
    return first_lines
end

-- Opens vim.ui.select with each located Block. The callback gets the 0-based block_index and
-- whether the picked Block is refused. A cancel gives no callback.
---@param blocks { parse_failed: boolean, ranges: { start_line: integer, refusal: string? }[] } a decoded blocks envelope
---@param source string the text of the locate that gave `blocks`, which can differ from the buffer after a wait
---@param on_pick fun(block_index: integer, refused: boolean)
function M.open(blocks, source, on_pick)
    vim.ui.select(M.items(blocks, M.first_lines(blocks, source)), { prompt = "Run request:" }, function(_, idx)
        if idx == nil then
            return
        end
        on_pick(idx - 1, blocks.ranges[idx].refusal ~= nil)
    end)
end

return M
