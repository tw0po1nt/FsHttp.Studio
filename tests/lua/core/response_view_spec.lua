local core_env = require("lua.core_env")

describe("fshttp.response_view", function()
    local response_view = core_env.load("fshttp.response_view")
    local refusals = core_env.load("fshttp.refusals")

    -- The text that a statusline expression shows, with each highlight item removed.
    local function shown(winbar)
        return (winbar:gsub("%%#[^#]*#", ""):gsub("%%%*", ""):gsub("%%<", ""):gsub("%%%%", "%%"))
    end

    local function result(overrides)
        local value = {
            status = 200,
            reason = "OK",
            headers = {
                { name = "Content-Type", value = "application/json" },
                { name = "Content-Length", value = "26" },
            },
            content_type = "application/json; charset=utf-8",
            body = '{"probe":"ui-test-server"}',
            request_ms = 293.6,
            total_ms = 336.2,
            request = {
                method = "GET",
                url = "http://127.0.0.1:5000/json",
                headers = { { name = "Accept", value = "*/*" } },
                body = { state = "none", bytes = "", reason = "" },
            },
        }
        for key, override in pairs(overrides or {}) do
            value[key] = override
        end
        return value
    end

    describe("for a response", function()
        it("puts the Request, the Response headers, and the Body in this order", function()
            assert.same({
                "▾ Request",
                "  GET http://127.0.0.1:5000/json",
                "  Accept: */*",
                "▾ Response headers  (2)",
                "  Content-Type: application/json",
                "  Content-Length: 26",
                "▾ Body  application/json · 26 B",
                "{",
                '  "probe": "ui-test-server"',
                "}",
            }, response_view.result(result()).lines)
        end)

        it("closes the Request fold and the Response headers fold, and opens the Body fold", function()
            assert.same({
                { first = 1, last = 3, closed = true },
                { first = 4, last = 6, closed = true },
                { first = 8, last = 10, closed = false },
                { first = 7, last = 10, closed = false },
            }, response_view.result(result()).folds)
        end)

        it("shows the status, the two times, the size, the method, and the URL in the winbar", function()
            local winbar = response_view.result(result()).winbar
            assert.equal(" 200 OK  294 ms · 336 ms total  26 B  GET http://127.0.0.1:5000/json", shown(winbar))
        end)

        it("cuts the start of the URL when the winbar is too narrow", function()
            local winbar = response_view.result(result()).winbar
            assert.equal("%<%#FsHttpResponseUrl#http://127.0.0.1:5000/json%*", winbar:match("%%<.*$"))
        end)

        it("escapes a percent sign in the URL of the winbar", function()
            local request = result().request
            request.url = "http://127.0.0.1:5000/a%20b"
            local winbar = response_view.result(result({ request = request })).winbar
            assert.truthy(winbar:find("/a%%20b", 1, true))
        end)

        it("colors the status by its class", function()
            local winbar = response_view.result(result({ status = 404, reason = "Not Found" })).winbar
            assert.truthy(winbar:find("%#FsHttpResponseStatus4xx# 404 Not Found%*", 1, true))
        end)

        it("keeps the exact bytes of the body", function()
            local body = "line one\r\n  line two\t\n\nend\n"
            local view = response_view.result(result({ body = body, content_type = "text/plain" }))
            local body_lines = {}
            for i = 8, #view.lines do
                body_lines[#body_lines + 1] = view.lines[i]
            end
            assert.equal(body, table.concat(body_lines, "\n"))
        end)

        it("gives an empty body no fold", function()
            local view = response_view.result(result({ body = "", content_type = "" }))
            assert.equal("▾ Body  0 B", view.lines[#view.lines])
            assert.equal(2, #view.folds)
        end)

        it("shows the Captured body and its size in the Request section", function()
            local request = result().request
            request.method = "POST"
            request.body = { state = "captured", bytes = '{"posted":"request-section-fixture"}', reason = "" }
            local view = response_view.result(result({ request = request }))
            assert.same({
                "▾ Request  (36 B)",
                "  POST http://127.0.0.1:5000/json",
                "  Accept: */*",
                "",
                '  {"posted":"request-section-fixture"}',
            }, { unpack(view.lines, 1, 5) })
            assert.same({ first = 1, last = 5, closed = true }, view.folds[1])
        end)

        it("shows the reason for a Captured body that the companion did not read", function()
            local request = result().request
            request.body = { state = "notCaptured", bytes = "", reason = "The body is a stream." }
            local view = response_view.result(result({ request = request }))
            assert.same(
                { "▾ Request", "  GET http://127.0.0.1:5000/json", "  Accept: */*", "", "  The body is a stream." },
                {
                    unpack(view.lines, 1, 5),
                }
            )
            local reason_highlights = vim.tbl_filter(function(highlight)
                return highlight.line == 5
            end, view.highlights)
            assert.same(
                { { line = 5, first_col = 2, last_col = 2 + #"The body is a stream.", group = "FsHttpResponseDetail" } },
                reason_highlights
            )
        end)

        it("shows a binary body as the note and the hex dump of the VSCode Response viewer", function()
            local body = "\0\1\2\3\4\5\6\7\8\9\10\11\12\13\14\15\16\17PK"
            local view = response_view.result(result({ body = body, content_type = "application/octet-stream" }))
            assert.same({
                "▾ Body  application/octet-stream · 20 B",
                "Binary body: 20 B",
                "00000000  00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f  ................",
                "00000010  10 11 50 4b                                      ..PK",
            }, { unpack(view.lines, 7) })
            assert.same({ first = 7, last = 10, closed = false }, view.folds[3])
            assert.same(
                { line = 8, first_col = 0, last_col = #"Binary body: 20 B", group = "FsHttpResponseDetail" },
                view.highlights[#view.highlights]
            )
        end)

        describe("with an HTML body", function()
            it("puts the open hint below the Body title", function()
                local view = response_view.result(result({ body = "<p>hi</p>", content_type = "text/html" }))
                assert.same({
                    {
                        line = 7,
                        text = ":FsHttp open  shows the rendered page in the browser, with scripts blocked",
                    },
                }, view.hints)
            end)

            it("adds no hint for an empty body", function()
                assert.same({}, response_view.result(result({ body = "", content_type = "text/html" })).hints)
            end)
        end)

        it("adds no hint for a JSON body", function()
            assert.same({}, response_view.result(result()).hints)
        end)

        describe("with an image body", function()
            local png = "\137PNG\r\n\26\n\0\0\0\13IHDR\0\0\0\100\0\0\0\100" .. string.rep("\0", 10)
            local image = result({ body = png, content_type = "image/png" })

            it("shows the pixel size in place of the hex dump, and keeps the image to place", function()
                local view = response_view.result(image, nil, function()
                    return nil
                end)
                assert.same({ "100×100 px" }, { unpack(view.lines, 8) })
                assert.same({ line = 8, content_type = "image/png", bytes = png }, view.image)
            end)

            it("adds the reason, and keeps no image to place", function()
                local view = response_view.result(image, nil, function()
                    return "the terminal does not support images"
                end)
                assert.same({ "100×100 px  the terminal does not support images" }, { unpack(view.lines, 8) })
                assert.is_nil(view.image)
            end)

            it("reports that snacks.nvim is missing when the client gives no lookup", function()
                local view = response_view.result(image)
                assert.same({ "100×100 px  snacks.nvim is not installed" }, { unpack(view.lines, 8) })
                assert.is_nil(view.image)
            end)

            it("puts the open hint below the Body title", function()
                local view = response_view.result(image)
                assert.same({ { line = 7, text = ":FsHttp open  shows the image in the system viewer" } }, view.hints)
            end)
        end)

        it("shows a body with no NUL byte and few control bytes as text", function()
            local view = response_view.result(result({ body = "a\tb\r\n\27[0m", content_type = "text/plain" }))
            assert.same({ "a\tb\r", "\27[0m" }, { unpack(view.lines, 8) })
        end)

        it("shows a binary Captured body as the hex view in the Request section", function()
            local request = result().request
            request.method = "POST"
            request.body = { state = "captured", bytes = "\0\1\2\255\0\128", reason = "" }
            local view = response_view.result(result({ request = request }))
            assert.same({
                "▾ Request  (6 B)",
                "  POST http://127.0.0.1:5000/json",
                "  Accept: */*",
                "",
                "  Binary body: 6 B",
                "  00000000  00 01 02 ff 00 80                                ......",
            }, { unpack(view.lines, 1, 6) })
            assert.same({ first = 1, last = 6, closed = true }, view.folds[1])
        end)
    end)

    it("shows a Run in progress with its seconds", function()
        local view = response_view.running(3)
        assert.same({ "Running… 3s" }, view.lines)
        assert.equal("", view.winbar)
        assert.same({}, view.folds)
    end)

    it("shows a Runtime error as the text of the VSCode Response viewer", function()
        local view = response_view.runtime_error("Connection refused (127.0.0.1:9)\nat Send")
        assert.same({ "Runtime error: Connection refused (127.0.0.1:9)", "at Send" }, view.lines)
        assert.equal("", view.winbar)
    end)

    it("shows a Compile error as the text of the VSCode Response viewer", function()
        local view = response_view.compile_error({
            { message = "The value 'x' is not defined.", range = { start_line = 12, start_col = 4 } },
        })
        assert.same({ "Compile error:", "(12,5) The value 'x' is not defined." }, view.lines)
    end)

    it("removes the trailing spaces from each line of a Compile error", function()
        local view = response_view.compile_error({
            { message = "First line   \n  second line  ", range = { start_line = 3, start_col = 0 } },
        })
        assert.same({ "Compile error:", "(3,1) First line", "  second line" }, view.lines)
    end)

    it("gives a Compile error the winbar that names the <CR> jump, and the position of each (line,col) line", function()
        local view = response_view.compile_error({
            { message = "one\ntwo", range = { start_line = 12, start_col = 4 } },
            { message = "three", range = { start_line = 20, start_col = 0 } },
        })
        local plain = view.winbar:gsub("%%#[^#]*#", ""):gsub("%%%*", "")
        assert.equal("Compile error  <CR> on a (line,col) moves to it in the script", plain)
        assert.same({ [2] = { line = 12, col = 4 }, [4] = { line = 20, col = 0 } }, view.positions)
    end)

    it("shows a Refused Run for a value that another Block binds with its title and its detail", function()
        local view = response_view.refused("unboundBlockValue", "dexId")
        local detail = refusals.unbound_block_value.detail:gsub("{name}", "dexId")
        assert.same({ "Cannot run: depends on another request", "", detail }, view.lines)
        assert.falsy(view.lines[3]:find("{name}", 1, true))
    end)

    it("shows the Neovim sentence for a stale Block index", function()
        local view = response_view.refused("staleBlockIndex", nil)
        assert.same({ refusals.stale_block_index.title, "", refusals.stale_block_index.detail }, view.lines)
    end)

    it("shows a refused position with the title and the detail of its Refusal code", function()
        local view = response_view.refused("loopBody", nil)
        assert.same({ refusals.codes.loopBody.title, "", refusals.codes.loopBody.detail }, view.lines)
    end)

    it("gives each line of a fold its fold level", function()
        local levels = response_view.fold_levels(6, {
            { first = 1, last = 2, closed = true },
            { first = 3, last = 6, closed = false },
            { first = 4, last = 5, closed = false },
        })
        assert.same({ ">1", "1", ">1", ">2", "2", "1" }, levels)
    end)

    it("shows a closed section with its title and its line count", function()
        assert.equal("▸ Request  3 lines", response_view.fold_text("▾ Request", 3))
        assert.equal("▸ Response headers  (26)  26 lines", response_view.fold_text("▾ Response headers  (26)", 26))
        assert.equal("▸ Body  text/plain · 2 B  1 line", response_view.fold_text("▾ Body  text/plain · 2 B", 1))
    end)

    it("gives the size in the units of the VSCode status line", function()
        assert.equal("0 B", response_view.human_size(0))
        assert.equal("1023 B", response_view.human_size(1023))
        assert.equal("1.0 KB", response_view.human_size(1024))
        assert.equal("6.2 KB", response_view.human_size(6349))
        assert.equal("2.0 MB", response_view.human_size(2 * 1024 * 1024))
    end)
end)
