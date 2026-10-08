local core_env = require("lua.core_env")

describe("the Body of fshttp.response_view", function()
    local response_view = core_env.load("fshttp.response_view")

    local function result(content_type, body)
        return {
            status = 200,
            reason = "OK",
            headers = {},
            content_type = content_type,
            body = body,
            request_ms = 1,
            total_ms = 2,
            request = {
                method = "GET",
                url = "http://127.0.0.1:5000/json",
                headers = {},
                body = { state = "none", bytes = "", reason = "" },
            },
        }
    end

    -- The lines below the Body title.
    local function body_lines(view)
        for i, line in ipairs(view.lines) do
            if line:sub(1, #"▾ Body") == "▾ Body" then
                return { unpack(view.lines, i + 1) }, i
            end
        end
        error("the view has no Body section")
    end

    -- The folds that start on the Body title or below it.
    local function body_folds(view, title)
        local folds = {}
        for _, fold in ipairs(view.folds) do
            if fold.first >= title then
                folds[#folds + 1] = fold
            end
        end
        return folds
    end

    local function no_parser()
        return nil
    end

    local nested = '{"name":"snorlax","moves":["rest","snore"],"stats":{"hp":160}}'

    describe("with no parser", function()
        it("pretty-prints a JSON body, with a fold for each object and each array", function()
            local view = response_view.result(result("application/json; charset=utf-8", nested), no_parser)
            local lines, title = body_lines(view)
            assert.same({
                "{",
                '  "name": "snorlax",',
                '  "moves": [',
                '    "rest",',
                '    "snore"',
                "  ],",
                '  "stats": {',
                '    "hp": 160',
                "  }",
                "}",
            }, lines)
            assert.same({
                { first = title + 3, last = title + 6, closed = false },
                { first = title + 7, last = title + 9, closed = false },
                { first = title + 1, last = title + 10, closed = false },
                { first = title, last = title + 10, closed = false },
            }, body_folds(view, title))
        end)

        it("pretty-prints a body of each JSON type", function()
            for _, content_type in ipairs({ "application/json", "text/json", "application/problem+json" }) do
                local lines = body_lines(response_view.result(result(content_type, "[1]"), no_parser))
                assert.same({ "[", "  1", "]" }, lines, content_type)
            end
        end)

        it("pretty-prints a JSON body when the caller gives no lookup", function()
            assert.same({ "[", "  1", "]" }, body_lines(response_view.result(result("application/json", "[1]"))))
        end)

        it("keeps the exact bytes of a JSON body that does not parse, with no fold", function()
            local view = response_view.result(result("application/json", '{"a":1,\n"b":'), no_parser)
            assert.same({ '{"a":1,', '"b":' }, body_lines(view))
            assert.equal(1, #body_folds(view, select(2, body_lines(view))))
        end)

        it("keeps the exact bytes of an HTML body and an XML body", function()
            local html = "<html><body>\n<p>snorlax</p></body></html>"
            local xml = "<pokemon><name>snorlax</name>\n</pokemon>"
            for content_type, body in pairs({ ["text/html"] = html, ["application/xml"] = xml }) do
                local view = response_view.result(result(content_type, body), no_parser)
                assert.equal(body, table.concat(body_lines(view), "\n"), content_type)
                assert.equal(1, #body_folds(view, select(2, body_lines(view))), content_type)
            end
        end)
    end)

    describe("with a parser", function()
        it("asks for the language of each body type that gets highlights and folds", function()
            local cases = {
                ["application/json; charset=utf-8"] = "json",
                ["text/json"] = "json",
                ["application/problem+json"] = "json",
                ["text/html"] = "html",
                ["application/xhtml+xml"] = "html",
                ["application/xml"] = "xml",
                ["text/xml"] = "xml",
                ["application/atom+xml"] = "xml",
                ["text/plain"] = false,
                ["application/octet-stream"] = false,
                [""] = false,
            }
            for content_type, expected in pairs(cases) do
                local asked = false
                response_view.result(result(content_type, "[1]"), function(language)
                    asked = language
                    return nil
                end)
                assert.equal(expected, asked, content_type)
            end
        end)

        it("keeps the exact bytes, and puts the folds and the highlights of the parser on the body lines", function()
            local body = '{"name":\n  "snorlax"}'
            local asked
            local view = response_view.result(result("application/json", body), function(language, text)
                asked = { language, text }
                return {
                    highlights = { { line = 2, first_col = 2, last_col = 11, group = "@string.json" } },
                    folds = { { first = 1, last = 2 } },
                }
            end)
            local lines, title = body_lines(view)
            assert.same({ "json", body }, asked)
            assert.same({ '{"name":', '  "snorlax"}' }, lines)
            assert.same({
                { first = title + 1, last = title + 2, closed = false },
                { first = title, last = title + 2, closed = false },
            }, body_folds(view, title))
            assert.same(
                { line = title + 2, first_col = 2, last_col = 11, group = "@string.json" },
                view.highlights[#view.highlights]
            )
        end)

        it("asks for no language for an empty body", function()
            local asked = false
            response_view.result(result("application/json", ""), function()
                asked = true
                return nil
            end)
            assert.equal(false, asked)
        end)
    end)
end)
