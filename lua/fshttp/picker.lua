-- The picker that lists each located Block when the cursor is outside every Block.
local block_mark = require("fshttp.block_mark")

local M = {}

-- Each item shows the glyph of the lens title, the line number, and the first source line of the
-- Block. The items are in source order, refused Blocks too.
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

-- Opens vim.ui.select with each located Block. The callback gets the 0-based block_index and
-- whether the picked Block is refused. A cancel gives no callback.
---@param blocks { parse_failed: boolean, ranges: { start_line: integer, refusal: string? }[] } a decoded blocks envelope
---@param buf integer the Script buffer, for the first source line of each Block
---@param on_pick fun(block_index: integer, refused: boolean)
function M.open(blocks, buf, on_pick)
    local first_lines = {}
    for i, range in ipairs(blocks.ranges) do
        local line = vim.api.nvim_buf_get_lines(buf, range.start_line - 1, range.start_line, false)[1] or ""
        first_lines[i] = vim.trim(line)
    end
    vim.ui.select(M.items(blocks, first_lines), { prompt = "Run request:" }, function(_, idx)
        if idx == nil then
            return
        end
        on_pick(idx - 1, blocks.ranges[idx].refusal ~= nil)
    end)
end

return M
