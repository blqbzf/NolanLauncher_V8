-- require_shim.lua - Provides require() for WoW 3.3.5 AIO compatibility
-- WoW 3.3.5 has built-in `bit` library but no `require` function

if require then return end

local loaded = {}
local preload = {}

-- Pre-register built-in WoW modules
preload["bit"] = function() return bit end
preload["bit.numberlua"] = function() return bit end
preload["bit32"] = function() return bit end
preload["bit53"] = function() return bit end

function require(name)
    if loaded[name] then return loaded[name] end
    if preload[name] then
        local mod = preload[name]()
        loaded[name] = mod
        return mod
    end
    -- Try to find as a global
    local mod = _G[name]
    if mod then
        loaded[name] = mod
        return mod
    end
    error("module '" .. name .. "' not found")
end
