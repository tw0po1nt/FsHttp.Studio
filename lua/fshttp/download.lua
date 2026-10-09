local rule = require("fshttp.download_rule")

local M = {}

local curl_timeout_s = 600
local connect_timeout_s = 20
local blocking_timeout_ms = 15 * 60 * 1000

---@return string
function M.root()
    return vim.fs.joinpath(vim.fn.stdpath("data"), "fshttp-studio", "companion")
end

---@param version string
---@return string
function M.folder(version)
    return vim.fs.joinpath(M.root(), version)
end

---@param folder string
---@return boolean
function M.is_installed(folder)
    return vim.uv.fs_stat(vim.fs.joinpath(folder, "Companion.dll")) ~= nil
end

---@return string
local function base_url()
    local value = vim.env[rule.base_url_variable]
    if value and value ~= "" then
        return value
    end
    return rule.default_base_url
end

---@param cmd string[]
---@param callback fun(result: vim.SystemCompleted)
---@param cwd? string
local function run(cmd, callback, cwd)
    local ok, err = pcall(vim.system, cmd, { text = true, cwd = cwd }, vim.schedule_wrap(callback))
    if not ok then
        vim.schedule(function()
            callback({ code = -1, signal = 0, stdout = "", stderr = tostring(err) })
        end)
    end
end

---@param text string?
---@return string
local function first_line(text)
    return vim.trim((text or ""):match("[^\r\n]+") or "")
end

---@param folder_path string
local function delete_tree(folder_path)
    vim.fn.delete(folder_path, "rf")
end

-- A folder that starts with a dot is the work folder of a download in progress, in this instance or in another.
---@param keep string the version
local function delete_older_versions(keep)
    local root = M.root()
    for name, kind in vim.fs.dir(root) do
        if kind == "directory" and name ~= keep and name:sub(1, 1) ~= "." then
            delete_tree(vim.fs.joinpath(root, name))
        end
    end
end

---@param callback fun(result: fshttp.DownloadResult)
---@param work string
---@param result fshttp.DownloadResult
local function finish(callback, work, result)
    delete_tree(work)
    callback(result)
end

-- The file has a temporary name until curl finishes.
---@param url string
---@param target string
---@param callback fun(status: "ok"|"notFound"|"failed", detail: string?)
local function fetch_file(url, target, callback)
    local part = target .. ".part"
    local cmd = {
        "curl",
        "-sS",
        "-L",
        "--connect-timeout",
        tostring(connect_timeout_s),
        "--max-time",
        tostring(curl_timeout_s),
        "-o",
        part,
        "-w",
        "%{http_code}",
        url,
    }
    run(cmd, function(result)
        if result.code ~= 0 then
            local detail = first_line(result.stderr)
            callback("failed", detail ~= "" and detail or ("exit code " .. result.code))
            return
        end
        local http_status = vim.trim(result.stdout or "")
        if http_status == "404" then
            callback("notFound", nil)
        elseif http_status ~= "200" then
            callback("failed", "HTTP status " .. http_status .. " from " .. url)
        elseif not vim.uv.fs_rename(part, target) then
            callback("failed", "could not rename " .. part)
        else
            callback("ok", nil)
        end
    end)
end

-- The callback runs on the main loop.
---@param version string
---@param callback fun(result: fshttp.DownloadResult)
function M.fetch(version, callback)
    local root = M.root()
    local target = M.folder(version)
    -- The work folder is private to this download, so two Neovim instances can download at the same time.
    local work = vim.fs.joinpath(root, string.format(".work-%s-%d", version, vim.fn.getpid()))
    vim.fn.mkdir(work, "p")

    local archive = vim.fs.joinpath(work, rule.archive_name(version))
    local checksum_file = vim.fs.joinpath(work, rule.checksum_name(version))
    local unpacked_name = "companion"
    local unpacked = vim.fs.joinpath(work, unpacked_name)

    ---@param cause fshttp.DownloadCause
    ---@param detail string
    local function fail(cause, detail)
        finish(callback, work, { kind = "failed", cause = cause, detail = detail })
    end

    local function install()
        -- Another Neovim instance can finish the same download first, before the rename or during it.
        local renamed = not M.is_installed(target) and vim.uv.fs_rename(unpacked, target)
        if renamed then
            delete_older_versions(version)
        end
        if renamed or M.is_installed(target) then
            finish(callback, work, { kind = "installed", folder = target })
        else
            fail("tar", "could not move the unpacked files to " .. target)
        end
    end

    local function unpack()
        vim.fn.mkdir(unpacked, "p")
        -- GNU tar reads the drive letter of a Windows path (C:/...) as a host name, so tar gets relative names.
        local tar = { "tar", "-xzf", rule.archive_name(version), "-C", unpacked_name }
        run(tar, function(result)
            if result.code ~= 0 then
                local detail = first_line(result.stderr)
                fail("tar", detail ~= "" and detail or ("exit code " .. result.code))
            elseif not M.is_installed(unpacked) then
                fail("tar", "the archive contains no Companion.dll")
            else
                install()
            end
        end, work)
    end

    local function verify()
        local file = io.open(checksum_file, "rb")
        local expected = file and file:read("*a") or ""
        if file then
            file:close()
        end
        run(rule.checksum_command(vim.uv.os_uname().sysname, archive), function(result)
            if result.code ~= 0 then
                fail("checksum", "the checksum command failed: " .. first_line(result.stderr))
            elseif rule.checksum_matches(expected, result.stdout or "") then
                unpack()
            else
                fail("checksum", rule.checksum_name(version) .. " does not match the archive")
            end
        end)
    end

    local function fetch_checksum()
        local url = rule.url(base_url(), version, rule.checksum_name(version))
        fetch_file(url, checksum_file, function(status, detail)
            if status == "ok" then
                verify()
            else
                fail("curl", detail or ("HTTP status 404 from " .. url))
            end
        end)
    end

    local url = rule.url(base_url(), version, rule.archive_name(version))
    fetch_file(url, archive, function(status, detail)
        if status == "ok" then
            fetch_checksum()
        elseif status == "notFound" then
            finish(callback, work, { kind = "noRelease" })
        else
            fail("curl", detail or "")
        end
    end)
end

-- A plugin manager calls this build hook at install and at each update, so it waits for the download.
---@param companion_path string? the companion_path option
---@return boolean ok
---@return string message
function M.build(companion_path)
    if companion_path ~= nil then
        return true, rule.in_use_message(companion_path)
    end
    local version = require("fshttp.version")
    local folder = M.folder(version)
    if M.is_installed(folder) then
        return true, rule.installed_message(version, folder)
    end
    ---@type fshttp.DownloadResult?
    local outcome
    M.fetch(version, function(result)
        outcome = result
    end)
    vim.wait(blocking_timeout_ms, function()
        return outcome ~= nil
    end, 50)
    if outcome == nil then
        return false, rule.failed_notice("curl", "the download did not finish in time")
    elseif outcome.kind == "installed" then
        return true, rule.downloaded_message(version, outcome.folder)
    end
    return false, rule.failure_notice(outcome, version)
end

return M
