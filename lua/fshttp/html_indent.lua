local M = {}

local function set(names)
    local found = {}
    for _, name in ipairs(names) do
        found[name] = true
    end
    return found
end

local inline_elements = set({
    "a",
    "abbr",
    "b",
    "bdi",
    "bdo",
    "br",
    "button",
    "cite",
    "code",
    "data",
    "del",
    "dfn",
    "em",
    "i",
    "img",
    "input",
    "ins",
    "kbd",
    "label",
    "mark",
    "meter",
    "output",
    "picture",
    "progress",
    "q",
    "s",
    "samp",
    "select",
    "small",
    "span",
    "strong",
    "sub",
    "sup",
    "svg",
    "time",
    "u",
    "var",
    "wbr",
})

local void_elements =
    set({ "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr" })

local raw_text_elements = set({ "pre", "textarea", "script", "style" })

---@class fshttp.HtmlNode
---@field kind "element"|"raw"|"text"|"cdata"|"own_line"|"stray"
---@field name string? the lower-case element name
---@field text string? the exact bytes of a text, cdata, own_line, or stray node
---@field open string? the exact bytes of the open tag
---@field close string? the exact bytes of the close tag, nil when the body has none
---@field content string? the exact bytes between the open tag and the close tag of a raw node
---@field children fshttp.HtmlNode[]?
---@field block boolean? the cached answer of `is_block`

---@param text string
---@param pos integer the position of `<`
---@return integer stop the position of `>`, or the end of the text when the tag has no `>`
local function tag_end(text, pos)
    local i = pos + 1
    while true do
        local found = text:find("[>=]", i)
        if not found then
            return #text
        elseif text:sub(found, found) == ">" then
            return found
        end
        local value = text:find("[^ \t\r\n]", found + 1)
        if not value then
            return #text
        end
        local quote = text:sub(value, value)
        if quote == '"' or quote == "'" then
            local close = text:find(quote, value + 1, true)
            if not close then
                return #text
            end
            i = close + 1
        else
            i = value
        end
    end
end

---@param text string
---@param pos integer
---@param terminator string
---@return integer stop the position of the last byte of `terminator`, or the end of the text
local function find_end(text, pos, terminator)
    local _, stop = text:find(terminator, pos, true)
    return stop or #text
end

