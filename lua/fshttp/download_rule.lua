-- The rules of the Companion archive download: the names, the URLs, the checksum command, and the
-- text of each notice. The download itself runs in fshttp.download.
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

-- The command that prints the SHA-256 hash of a file.
---@param sysname string the `sysname` field of `vim.uv.os_uname()`
---@param path string
---@return string[]
function M.checksum_command(sysname, path)
    if sysname == "Darwin" then
        return { "shasum", "-a", "256", path }
    elseif sysname == "Windows_NT" then
        return { "certutil", "-hashfile", path, "SHA256" }
    end
    return { "sha256sum", path }
end

-- Reads a SHA-256 hash from a `.sha256` file or from the output of a checksum command. The first
-- line that holds a hash gives it. A line is either `<hash>  <name>`, or a hash that certutil
-- writes in groups of two digits.
---@param text string
---@return string? hash 64 lower-case hexadecimal digits, or nil when the text holds no hash
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

---@param folder string the companion_path option
---@return string
function M.not_found_notice(folder)
    return string.format(
        "FsHttp.Studio found no Companion.dll in companion_path (%s). "
            .. "Set companion_path to the folder that holds Companion.dll, or remove companion_path to download the companion. "
            .. "Then run :FsHttp restart.",
        folder
    )
end

---@param folder string the companion_path option
---@return string
function M.in_use_message(folder)
    return string.format("FsHttp.Studio downloads no companion, because companion_path is in use (%s).", folder)
end

return M
