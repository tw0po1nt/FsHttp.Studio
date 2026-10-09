local cursor = require("fshttp.cursor")
local refusals = require("fshttp.refusals")

local M = {}

M.default_request_timeout_ms = 30000

---@alias fshttp.NoticeLevel "INFO"|"WARN"

---@class fshttp.RunTarget
---@field kind "run"|"notice"|"outside"
---@field block_index? integer the 0-based index of the run envelope, for a "run" target
---@field level? fshttp.NoticeLevel for a "notice" target
---@field message? string for a "notice" target

---@param blocks { parse_failed: boolean, ranges: { start_line: integer, start_col: integer, end_line: integer, refusal: string? }[] } a decoded blocks envelope
---@param cursor_line integer 1-based
---@return fshttp.RunTarget
function M.for_cursor(blocks, cursor_line)
    if #blocks.ranges == 0 then
        if blocks.parse_failed then
            return { kind = "notice", level = "WARN", message = refusals.no_blocks_parse_failure }
        end
        return { kind = "notice", level = "INFO", message = refusals.no_blocks_empty }
    end
    local block_index = cursor.block_index(blocks.ranges, cursor_line)
    if block_index == nil then
        return { kind = "outside" }
    end
    local refusal = blocks.ranges[block_index + 1].refusal
    if refusal ~= nil then
        return { kind = "notice", level = "WARN", message = refusals.entry(refusal).detail }
    end
    return { kind = "run", block_index = block_index }
end

-- A value that is not a finite number at or above 0 gives the default, as in the VSCode extension host.
---@param value any the request_timeout_ms option
---@return integer
function M.request_timeout_ms(value)
    if type(value) ~= "number" or value ~= value or value < 0 or value == math.huge then
        return M.default_request_timeout_ms
    end
    return math.floor(value)
end

-- The run envelope carries the same source text as the locate that gave the Block index.
---@param source string
---@param block_index integer
---@param script_file_name string? the absolute path of the Script, which FSI needs for __SOURCE_DIRECTORY__
---@param request_timeout_ms any the request_timeout_ms option
---@return table envelope
function M.run_envelope(source, block_index, script_file_name, request_timeout_ms)
    if script_file_name == "" then
        script_file_name = nil
    end
    return {
        tag = "run",
        source = source,
        block_index = block_index,
        script_file_name = script_file_name,
        timeout_ms = M.request_timeout_ms(request_timeout_ms),
    }
end

return M
