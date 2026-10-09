local binary_body = require("fshttp.binary_body")
local image_body = require("fshttp.image_body")
local json = require("fshttp.json")
local open_rule = require("fshttp.open_rule")
local refusals = require("fshttp.refusals")

local M = {}

---@class fshttp.Header
---@field name string
---@field value string

---@class fshttp.SentRequest
---@field method string
---@field url string
---@field headers fshttp.Header[]
---@field body { state: "none"|"captured"|"notCaptured", bytes: string, reason: string }

---@class fshttp.RunResult
---@field status integer
---@field reason string
---@field headers fshttp.Header[]
---@field content_type string
---@field body string the decoded bytes of the body
---@field request_ms number
---@field total_ms number
---@field request fshttp.SentRequest

---@class fshttp.Fold
---@field first integer 1-based
---@field last integer 1-based
---@field closed boolean

---@class fshttp.Highlight
---@field line integer 1-based
---@field first_col integer 0-based byte column
---@field last_col integer 0-based byte column after the last byte
---@field group string

-- Each line is 1-based in the text of the body.
---@class fshttp.BodySyntax
---@field highlights fshttp.Highlight[]
---@field folds { first: integer, last: integer }[]

-- Gives nil when Neovim has no parser for the language.
---@alias fshttp.BodySyntaxLookup fun(language: string, text: string): fshttp.BodySyntax?

-- Gives nil when the client can show an image, and the reason when it cannot.
---@alias fshttp.ImageSupport fun(content_type: string): string?

---@class fshttp.ImageBody
---@field line integer 1-based line of the pixel size
---@field content_type string the type with no parameters
---@field bytes string

---@class fshttp.ResponseView
---@field lines string[]
---@field winbar string a statusline expression, or "" for no winbar
---@field folds fshttp.Fold[]
---@field highlights fshttp.Highlight[]
---@field hints { line: integer, text: string }[] a virtual line below each 1-based line
---@field image fshttp.ImageBody? set when the client can show the image body
---@field positions table<integer, fshttp.ScriptPosition>? the position of each `(line,col)` line, by 1-based line

---@class fshttp.ScriptPosition
---@field line integer 1-based
---@field col integer 0-based

local open_glyph = "▾"
local closed_glyph = "▸"

---@return fshttp.ResponseView
local function new_view()
    return { lines = {}, winbar = "", folds = {}, highlights = {}, hints = {} }
end

