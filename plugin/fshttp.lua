if vim.g.loaded_fshttp then
    return
end
vim.g.loaded_fshttp = true

local group = vim.api.nvim_create_augroup("fshttp", { clear = true })

vim.api.nvim_create_autocmd({ "BufReadPost", "BufNewFile" }, {
    group = group,
    pattern = "*.fsx",
    once = true,
    callback = function()
        require("fshttp").start()
    end,
})

-- lazy.nvim can load the client after Neovim reads the first Script. lazy.nvim calls setup() after
-- it sources this file, so the scan waits for the next turn of the event loop.
vim.schedule(function()
    for _, buf in ipairs(vim.api.nvim_list_bufs()) do
        if vim.api.nvim_buf_is_loaded(buf) and vim.api.nvim_buf_get_name(buf):match("%.fsx$") then
            require("fshttp").start()
            return
        end
    end
end)
