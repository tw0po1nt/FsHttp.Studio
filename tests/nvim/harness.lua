-- A Check defines no wait and no Budget of its own.
local MiniTest = require("mini.test")

local M = {}

local sysname = vim.uv.os_uname().sysname
local is_windows = sysname == "Windows_NT"

-- Each Budget comes from the timing table in its CI job summary: a suite Budget is the slowest suite plus 40%.
local budgets_ms = {
    Linux = { harness_setup = 60000, check = 30000, suite = 255000 },
    Darwin = { harness_setup = 60000, check = 30000, suite = 265000 },
    Windows_NT = { harness_setup = 60000, check = 30000, suite = 315000 },
}
local budget_ms = budgets_ms[sysname] or budgets_ms.Linux
M.harness_setup_budget_ms = budget_ms.harness_setup
M.check_budget_ms = budget_ms.check
M.suite_budget_ms = budget_ms.suite

M.sidecar_deadline_ms = 30000
M.companion_exists_deadline_ms = 30000
M.companion_gone_deadline_ms = 15000
M.notice_deadline_ms = 15000
M.block_mark_deadline_ms = 15000
M.status_line_text_deadline_ms = 15000
M.response_deadline_ms = 30000

-- A child Neovim that gives no answer to one request in this time is hung, and the Harness stops it.
M.child_answer_deadline_ms = 10000

-- How long a claim that a thing does not occur must stay true before the Check believes it.
M.absence_settle_ms = 3000

M.poll_interval_ms = 100

-- Cross-process contract for `GET /json`. It must match `UiTestServer.Server.jsonProbeBody`.
M.json_probe_body = '{"probe":"ui-test-server"}'
-- The child Neovim has no JSON parser, so the Body shows the probe body pretty-printed. This is one line of it.
M.json_probe_body_line = '  "probe": "ui-test-server"'

local root = vim.uv.cwd()
local suite_dir = root .. "/tests/nvim"
local fixtures_dir = suite_dir .. "/fixtures"
local ui_fixtures_dir = root .. "/tests/ui.Tests/fixtures"
local sidecar_path = fixtures_dir .. "/sidecar.json"
local child_init = suite_dir .. "/child_init.lua"

-- curl exits with this code when nothing accepts the connection.
local curl_could_not_connect = 7

local shell_timeout_ms = 30000

---@return number
local function now()
    return vim.uv.hrtime() / 1e6
end

---@param cause string
local function fail_harness_setup(cause)
    error("Harness setup failed: " .. cause, 0)
end

---@param name string
---@param purpose string
---@return string
local function required_env(name, purpose)
    local value = vim.env[name]
    if value == nil or value == "" then
        fail_harness_setup(
            string.format("%s is not set, so the Harness cannot find %s. Run tests/nvim/run.sh.", name, purpose)
        )
    end
    ---@cast value string
    return value
end

---@return string
function M.companion_path()
    return required_env("NVIM_TEST_COMPANION_PATH", "the companion")
end

---@return string
function M.archive_dir()
    return required_env("NVIM_TEST_ARCHIVE_DIR", "the Companion archive from scripts/pack-companion.sh")
end

---@param name string
---@return string
function M.fixture(name)
    return fixtures_dir .. "/" .. name
end

-- A ported Check opens the same Script as its UI suite Check.
---@param name string
---@return string
function M.ui_fixture(name)
    return ui_fixtures_dir .. "/" .. name
end

---@param path string
---@return string?
local function read_file(path)
    local file = io.open(path, "rb")
    if not file then
        return nil
    end
    local text = file:read("*a")
    file:close()
    return text
end

---@param cmd string[]
---@return vim.SystemCompleted?
local function run(cmd)
    local ok, process = pcall(vim.system, cmd, { text = true, timeout = shell_timeout_ms })
    if not ok then
        return nil
    end
    return process:wait()
end

