local M = {}

M.default_base_url = "https://github.com/tw0po1nt/FsHttp.Studio/releases/download"

-- A test-only variable. The Neovim suite sets it to the URL of its test server.
M.base_url_variable = "FSHTTP_STUDIO_DOWNLOAD_BASE_URL"

---@param version string
---@return string
function M.archive_name(version)
    return "fshttp-studio-companion-" .. version .. ".tar.gz"
end

---@param version string
---@return string
function M.checksum_name(version)
    return M.archive_name(version) .. ".sha256"
end

-- The release tag of a version is `v<version>`.
---@param base_url string
---@param version string
---@param file_name string
---@return string
function M.url(base_url, version, file_name)
    return (base_url:gsub("/+$", "")) .. "/v" .. version .. "/" .. file_name
end

---@param sysname string the `sysname` field of `vim.uv.os_uname()`
---@return string
function M.checksum_tool(sysname)
    if sysname == "Darwin" then
        return "shasum"
    elseif sysname == "Windows_NT" then
        return "certutil"
    end
    return "sha256sum"
end

---@param sysname string the `sysname` field of `vim.uv.os_uname()`
---@param path string
---@return string[]
function M.checksum_command(sysname, path)
    local tool = M.checksum_tool(sysname)
    if tool == "shasum" then
        return { tool, "-a", "256", path }
    elseif tool == "certutil" then
        return { tool, "-hashfile", path, "SHA256" }
    end
    return { tool, path }
end

-- A line is either `<hash>  <name>`, or a hash that certutil writes in groups of two digits.
---@param text string
---@return string? hash 64 lower-case hexadecimal digits, or nil when the text contains no hash
function M.parse_hash(text)
    for line in text:gmatch("[^\r\n]+") do
        local first = line:match("^%s*(%S+)")
        if first and #first == 64 and first:match("^%x+$") then
            return first:lower()
        end
        local joined = line:gsub("%s", "")
        if #joined == 64 and joined:match("^%x+$") then
            return joined:lower()
        end
    end
    return nil
end

---@param checksum_file_text string
---@param command_output string
---@return boolean
function M.checksum_matches(checksum_file_text, command_output)
    local expected = M.parse_hash(checksum_file_text)
    return expected ~= nil and expected == M.parse_hash(command_output)
end

---@param version string
---@return string
function M.downloading_notice(version)
    return string.format("FsHttp.Studio is downloading the companion for v%s.", version)
end

---@alias fshttp.DownloadCause "curl"|"tar"|"checksum"

---@class fshttp.Installed
---@field kind "installed"
---@field folder string

---@class fshttp.NoRelease
---@field kind "noRelease"

---@class fshttp.DownloadFailed
---@field kind "failed"
---@field cause fshttp.DownloadCause
---@field detail string

---@alias fshttp.DownloadResult fshttp.Installed|fshttp.NoRelease|fshttp.DownloadFailed

---@param cause fshttp.DownloadCause
---@param detail string
---@return string
function M.failed_notice(cause, detail)
    local what = {
        curl = "curl could not fetch the Companion archive",
        tar = "tar could not unpack the Companion archive",
        checksum = "the checksum of the Companion archive did not verify",
    }
    return string.format(
        "FsHttp.Studio could not download the companion: %s (%s). Run :FsHttp restart to try again.",
        what[cause],
        detail
    )
end

---@param version string
---@return string
function M.no_release_notice(version)
    return string.format(
        "No release has the FsHttp.Studio version v%s, so no companion started. "
            .. "Pin the plugin to a release, or set companion_path to a build of the companion.",
        version
    )
end

---@param result fshttp.NoRelease|fshttp.DownloadFailed
---@param version string
---@return string
function M.failure_notice(result, version)
    if result.kind == "noRelease" then
        return M.no_release_notice(version)
    end
    ---@cast result fshttp.DownloadFailed
    return M.failed_notice(result.cause, result.detail)
end

---@param folder string the companion_path option
---@return string
function M.not_found_notice(folder)
    return string.format(
        "FsHttp.Studio found no Companion.dll in companion_path (%s). "
            .. "Set companion_path to the folder that contains Companion.dll, or remove companion_path to download the companion. "
            .. "Then run :FsHttp restart.",
        folder
    )
end

---@param folder string the companion_path option
---@return string
function M.in_use_message(folder)
    return string.format("FsHttp.Studio downloads no companion, because companion_path is in use (%s).", folder)
end

---@param version string
---@param folder string
---@return string
function M.installed_message(version, folder)
    return string.format("FsHttp.Studio has the companion for v%s at %s.", version, folder)
end

---@param version string
---@param folder string
---@return string
function M.downloaded_message(version, folder)
    return string.format("FsHttp.Studio downloaded the companion for v%s to %s.", version, folder)
end

return M
