local MiniTest = require("mini.test")
local harness = require("nvim.harness")

-- A flaky Check is a defect to fix, so the suite runs each Check one time.
local T = MiniTest.new_set({ hooks = harness.hooks, n_retry = 1 })

T["Harness self-check"] = require("nvim.self_check")
T["start sequence"] = require("nvim.start_sequence_checks")
T["Block marks"] = require("nvim.block_mark_checks")
T[":FsHttp run"] = require("nvim.run_checks")
T["the Body of the Response buffer"] = require("nvim.body_checks")
T["the Companion archive download"] = require("nvim.download_checks")
T["the image body of the Response buffer"] = require("nvim.image_checks")
T["settings"] = require("nvim.settings_checks")
T[":FsHttp open"] = require("nvim.open_checks")
T["reference screenshots of the Response buffer"] = require("nvim.screenshot_checks")
T["version check"] = require("nvim.version_check_checks")
T["Status line text"] = require("nvim.status_line_checks")
T[":checkhealth fshttp"] = require("nvim.health_checks")
T[":help fshttp"] = require("nvim.help_checks")
T["Harness watchdog"] = require("nvim.watchdog_checks")

return T
