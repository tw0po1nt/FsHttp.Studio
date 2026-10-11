local M = {}

M.stop_delay_ms = 1000

M.taskkill_timeout_ms = 1000

---@alias fshttp.KillCommand { kind: "signal_group", pid: integer }|{ kind: "exec", command: string[] }

---@param sysname string the `sysname` field of `vim.uv.os_uname()`
---@param pid integer
---@return fshttp.KillCommand
function M.kill_command(sysname, pid)
    if sysname == "Windows_NT" then
        return { kind = "exec", command = { "taskkill", "/T", "/F", "/PID", tostring(pid) } }
    end
    return { kind = "signal_group", pid = pid }
end

-- Windows has no process group to signal, and a detached child there opens a console window.
---@param sysname string the `sysname` field of `vim.uv.os_uname()`
---@return boolean
function M.detach_on_spawn(sysname)
    return sysname ~= "Windows_NT"
end

return M
