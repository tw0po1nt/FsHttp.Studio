local M = {}

function M.new_env()
    local env = setmetatable({}, {
        __index = function(_, key)
            if key == "vim" then
                return nil
            end
            return _G[key]
        end,
    })
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
