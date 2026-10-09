local core_env = require("lua.core_env")

describe("fshttp.download_rule", function()
    local rule = core_env.load("fshttp.download_rule")
    local hash = string.rep("ab", 32)

    it("names the archive and the checksum file by version", function()
        assert.equal("fshttp-studio-companion-0.2.0.tar.gz", rule.archive_name("0.2.0"))
        assert.equal("fshttp-studio-companion-0.2.0.tar.gz.sha256", rule.checksum_name("0.2.0"))
    end)

    it("builds the release URL from the tag v<version>", function()
        assert.equal("http://host/base/v0.2.0/file.tar.gz", rule.url("http://host/base/", "0.2.0", "file.tar.gz"))
    end)

    it("picks the checksum tool by operating system", function()
        assert.equal("sha256sum", rule.checksum_tool("Linux"))
        assert.equal("shasum", rule.checksum_tool("Darwin"))
        assert.equal("certutil", rule.checksum_tool("Windows_NT"))
        assert.same({ "sha256sum", "f" }, rule.checksum_command("Linux", "f"))
        assert.same({ "shasum", "-a", "256", "f" }, rule.checksum_command("Darwin", "f"))
        assert.same({ "certutil", "-hashfile", "f", "SHA256" }, rule.checksum_command("Windows_NT", "f"))
    end)

    it("reads the hash from a sha256sum line", function()
        assert.equal(hash, rule.parse_hash(hash .. "  file.tar.gz\n"))
    end)

    it("reads the hash from certutil output", function()
        local spaced = hash:gsub("(%x%x)", "%1 "):gsub(" $", "")
        local output = "SHA256 hash of f:\r\n"
            .. spaced
            .. "\r\nCertUtil: -hashfile command completed successfully.\r\n"
        assert.equal(hash, rule.parse_hash(output))
    end)

    it("gives no hash for text that contains none", function()
        assert.is_nil(rule.parse_hash("<html>Not Found</html>"))
    end)

    it("matches equal hashes and rejects different or missing hashes", function()
        assert.is_true(rule.checksum_matches(hash .. "  a\n", hash:upper() .. "  b\n"))
        assert.is_false(rule.checksum_matches(string.rep("0", 64) .. "  a\n", hash .. "  b\n"))
        assert.is_false(rule.checksum_matches("", ""))
    end)

    it("gives the no-release notice or the failed notice for a failed download", function()
        assert.equal(rule.no_release_notice("0.2.0"), rule.failure_notice({ kind = "noRelease" }, "0.2.0"))
        assert.equal(
            rule.failed_notice("checksum", "no match"),
            rule.failure_notice({ kind = "failed", cause = "checksum", detail = "no match" }, "0.2.0")
        )
    end)
end)
