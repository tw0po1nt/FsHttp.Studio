-- The init file of the child Neovim that the Neovim suite drives. lazy.minit loads the client as a
-- lazy.nvim spec, with the opts that the Harness puts in NVIM_TEST_CLIENT_OPTS. The child inherits
-- the XDG paths of the runner, so it finds the lazy.nvim that the runner installed.

-- lazy.minit reads the script arguments from _G.arg, and Neovim sets _G.arg only for `nvim -l`.
_G.arg = {}

-- The runner already installed and updated each plugin.
vim.env.LAZY_OFFLINE = "1"

-- The Harness reads each notice from this list. A headless child shows no notice on a screen.
local notices = {}
_G.fshttp_suite_notices = notices
---@diagnostic disable-next-line: duplicate-set-field
vim.notify = function(message, level)
    notices[#notices + 1] = { message = message, level = level }
end

-- A Check sets NVIM_TEST_CLIENT_VERSION to make the client version different from the companion version.
if vim.env.NVIM_TEST_CLIENT_VERSION then
    package.loaded["fshttp.version"] = vim.env.NVIM_TEST_CLIENT_VERSION
end

vim.opt.rtp:prepend(vim.fn.stdpath("data") .. "/lazy/lazy.nvim")

local spec = {
    { dir = vim.uv.cwd(), opts = vim.json.decode(vim.env.NVIM_TEST_CLIENT_OPTS or "{}") },
}
-- A Check sets NVIM_TEST_LUALINE to load lualine.nvim. The runner of the suite installed it.
if vim.env.NVIM_TEST_LUALINE == "1" then
    spec[#spec + 1] = { "nvim-lualine/lualine.nvim" }
end

require("lazy.minit").setup({ spec = spec })
