local M = {}

-- `epoch` numbers the start sequence that the client owns now.
---@class fshttp.RestartSession
---@field epoch integer
---@field stopping boolean

---@alias fshttp.RestartDecision "notifyRestarting"|"beginRestart"

---@return fshttp.RestartSession
function M.initial()
    return { epoch = 0, stopping = false }
end

-- The epoch moves on at once, so each later event of the old companion is stale.
---@param session fshttp.RestartSession
---@return fshttp.RestartSession
---@return fshttp.RestartDecision
function M.on_command(session)
    if session.stopping then
        return session, "notifyRestarting"
    end
    return { epoch = session.epoch + 1, stopping = true }, "beginRestart"
end

---@param session fshttp.RestartSession
---@return fshttp.RestartSession
function M.on_stopped(session)
    return { epoch = session.epoch, stopping = false }
end

---@param session fshttp.RestartSession
---@return fshttp.RestartSession
function M.on_quit(session)
    return { epoch = session.epoch + 1, stopping = session.stopping }
end

---@param session fshttp.RestartSession
---@param epoch integer the epoch of the start sequence that sent the event
---@return boolean
function M.is_current(session, epoch)
    return session.epoch == epoch
end

return M
