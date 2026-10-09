local image_body = require("fshttp.image_body")
local notify = require("fshttp.notify").notify

local M = {}

---@type { placement: { close: fun(self) }?, file: string }?
local current

---@return table? snacks nil when the user did not install snacks.nvim
local function snacks()
    local ok, module = pcall(require, "snacks")
    if ok and type(module) == "table" then
        return module
    end
    return nil
end

---@param content_type string the type with no parameters
---@return string? reason nil when the client can show an image of this type
function M.unsupported_reason(content_type)
    if not require("fshttp").config().response_buffer.images then
        return image_body.images_off_reason
    end
    local module = snacks()
    if not module then
        return image_body.snacks_missing_reason
    end
    local image = module.image
    if type(image) ~= "table" or type(image.placement) ~= "table" or type(image.placement.new) ~= "function" then
        return "snacks.nvim has no image module"
    end
    if type(image.supports_terminal) == "function" and not image.supports_terminal() then
        return "the terminal does not support images"
    end
    local file = "body." .. image_body.extension(content_type)
    if type(image.supports_file) == "function" and not image.supports_file(file) then
        return "snacks.nvim does not show " .. content_type
    end
    return nil
end

function M.clear()
    if not current then
        return
    end
    local last = current
    current = nil
    if last.placement then
        pcall(last.placement.close, last.placement)
    end
    vim.uv.fs_unlink(last.file)
end

---@param buf integer
---@param image fshttp.ImageBody
function M.place(buf, image)
    M.clear()
    local file = vim.fn.tempname() .. "." .. image_body.extension(image.content_type)
    local handle, open_error = io.open(file, "wb")
    if not handle then
        notify("FsHttp.Studio: cannot write the image: " .. tostring(open_error), vim.log.levels.WARN)
        return
    end
    handle:write(image.bytes)
    handle:close()
    current = { file = file }
    local module = assert(snacks())
    local ok, placement = pcall(function()
        return module.image.placement.new(buf, file, { pos = { image.line, 0 }, inline = false, auto_resize = true })
    end)
    if ok then
        current.placement = placement
    else
        notify("FsHttp.Studio: snacks.nvim could not show the image: " .. tostring(placement), vim.log.levels.WARN)
    end
end

return M
