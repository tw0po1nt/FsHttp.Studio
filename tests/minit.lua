vim.env.LAZY_STDPATH = ".tests"

-- lazy.nvim v11.17.5. A clone of the default branch would let an upstream change alter the suite.
local lazy_commit = "85c7ff3711b730b4030d03144f6db6375044ae82"
-- ":p" appends a separator once the folder exists. A trailing backslash would escape the comma that
-- joins the entries of runtimepath on Windows, so the separator is removed.
local lazy_path = vim.fn.fnamemodify(".tests/data/nvim/lazy/lazy.nvim", ":p"):gsub("[/\\]+$", "")

if not vim.uv.fs_stat(lazy_path) then
    vim.fn.system({ "git", "clone", "--filter=blob:none", "https://github.com/folke/lazy.nvim.git", lazy_path })
    assert(vim.v.shell_error == 0, "git clone of lazy.nvim failed")
end
vim.fn.system({ "git", "-C", lazy_path, "checkout", "--quiet", lazy_commit })
assert(vim.v.shell_error == 0, "git checkout of lazy.nvim " .. lazy_commit .. " failed")
vim.opt.rtp:prepend(lazy_path)
-- The child Neovim of the Neovim suite cannot derive this path: stdpath("data") ends in nvim-data on
-- Windows, and the clone above is in a nvim folder.
vim.env.NVIM_TEST_LAZY_PATH = lazy_path

-- The child Neovim of the lualine Check runs offline, so the runner installs lualine.nvim for it.
local lualine_commit = "221ce6b2d999187044529f49da6554a92f740a96"

-- lazy.minit installs mini.test and luassert on its own. These commits replace the latest commit of
-- each, so an upstream change cannot alter a run. lazy.nvim merges the spec by plugin name.
local mini_test_commit = "72fc8c0ef64a2c5e17cd00447aff586abbe6c27a"
local luassert_commit = "a1c4902b0528d90f04214e2f334a01c2fb747bce"

require("lazy.minit").setup({
    spec = {
        { "folke/lazy.nvim", commit = lazy_commit },
        { "echasnovski/mini.test", commit = mini_test_commit },
        { "lunarmodules/luassert", commit = luassert_commit },
        { "nvim-lualine/lualine.nvim", commit = lualine_commit, lazy = true },
        { dir = vim.uv.cwd() },
    },
})
