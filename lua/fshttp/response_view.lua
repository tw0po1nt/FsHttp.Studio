-- The lines, the folds, the highlights, and the winbar of the Response buffer for each Run outcome.
local binary_body = require("fshttp.binary_body")
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

---@class fshttp.ResponseView
---@field lines string[]
---@field winbar string a statusline expression, or "" for no winbar
---@field folds fshttp.Fold[]
---@field highlights fshttp.Highlight[]

local open_glyph = "▾"
local closed_glyph = "▸"

---@return fshttp.ResponseView
local function new_view()
    return { lines = {}, winbar = "", folds = {}, highlights = {} }
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

-- The type in lower case, with no parameters such as charset.
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

-- Appends one line made of { text, group } chunks, and returns its 1-based line number.
---@param view fshttp.ResponseView
---@param chunks { [1]: string, [2]: string? }[]
---@return integer
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

-- A section is a title line and the lines below it. A fold holds the section when it has a line
-- below the title.
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

-- The note and the hex dump of the VSCode Response viewer.
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

---@param seconds integer
---@return fshttp.ResponseView
function M.running(seconds)
    local view = new_view()
    add(view, { { string.format("Running… %ds", seconds), "FsHttpResponseDetail" } })
    return view
end

---@param result fshttp.RunResult
---@return fshttp.ResponseView
function M.result(result)
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
        if binary_body.looks_binary(result.body) then
            add_hex_view(view, result.body, "")
        elseif #result.body > 0 then
            add_text(view, result.body)
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

-- TODO(https://github.com/tw0po1nt/FsHttp.Studio/issues/276): remove the trailing spaces, and add the
-- Compile error winbar and the <CR> jump.
-- The text of the Compile error of the VSCode Response viewer.
---@param diagnostics { message: string, range: { start_line: integer, start_col: integer } }[]
---@return fshttp.ResponseView
function M.compile_error(diagnostics)
    local view = new_view()
    add(view, { { "Compile error:", "FsHttpResponseError" } })
    for _, diagnostic in ipairs(diagnostics) do
        local position = string.format("(%d,%d)", diagnostic.range.start_line, diagnostic.range.start_col + 1)
        add_text(view, position .. " " .. diagnostic.message)
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
    return refusals.codes[code] or refusals.codes[refusals.fallback_code]
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

-- The value of 'foldexpr' for each line: ">N" where a fold starts, and the fold depth elsewhere.
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

-- A closed section shows its title and the count of the lines below the title.
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
