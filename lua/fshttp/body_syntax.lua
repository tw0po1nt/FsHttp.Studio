-- The highlights and the folds of a body, from the tree-sitter parser of its language.
local M = {}

-- The highlighter of Neovim gives no color to these captures.
local uncolored_captures = { spell = true, nospell = true, conceal = true }

---@param query vim.treesitter.Query
---@param root TSNode
---@param text string
---@param lines string[]
---@param language string
---@return fshttp.Highlight[]
local function highlights(query, root, text, lines, language)
    local found = {}
    for id, node in query:iter_captures(root, text) do
        local name = query.captures[id]
        if name:sub(1, 1) ~= "_" and not uncolored_captures[name] then
            local group = "@" .. name .. "." .. language
            local first_row, first_col, last_row, last_col = node:range()
            for row = first_row, last_row do
                local from = row == first_row and first_col or 0
                local to = row == last_row and last_col or #(lines[row + 1] or "")
                if to > from then
                    found[#found + 1] = { line = row + 1, first_col = from, last_col = to, group = group }
                end
            end
        end
    end
    return found
end

-- A node that ends at column 0 ends on the line above. A node on one line gives no fold.
---@param query vim.treesitter.Query
---@param root TSNode
---@param text string
---@return { first: integer, last: integer }[]
local function folds(query, root, text)
    local found = {}
    local seen = {}
    for id, node in query:iter_captures(root, text) do
        if query.captures[id] == "fold" then
            local first_row, _, last_row, last_col = node:range()
            if last_col == 0 and last_row > first_row then
                last_row = last_row - 1
            end
            local key = first_row .. ":" .. last_row
            if last_row > first_row and not seen[key] then
                seen[key] = true
                found[#found + 1] = { first = first_row + 1, last = last_row + 1 }
            end
        end
    end
    return found
end

---@param language string
---@param text string
---@return fshttp.BodySyntax
local function parse(language, text)
    local root = vim.treesitter.get_string_parser(text, language):parse()[1]:root()
    local lines = vim.split(text, "\n", { plain = true })
    local highlights_query = vim.treesitter.query.get(language, "highlights")
    local folds_query = vim.treesitter.query.get(language, "folds")
    return {
        highlights = highlights_query and highlights(highlights_query, root, text, lines, language) or {},
        folds = folds_query and folds(folds_query, root, text) or {},
    }
end

-- Gives nil when Neovim has no parser for the language, or when the parser or a query fails.
---@type fshttp.BodySyntaxLookup
function M.parse(language, text)
    local loaded, added = pcall(vim.treesitter.language.add, language)
    if not loaded or not added then
        return nil
    end
    local ok, syntax = pcall(parse, language, text)
    return ok and syntax or nil
end

return M
