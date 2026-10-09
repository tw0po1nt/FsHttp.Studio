local refusals = require("fshttp.refusals")

local M = {}

---@class fshttp.BlockMark
---@field line integer 1-based
---@field title string
---@field on_block boolean false for the line-1 mark of a Parse failure with no Block
---@field runnable boolean

---@param refusal string?
---@return string
local function title(refusal)
    if refusal == nil then
        return refusals.run_block_mark_title
    end
    local entry = refusals.codes[refusal] or refusals.codes[refusals.fallback_code]
    return entry.block_mark_title
end

---@param blocks { parse_failed: boolean, ranges: { start_line: integer, refusal: string? }[] } a decoded blocks envelope
---@return fshttp.BlockMark[]
function M.for_blocks(blocks)
    if #blocks.ranges == 0 then
        if blocks.parse_failed then
            return {
                {
                    line = 1,
                    title = refusals.no_blocks_parse_failure_block_mark_title,
                    on_block = false,
                    runnable = false,
                },
            }
        end
        return {}
    end
    local marks = {}
    for i, range in ipairs(blocks.ranges) do
        marks[i] = {
            line = range.start_line,
            title = title(range.refusal),
            on_block = true,
            runnable = range.refusal == nil,
        }
    end
    return marks
end

---@param mark_title string
---@return string
function M.glyph(mark_title)
    return (mark_title:match("^(%S+)"))
end

return M