---@param timeout_ms integer
---@param subject string the thing that the wait expects, which a timeout names
---@param predicate fun(): boolean
function M.eventually(timeout_ms, subject, predicate)
    local deadline = now() + timeout_ms
    while not predicate() do
        if now() >= deadline then
            error(string.format("Timed out after %d ms waiting for %s", timeout_ms, subject), 0)
        end
        vim.wait(M.poll_interval_ms)
    end
end

---@param timeout_ms integer
---@param subject string
---@param expected string
---@param read fun(): string
function M.eventually_equal(timeout_ms, subject, expected, read)
    local observed
    local ok, err = pcall(M.eventually, timeout_ms, subject, function()
        observed = read()
        return observed == expected
    end)
    if not ok then
        error(string.format("%s\nexpected:\n%s\nobserved:\n%s", err, expected, tostring(observed)), 0)
    end
end

---@param subject string
---@param predicate fun(): boolean
function M.holds_for_settle(subject, predicate)
    local deadline = now() + M.absence_settle_ms
    repeat
        if not predicate() then
            error(string.format("%s failed at a poll in the %d ms settle window", subject, M.absence_settle_ms), 0)
        end
        vim.wait(M.poll_interval_ms)
    until now() >= deadline
end

-- A companion of another editor has a different folder, so this list leaves it out.
---@return integer[]
function M.companion_pids()
    local result
    if is_windows then
        -- This PowerShell process has the folder in its command line too, so the query matches dotnet.exe only.
        local folder = M.companion_path():gsub("\\", "/"):gsub("'", "''")
        local script = string.format(
            "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine.Replace('\\', '/').Contains('%s/Companion.dll') } | ForEach-Object { $_.ProcessId }",
            folder
        )
        result = run({ "powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script })
    else
        result = run({ "pgrep", "-f", M.companion_path() .. "/Companion.dll" })
    end
    local pids = {}
    for pid in ((result and result.stdout) or ""):gmatch("%d+") do
        pids[#pids + 1] = tonumber(pid)
    end
    table.sort(pids)
    return pids
end

---@param known integer[]
---@return integer[]
function M.new_companion_pids(known)
    local seen = {}
    for _, pid in ipairs(known) do
        seen[pid] = true
    end
    local new = {}
    for _, pid in ipairs(M.companion_pids()) do
        if not seen[pid] then
            new[#new + 1] = pid
        end
    end
    return new
end

---@param pid integer
---@return boolean
function M.process_exists(pid)
    return vim.uv.kill(pid, 0) == 0
end

-- Windows has no SIGSTOP, so the freeze there suspends the process through NtSuspendProcess.
---@param pid integer
function M.freeze_process(pid)
    if not is_windows then
        vim.uv.kill(pid, "sigstop")
        return
    end
    local script = string.format(
        "Add-Type -Name Native -Namespace Suite -MemberDefinition '[DllImport(\"ntdll.dll\")] public static extern int NtSuspendProcess(IntPtr handle);'; "
            .. "[Suite.Native]::NtSuspendProcess((Get-Process -Id %d).Handle) | Out-Null",
        pid
    )
    local result = run({ "powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script })
    assert(result and result.code == 0, "could not freeze the process " .. pid .. ": " .. vim.inspect(result))
end

---@class nvim_suite.Child
---@field mini table the child Neovim object of mini.test
---@field job_id integer
---@field pid integer
---@field stopped boolean

---@class nvim_suite.Notice
---@field message string
---@field level integer

---@class nvim_suite.BudgetRow
---@field name string
---@field elapsed_ms number
---@field budget_ms number

---@type nvim_suite.Child[]
local children = {}
---@type nvim_suite.Child?
local harness_setup_child

-- A kill of the child makes the blocked request return an error, so the runner goes on to the next Check.
---@param child nvim_suite.Child
---@param subject string
---@param fn fun(): any
---@return any
local function guarded(child, subject, fn)
    local fired = false
    local timer = assert(vim.uv.new_timer())
    timer:start(M.child_answer_deadline_ms, 0, function()
        fired = true
        vim.uv.kill(child.pid, "sigkill")
    end)
    local ok, result = pcall(fn)
    timer:stop()
    timer:close()
    if fired then
        error(
            string.format(
                "the child Neovim gave no answer to %s in %d ms, so the Harness stopped it",
                subject,
                M.child_answer_deadline_ms
            ),
            0
        )
    end
    if not ok then
        error(result, 0)
    end
    return result
end

---@param opts table
---@param client_version? string replaces the client version in the child
---@param with_lualine? boolean
---@return nvim_suite.Child
function M.start_child(opts, client_version, with_lualine)
    vim.env.NVIM_TEST_CLIENT_OPTS = vim.json.encode(opts)
    vim.env.NVIM_TEST_CLIENT_VERSION = client_version
    vim.env.NVIM_TEST_LUALINE = with_lualine and "1" or nil
    local mini = MiniTest.new_child_neovim()
    mini.start({ "-u", child_init })
    local child = { mini = mini, job_id = mini.job.id, pid = vim.fn.jobpid(mini.job.id), stopped = false }
    children[#children + 1] = child
    return child
end

-- The sleep keeps the companion in the starting state long enough for a Check to drive :FsHttp run.
---@param delay_s integer
---@param end_state? "ready"|"stopped"|"sdkNotFound" default "ready"
---@return string path to an executable wrapper script
function M.slow_dotnet(delay_s, end_state)
    local real = vim.fn.exepath("dotnet")
    assert(real ~= "", "dotnet is not on PATH in the runner")
    local steps = {
        ready = { { "real" }, { "sleep", "real" } },
        stopped = { { "real" }, { "sleep", "exit1" } },
        sdkNotFound = { { "sleep", "exit0" }, { "real" } },
    }
    local plan = assert(steps[end_state or "ready"], "no wrapper for the end state " .. tostring(end_state))
    local shell
    if is_windows then
        -- ping waits with no console. timeout fails when stdin is not a console.
        shell = {
            commands = {
                sleep = { string.format("ping -n %d 127.0.0.1 >nul", delay_s + 1) },
                real = { string.format('"%s" %%*', real), "exit /b %errorlevel%" },
                exit0 = { "exit /b 0" },
                exit1 = { "exit /b 1" },
            },
            head = { "@echo off", 'if "%1"=="--list-sdks" goto sdks', "goto companion", ":sdks" },
            middle = ":companion",
            extension = ".cmd",
            newline = "\r\n",
        }
    else
        shell = {
            commands = {
                sleep = { string.format("sleep %d", delay_s) },
                real = { string.format('exec %s "$@"', real) },
                exit0 = { "exit 0" },
                exit1 = { "exit 1" },
            },
            head = { "#!/bin/sh", 'if [ "$1" = "--list-sdks" ]; then' },
            middle = "fi",
            extension = "",
            newline = "\n",
        }
    end
    local lines = vim.list_extend({}, shell.head)
    for _, step in ipairs(plan[1]) do
        vim.list_extend(lines, shell.commands[step])
    end
    lines[#lines + 1] = shell.middle
    for _, step in ipairs(plan[2]) do
        vim.list_extend(lines, shell.commands[step])
    end
    local path = vim.fn.tempname() .. "-slow-dotnet" .. shell.extension
    local file = assert(io.open(path, "wb"))
    file:write(table.concat(lines, shell.newline) .. shell.newline)
    file:close()
    vim.uv.fs_chmod(path, 493)
    return path
end

---@param child nvim_suite.Child
---@param expression string
---@param args? any[]
function M.lua_get(child, expression, args)
    return guarded(child, "the Lua expression " .. expression, function()
        return child.mini.lua_get(expression, args)
    end)
end

---@param child nvim_suite.Child
---@param command string
function M.cmd(child, command)
    return guarded(child, "the command " .. command, function()
        return child.mini.cmd(command)
    end)
end

---@param child nvim_suite.Child
---@param path string
function M.edit(child, path)
    M.cmd(child, "edit " .. vim.fn.fnameescape(path))
end

---@param child nvim_suite.Child
---@param ... string
function M.type_keys(child, ...)
    local keys = { ... }
    return guarded(child, "the keys " .. table.concat(keys), function()
        return child.mini.type_keys(unpack(keys))
    end)
end

---@param child nvim_suite.Child
---@return table
function M.screenshot(child)
    return guarded(child, "the screenshot", function()
        return child.mini.get_screenshot()
    end)
end

---@param child nvim_suite.Child
---@return string
function M.block_marks(child)
    return M.lua_get(
        child,
        [[(function()
            local namespace = vim.api.nvim_get_namespaces()["fshttp.block_mark"]
            if not namespace then
                return ""
            end
            local lines = {}
            for _, extmark in ipairs(vim.api.nvim_buf_get_extmarks(0, namespace, 0, -1, { details = true })) do
                local details = extmark[4]
                if not details.invalid then
                    local text = {}
                    for _, chunk in ipairs((details.virt_lines or {})[1] or {}) do
                        text[#text + 1] = chunk[1]
                    end
                    lines[#lines + 1] = string.format(
                        "%d: %s [%s]",
                        extmark[2] + 1,
                        vim.trim(table.concat(text)),
                        vim.trim(details.sign_text or "")
                    )
                end
            end
            return table.concat(lines, "\n")
        end)()]]
    )
end

---@param child nvim_suite.Child
---@param line integer
function M.await_block_mark(child, line)
    M.eventually(M.block_mark_deadline_ms, "a Block mark on line " .. line, function()
        return ("\n" .. M.block_marks(child)):find("\n" .. line .. ": ", 1, true) ~= nil
    end)
end

---@param child nvim_suite.Child
---@return string text "nil" when status() gives nil
function M.status(child)
    return M.lua_get(child, [[tostring(require("fshttp").status())]])
end

-- The edit and the read are one request, so no answer of the companion can arrive between them.
---@param child nvim_suite.Child
---@param path string
---@return string
function M.edit_and_read_status(child, path)
    return M.lua_get(
        child,
        [[(function(path)
            vim.cmd.edit(vim.fn.fnameescape(path))
            return tostring(require("fshttp").status())
        end)(...)]],
        { path }
    )
end

---@param child nvim_suite.Child
---@param subject string
---@param expected string
function M.expect_status(child, subject, expected)
    M.eventually_equal(M.status_line_text_deadline_ms, subject, expected, function()
        return M.status(child)
    end)
end

---@param child nvim_suite.Child
---@return string
function M.fshttp_status_echo(child)
    return M.lua_get(child, [[vim.api.nvim_exec2("FsHttp status", { output = true }).output]])
end

---@class nvim_suite.HealthItem
---@field section string the title of the section that contains the item
---@field level "OK"|"WARNING"|"ERROR"|"INFO"
---@field text string
---@field advice string[]

-- The levels that `vim.health` writes after the icon of a line. An INFO line has no icon and no level.
local health_levels = { OK = true, WARNING = true, ERROR = true }

-- The child then shows the window that was current before, and has no health buffer, so a later Check sees no change.
---@param child nvim_suite.Child
---@return nvim_suite.HealthItem[]
function M.checkhealth(child)
    local lines = M.lua_get(
        child,
        [[(function()
            local win = vim.api.nvim_get_current_win()
            vim.cmd("checkhealth fshttp")
            local buf = vim.api.nvim_get_current_buf()
            local lines = vim.api.nvim_buf_get_lines(buf, 0, -1, false)
            if vim.api.nvim_get_current_win() ~= win and #vim.api.nvim_list_tabpages() > 1 then
                vim.cmd("tabclose")
            end
            vim.api.nvim_set_current_win(win)
            vim.api.nvim_buf_delete(buf, { force = true })
            return lines
        end)()]]
    )
    local items = {}
    local section = ""
    for _, line in ipairs(lines) do
        local title = line:match("^(.-) ~$")
        local level, text = line:match("^%- %S+ (%u+) (.*)$")
        if not health_levels[level] then
            level, text = nil, nil
        end
        local advice = line:match("^    %- (.*)$")
        if title then
            section = title
        elseif level then
            items[#items + 1] = { section = section, level = level, text = text, advice = {} }
        elseif advice and #items > 0 then
            table.insert(items[#items].advice, advice)
        elseif line:match("^%- ") then
            items[#items + 1] = { section = section, level = "INFO", text = line:sub(3), advice = {} }
        end
    end
    return items
end

---@class nvim_suite.ClosedFold
---@field first integer
---@field last integer
---@field text string the text that the closed fold shows

---@class nvim_suite.ResponseWindow
---@field id integer
---@field col integer the screen column of the window
---@field winbar string the winbar as the window shows it, or "" for no winbar
---@field winbar_expression string the 'winbar' option
---@field wrap boolean
---@field linebreak boolean
---@field breakindent boolean
---@field closed_folds nvim_suite.ClosedFold[]

---@class nvim_suite.ResponseBuffer
---@field count integer the number of buffers with the fshttp_response filetype
---@field buftype? string
---@field lines? string[]
---@field windows nvim_suite.ResponseWindow[] each window of the current tab page that shows the Response buffer
---@field current_buffer_name string
---@field current_col integer the screen column of the current window

---@param child nvim_suite.Child
---@return nvim_suite.ResponseBuffer
function M.response_buffer(child)
    return M.lua_get(
        child,
        [[(function()
            local found = {}
            for _, buf in ipairs(vim.api.nvim_list_bufs()) do
                if vim.api.nvim_buf_is_valid(buf) and vim.bo[buf].filetype == "fshttp_response" then
                    found[#found + 1] = buf
                end
            end
            local snapshot = {
                count = #found,
                windows = {},
                current_buffer_name = vim.api.nvim_buf_get_name(0),
                current_col = vim.api.nvim_win_get_position(0)[2],
            }
            local buf = found[1]
            if not buf then
                return snapshot
            end
            snapshot.buftype = vim.bo[buf].buftype
            snapshot.lines = vim.api.nvim_buf_get_lines(buf, 0, -1, false)
            for _, win in ipairs(vim.api.nvim_tabpage_list_wins(0)) do
                if vim.api.nvim_win_get_buf(win) == buf then
                    local expression = vim.wo[win].winbar
                    local window = {
                        id = win,
                        col = vim.api.nvim_win_get_position(win)[2],
                        winbar_expression = expression,
                        winbar = expression == "" and ""
                            or vim.api.nvim_eval_statusline(expression, { winid = win, use_winbar = true }).str,
                        wrap = vim.wo[win].wrap,
                        linebreak = vim.wo[win].linebreak,
                        breakindent = vim.wo[win].breakindent,
                        closed_folds = {},
                    }
                    vim.api.nvim_win_call(win, function()
                        local line = 1
                        local last_line = vim.api.nvim_buf_line_count(buf)
                        while line <= last_line do
                            if vim.fn.foldclosed(line) == line then
                                local last = vim.fn.foldclosedend(line)
                                window.closed_folds[#window.closed_folds + 1] =
                                    { first = line, last = last, text = vim.fn.foldtextresult(line) }
                                line = last + 1
                            else
                                line = line + 1
                            end
                        end
                    end)
                    snapshot.windows[#snapshot.windows + 1] = window
                end
            end
            return snapshot
        end)()]]
    )
end

---@param child nvim_suite.Child
---@param line integer
function M.run_at(child, line)
    M.cmd(child, string.format("call cursor(%d, 1)", line))
    M.cmd(child, "FsHttp run")
end

---@param child nvim_suite.Child
function M.close_response_windows(child)
    M.lua_get(
        child,
        [[(function()
            for _, win in ipairs(vim.api.nvim_list_wins()) do
                local buf = vim.api.nvim_win_get_buf(win)
                if vim.bo[buf].filetype == "fshttp_response" and #vim.api.nvim_list_wins() > 1 then
                    vim.api.nvim_win_close(win, true)
                end
            end
        end)()]]
    )
end

---@param child nvim_suite.Child
---@return nvim_suite.Notice[]
function M.notices(child)
    return M.lua_get(child, "_G.fshttp_suite_notices")
end

---@param child nvim_suite.Child
---@param level integer
---@return nvim_suite.Notice[]
function M.notices_at(child, level)
    local found = {}
    for _, notice in ipairs(M.notices(child)) do
        if notice.level == level then
            found[#found + 1] = notice
        end
    end
    return found
end

---@param child nvim_suite.Child
---@param count integer
---@return nvim_suite.Notice[]
function M.notices_after(child, count)
    local all = M.notices(child)
    local found = {}
    for i = count + 1, #all do
        found[#found + 1] = all[i]
    end
    return found
end

---@param child nvim_suite.Child
function M.stop_child(child)
    if child.stopped then
        return
    end
    child.stopped = true
    pcall(guarded, child, "the quit", function()
        child.mini.stop()
    end)
    if vim.fn.jobwait({ child.job_id }, 0)[1] == -1 then
        vim.uv.kill(child.pid, "sigkill")
    end
end

-- A hung companion that outlives its child Neovim must not reach the next Check.
---@param keep integer[]
local function kill_companions(keep)
    for _, pid in ipairs(M.new_companion_pids(keep)) do
        vim.uv.kill(pid, "sigkill")
    end
end

---@class nvim_suite.ProvenLive
---@field server_live boolean
---@field child_answers boolean
---@field client_loaded boolean
---@field fixture_open boolean
---@field companion_exists boolean

---@return nvim_suite.ProvenLive
local function nothing_proven()
    return {
        server_live = false,
        child_answers = false,
        client_loaded = false,
        fixture_open = false,
        companion_exists = false,
    }
end

local proven_live = nothing_proven()
---@type integer[]
local harness_setup_companions = {}
---@type vim.SystemObj?
local server
local harness_setup_elapsed_ms = 0
---@type number?
local suite_start_ms
---@type number?
local check_start_ms
---@type nvim_suite.BudgetRow[]
local check_rows = {}
local timing_table_emitted = false

---@return nvim_suite.ProvenLive
function M.proven_live_state()
    return vim.deepcopy(proven_live)
end

---@return boolean
function M.is_proven_live()
    for _, proven in pairs(proven_live) do
        if not proven then
            return false
        end
    end
    return true
end

---@return boolean
function M.timing_table_was_emitted()
    return timing_table_emitted
end

---@return nvim_suite.Child
function M.harness_setup_child()
    return assert(harness_setup_child, "Harness setup started no child Neovim")
end

---@return integer[]
function M.harness_setup_companion_pids()
    return vim.deepcopy(harness_setup_companions)
end

---@return string base_url, string dead_url
local function read_sidecar()
    local text = read_file(sidecar_path)
    if not text then
        fail_harness_setup("the Sidecar is missing at " .. sidecar_path)
    end
    local ok, sidecar = pcall(vim.json.decode, text)
    if not ok or type(sidecar) ~= "table" or type(sidecar.baseUrl) ~= "string" or type(sidecar.deadUrl) ~= "string" then
        fail_harness_setup(string.format("the Sidecar at %s does not parse: %s", sidecar_path, text))
    end
    return sidecar.baseUrl, sidecar.deadUrl
end

-- The URL must match the URL that the fixtures compute, which has no trailing slash.
---@return string
function M.base_url()
    local base_url = read_sidecar()
    return (base_url:gsub("/$", ""))
end

function M.release_slow()
    run({ "curl", "-sS", "-m", "10", M.base_url() .. "/release" })
end

-- The test HTTP server serves this folder at /download/ in place of the GitHub releases.
local downloads_dir = vim.fn.tempname() .. "-downloads"

---@param version string
---@param corrupt_checksum? boolean the .sha256 file then names a hash that matches no archive
function M.publish_release(version, corrupt_checksum)
    local folder = string.format("%s/v%s", downloads_dir, version)
    vim.fn.mkdir(folder, "p")
    local archive = vim.fn.glob(M.archive_dir() .. "/fshttp-studio-companion-*.tar.gz")
    assert(archive ~= "", "no Companion archive in " .. M.archive_dir())
    local name = string.format("fshttp-studio-companion-%s.tar.gz", version)
    assert(vim.uv.fs_copyfile(archive, folder .. "/" .. name))
    if corrupt_checksum then
        local file = assert(io.open(folder .. "/" .. name .. ".sha256", "wb"))
        file:write(string.rep("0", 64) .. "  " .. name .. "\n")
        file:close()
    else
        assert(vim.uv.fs_copyfile(archive .. ".sha256", folder .. "/" .. name .. ".sha256"))
    end
end

---@param child nvim_suite.Child
---@return string
function M.download_root(child)
    return M.lua_get(child, [[require("fshttp.download").root()]])
end

local function start_server()
    vim.fn.delete(downloads_dir, "rf")
    vim.fn.mkdir(downloads_dir, "p")
    local server_bin = required_env("NVIM_TEST_SERVER", "the test HTTP server")
    os.remove(sidecar_path)
    local ok, started = pcall(vim.system, { server_bin }, {
        cwd = suite_dir,
        env = { UI_TEST_SERVER_DOWNLOADS = downloads_dir },
        stdout = false,
        stderr = false,
    })
    if not ok then
        fail_harness_setup(string.format("the test HTTP server at %s did not start: %s", server_bin, started))
    end
    server = started
    M.eventually(M.sidecar_deadline_ms, "the test HTTP server to write the Sidecar", function()
        if started:is_closing() then
            fail_harness_setup("the test HTTP server stopped before it wrote the Sidecar")
        end
        local text = read_file(sidecar_path)
        return text ~= nil and pcall(vim.json.decode, text)
    end)

    local base_url, dead_url = read_sidecar()
    -- Each child Neovim downloads from the test HTTP server, and none reaches GitHub.
    vim.env.FSHTTP_STUDIO_DOWNLOAD_BASE_URL = (base_url:gsub("/$", "")) .. "/download"
    local health = run({ "curl", "-sS", "-m", "10", base_url .. "/json" })
    local body = health and health.stdout or ""
    if body ~= M.json_probe_body then
        fail_harness_setup(
            string.format("the healthcheck at %s/json got %q, and expected %s", base_url, body, M.json_probe_body)
        )
    end
    local probe = run({ "curl", "-sS", "-m", "10", dead_url })
    if not probe or probe.code ~= curl_could_not_connect then
        fail_harness_setup(
            string.format("the Dead port at %s accepted a connection, so the Sidecar can be stale", dead_url)
        )
    end
end

local function stop_server()
    if server and not server:is_closing() then
        server:kill("sigterm")
        server:wait(5000)
    end
    os.remove(sidecar_path)
    vim.fn.delete(downloads_dir, "rf")
end

---@param caption string
---@param rows nvim_suite.BudgetRow[]
local function emit_timing_table(caption, rows)
    local lines = { "#### " .. caption, "", "| Phase | Elapsed | Budget |", "| --- | ---: | ---: |" }
    for _, row in ipairs(rows) do
        lines[#lines + 1] = string.format("| %s | %.0f ms | %.0f ms |", row.name, row.elapsed_ms, row.budget_ms)
    end
    local markdown = table.concat(lines, "\n") .. "\n\n"
    io.stdout:write(markdown)
    local summary = vim.env.GITHUB_STEP_SUMMARY
    if summary and summary ~= "" then
        local file = io.open(summary, "a")
        if file then
            file:write(markdown)
            file:close()
        end
    end
    timing_table_emitted = true
end

---@param row nvim_suite.BudgetRow
local function assert_budget(row)
    if row.elapsed_ms > row.budget_ms then
        error(
            string.format(
                "%s exceeded the %d s budget (observed %.0f ms)",
                row.name,
                row.budget_ms / 1000,
                row.elapsed_ms
            ),
            0
        )
    end
end

local function harness_setup_row()
    return { name = "Harness setup", elapsed_ms = harness_setup_elapsed_ms, budget_ms = M.harness_setup_budget_ms }
end

local function suite_row()
    local elapsed = suite_start_ms and (now() - suite_start_ms) or 0
    return { name = "Suite", elapsed_ms = elapsed, budget_ms = M.suite_budget_ms }
end

local function run_harness_setup()
    local start = now()
    proven_live = nothing_proven()
    local known_companions = M.companion_pids()

    local ok, err = pcall(function()
        start_server()
        proven_live.server_live = true

        local child = M.start_child({ companion_path = M.companion_path() })
        harness_setup_child = child
        if M.lua_get(child, "1 + 1") ~= 2 then
            fail_harness_setup("the child Neovim gave a wrong answer over RPC")
        end
        proven_live.child_answers = true

        local loaded = M.lua_get(
            child,
            [[(function()
                for _, plugin in pairs(require("lazy.core.config").plugins) do
                    if vim.fs.normalize(plugin.dir) == vim.fs.normalize(vim.uv.cwd()) and plugin._.loaded then
                        return vim.g.loaded_fshttp == true
                    end
                end
                return false
            end)()]]
        )
        if not loaded then
            fail_harness_setup("lazy.nvim did not load the client in the child Neovim")
        end
        proven_live.client_loaded = true

        local fixture = M.fixture("harness-setup.fsx")
        M.edit(child, fixture)
        local open = M.lua_get(child, "{ vim.api.nvim_buf_get_name(0), vim.bo.filetype }")
        if open[1] ~= fixture or open[2] ~= "fsharp" then
            fail_harness_setup(string.format("the fixture did not open as F#: the buffer is %s (%s)", open[1], open[2]))
        end
        proven_live.fixture_open = true

        M.eventually(M.companion_exists_deadline_ms, "a companion process to exist", function()
            return #M.new_companion_pids(known_companions) > 0
        end)
        harness_setup_companions = M.new_companion_pids(known_companions)
        proven_live.companion_exists = true
    end)

    harness_setup_elapsed_ms = now() - start
    emit_timing_table("Harness setup", { harness_setup_row() })
    if not ok then
        error(err, 0)
    end
end

---@return string
local function current_check_name()
    local case = MiniTest.current.case
    if not case then
        return "unknown Check"
    end
    return "Check " .. table.concat(case.desc, ": ", 2)
end

M.hooks = {
    pre_once = run_harness_setup,
    pre_case = function()
        suite_start_ms = suite_start_ms or now()
        check_start_ms = now()
        if not M.is_proven_live() then
            error("Harness setup did not reach Proven-live, so no Check can run", 0)
        end
    end,
    post_case = function()
        local elapsed = check_start_ms and (now() - check_start_ms) or 0
        check_start_ms = nil
        for _, child in ipairs(children) do
            if child ~= harness_setup_child then
                M.stop_child(child)
            end
        end
        kill_companions(harness_setup_companions)
        local row = { name = current_check_name(), elapsed_ms = elapsed, budget_ms = M.check_budget_ms }
        check_rows[#check_rows + 1] = row
        assert_budget(row)
    end,
    post_once = function()
        for _, child in ipairs(children) do
            M.stop_child(child)
        end
        kill_companions({})
        stop_server()

        local rows = { harness_setup_row() }
        vim.list_extend(rows, check_rows)
        rows[#rows + 1] = suite_row()
        emit_timing_table("Neovim suite timings", rows)
        assert_budget(harness_setup_row())
        assert_budget(suite_row())
    end,
}

return M