-- Each "\n" ends a line, so the joined lines give the text again byte for byte.
---@param text string
---@return string[]
function M.split_lines(text)
    local lines = {}
    local start = 1
    while true do
        local stop = text:find("\n", start, true)
        if not stop then
            lines[#lines + 1] = text:sub(start)
            return lines
        end
        lines[#lines + 1] = text:sub(start, stop - 1)
        start = stop + 1
    end
end

---@param bytes integer
---@return string
function M.human_size(bytes)
    if bytes < 1024 then
        return string.format("%d B", bytes)
    elseif bytes < 1024 * 1024 then
        return string.format("%.1f KB", bytes / 1024)
    end
    return string.format("%.1f MB", bytes / 1024 / 1024)
end

---@param content_type string
---@return string
function M.normalize_content_type(content_type)
    local bare = content_type:match("^[^;]*")
    return (bare:gsub("^%s+", ""):gsub("%s+$", ""):lower())
end

---@param status integer
---@return string
function M.status_group(status)
    if status >= 200 and status < 300 then
        return "FsHttpResponseStatus2xx"
    elseif status >= 300 and status < 400 then
        return "FsHttpResponseStatus3xx"
    elseif status >= 400 and status < 500 then
        return "FsHttpResponseStatus4xx"
    elseif status >= 500 and status < 600 then
        return "FsHttpResponseStatus5xx"
    end
    return "FsHttpResponseStatusOther"
end

---@param ms number
---@return string
local function milliseconds(ms)
    return string.format("%d ms", math.floor(ms + 0.5))
end

---@param view fshttp.ResponseView
---@param chunks { [1]: string, [2]: string? }[]
---@return integer line 1-based
local function add(view, chunks)
    local line = #view.lines + 1
    local parts = {}
    local col = 0
    for _, chunk in ipairs(chunks) do
        local text, group = chunk[1], chunk[2]
        parts[#parts + 1] = text
        if group and #text > 0 then
            view.highlights[#view.highlights + 1] =
                { line = line, first_col = col, last_col = col + #text, group = group }
        end
        col = col + #text
    end
    view.lines[line] = table.concat(parts)
    return line
end

---@param view fshttp.ResponseView
---@param text string
---@param group string?
local function add_text(view, text, group)
    for _, line in ipairs(M.split_lines(text)) do
        add(view, { { line, group } })
    end
end

---@param view fshttp.ResponseView
---@param title string
---@param detail string?
---@param closed boolean
---@param fill fun()
local function section(view, title, detail, closed, fill)
    local chunks = { { open_glyph .. " " .. title, "FsHttpResponseSection" } }
    if detail then
        chunks[2] = { "  " .. detail, "FsHttpResponseDetail" }
    end
    local first = add(view, chunks)
    fill()
    if #view.lines > first then
        view.folds[#view.folds + 1] = { first = first, last = #view.lines, closed = closed }
    end
end

---@param view fshttp.ResponseView
---@param headers fshttp.Header[]
local function header_rows(view, headers)
    for _, header in ipairs(headers) do
        add(view, { { "  " }, { header.name, "FsHttpResponseHeaderName" }, { ": " .. header.value } })
    end
end

-- The hex view must match the note and the hex dump of the VSCode Response viewer.
---@param view fshttp.ResponseView
---@param bytes string
---@param indent string
local function add_hex_view(view, bytes, indent)
    add(view, { { indent }, { "Binary body: " .. M.human_size(#bytes), "FsHttpResponseDetail" } })
    for _, line in ipairs(M.split_lines(binary_body.hex_dump(bytes))) do
        add(view, { { indent .. line } })
    end
end

---@param text string
---@return string
local function escape_statusline(text)
    return (text:gsub("%%", "%%%%"))
end

---@param chunks { [1]: string, [2]: string }[]
---@return string
local function statusline(chunks)
    local parts = {}
    for _, chunk in ipairs(chunks) do
        parts[#parts + 1] = "%#" .. chunk[2] .. "#" .. escape_statusline(chunk[1]) .. "%*"
    end
    return table.concat(parts)
end

-- The `%<` item cuts the start of the URL when the window is too narrow for the winbar.
---@param result fshttp.RunResult
---@return string
local function result_winbar(result)
    return statusline({
        { string.format(" %d %s", result.status, result.reason), M.status_group(result.status) },
        { "  " .. milliseconds(result.request_ms), "FsHttpResponseTime" },
        { " · " .. milliseconds(result.total_ms) .. " total", "FsHttpResponseDetail" },
        { "  " .. M.human_size(#result.body), "FsHttpResponseDetail" },
        { "  " .. result.request.method .. " ", "FsHttpResponseMethod" },
    }) .. "%<" .. statusline({ { result.request.url, "FsHttpResponseUrl" } })
end

---@param content_type string the type with no parameters
---@return string?
local function body_language(content_type)
    if open_rule.is_json(content_type) then
        return "json"
    elseif open_rule.is_html(content_type) then
        return "html"
    elseif open_rule.is_xml(content_type) then
        return "xml"
    end
    return nil
end

-- The VSCode Response viewer shows an HTML body and an image body before its binary test.
---@param content_type string the type with no parameters
---@return boolean
local function skips_binary_test(content_type)
    return body_language(content_type) == "html" or image_body.is_image(content_type)
end

---@param view fshttp.ResponseView
---@param content_type string the type with no parameters
---@param body string
---@param body_syntax fshttp.BodySyntaxLookup?
local function add_body(view, content_type, body, body_syntax)
    local offset = #view.lines
    local language = body_language(content_type)
    local syntax = language and body_syntax and body_syntax(language, body)
    local text, folds, highlights = body, {}, {}
    if syntax then
        folds, highlights = syntax.folds, syntax.highlights
    elseif language == "json" then
        local pretty, pretty_folds = json.pretty_print(body)
        if pretty and pretty_folds then
            text, folds = pretty, pretty_folds
        end
    end
    add_text(view, text)
    for _, fold in ipairs(folds) do
        view.folds[#view.folds + 1] = { first = offset + fold.first, last = offset + fold.last, closed = false }
    end
    for _, highlight in ipairs(highlights) do
        view.highlights[#view.highlights + 1] = {
            line = offset + highlight.line,
            first_col = highlight.first_col,
            last_col = highlight.last_col,
            group = highlight.group,
        }
    end
end

---@param seconds integer
---@return fshttp.ResponseView
function M.running(seconds)
    local view = new_view()
    add(view, { { string.format("Running… %ds", seconds), "FsHttpResponseDetail" } })
    return view
end

---@param view fshttp.ResponseView
---@param content_type string the type with no parameters
---@param body string
---@param image_support fshttp.ImageSupport?
local function add_image(view, content_type, body, image_support)
    ---@type string?
    local reason = image_body.snacks_missing_reason
    if image_support then
        reason = image_support(content_type)
    end
    local line = add(view, { { image_body.size_line(body, reason), "FsHttpResponseDetail" } })
    if not reason then
        view.image = { line = line, content_type = content_type, bytes = body }
    end
end

---@param result fshttp.RunResult
---@param body_syntax fshttp.BodySyntaxLookup?
---@param image_support fshttp.ImageSupport?
---@return fshttp.ResponseView
function M.result(result, body_syntax, image_support)
    local view = new_view()
    view.winbar = result_winbar(result)

    local request = result.request
    local request_detail
    if request.body.state == "captured" then
        request_detail = "(" .. M.human_size(#request.body.bytes) .. ")"
    end
    section(view, "Request", request_detail, true, function()
        add(
            view,
            { { "  " }, { request.method, "FsHttpResponseMethod" }, { " " }, { request.url, "FsHttpResponseUrl" } }
        )
        header_rows(view, request.headers)
        if request.body.state == "captured" then
            add(view, { { "" } })
            if binary_body.looks_binary(request.body.bytes) then
                add_hex_view(view, request.body.bytes, "  ")
            else
                for _, line in ipairs(M.split_lines(request.body.bytes)) do
                    add(view, { { "  " .. line } })
                end
            end
        elseif request.body.state == "notCaptured" then
            add(view, { { "" } })
            for _, line in ipairs(M.split_lines(request.body.reason)) do
                add(view, { { "  " }, { line, "FsHttpResponseDetail" } })
            end
        end
    end)

    section(view, "Response headers", "(" .. #result.headers .. ")", true, function()
        header_rows(view, result.headers)
    end)

    local content_type = M.normalize_content_type(result.content_type)
    local size = M.human_size(#result.body)
    local body_detail = content_type == "" and size or (content_type .. " · " .. size)
    section(view, "Body", body_detail, false, function()
        local hint = #result.body > 0 and open_rule.hint(content_type)
        if hint then
            view.hints[#view.hints + 1] = { line = #view.lines, text = hint }
        end
        if image_body.is_image(content_type) and #result.body > 0 then
            add_image(view, content_type, result.body, image_support)
        elseif not skips_binary_test(content_type) and binary_body.looks_binary(result.body) then
            add_hex_view(view, result.body, "")
        elseif #result.body > 0 then
            add_body(view, content_type, result.body, body_syntax)
        end
    end)
    return view
end

---@param message string the message of a runtimeError envelope
---@return fshttp.ResponseView
function M.runtime_error(message)
    local view = new_view()
    local lines = M.split_lines("Runtime error: " .. message)
    add(view, { { "Runtime error:", "FsHttpResponseError" }, { lines[1]:sub(#"Runtime error:" + 1) } })
    for i = 2, #lines do
        add(view, { { lines[i] } })
    end
    return view
end

-- The view must match the Compile error text of the VSCode Response viewer, with no trailing spaces.
---@param diagnostics { message: string, range: { start_line: integer, start_col: integer } }[]
---@return fshttp.ResponseView
function M.compile_error(diagnostics)
    local view = new_view()
    view.winbar = statusline({
        { "Compile error", "FsHttpResponseError" },
        { "  <CR> on a (line,col) moves to it in the script", "FsHttpResponseDetail" },
    })
    add(view, { { "Compile error:", "FsHttpResponseError" } })
    view.positions = {}
    for _, diagnostic in ipairs(diagnostics) do
        local position = string.format("(%d,%d)", diagnostic.range.start_line, diagnostic.range.start_col + 1)
        view.positions[#view.lines + 1] = { line = diagnostic.range.start_line, col = diagnostic.range.start_col }
        for _, line in ipairs(M.split_lines(position .. " " .. diagnostic.message)) do
            add(view, { { (line:gsub(" +$", "")) } })
        end
    end
    return view
end

-- `unboundBlockValue` and `staleBlockIndex` are Run outcomes only, so they have no catalog entry.
---@param code string
---@param name string?
---@return { title: string, detail: string }
local function refusal_for(code, name)
    if code == "unboundBlockValue" and name then
        local escaped = name:gsub("%%", "%%%%")
        local entry = refusals.unbound_block_value
        return {
            title = entry.title,
            detail = (entry.detail:gsub("{name}", escaped)),
        }
    elseif code == "staleBlockIndex" and not name then
        return refusals.stale_block_index
    end
    return refusals.entry(code)
end

---@param code string
---@param name string?
---@return fshttp.ResponseView
function M.refused(code, name)
    local view = new_view()
    local refusal = refusal_for(code, name)
    add(view, { { refusal.title, "FsHttpResponseRefused" } })
    add(view, { { "" } })
    add_text(view, refusal.detail)
    return view
end

-- For an outcome that the companion did not give, such as a stopped companion.
---@param message string
---@return fshttp.ResponseView
function M.message(message)
    local view = new_view()
    add_text(view, message)
    return view
end

---@param line_count integer
---@param folds fshttp.Fold[]
---@return string[]
function M.fold_levels(line_count, folds)
    local depth = {}
    local starts = {}
    for line = 1, line_count do
        depth[line] = 0
    end
    for _, fold in ipairs(folds) do
        starts[fold.first] = true
        for line = fold.first, math.min(fold.last, line_count) do
            depth[line] = depth[line] + 1
        end
    end
    local levels = {}
    for line = 1, line_count do
        levels[line] = (starts[line] and ">" or "") .. depth[line]
    end
    return levels
end

---@param first_line string the text of the first line of the fold
---@param hidden_count integer the count of the lines below the first line
---@return string
function M.fold_text(first_line, hidden_count)
    local count = hidden_count == 1 and "1 line" or string.format("%d lines", hidden_count)
    if first_line:sub(1, #open_glyph) == open_glyph then
        first_line = closed_glyph .. first_line:sub(#open_glyph + 1)
    end
    return first_line .. "  " .. count
end

return M
