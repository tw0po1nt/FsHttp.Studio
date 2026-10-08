-- The defaults of the client, and the check of the opts that setup() takes. Pure, so the Lua core
-- suite runs it.
local M = {}

---@class fshttp.Config
---@field dotnet_path string?
---@field companion_path string?
---@field request_timeout_ms number
---@field block_mark { virtual_line: boolean, sign: boolean }
---@field response_buffer { split: "right"|"left"|"below"|"above", images: boolean, keys: boolean }
---@field status_line { lualine: boolean }

---@return fshttp.Config
function M.defaults()
    return {
        request_timeout_ms = 30000,
        block_mark = { virtual_line = true, sign = true },
        response_buffer = { split = "right", images = true, keys = true },
        status_line = { lualine = true },
    }
end

---@class fshttp.OptionProblem
---@field level "ERROR"|"WARN"
---@field key string the dotted name of the key
---@field message string

local splits = { "right", "left", "below", "above" }

---@param value any
---@return boolean
local function is_timeout(value)
    return type(value) == "number" and value == value and value >= 0 and value ~= math.huge
end

---@param value any
---@return boolean
local function is_boolean(value)
    return type(value) == "boolean"
end

---@param value any
---@return boolean
local function is_string(value)
    return type(value) == "string"
end

---@param value any
---@return boolean
local function is_split(value)
    for _, split in ipairs(splits) do
        if value == split then
            return true
        end
    end
    return false
end

-- A rule with `accept` is a key. A rule without it is a group of keys.
local schema = {
    dotnet_path = { accept = is_string, accepted = "a string or nil" },
    companion_path = { accept = is_string, accepted = "a string or nil" },
    request_timeout_ms = { accept = is_timeout, accepted = "a number of 0 or more (0 sets no bound)" },
    block_mark = {
        virtual_line = { accept = is_boolean, accepted = "true or false" },
        sign = { accept = is_boolean, accepted = "true or false" },
    },
    response_buffer = {
        split = { accept = is_split, accepted = "right, left, below, or above" },
        images = { accept = is_boolean, accepted = "true or false" },
        keys = { accept = is_boolean, accepted = "true or false" },
    },
    status_line = {
        lualine = { accept = is_boolean, accepted = "true or false" },
    },
}

---@param value any
---@return string
local function describe(value)
    if type(value) == "string" then
        return string.format("%q", value)
    elseif type(value) == "number" or type(value) == "boolean" then
        return tostring(value)
    end
    return "a " .. type(value)
end

---@param t table
---@return string[]
local function sorted_keys(t)
    local keys = {}
    for key in pairs(t) do
        keys[#keys + 1] = tostring(key)
    end
    table.sort(keys)
    return keys
end

---@param key string
---@param value any
---@param accepted string
---@param default any
---@return fshttp.OptionProblem
local function bad_value(key, value, accepted, default)
    return {
        level = "ERROR",
        key = key,
        message = string.format(
            "FsHttp.Studio: the option %s is %s. It takes %s. The option keeps its default (%s).",
            key,
            describe(value),
            accepted,
            tostring(default)
        ),
    }
end

---@param key string
---@return fshttp.OptionProblem
local function unknown_key(key)
    return {
        level = "WARN",
        key = key,
        message = string.format("FsHttp.Studio: setup() has no option %s. The client ignores it.", key),
    }
end

-- Applies the opts to the defaults. A bad value keeps its default, and an unknown key applies
-- nothing. Each other key applies. The two path options get a type check only.
---@param opts any
---@return fshttp.Config config
---@return fshttp.OptionProblem[] problems
function M.resolve(opts)
    local config = M.defaults()
    ---@type fshttp.OptionProblem[]
    local problems = {}
    if opts == nil then
        return config, problems
    end
    if type(opts) ~= "table" then
        problems[1] = {
            level = "ERROR",
            key = "opts",
            message = string.format(
                "FsHttp.Studio: setup() takes a table, and got %s. The client uses the defaults.",
                describe(opts)
            ),
        }
        return config, problems
    end
    for _, name in ipairs(sorted_keys(opts)) do
        local value = opts[name]
        local rule = schema[name]
        if rule == nil then
            problems[#problems + 1] = unknown_key(name)
        elseif rule.accept then
            if rule.accept(value) then
                config[name] = value
            elseif value ~= nil then
                problems[#problems + 1] = bad_value(name, value, rule.accepted, config[name])
            end
        elseif type(value) ~= "table" then
            if value ~= nil then
                problems[#problems + 1] = bad_value(name, value, "a table", "the defaults of its keys")
            end
        else
            for _, sub in ipairs(sorted_keys(value)) do
                local key = name .. "." .. sub
                local sub_rule = rule[sub]
                if sub_rule == nil then
                    problems[#problems + 1] = unknown_key(key)
                elseif sub_rule.accept(value[sub]) then
                    config[name][sub] = value[sub]
                else
                    problems[#problems + 1] = bad_value(key, value[sub], sub_rule.accepted, config[name][sub])
                end
            end
        end
    end
    return config, problems
end

-- The names of the path options that differ between two configurations.
---@param old fshttp.Config
---@param new fshttp.Config
---@return string[]
function M.changed_paths(old, new)
    local changed = {}
    for _, name in ipairs({ "dotnet_path", "companion_path" }) do
        if old[name] ~= new[name] then
            changed[#changed + 1] = name
        end
    end
    return changed
end

return M
