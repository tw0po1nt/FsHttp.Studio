local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "rb"))
    local bytes = file:read("*a")
    file:close()
    return bytes
end

describe("fshttp.status_line_text", function()
    local status_line_text = core_env.load("fshttp.status_line_text")
    local json = core_env.load("fshttp.json")
    local bytes = read("tests/golden/status/status-text.json")
    local golden_fixture = json.decode(bytes)

    local client_version = "0.3.0"

    local function view_of(case)
        if case.view == "script" then
            return { kind = "script", blocks = case.blocks, parse_failed = case.parseFailed }
        end
        return { kind = case.view }
    end

    local function text_of(case)
        local text = status_line_text.text(case.state, view_of(case), client_version)
        if text == nil then
            return json.null
        end
        return text
    end

    local function name_of(case)
        return string.format("%s, %s %s %s", case.state, case.view, tostring(case.blocks), tostring(case.parseFailed))
    end

    it("gives each state and script view the text of the Golden fixture", function()
        for _, case in ipairs(golden_fixture.cases) do
            assert.equal(case.text, text_of(case), name_of(case))
        end
    end)

    it("writes the Status line text Golden fixture byte for byte", function()
        local cases = {}
        for i, case in ipairs(golden_fixture.cases) do
            cases[i] = json.object({
                { "blocks", case.blocks },
                { "parseFailed", case.parseFailed },
                { "state", case.state },
                { "text", text_of(case) },
                { "view", case.view },
            })
        end
        assert.equal(bytes, json.encode(json.object({ { "cases", json.array(cases) } })))
    end)

    local script = { kind = "script", blocks = 2, parse_failed = false }

    it("gives the download in progress its row, which outranks the script view", function()
        assert.equal("downloading companion…", status_line_text.text("downloading", script, client_version))
    end)

    it("gives a failed download its row", function()
        assert.equal("companion download failed", status_line_text.text("downloadFailed", script, client_version))
    end)

    it("names the client version when no release has it", function()
        assert.equal("no companion for v0.3.0", status_line_text.text("noRelease", script, client_version))
    end)

    it("gives a companion_path with no companion its row", function()
        assert.equal("companion not found", status_line_text.text("companionNotFound", script, client_version))
    end)

    it("hides each row that only Neovim has in a buffer that is not F#", function()
        for _, state in ipairs({ "downloading", "downloadFailed", "noRelease", "companionNotFound" }) do
            assert.equal(nil, status_line_text.text(state, { kind = "noFSharpDocument" }, client_version), state)
        end
    end)

    it("gives a companion that the start sequence has not started its row", function()
        assert.equal("companion not started", status_line_text.text(nil, script, client_version))
    end)

    it("starts each row with the prefix", function()
        assert.equal("FsHttp.Studio: 2 requests", status_line_text.row("ready", script, client_version))
        assert.is_nil(status_line_text.row("ready", { kind = "noFSharpDocument" }, client_version))
    end)

    it("gives the companion state row for each state", function()
        local rows = {
            ready = "FsHttp.Studio: companion ready",
            starting = "FsHttp.Studio: starting…",
            sdkNotFound = "FsHttp.Studio: .NET SDK not found",
            stopped = "FsHttp.Studio: companion stopped",
            downloading = "FsHttp.Studio: downloading companion…",
            downloadFailed = "FsHttp.Studio: companion download failed",
            noRelease = "FsHttp.Studio: no companion for v0.3.0",
            companionNotFound = "FsHttp.Studio: companion not found",
        }
        for state, row in pairs(rows) do
            assert.equal(row, status_line_text.state_row(state, client_version), state)
        end
        assert.equal("FsHttp.Studio: companion not started", status_line_text.state_row(nil, client_version))
    end)
end)
