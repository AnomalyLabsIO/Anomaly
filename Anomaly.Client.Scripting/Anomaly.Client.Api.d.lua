---@meta
-- Generated from the approved script catalog. Definitions only; no host objects are constructed.

---@class ClientConnectedEventArgs
---@field ServerIp string|nil # Read-only.
---@field ServerPort integer # Read-only.
local _type_ClientConnectedEventArgs = {}

---@class ClientReadyEventArgs
---@field SceneName string|nil # Read-only.
local _type_ClientReadyEventArgs = {}

---@class Player
---@field IsDestroyed boolean # Read-only. Whether the underlying player object has been destroyed.
---@field Nickname string|nil # Read-only. The nickname known to this client, or an empty string when unavailable.
---@field PlayerId integer # Read-only. The replicated player ID, or -1 after the player object is destroyed.
local _type_Player = {}

-- Host-installed subscription registry; callbacks receive frozen event data.
Events = {}
Events.Client = {}
Events.Client.Connected = {}
---@param callback fun(ev: ClientConnectedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Connected.Add(callback) end
---@param callbackOrToken fun(ev: ClientConnectedEventArgs|nil)|string
---@return boolean removed
function Events.Client.Connected.Remove(callbackOrToken) end
--- Reports the client's scene-ready lifecycle callback. This can precede the local player hub, populated world collections, and server enrollment disclosure.
Events.Client.Ready = {}
---@param callback fun(ev: ClientReadyEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Ready.Add(callback) end
---@param callbackOrToken fun(ev: ClientReadyEventArgs|nil)|string
---@return boolean removed
function Events.Client.Ready.Remove(callbackOrToken) end
