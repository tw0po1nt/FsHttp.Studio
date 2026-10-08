-- The :FsHttp command. Each subcommand is one entry of M.subcommands.
local M = {}

---@alias fshttp.Subcommand fun(args: string[])

---@type table<string, fshttp.Subcommand>
M.subcommands = {
    run = function()
        require("fshttp.run").at_cursor()
    end,
    status = function()
        require("fshttp.status_line").echo()
    end,
}

---@return string[]
local function names()
    local list = vim.tbl_keys(M.subcommands)
    table.sort(list)
    return list
end

---@param opts vim.api.keyset.create_user_command.command_args
function M.dispatch(opts)
    local name = opts.fargs[1]
    local subcommand = M.subcommands[name]
    if not subcommand then
        vim.notify(
            string.format(":FsHttp has no subcommand %s. The subcommands are: %s.", name, table.concat(names(), ", ")),
            vim.log.levels.ERROR,
            { title = "FsHttp.Studio" }
        )
        return
    end
    subcommand(vim.list_slice(opts.fargs, 2))
end

-- Completes the subcommand name only.
---@param arg_lead string
---@param cmdline string
---@return string[]
function M.complete(arg_lead, cmdline)
    if cmdline:match("^%s*%S+%s+%S+%s") then
        return {}
    end
    return vim.tbl_filter(function(name)
        return vim.startswith(name, arg_lead)
    end, names())
end

return M
