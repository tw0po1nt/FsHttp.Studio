-- The cursor rule that both Clients hold. The VSCode extension host holds the same rule.
local M = {}

-- Of the Blocks that hold the line, the Block with the latest start is the target. The column of
-- the cursor has no effect.
---@param ranges { start_line: integer, start_col: integer, end_line: integer }[] in FCS numbering
---@param cursor_line integer 1-based, as nvim_win_get_cursor gives it
---@return integer|nil block_index the 0-based index that the run envelope carries, or nil when the cursor is outside every Block
function M.block_index(ranges, cursor_line)
    local target
    for i, range in ipairs(ranges) do
        if range.start_line <= cursor_line and cursor_line <= range.end_line then
            local best = target and ranges[target]
            if
                not best
                or range.start_line > best.start_line
                or (range.start_line == best.start_line and range.start_col > best.start_col)
            then
                target = i
            end
        end
    end
    return target and target - 1
end

return M
