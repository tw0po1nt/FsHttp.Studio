local core_env = require("lua.core_env")

describe("fshttp.open_rule", function()
    local open_rule = core_env.load("fshttp.open_rule")

    it("gives the extension of the type", function()
        assert.equal("html", open_rule.extension("text/html"))
        assert.equal("html", open_rule.extension("application/xhtml+xml"))
        assert.equal("png", open_rule.extension("image/png"))
        assert.equal("json", open_rule.extension("text/json"))
        assert.equal("json", open_rule.extension("application/problem+json"))
        assert.equal("xml", open_rule.extension("text/xml"))
        assert.equal("txt", open_rule.extension("text/plain"))
        assert.equal("bin", open_rule.extension("application/octet-stream"))
        assert.equal("bin", open_rule.extension(""))
    end)

    it("gives a hint for an HTML type and an image type only", function()
        assert.equal(
            ":FsHttp open  shows the rendered page in the browser, with scripts blocked",
            open_rule.hint("text/html")
        )
        assert.equal(":FsHttp open  shows the image in the system viewer", open_rule.hint("image/png"))
        assert.is_nil(open_rule.hint("application/json"))
    end)

    describe("the policy", function()
        local meta = '<meta http-equiv="Content-Security-Policy" content="' .. open_rule.policy .. '">'

        it("blocks each script and allows styles and images", function()
            assert.truthy(open_rule.policy:find("script-src 'none'", 1, true))
            assert.truthy(open_rule.policy:find("style-src * 'unsafe-inline'", 1, true))
            assert.truthy(open_rule.policy:find("img-src *", 1, true))
        end)

        it("goes first in the head", function()
            assert.equal(
                "<!doctype html><html><head>" .. meta .. "<script>x()</script></head></html>",
                open_rule.with_policy("<!doctype html><html><head><script>x()</script></head></html>")
            )
        end)

        it("finds a head tag in any case, with attributes", function()
            assert.equal(
                '<HTML><HEAD lang="en">' .. meta .. "</HEAD></HTML>",
                open_rule.with_policy('<HTML><HEAD lang="en"></HEAD></HTML>')
            )
        end)

        it("does not take a header tag for a head tag", function()
            assert.equal(
                "<html><head>" .. meta .. "</head><header></header></html>",
                open_rule.with_policy("<html><header></header></html>")
            )
        end)

        it("adds a head when the page has an html tag and no head", function()
            assert.equal(
                "<html><head>" .. meta .. "</head><p>a</p></html>",
                open_rule.with_policy("<html><p>a</p></html>")
            )
        end)

        it("goes after the doctype when the page has no html tag", function()
            assert.equal("<!DOCTYPE html>" .. meta .. "<p>a</p>", open_rule.with_policy("<!DOCTYPE html><p>a</p>"))
        end)

        it("goes first in a fragment", function()
            assert.equal(meta .. "<p>a</p>", open_rule.with_policy("<p>a</p>"))
        end)
    end)

    it("changes an HTML body only", function()
        assert.is_true(open_rule.file_content("text/html", "<p>a</p>") ~= "<p>a</p>")
        assert.equal("\137PNG", open_rule.file_content("image/png", "\137PNG"))
    end)
end)
