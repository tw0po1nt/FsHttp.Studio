-- The text of each Block mark that a locate gives. The VSCode CodeLens provider holds the same rule.
local refusals = require("fshttp.refusals")

local M = {}

---@class fshttp.BlockMark
---@field line integer the 1-based line that the mark sits on
---@field title string the text of the virtual line
---@field on_block boolean false for the line-1 mark of a Parse failure with no Block

M.run_title = refusals.run_block_mark_title
M.stopped_title = refusals.companion_stopped_block_mark_title

-- An unknown Refusal code gets the title of the fallback code.
---@param refusal string?
---@return string
local function title(refusal)
    if refusal == nil then
        return M.run_title
    end
    local entry = refusals.codes[refusal] or refusals.codes[refusals.fallback_code]
    return entry.block_mark_title
end

---@param blocks { parse_failed: boolean, ranges: { start_line: integer, refusal: string? }[] } a decoded blocks envelope
---@return fshttp.BlockMark[]
function M.for_blocks(blocks)
    if #blocks.ranges == 0 then
        if blocks.parse_failed then
            return { { line = 1, title = refusals.no_blocks_parse_failure_block_mark_title, on_block = false } }
        end
        return {}
    end
    local marks = {}
    for i, range in ipairs(blocks.ranges) do
        marks[i] = { line = range.start_line, title = title(range.refusal), on_block = true }
    end
    return marks
end

-- The sign shows this glyph.
---@param mark_title string
---@return string
function M.glyph(mark_title)
    return (mark_title:match("^(%S+)"))
end

return M
