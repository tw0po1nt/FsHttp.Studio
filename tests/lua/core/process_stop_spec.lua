local core_env = require("lua.core_env")

describe("fshttp.process_stop", function()
    local process_stop = core_env.load("fshttp.process_stop")

    it("signals the process group of the companion on Linux and macOS", function()
        assert.same({ kind = "signal_group", pid = 4242 }, process_stop.kill_command("Linux", 4242))
        assert.same({ kind = "signal_group", pid = 4242 }, process_stop.kill_command("Darwin", 4242))
    end)

    it("runs taskkill on each process that the companion started on Windows", function()
        assert.same(
            { kind = "exec", command = { "taskkill", "/T", "/F", "/PID", "4242" } },
            process_stop.kill_command("Windows_NT", 4242)
        )
    end)

    it("spawns the companion detached on Linux and macOS only", function()
        assert.is_true(process_stop.detach_on_spawn("Linux"))
        assert.is_true(process_stop.detach_on_spawn("Darwin"))
        assert.is_false(process_stop.detach_on_spawn("Windows_NT"))
    end)

    it("waits 1000 ms before the kill", function()
        assert.equal(1000, process_stop.stop_delay_ms)
    end)
end)
