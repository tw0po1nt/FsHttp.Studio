-- The :FsHttp command. Each subcommand is one entry of the table below.
local M = {}

---@type table<string, fun(args: string[])>
local subcommands = {
    status = function()
        require("fshttp.status_line").echo()
    end,
}

---@return string[]
local function names()
    local found = vim.tbl_keys(subcommands)
    table.sort(found)
    return found
end

---@param fargs string[] the subcommand name, then its arguments
function M.run(fargs)
    local subcommand = subcommands[fargs[1]]
    if not subcommand then
        vim.notify(
            string.format(
                "FsHttp.Studio has no subcommand '%s'. The subcommands are: %s.",
                fargs[1] or "",
                table.concat(names(), ", ")
            ),
            vim.log.levels.ERROR,
            { title = "FsHttp.Studio" }
        )
        return
    end
    subcommand(vim.list_slice(fargs, 2))
end

-- Completes the subcommand name, which is the first argument.
---@param arg_lead string
---@param cmdline string
---@return string[]
function M.complete(arg_lead, cmdline)
    if #vim.split(vim.trim(cmdline), "%s+") > (arg_lead == "" and 1 or 2) then
        return {}
    end
    return vim.tbl_filter(function(name)
        return vim.startswith(name, arg_lead)
    end, names())
end

return M
