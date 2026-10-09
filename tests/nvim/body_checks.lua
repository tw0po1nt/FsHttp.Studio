local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

local root = vim.uv.cwd()
local queries_dir = root .. "/tests/nvim/treesitter"
local fixture = harness.fixture("json-body.fsx")
local block_line = 25

-- Cross-process contract for `GET /nested-json`. It must match `UiTestServer.Server.nestedJsonBody`.
local nested_json_lines = {
    '{"name": "snorlax",',
    ' "moves": [',
    '  "rest",',
    '  "snore"',
    " ],",
    ' "stats": {"hp": 160,',
    '  "speed": 30}}',
}

---@return string
local function json_parser_path()
    local path = vim.env.NVIM_TEST_JSON_PARSER
    if path == nil or path == "" then
        error("NVIM_TEST_JSON_PARSER is not set, so the Check cannot find the JSON parser. Run tests/nvim/run.sh.", 0)
    end
    return path
end

---@class nvim_suite.ResponseBody
---@field lines string[] the lines below the Body title
---@field folds string[] each fold of the body as "<first>-<last>", with lines that count from the first body line
---@field highlights string[] each highlight of the body as "<line>:<first col>-<last col> <group>"

---@param child nvim_suite.Child
---@return nvim_suite.ResponseBody?
local function response_body(child)
    return harness.lua_get(
        child,
        [[(function()
            local buf
            for _, candidate in ipairs(vim.api.nvim_list_bufs()) do
                if vim.api.nvim_buf_is_valid(candidate) and vim.bo[candidate].filetype == "fshttp_response" then
                    buf = candidate
                end
            end
            local win = buf and vim.fn.win_findbuf(buf)[1]
            if not win then
                return nil
            end
            local lines = vim.api.nvim_buf_get_lines(buf, 0, -1, false)
            local title
            for i, line in ipairs(lines) do
                if line:sub(1, #"▾ Body") == "▾ Body" then
                    title = i
                end
            end
            if not title then
                return nil
            end
            local body = { lines = { unpack(lines, title + 1) }, folds = {}, highlights = {} }
            local seen = {}
            vim.api.nvim_win_call(win, function()
                for line = title + 1, #lines do
                    if pcall(vim.cmd, line .. "foldclose") then
                        local fold = string.format(
                            "%d-%d",
                            vim.fn.foldclosed(line) - title,
                            vim.fn.foldclosedend(line) - title
                        )
                        vim.cmd(line .. "foldopen")
                        if not seen[fold] then
                            seen[fold] = true
                            body.folds[#body.folds + 1] = fold
                        end
                    end
                end
            end)
            table.sort(body.folds)
            local namespace = vim.api.nvim_get_namespaces()["fshttp.response_buffer"]
            for _, extmark in ipairs(vim.api.nvim_buf_get_extmarks(buf, namespace, { title, 0 }, -1, { details = true })) do
                local details = extmark[4]
                body.highlights[#body.highlights + 1] = string.format(
                    "%d:%d-%d %s",
                    extmark[2] + 1 - title,
                    extmark[3],
                    details.end_col,
                    details.hl_group
                )
            end
            return body
        end)()]]
    )
end

---@param child nvim_suite.Child
---@param expected_lines string[]
---@return nvim_suite.ResponseBody
local function run_json_block(child, expected_lines)
    harness.edit(child, fixture)
    harness.await_block_mark(child, block_line)

    harness.run_at(child, block_line)

    local body
    local ok, err = pcall(harness.eventually, harness.response_deadline_ms, "the JSON body in the Body", function()
        body = response_body(child)
        return type(body) == "table" and vim.deep_equal(body.lines, expected_lines)
    end)
    if not ok then
        error(string.format("%s\nlast Body:\n%s", err, vim.inspect(body)), 0)
    end
    return body
end

T["a JSON body with no parser shows pretty-printed, with folds from its structure"] = function()
    local child = harness.harness_setup_child()
    assert.equal(
        false,
        harness.lua_get(child, [[not not vim.treesitter.language.add("json")]]),
        "the child Neovim has no JSON parser"
    )

    local body = run_json_block(child, {
        "{",
        '  "name": "snorlax",',
        '  "moves": [',
        '    "rest",',
        '    "snore"',
        "  ],",
        '  "stats": {',
        '    "hp": 160,',
        '    "speed": 30',
        "  }",
        "}",
    })

    assert.same({ "1-11", "3-6", "7-10" }, body.folds)
    assert.same({}, body.highlights)
end

T["a JSON body with the parser keeps its bytes, with tree-sitter folds and highlights"] = function()
    local child = harness.start_child({ companion_path = harness.companion_path() })
    harness.lua_get(
        child,
        [[(function(parser, queries)
            vim.opt.runtimepath:append(queries)
            return vim.treesitter.language.add("json", { path = parser })
        end)(...)]],
        { json_parser_path(), queries_dir }
    )

    local body = run_json_block(child, nested_json_lines)

    assert.same({ "1-7", "2-5", "6-7" }, body.folds)
    assert.equal(true, vim.tbl_contains(body.highlights, "1:1-7 @property.json"), vim.inspect(body.highlights))
    assert.equal(true, vim.tbl_contains(body.highlights, "6:17-20 @number.json"), vim.inspect(body.highlights))
end

return T
