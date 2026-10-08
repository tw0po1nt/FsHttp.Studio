-- Puts the Request, the Response headers, or the Body of the latest Run in a register.
local copy_payload = require("fshttp.copy_payload")

local M = {}

---@type fshttp.RunResult?
local latest

-- The result of the latest Run, or nil when that Run gave no response.
---@param result fshttp.RunResult?
function M.remember(result)
    latest = result
end

---@param message string
---@param level integer
local function notify(message, level)
    vim.notify(message, level, { title = "FsHttp.Studio" })
end

---@type table<string, { label: string, text: fun(result: fshttp.RunResult): string? }>
local payloads = {
    request = { label = "Request", text = copy_payload.request },
    headers = { label = "Response headers", text = copy_payload.headers },
    body = { label = "Body", text = copy_payload.body },
}

---@return string[]
function M.names()
    return { "request", "headers", "body" }
end

---@param register string
---@param text string
---@return string? failure
local function write_register(register, text)
    if (register == "+" or register == "*") and vim.fn.has("clipboard") == 0 then
        return "Neovim has no clipboard provider. Install one, such as pbcopy, xclip, or wl-clipboard."
    end
    local ok, err = pcall(vim.fn.setreg, register, text)
    if not ok then
        return tostring(err)
    end
end

-- Uses the register of the command, so that `"+yr` reaches the system clipboard.
---@param name string request, headers, or body
function M.yank(name)
    local payload = payloads[name]
    if not payload then
        notify(string.format(":FsHttp yank takes one of: %s.", table.concat(M.names(), ", ")), vim.log.levels.ERROR)
        return
    elseif not latest then
        notify("The latest Run gave no response. Run a request first.", vim.log.levels.WARN)
        return
    end
    local text = payload.text(latest)
    if not text then
        notify("The " .. payload.label .. " is empty, so nothing was yanked.", vim.log.levels.INFO)
        return
    end
    local register = vim.v.register
    local failure = write_register(register, text)
    if failure then
        notify(
            string.format("Could not yank the %s to register %s. %s", payload.label, register, failure),
            vim.log.levels.ERROR
        )
        return
    end
    notify(string.format("Yanked the %s to register %s.", payload.label, register), vim.log.levels.INFO)
end

return M
