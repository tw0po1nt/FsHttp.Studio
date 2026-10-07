local core_env = require("lua.core_env")

local function read(path)
    local file = assert(io.open(path, "r"))
    local text = file:read("*a")
    file:close()
    return text
end

describe("the core environment", function()
    it("has no vim global", function()
        assert.is_nil(core_env.new_env().vim)
    end)

    it("reaches no vim through _G, rawget, or package", function()
        local env = core_env.new_env()
        assert.is_nil(env._G.vim)
        assert.is_nil(rawget(env._G, "vim"))
        assert.is_nil(env.package)
    end)
end)

describe("fshttp.refusals", function()
    local refusals = core_env.load("fshttp.refusals")

    it("loads with no vim global", function()
        assert.is_table(refusals.codes)
    end)

    it("ends the stale Block sentence with the Neovim command", function()
        local detail = refusals.stale_block_index.detail
        local tail = "To run this request, run :FsHttp run again."
        assert.equal(tail, detail:sub(-#tail))
    end)

    it("names no lens in the stale Block sentence", function()
        assert.is_nil(refusals.stale_block_index.detail:find("lens", 1, true))
    end)

    it("holds the two no-Block sentences", function()
        assert.equal("No requests found: this script has a syntax error.", refusals.no_blocks_parse_failure)
        assert.equal("This script has no request. Write an http { } block to run one.", refusals.no_blocks_empty)
    end)

    it("holds a Block mark title for each catalog code", function()
        for code, refusal in pairs(refusals.codes) do
            assert.equal("⊘ " .. refusal.title, refusal.block_mark_title, code)
        end
        assert.is_table(refusals.codes[refusals.fallback_code])
    end)
end)

describe("fshttp.version", function()
    it("equals the version in package.json", function()
        local version = core_env.load("fshttp.version")
        assert.equal(vim.json.decode(read("package.json")).version, version)
    end)
end)
