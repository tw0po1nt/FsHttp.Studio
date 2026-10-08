-- The entry point of the Neovim suite. tests/nvim/run.sh passes this file to tests/minit.lua.
local MiniTest = require("mini.test")
local harness = require("nvim.harness")

-- A flaky Check is a defect to fix, so the suite runs each Check one time.
local T = MiniTest.new_set({ hooks = harness.hooks, n_retry = 1 })

T["Harness self-check"] = require("nvim.self_check")
T["start sequence"] = require("nvim.start_sequence_checks")

return T
