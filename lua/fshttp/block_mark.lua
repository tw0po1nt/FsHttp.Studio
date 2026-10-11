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
    return refusals.entry(refusal).block_mark_title
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

---@class fshttp.ShownMark
---@field id integer the extmark id
---@field row integer 0-based
---@field on_block boolean false for a mark on no Block, or on Block text that was deleted

---@class fshttp.MarkChange
---@field id integer the extmark id
---@field row integer 0-based
---@field title string? nil removes the mark

local failed_start_states = { sdkNotFound = true, downloadFailed = true, noRelease = true, companionNotFound = true }

---@param shown fshttp.ShownMark[] the Block marks of one Script
---@param state fshttp.CompanionState
---@return fshttp.MarkChange[]? changes nil when each mark keeps its title
function M.not_ready_changes(shown, state)
    local title
    if state == "starting" then
        title = refusals.companion_starting_block_mark_title
    elseif state == "stopped" or failed_start_states[state] then
        title = refusals.companion_stopped_block_mark_title
    else
        return nil
    end
    local changes = {}
    for i, mark in ipairs(shown) do
        changes[i] = { id = mark.id, row = mark.row, title = mark.on_block and title or nil }
    end
    return changes
end

---@param mark_title string
---@return string
function M.glyph(mark_title)
    return (mark_title:match("^(%S+)"))
end

return M
