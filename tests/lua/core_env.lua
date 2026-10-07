local M = {}

-- A core module sees these pure Lua globals and nothing else, so it cannot reach vim, io, os, or package.
local allowed = {
    "assert",
    "error",
    "getmetatable",
    "ipairs",
    "math",
    "next",
    "pairs",
    "pcall",
    "select",
    "setmetatable",
    "string",
    "table",
    "tonumber",
    "tostring",
    "type",
    "unpack",
}

function M.new_env()
    local env = {}
    for _, key in ipairs(allowed) do
        env[key] = _G[key]
    end
    env._G = env
    env.require = function(name)
        return M.load(name, env)
    end
    return env
end

function M.load(module, env)
    env = env or M.new_env()
    local file = "lua/" .. module:gsub("%.", "/") .. ".lua"
    return assert(loadfile(file, nil, env))()
end

return M
