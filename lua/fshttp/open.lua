local notify = require("fshttp.notify").notify
local open_rule = require("fshttp.open_rule")
local response_view = require("fshttp.response_view")

local M = {}

function M.open()
    local result = require("fshttp.yank").latest()
    if not result then
        notify("The latest Run gave no response. Run a Block first.", vim.log.levels.WARN)
        return
    end
    local content_type = response_view.normalize_content_type(result.content_type)
    -- Each call writes a new file, so a later Run never changes a page that is already open.
    local path = vim.fn.tempname() .. "." .. open_rule.extension(content_type)
    local handle, write_error = io.open(path, "wb")
    if not handle then
        notify("Could not write the body to a file. " .. tostring(write_error), vim.log.levels.ERROR)
        return
    end
    handle:write(open_rule.file_content(content_type, result.body))
    handle:close()
    local _, open_error = vim.ui.open(path)
    if open_error then
        notify(string.format("Could not open %s. %s", path, open_error), vim.log.levels.ERROR)
    end
end

return M