---@param text string
---@return fshttp.HtmlNode root
local function parse(text)
    local lower = text:lower()
    local root = { kind = "element", children = {} }
    local stack = { root }

    local function add(node)
        local children = stack[#stack].children
        children[#children + 1] = node
    end

    local function add_text(piece)
        local children = stack[#stack].children
        local last = children[#children]
        if last and last.kind == "text" then
            last.text = last.text .. piece
        else
            add({ kind = "text", text = piece })
        end
    end

    local pos = 1
    while pos <= #text do
        local lt = text:find("<", pos, true)
        if not lt then
            add_text(text:sub(pos))
            break
        elseif lt > pos then
            add_text(text:sub(pos, lt - 1))
        end
        local open_name = text:match("^<([%a][^%s/>]*)", lt)
        local close_name = text:match("^</([%a][^%s/>]*)", lt)
        if text:sub(lt, lt + 3) == "<!--" then
            local stop = find_end(text, lt + 4, "-->")
            add({ kind = "own_line", text = text:sub(lt, stop) })
            pos = stop + 1
        elseif text:sub(lt, lt + 8) == "<![CDATA[" then
            local stop = find_end(text, lt + 9, "]]>")
            add({ kind = "cdata", text = text:sub(lt, stop) })
            pos = stop + 1
        elseif text:match("^<[!?]", lt) then
            local stop = find_end(text, lt + 2, ">")
            add({ kind = "own_line", text = text:sub(lt, stop) })
            pos = stop + 1
        elseif open_name then
            local name = open_name:lower()
            local stop = tag_end(text, lt)
            local open = text:sub(lt, stop)
            local self_closing = open:sub(-2) == "/>"
            pos = stop + 1
            if self_closing or void_elements[name] then
                add({ kind = "element", name = name, open = open, children = {} })
            elseif raw_text_elements[name] then
                local close_start = lower:find("</" .. name .. "[%s/>]", pos) or lower:find("</" .. name .. "$", pos)
                local node = { kind = "raw", name = name, open = open }
                if close_start then
                    local close_stop = find_end(text, close_start, ">")
                    node.content = text:sub(pos, close_start - 1)
                    node.close = text:sub(close_start, close_stop)
                    pos = close_stop + 1
                else
                    node.content = text:sub(pos)
                    pos = #text + 1
                end
                add(node)
            else
                local node = { kind = "element", name = name, open = open, children = {} }
                add(node)
                stack[#stack + 1] = node
            end
        elseif close_name then
            local name = close_name:lower()
            local stop = find_end(text, lt, ">")
            local close = text:sub(lt, stop)
            pos = stop + 1
            local match
            for depth = #stack, 2, -1 do
                if stack[depth].name == name then
                    match = depth
                    break
                end
            end
            if match then
                stack[match].close = close
                for depth = #stack, match, -1 do
                    stack[depth] = nil
                end
            else
                add({ kind = "stray", name = name, text = close })
            end
        else
            add_text("<")
            pos = lt + 1
        end
    end
    return root
end

---@param node fshttp.HtmlNode
---@return boolean
local function is_block(node)
    if node.block == nil then
        if node.kind == "text" or node.kind == "cdata" then
            node.block = false
        elseif node.kind == "own_line" or node.kind == "raw" or not inline_elements[node.name] then
            node.block = true
        else
            node.block = false
            for _, child in ipairs(node.children or {}) do
                if is_block(child) then
                    node.block = true
                    break
                end
            end
        end
    end
    return node.block
end

---@param element fshttp.HtmlNode
---@return boolean
local function has_block_child(element)
    for _, child in ipairs(element.children) do
        if is_block(child) then
            return true
        end
    end
    return false
end

---@param node fshttp.HtmlNode a node with no block node inside it
---@param pieces string[]
local function add_inline(node, pieces)
    if node.kind == "text" then
        pieces[#pieces + 1] = node.text:gsub("[ \t\r\n]+", " ")
    elseif node.kind == "element" then
        pieces[#pieces + 1] = node.open
        for _, child in ipairs(node.children) do
            add_inline(child, pieces)
        end
        pieces[#pieces + 1] = node.close
    else
        pieces[#pieces + 1] = node.text
    end
end

---@param text string
---@return string
local function trim(text)
    return (text:gsub("^ +", ""):gsub(" +$", ""))
end

---@class fshttp.HtmlIndent
---@field lines string[] an entry can contain the line breaks of a tag or of raw text
---@field line integer the count of lines in `lines`, which includes the line breaks inside each entry
---@field folds { first: integer, last: integer }[]
---@field pieces string[] the inline pieces of the line in progress

---@param state fshttp.HtmlIndent
---@param depth integer
---@param text string
local function add_line(state, depth, text)
    state.lines[#state.lines + 1] = string.rep("  ", depth) .. text
    state.line = state.line + 1 + select(2, text:gsub("\n", ""))
end

---@param state fshttp.HtmlIndent
---@param depth integer
local function end_line(state, depth)
    local text = trim(table.concat(state.pieces))
    state.pieces = {}
    if text ~= "" then
        add_line(state, depth, text)
    end
end

---@param state fshttp.HtmlIndent
---@param first integer the line of the open tag
local function add_fold(state, first)
    if state.line > first then
        state.folds[#state.folds + 1] = { first = first, last = state.line }
    end
end

local add_children

---@param state fshttp.HtmlIndent
---@param node fshttp.HtmlNode
---@param depth integer
local function add_block(state, node, depth)
    end_line(state, depth)
    local first = state.line + 1
    if node.kind == "own_line" or node.kind == "stray" then
        add_line(state, depth, node.text)
    elseif node.kind == "raw" then
        add_line(state, depth, node.open .. node.content .. (node.close or ""))
        add_fold(state, first)
    elseif has_block_child(node) then
        add_line(state, depth, node.open)
        add_children(state, node.children, depth + 1)
        if node.close then
            add_line(state, depth, node.close)
        end
        add_fold(state, first)
    else
        local pieces = {}
        for _, child in ipairs(node.children) do
            add_inline(child, pieces)
        end
        add_line(state, depth, node.open .. trim(table.concat(pieces)) .. (node.close or ""))
        add_fold(state, first)
    end
end

---@param state fshttp.HtmlIndent
---@param children fshttp.HtmlNode[]
---@param depth integer
add_children = function(state, children, depth)
    for _, child in ipairs(children) do
        if is_block(child) then
            add_block(state, child, depth)
        else
            add_inline(child, state.pieces)
        end
    end
    end_line(state, depth)
end

-- Each tag, each comment, the doctype, and the content of a raw text element keep their exact bytes.
---@param text string
---@return string indented
---@return { first: integer, last: integer }[] folds
function M.indent(text)
    local state = { lines = {}, line = 0, folds = {}, pieces = {} }
    add_children(state, parse(text).children, 0)
    return table.concat(state.lines, "\n"), state.folds
end

return M
