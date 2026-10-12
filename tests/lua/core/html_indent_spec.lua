local core_env = require("lua.core_env")

describe("fshttp.html_indent", function()
    local html_indent = core_env.load("fshttp.html_indent")

    local function lines_of(body)
        local indented = html_indent.indent(body)
        local lines = {}
        for line in (indented .. "\n"):gmatch("(.-)\n") do
            lines[#lines + 1] = line
        end
        return lines
    end

    local card =
        '<html><body><div class="card"><h1>Snorlax</h1><p>Sleeps <b>a lot</b>, see <a href="/x">more</a>.</p><ul><li>rest</li><li>snore</li></ul></div></body></html>'
    local card_lines = {
        "<html>",
        "  <body>",
        '    <div class="card">',
        "      <h1>Snorlax</h1>",
        '      <p>Sleeps <b>a lot</b>, see <a href="/x">more</a>.</p>',
        "      <ul>",
        "        <li>rest</li>",
        "        <li>snore</li>",
        "      </ul>",
        "    </div>",
        "  </body>",
        "</html>",
    }

    it("puts each block element on a new line, one step deeper than its parent", function()
        assert.same(card_lines, lines_of(card))
    end)

    it("gives a body that has an indent already the same lines as the minified body", function()
        local indented = table.concat({
            "<html>",
            "    <body>",
            '\t<div class="card">',
            "        <h1>Snorlax</h1>",
            "        <p>",
            "          Sleeps <b>a lot</b>,",
            '          see <a href="/x">more</a>.',
            "        </p>",
            "        <ul>",
            "          <li>rest</li>",
            "          <li>",
            "            snore",
            "          </li>",
            "        </ul>",
            "\t</div>",
            "    </body>",
            "</html>",
            "",
        }, "\r\n")
        assert.same(card_lines, lines_of(indented))
    end)

    it("keeps each inline element in its line of text", function()
        local body = "<p>A <em>sleepy</em> <strong>Pokémon</strong><br>that <code>snores</code>"
            .. ' <span class="x">a lot</span><img src="z.png"><sup>1</sup>.</p>'
        assert.same({
            "<p>A <em>sleepy</em> <strong>Pokémon</strong><br>that <code>snores</code>"
                .. ' <span class="x">a lot</span><img src="z.png"><sup>1</sup>.</p>',
        }, lines_of(body))
    end)

    it("puts a custom element on a new line as a block element", function()
        assert.same(
            { "<div>", "  <my-card>Snorlax</my-card>", "  <my-card>Munchlax</my-card>", "</div>" },
            lines_of("<div><my-card>Snorlax</my-card><my-card>Munchlax</my-card></div>")
        )
    end)

    it("lays out an inline element that contains a block element as a block element", function()
        assert.same(
            { "<div>", '  <a href="/x">', "    <div>card</div>", "  </a>", "</div>" },
            lines_of('<div><a href="/x"><div>card</div></a></div>')
        )
    end)

    it("keeps the content and the close tag of each raw text element byte for byte", function()
        for _, name in ipairs({ "pre", "textarea", "script", "style" }) do
            local raw = "<" .. name .. ' id="r">\n  a  <b>x</b>\n\tz </' .. name .. ">"
            assert.same({
                "<div>",
                "  <p>before</p>",
                "  <" .. name .. ' id="r">',
                "  a  <b>x</b>",
                "\tz </" .. name .. ">",
                "</div>",
            }, lines_of("<div><p>before</p>" .. raw .. "</div>"), name)
        end
    end)

    it("finds the close tag of a raw text element in any letter case, and ignores a tag inside it", function()
        assert.same(
            { "<SCRIPT>if (a < b) { x = '</div>'; }</Script>", "<p>after</p>" },
            lines_of("<SCRIPT>if (a < b) { x = '</div>'; }</Script><p>after</p>")
        )
    end)

    it("keeps the rest of the body as the content of a raw text element with no close tag", function()
        assert.same({ "<div>", "  <pre>  a", "  b</div>" }, lines_of("<div><pre>  a\n  b</div>"))
    end)

    it("puts each comment on a line of its own, with its text unchanged", function()
        assert.same({
            "<div>",
            "  <p>before</p>",
            "  <!--  a",
            "  comment  -->",
            "  <p>",
            "    one",
            "    <!-- two -->",
            "    three",
            "  </p>",
            "</div>",
        }, lines_of("<div><p>before</p><!--  a\n  comment  --><p>one <!-- two --> three</p></div>"))
    end)

    it("puts the doctype on a line of its own, with its text unchanged", function()
        assert.same(
            { "<!DOCTYPE  html>", "<html>", "  <head>", "    <title>probe</title>", "  </head>", "</html>" },
            lines_of("<!DOCTYPE  html><html><head><title>probe</title></head></html>")
        )
    end)

    it("keeps a CDATA section and each character reference unchanged", function()
        assert.same(
            { "<p>Fish &amp; chips &#169; <![CDATA[  a  <b>  ]]> &nbsp;</p>" },
            lines_of("<p>Fish &amp; chips  &#169;  <![CDATA[  a  <b>  ]]> &nbsp;</p>")
        )
    end)

    it("adds no depth for a void element", function()
        assert.same(
            {
                "<head>",
                '  <meta charset="utf-8">',
                '  <link rel="icon" href="/i.png">',
                "  <title>probe</title>",
                "</head>",
                "<div>",
                "  <p>a</p>",
                "  <hr>",
                '  <p>b<br>c<input type="text"></p>',
                "</div>",
            },
            lines_of(
                '<head><meta charset="utf-8"><link rel="icon" href="/i.png"><title>probe</title></head>'
                    .. '<div><p>a</p><hr><p>b<br>c<input type="text"></p></div>'
            )
        )
    end)

    it("adds no depth for a self-closing tag", function()
        assert.same(
            { "<div>", '  <x-icon name="a" />', "  <p>after</p>", "</div>" },
            lines_of('<div><x-icon name="a" /><p>after</p></div>')
        )
    end)

    it("keeps each line break between the attributes of a tag", function()
        assert.same({
            "<div",
            '  class="card"',
            "  data-x='a > b'>",
            "  <p>Snorlax</p>",
            "</div>",
        }, lines_of("<div\n  class=\"card\"\n  data-x='a > b'><p>Snorlax</p></div>"))
    end)

    it("closes each open element between a close tag and its matching open tag", function()
        assert.same(
            { "<div>", "  <section>", "    <p>a</p>", "</div>", "<p>after</p>" },
            lines_of("<div><section><p>a</p></div><p>after</p>")
        )
    end)

    it("keeps an element with no close tag open until the body ends", function()
        assert.same({ "<ul>", "  <li>", "    a", "    <li>b", "</ul>" }, lines_of("<ul><li>a<li>b</ul>"))
        assert.same({ "<div>", "  <p>a</p>", "  <p>b" }, lines_of("<div><p>a</p><p>b"))
    end)

    it("keeps a stray close tag, and changes no depth", function()
        assert.same({
            "<div>",
            "  <p>a</p>",
            "  </section>",
            "  <p>b</span>c</p>",
            "</div>",
        }, lines_of("<div><p>a</p></section><p>b</span>c</p></div>"))
    end)

    it("matches upper-case element names, and keeps their letter case", function()
        assert.same(
            { "<DIV>", "  <P>Sleeps <B>a lot</B></P>", "  <Ul>", "    <LI>rest</li>", "  </uL>", "</div>" },
            lines_of("<DIV><P>Sleeps <B>a lot</B></P><Ul><LI>rest</li></uL></div>")
        )
    end)

    it("collapses each run of whitespace in text to one space, and trims it at the start and end of a line", function()
        assert.same(
            { "<div>", "  <p>Sleeps <b> a lot </b> today</p>", "  some text", "</div>" },
            lines_of("<div>\n\n  <p>\n Sleeps  <b> a \t lot </b>\r\n today\n</p>  some \n text  </div>")
        )
    end)

    it("gives a body that has no tag as one line of text", function()
        assert.same({ "just text here, a < b" }, lines_of("  just   text \n here,  a < b \n"))
    end)

    it("gives a fold for each element that spans more than one line", function()
        local _, folds = html_indent.indent(card)
        assert.same(
            { { first = 6, last = 9 }, { first = 3, last = 10 }, { first = 2, last = 11 }, { first = 1, last = 12 } },
            folds
        )
    end)

    it("gives a fold for a one-line element whose tag or raw text spans more than one line", function()
        local _, folds = html_indent.indent('<p>a</p><div\n  id="x">b</div><pre>c\nd</pre><!--\n-->')
        assert.same({ { first = 2, last = 3 }, { first = 4, last = 5 } }, folds)
    end)

    it("gives a fold that ends at the last line of an element with no close tag", function()
        local _, folds = html_indent.indent("<div><p>a</p><p>b")
        assert.same({ { first = 1, last = 3 } }, folds)
    end)
end)
