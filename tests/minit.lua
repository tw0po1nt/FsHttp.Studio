vim.env.LAZY_STDPATH = ".tests"
load(vim.fn.system({ "curl", "-fsSL", "https://raw.githubusercontent.com/folke/lazy.nvim/main/bootstrap.lua" }))()
require("lazy.minit").setup({ spec = { { dir = vim.uv.cwd() } } })
