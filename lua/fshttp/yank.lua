local copy_text = require("fshttp.copy_text")

local M = {}

---@type fshttp.RunResult?
local latest

---@param result fshttp.RunResult? nil when the latest Run gave no response
function M.remember(result)
    latest = result
end

---@return fshttp.RunResult?
function M.latest()
    return latest
end

---@param message string
---@param level integer
local function notify(message, level)
    vim.notify(message, level, { title = "FsHttp.Studio" })
end

---@class fshttp.YankKind
---@field name string
---@field label string
---@field text fun(result: fshttp.RunResult): string?

-- The completion of :FsHttp yank uses this order.
---@type fshttp.YankKind[]
local kinds = {
    { name = "request", label = "Request", text = copy_text.request },
    { name = "headers", label = "Response headers", text = copy_text.headers },
    { name = "body", label = "Body", text = copy_text.body },
}

---@return string[]
function M.names()
    return vim.tbl_map(function(kind)
        return kind.name
    end, kinds)
end

---@param name string
---@return fshttp.YankKind?
local function kind_named(name)
    for _, kind in ipairs(kinds) do
        if kind.name == name then
            return kind
        end
    end
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

-- In an Ex command, v:register is always the default register, so `:FsHttp yank body +` names the register.
---@param name string request, headers, or body
---@param register? string one register name, or nil for v:register
function M.yank(name, register)
    local kind = kind_named(name)
    if not kind then
        notify(string.format(":FsHttp yank takes one of: %s.", table.concat(M.names(), ", ")), vim.log.levels.ERROR)
        return
    elseif register ~= nil and #register ~= 1 then
        notify(
            string.format(":FsHttp yank takes one register name, such as + or a. It got %s.", register),
            vim.log.levels.ERROR
        )
        return
    elseif not latest then
        notify("The latest Run gave no response. Run a Block first.", vim.log.levels.WARN)
        return
    end
    local text = kind.text(latest)
    if not text then
        notify("The " .. kind.label .. " is empty, so nothing was yanked.", vim.log.levels.INFO)
        return
    end
    register = register or vim.v.register
    local failure = write_register(register, text)
    if failure then
        notify(
            string.format("Could not yank the %s to register %s. %s", kind.label, register, failure),
            vim.log.levels.ERROR
        )
        return
    end
    notify(string.format("Yanked the %s to register %s.", kind.label, register), vim.log.levels.INFO)
end

return M
