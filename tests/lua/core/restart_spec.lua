local core_env = require("lua.core_env")

describe("fshttp.restart", function()
    local restart = core_env.load("fshttp.restart")

    it("begins a Restart when no stop is in progress", function()
        local session, decision = restart.on_command(restart.initial())
        assert.equal("beginRestart", decision)
        assert.is_true(session.stopping)
    end)

    it("gives the restarting notice for a command during the stop, and starts no second stop", function()
        local stopping = restart.on_command(restart.initial())
        local session, decision = restart.on_command(stopping)
        assert.equal("notifyRestarting", decision)
        assert.same(stopping, session)
    end)

    it("begins a Restart again after the stop ends", function()
        local stopping = restart.on_command(restart.initial())
        local _, decision = restart.on_command(restart.on_stopped(stopping))
        assert.equal("beginRestart", decision)
    end)

    it("makes an event of the old companion stale as soon as the command runs", function()
        local old_epoch = restart.initial().epoch
        local session = restart.on_command(restart.initial())
        assert.is_false(restart.is_current(session, old_epoch))
        assert.is_true(restart.is_current(session, session.epoch))
    end)

    it("keeps the epoch of the new start sequence when the stop ends", function()
        local stopping = restart.on_command(restart.initial())
        assert.is_true(restart.is_current(restart.on_stopped(stopping), stopping.epoch))
    end)

    it("makes an event of the companion stale when Neovim quits", function()
        local session = restart.initial()
        assert.is_false(restart.is_current(restart.on_quit(session), session.epoch))
    end)
end)
