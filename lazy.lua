return {
    -- This spec has an effect only when the user installs lualine.nvim.
    {
        "nvim-lualine/lualine.nvim",
        optional = true,
        opts = function(_, opts)
            opts.sections = opts.sections or {}
            -- lualine replaces its default lualine_x with the lualine_x of opts, so a new list starts from the default.
            opts.sections.lualine_x = opts.sections.lualine_x
                or require("lualine.config").get_config().sections.lualine_x
            table.insert(opts.sections.lualine_x, {
                function()
                    return require("fshttp").status() or ""
                end,
                cond = function()
                    return require("fshttp").config().status_line.lualine
                end,
            })
            -- lualine draws its statusline on a timer, so a change of the Status line text asks for a draw.
            vim.api.nvim_create_autocmd("User", {
                group = vim.api.nvim_create_augroup("fshttp.lualine", { clear = true }),
                pattern = "FsHttpStatusLineTextChanged",
                callback = function()
                    require("lualine").refresh({ place = { "statusline" } })
                end,
            })
        end,
    },
}
