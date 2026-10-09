if vim.g.loaded_fshttp then
    return
end
vim.g.loaded_fshttp = true

vim.api.nvim_create_user_command("FsHttp", function(opts)
    require("fshttp.command").dispatch(opts)
end, {
    nargs = "+",
    desc = "FsHttp.Studio",
    complete = function(arg_lead, cmdline)
        return require("fshttp.command").complete(arg_lead, cmdline)
    end,
})

vim.keymap.set("n", "<Plug>(FsHttpRun)", function()
    require("fshttp.command").subcommands.run({})
end, { desc = "FsHttp.Studio: run the request at the cursor" })

for name, plug in pairs({ request = "Request", headers = "Headers", body = "Body" }) do
    vim.keymap.set("n", "<Plug>(FsHttpYank" .. plug .. ")", function()
        require("fshttp.yank").yank(name)
    end, { desc = "FsHttp.Studio: yank the " .. name })
end

vim.keymap.set("n", "<Plug>(FsHttpJump)", function()
    require("fshttp.jump").jump()
end, { desc = "FsHttp.Studio: move to a Compile error position in the script" })

vim.keymap.set("n", "<Plug>(FsHttpHelp)", function()
    require("fshttp.response_keys").help()
end, { desc = "FsHttp.Studio: list the keys of the Response buffer" })

local group = vim.api.nvim_create_augroup("fshttp", { clear = true })

vim.api.nvim_create_autocmd({ "BufReadPost", "BufNewFile" }, {
    group = group,
    pattern = "*.fsx",
    once = true,
    callback = function()
        require("fshttp").start()
    end,
})

-- lazy.nvim can call setup() after Neovim reads the first Script, so the scan waits one loop turn.
vim.schedule(function()
    for _, buf in ipairs(vim.api.nvim_list_bufs()) do
        if vim.api.nvim_buf_is_loaded(buf) and vim.api.nvim_buf_get_name(buf):match("%.fsx$") then
            require("fshttp").start()
            return
        end
    end
end)
