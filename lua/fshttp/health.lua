-- :checkhealth fshttp. Neovim finds this module by its name.
local body_syntax = require("fshttp.body_syntax")
local companion = require("fshttp.companion")
local download = require("fshttp.download")
local download_rule = require("fshttp.download_rule")
local health_rule = require("fshttp.health_rule")
local image_body = require("fshttp.image_body")
local options = require("fshttp.options")
local refusals = require("fshttp.refusals")
local sdk = require("fshttp.sdk")
local status_line_text = require("fshttp.status_line_text")

local M = {}

---@param config fshttp.Config
local function check_dotnet(config)
    local found, floor, dotnet = companion.has_sdk(config, companion.folder(config))
    if found then
        local path = vim.fn.exepath(dotnet)
        vim.health.ok(health_rule.sdk_found(path ~= "" and path or dotnet, floor))
    else
        vim.health.error(sdk.not_found_notice(floor, config.dotnet_path))
    end
end

---@param config fshttp.Config
local function check_companion(config)
    local folder = companion.folder(config)
    local client_version = require("fshttp.version")
    if config.companion_path == nil then
        if download.is_installed(folder) then
            vim.health.ok(download_rule.installed_message(client_version, folder))
        else
            vim.health.error(health_rule.companion_missing(client_version, folder), health_rule.companion_missing_fix)
        end
    elseif download.is_installed(folder) then
        vim.health.ok(health_rule.companion_path_found(companion.version(), folder))
    else
        vim.health.error(download_rule.not_found_notice(folder))
    end
end

---@param config fshttp.Config
local function check_download_tools(config)
    for _, tool in ipairs(health_rule.download_tools(vim.uv.os_uname().sysname)) do
        local path = vim.fn.exepath(tool)
        if path ~= "" then
            vim.health.ok(health_rule.tool_found(tool, path))
        elseif config.companion_path ~= nil then
            vim.health.info(health_rule.tool_not_needed(tool))
        else
            vim.health.error(health_rule.tool_missing(tool), health_rule.tool_fix(tool))
        end
    end
end

local function check_images()
    local reason = require("fshttp.image_placement").unsupported_reason("image/png")
    if reason == nil then
        vim.health.ok(health_rule.images_on)
    elseif reason == image_body.images_off_reason then
        vim.health.ok(health_rule.images_off)
    else
        vim.health.warn(health_rule.images_missing(reason), health_rule.images_fix)
    end
end

local function check_parsers()
    for _, parser in ipairs(health_rule.parsers) do
        if body_syntax.has_parser(parser.language) then
            vim.health.ok(health_rule.parser_found(parser))
        else
            vim.health.warn(health_rule.parser_missing(parser), health_rule.parser_fix(parser))
        end
    end
end

local function check_state()
    local state = companion.state()
    local row = status_line_text.state_row(state, require("fshttp.version"))
    local level = health_rule.state_level(state)
    if level == "ok" then
        vim.health.ok(row)
    elseif level == "error" then
        local notice = companion.state_notice()
        local fix = state == "stopped" and refusals.companion_stopped.detail or (notice and notice.message)
        vim.health.error(row, fix)
    else
        vim.health.info(row)
    end
end

local function check_version()
    local mismatch = companion.version_mismatch_notice()
    local version = companion.version()
    if mismatch then
        vim.health.warn(mismatch)
    elseif version then
        vim.health.ok(health_rule.version_matches(version, require("fshttp.version")))
    else
        vim.health.info(health_rule.version_pending)
    end
end

---@param config fshttp.Config
local function check_options(config)
    local problems = require("fshttp").setup_problems()
    vim.health.ok(problems and health_rule.setup_applied or health_rule.setup_not_called)
    for _, change in ipairs(options.changed_values(config)) do
        vim.health.info(options.change_text(change))
    end
    for _, problem in ipairs(problems or {}) do
        if problem.level == "ERROR" then
            vim.health.error(problem.message)
        else
            vim.health.warn(problem.message)
        end
    end
end

function M.check()
    local config = require("fshttp").config()

    vim.health.start("Required items")
    check_dotnet(config)
    check_companion(config)
    check_download_tools(config)

    vim.health.start("Optional items")
    check_images()
    check_parsers()

    vim.health.start("Companion")
    check_state()
    check_version()

    vim.health.start("Options")
    check_options(config)
end

return M
