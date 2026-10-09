local image_body = require("fshttp.image_body")

local M = {}

M.html_hint = ":FsHttp open  shows the rendered page in the browser, with scripts blocked"
M.image_hint = ":FsHttp open  shows the image in the system viewer"

M.policy = "script-src 'none'; object-src 'none'; frame-src 'none'; style-src * 'unsafe-inline'; img-src * data:"

local meta = '<meta http-equiv="Content-Security-Policy" content="' .. M.policy .. '">'

---@param content_type string the type with no parameters
---@return boolean
function M.is_html(content_type)
    return content_type == "text/html" or content_type == "application/xhtml+xml"
end

---@param content_type string the type with no parameters
---@return boolean
function M.is_json(content_type)
    return content_type == "application/json" or content_type == "text/json" or content_type:match("%+json$") ~= nil
end

---@param content_type string the type with no parameters
---@return boolean
function M.is_xml(content_type)
    return content_type == "application/xml" or content_type == "text/xml" or content_type:match("%+xml$") ~= nil
end

---@param content_type string the type with no parameters
---@return string? hint the line below the Body title, or nil when the body has no hint
function M.hint(content_type)
    if M.is_html(content_type) then
        return M.html_hint
    elseif image_body.is_image(content_type) then
        return M.image_hint
    end
    return nil
end

---@param content_type string the type with no parameters
---@return string
function M.extension(content_type)
    if M.is_html(content_type) then
        return "html"
    elseif image_body.is_image(content_type) then
        return image_body.extension(content_type)
    elseif M.is_json(content_type) then
        return "json"
    elseif M.is_xml(content_type) then
        return "xml"
    elseif content_type == "text/plain" then
        return "txt"
    end
    return "bin"
end

---@param lowered string
---@param pattern string
---@return integer?
local function after_tag(lowered, pattern)
    local _, stop = lowered:find(pattern)
    return stop
end

-- The policy must come before each script, and the doctype must stay first.
---@param html string
---@return string
function M.with_policy(html)
    local lowered = html:lower()
    local head = after_tag(lowered, "<head%f[%W][^>]*>")
    if head then
        return html:sub(1, head) .. meta .. html:sub(head + 1)
    end
    local root = after_tag(lowered, "<html%f[%W][^>]*>")
    if root then
        return html:sub(1, root) .. "<head>" .. meta .. "</head>" .. html:sub(root + 1)
    end
    local doctype = after_tag(lowered, "^%s*<!doctype[^>]*>")
    if doctype then
        return html:sub(1, doctype) .. meta .. html:sub(doctype + 1)
    end
    return meta .. html
end

---@param content_type string the type with no parameters
---@param body string
---@return string
function M.file_content(content_type, body)
    if M.is_html(content_type) then
        return M.with_policy(body)
    end
    return body
end

return M
