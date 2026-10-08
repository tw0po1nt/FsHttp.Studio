local MiniTest = require("mini.test")
local harness = require("nvim.harness")

local T = MiniTest.new_set()

T["Proven-live: the child Neovim answers, the Sidecar parses, the fixture is open, and a companion exists"] = function()
    local state = harness.proven_live_state()

    assert.equal(true, state.server_live, "the test server healthcheck passed and the Sidecar parsed")
    assert.equal(true, state.child_answers, "the child Neovim answered over RPC")
    assert.equal(true, state.client_loaded, "lazy.nvim loaded the client in the child Neovim")
    assert.equal(true, state.fixture_open, "the fixture is open as an F# buffer")
    assert.equal(true, state.companion_exists, "a companion process exists")
    assert.equal(true, harness.is_proven_live(), "Harness setup reached Proven-live")
    assert.equal(true, harness.timing_table_was_emitted(), "Harness setup emitted the timing table")
end

return T
