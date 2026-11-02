---@meta
-- Generated from the approved script catalog. Definitions only; no host objects are constructed.

-- Standard-library subset installed by the Anomaly Lua sandbox.
-- Use the generated runtime.builtin settings with these references.
---@class _G
_G = {}
---@type string
_VERSION = ""

---Returns its arguments when value is truthy; otherwise raises a Lua error.
---@generic T
---@param value T
---@param message? any
---@param ... any
---@return T
---@return any ...
function assert(value, message, ...) end

---@param message string|number
---@param level? integer
function error(message, level) end

---@generic V
---@param list V[]
---@return fun(list: V[], index: integer): integer, V
---@return V[] list
---@return integer index
function ipairs(list) end

---@generic K, V
---@param value table<K, V>
---@param index? K
---@return K|nil index
---@return V|nil value
function next(value, index) end

---@generic K, V
---@param value table<K, V>
---@return fun(value: table<K, V>, index?: K): K, V
---@return table<K, V> value
function pairs(value) end

---@param callback function
---@param ... any
---@return boolean success
---@return any ... resultsOrError
function pcall(callback, ...) end

---@overload fun(index: "#", ...: any): integer
---@param index integer
---@param ... any
---@return any ...
function select(index, ...) end

---@param value any
---@param base? integer
---@return number|nil
function tonumber(value, base) end

---@param value any
---@return string
function tostring(value) end

---@param value any
---@return "nil"|"number"|"string"|"boolean"|"table"|"function"|"thread"|"userdata"
function type(value) end

---@param callback function
---@param handler? fun(error: any): any
---@param ... any
---@return boolean success
---@return any ... resultsOrError
function xpcall(callback, handler, ...) end

---@class mathlib
---@field pi number
---@field huge number # Largest finite double in this runtime.
math = {}

---@param value number
---@return number
function math.abs(value) end
---@param value number
---@return number
function math.acos(value) end
---@param value number
---@return number
function math.asin(value) end
---@param value number
---@return number
function math.atan(value) end
---@param y number
---@param x number
---@return number
function math.atan2(y, x) end
---@param value number
---@return integer
function math.ceil(value) end
---@param value number
---@return number
function math.cos(value) end
---@param value number
---@return number
function math.cosh(value) end
---@param value number
---@return number
function math.deg(value) end
---@param value number
---@return number
function math.exp(value) end
---@param value number
---@return integer
function math.floor(value) end
---@param value number
---@param divisor number
---@return number
function math.fmod(value, divisor) end
---@param value number
---@return number mantissa
---@return integer exponent
function math.frexp(value) end
---@param mantissa number
---@param exponent number
---@return number
function math.ldexp(mantissa, exponent) end
---@param value number
---@param base? number
---@return number
function math.log(value, base) end
---@param value number
---@param ... number
---@return number
function math.max(value, ...) end
---@param value number
---@param ... number
---@return number
function math.min(value, ...) end
---@param value number
---@return integer integral
---@return number fractional
function math.modf(value) end
---@param value number
---@param exponent number
---@return number
function math.pow(value, exponent) end
---@param value number
---@return number
function math.rad(value) end
---@overload fun(upper: integer): integer
---@overload fun(lower: integer, upper: integer): integer
---@return number
function math.random() end
---@param seed number
function math.randomseed(seed) end
---@param value number
---@return number
function math.sin(value) end
---@param value number
---@return number
function math.sinh(value) end
---@param value number
---@return number
function math.sqrt(value) end
---@param value number
---@return number
function math.tan(value) end
---@param value number
---@return number
function math.tanh(value) end

---@class stringlib
string = {}

---@param value string|number
---@param first? integer
---@param last? integer
---@return integer ...
function string.byte(value, first, last) end
---@param ... integer
---@return string
function string.char(...) end
---@param value? string|number
---@param part? string|number
---@return boolean
function string.contains(value, part) end
---@param value? string|number
---@param suffix? string|number
---@return boolean
function string.endsWith(value, suffix) end
---@param value string|number
---@param pattern string|number
---@param first? integer
---@param plain? boolean
---@return integer|nil first
---@return integer|nil last
---@return any ... captures
function string.find(value, pattern, first, plain) end
---@param format string|number
---@param ... any
---@return string
function string.format(format, ...) end
---@param value string|number
---@param pattern string|number
---@return fun(): any, ...
function string.gmatch(value, pattern) end
---@param value string|number
---@param pattern string|number
---@param replacement string|number|table|function
---@param limit? integer
---@return string
---@return integer count
function string.gsub(value, pattern, replacement, limit) end
---@param value string|number
---@return integer
function string.len(value) end
---@param value string|number
---@return string
function string.lower(value) end
---@param value string|number
---@param pattern string|number
---@param first? integer
---@return any ... captures
function string.match(value, pattern, first) end
---@param value string|number
---@param count number
---@param separator? string|number
---@return string
function string.rep(value, count, separator) end
---@param value string|number
---@return string
function string.reverse(value) end
---@param value? string|number
---@param prefix? string|number
---@return boolean
function string.startsWith(value, prefix) end
---@param value string|number
---@param first? integer
---@param last? integer
---@return string
function string.sub(value, first, last) end
---Returns UTF-16 code units from the selected string range.
---@param value string|number
---@param first? integer
---@param last? integer
---@return integer ...
function string.unicode(value, first, last) end
---@param value string|number
---@return string
function string.upper(value) end

---@class tablelib
table = {}

---@param list (string|number)[]
---@param separator? string|number
---@param first? integer
---@param last? integer
---@return string
function table.concat(list, separator, first, last) end
---@generic T
---@overload fun(list: T[], value: T): T[]
---@param list T[]
---@param position integer
---@param value T
---@return T[]
function table.insert(list, position, value) end
---@param ... any
---@return table # Array of arguments with an n field containing their count.
function table.pack(...) end
---@generic T
---@param list T[]
---@param position? integer
---@return T|nil
function table.remove(list, position) end
---@generic T
---@param list T[]
---@param compare? fun(left: T, right: T): boolean
---@return T[]
function table.sort(list, compare) end
---@generic T
---@param list T[]
---@param first? integer
---@param last? integer
---@return T ...
function table.unpack(list, first, last) end

-- Host enum registries are read-only. Values are canonical names; enum inputs also accept case-insensitive names.
-- Flags inputs accept comma-separated declared names and the unnamed zero string '0'; other numeric inputs are rejected.
Enums = {}

---@alias Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase
---| "Ended"
---| "Ending"
---| "InProgress"
---| "Restarted"
---| "Starting"
---| "WaitingForPlayers"
Enums["Anomaly.Shared.Networking.Messages.RoundPhase"] = {
    ["Ended"] = "Ended",
    ["Ending"] = "Ending",
    ["InProgress"] = "InProgress",
    ["Restarted"] = "Restarted",
    ["Starting"] = "Starting",
    ["WaitingForPlayers"] = "WaitingForPlayers",
}
Enums.Anomaly = {}
Enums.Anomaly.Shared = {}
Enums.Anomaly.Shared.Networking = {}
Enums.Anomaly.Shared.Networking.Messages = {}
Enums.Anomaly.Shared.Networking.Messages.RoundPhase = Enums["Anomaly.Shared.Networking.Messages.RoundPhase"]
Enums.RoundPhase = Enums["Anomaly.Shared.Networking.Messages.RoundPhase"]
RoundPhase = Enums["Anomaly.Shared.Networking.Messages.RoundPhase"]

---@alias Enum_ActionCategory
---| "Communication"
---| "Gameplay"
---| "Movement"
---| "Scp079"
---| "System"
---| "Unbindable"
---| "Weapons"
Enums["ActionCategory"] = {
    ["Communication"] = "Communication",
    ["Gameplay"] = "Gameplay",
    ["Movement"] = "Movement",
    ["Scp079"] = "Scp079",
    ["System"] = "System",
    ["Unbindable"] = "Unbindable",
    ["Weapons"] = "Weapons",
}
ActionCategory = Enums["ActionCategory"]

---@alias Enum_ActionName
---| "AltVoiceChat"
---| "CharacterInfo"
---| "DebugLogMenu"
---| "GameConsole"
---| "HideGUI"
---| "InspectItem"
---| "Interact"
---| "Inventory"
---| "Jump"
---| "MoveBackward"
---| "MoveForward"
---| "MoveLeft"
---| "MoveRight"
---| "NoClipFogToggle"
---| "Noclip"
---| "PauseMenu"
---| "PlayerList"
---| "Reload"
---| "RemoteAdmin"
---| "Run"
---| "Scp079Blackout"
---| "Scp079BreachScanner"
---| "Scp079FreeLook"
---| "Scp079LockDoor"
---| "Scp079Lockdown"
---| "Scp079PingLocation"
---| "Scp079UnlockAll"
---| "Shoot"
---| "Sneak"
---| "ThrowItem"
---| "ToggleFlashlight"
---| "VoiceChat"
---| "WeaponAlt"
---| "Zoom"
Enums["ActionName"] = {
    ["AltVoiceChat"] = "AltVoiceChat",
    ["CharacterInfo"] = "CharacterInfo",
    ["DebugLogMenu"] = "DebugLogMenu",
    ["GameConsole"] = "GameConsole",
    ["HideGUI"] = "HideGUI",
    ["InspectItem"] = "InspectItem",
    ["Interact"] = "Interact",
    ["Inventory"] = "Inventory",
    ["Jump"] = "Jump",
    ["MoveBackward"] = "MoveBackward",
    ["MoveForward"] = "MoveForward",
    ["MoveLeft"] = "MoveLeft",
    ["MoveRight"] = "MoveRight",
    ["NoClipFogToggle"] = "NoClipFogToggle",
    ["Noclip"] = "Noclip",
    ["PauseMenu"] = "PauseMenu",
    ["PlayerList"] = "PlayerList",
    ["Reload"] = "Reload",
    ["RemoteAdmin"] = "RemoteAdmin",
    ["Run"] = "Run",
    ["Scp079Blackout"] = "Scp079Blackout",
    ["Scp079BreachScanner"] = "Scp079BreachScanner",
    ["Scp079FreeLook"] = "Scp079FreeLook",
    ["Scp079LockDoor"] = "Scp079LockDoor",
    ["Scp079Lockdown"] = "Scp079Lockdown",
    ["Scp079PingLocation"] = "Scp079PingLocation",
    ["Scp079UnlockAll"] = "Scp079UnlockAll",
    ["Shoot"] = "Shoot",
    ["Sneak"] = "Sneak",
    ["ThrowItem"] = "ThrowItem",
    ["ToggleFlashlight"] = "ToggleFlashlight",
    ["VoiceChat"] = "VoiceChat",
    ["WeaponAlt"] = "WeaponAlt",
    ["Zoom"] = "Zoom",
}
ActionName = Enums["ActionName"]

---@alias Enum_AlphaWarheadNukesitePanel_002e_DiodeType
---| "BlastDoor"
---| "InProgress"
---| "OutsideDoor"
Enums["AlphaWarheadNukesitePanel.DiodeType"] = {
    ["BlastDoor"] = "BlastDoor",
    ["InProgress"] = "InProgress",
    ["OutsideDoor"] = "OutsideDoor",
}
Enums.AlphaWarheadNukesitePanel = {}
Enums.AlphaWarheadNukesitePanel.DiodeType = Enums["AlphaWarheadNukesitePanel.DiodeType"]
AlphaWarheadNukesitePanel = {}
AlphaWarheadNukesitePanel.DiodeType = Enums["AlphaWarheadNukesitePanel.DiodeType"]

---@alias Enum_AlphaWarheadNukesitePanel_002e_PanelColliderId
---| "Cancel"
---| "Lever"
Enums["AlphaWarheadNukesitePanel.PanelColliderId"] = {
    ["Cancel"] = "Cancel",
    ["Lever"] = "Lever",
}
Enums.AlphaWarheadNukesitePanel.PanelColliderId = Enums["AlphaWarheadNukesitePanel.PanelColliderId"]
AlphaWarheadNukesitePanel.PanelColliderId = Enums["AlphaWarheadNukesitePanel.PanelColliderId"]

---@alias Enum_AlphaWarheadOutsitePanel_002e_UpdateType
---| "Disabled"
---| "Empty"
---| "Ready"
---| "Skip"
---| "Time"
---| "Wait"
---| "Zero"
Enums["AlphaWarheadOutsitePanel.UpdateType"] = {
    ["Disabled"] = "Disabled",
    ["Empty"] = "Empty",
    ["Ready"] = "Ready",
    ["Skip"] = "Skip",
    ["Time"] = "Time",
    ["Wait"] = "Wait",
    ["Zero"] = "Zero",
}
Enums.AlphaWarheadOutsitePanel = {}
Enums.AlphaWarheadOutsitePanel.UpdateType = Enums["AlphaWarheadOutsitePanel.UpdateType"]
AlphaWarheadOutsitePanel = {}
AlphaWarheadOutsitePanel.UpdateType = Enums["AlphaWarheadOutsitePanel.UpdateType"]

---@alias Enum_AuthStatusType
---| "Connecting"
---| "Failure"
---| "PlatformAuthFailure"
---| "Success"
Enums["AuthStatusType"] = {
    ["Connecting"] = "Connecting",
    ["Failure"] = "Failure",
    ["PlatformAuthFailure"] = "PlatformAuthFailure",
    ["Success"] = "Success",
}
AuthStatusType = Enums["AuthStatusType"]

---@alias Enum_BanHandler_002e_BanType
---| "IP"
---| "NULL"
---| "UserId"
Enums["BanHandler.BanType"] = {
    ["IP"] = "IP",
    ["NULL"] = "NULL",
    ["UserId"] = "UserId",
}
Enums.BanHandler = {}
Enums.BanHandler.BanType = Enums["BanHandler.BanType"]
BanHandler = {}
BanHandler.BanType = Enums["BanHandler.BanType"]

---@alias Enum_Broadcast_002e_BroadcastFlags
---| "AdminChat"
---| "Normal"
---| "Truncated"
Enums["Broadcast.BroadcastFlags"] = {
    ["AdminChat"] = "AdminChat",
    ["Normal"] = "Normal",
    ["Truncated"] = "Truncated",
}
Enums.Broadcast = {}
Enums.Broadcast.BroadcastFlags = Enums["Broadcast.BroadcastFlags"]
Broadcast = {}
Broadcast.BroadcastFlags = Enums["Broadcast.BroadcastFlags"]

---@alias Enum_CentralAuthPreauthFlags
---| "AuthRejected"
---| "GloballyBanned"
---| "IgnoreBans"
---| "IgnoreGeoblock"
---| "IgnoreWhitelist"
---| "None"
---| "NorthwoodStaff"
---| "ReservedSlot"
Enums["CentralAuthPreauthFlags"] = {
    ["AuthRejected"] = "AuthRejected",
    ["GloballyBanned"] = "GloballyBanned",
    ["IgnoreBans"] = "IgnoreBans",
    ["IgnoreGeoblock"] = "IgnoreGeoblock",
    ["IgnoreWhitelist"] = "IgnoreWhitelist",
    ["None"] = "None",
    ["NorthwoodStaff"] = "NorthwoodStaff",
    ["ReservedSlot"] = "ReservedSlot",
}
CentralAuthPreauthFlags = Enums["CentralAuthPreauthFlags"]

---@alias Enum_ChallengeState
---| "Done"
---| "DoneRejoin"
---| "None"
---| "Processing"
Enums["ChallengeState"] = {
    ["Done"] = "Done",
    ["DoneRejoin"] = "DoneRejoin",
    ["None"] = "None",
    ["Processing"] = "Processing",
}
ChallengeState = Enums["ChallengeState"]

---@alias Enum_ChallengeType
---| "MD5"
---| "Reply"
---| "SHA1"
Enums["ChallengeType"] = {
    ["MD5"] = "MD5",
    ["Reply"] = "Reply",
    ["SHA1"] = "SHA1",
}
ChallengeType = Enums["ChallengeType"]

---@alias Enum_CoffeeTranslation
---| "Astral"
---| "Banzai"
---| "Beryl"
---| "Ced"
---| "ChemicalWolf"
---| "CombatSwift"
---| "Crimson"
---| "Danstr"
---| "Decimalis"
---| "Dellarie"
---| "Ender"
---| "EnoughCoffee"
---| "Georgia"
---| "Goop"
---| "Goop2"
---| "Jegg"
---| "JesusQC"
---| "Lion"
---| "M4st3r0re"
---| "MajorFox"
---| "Meep"
---| "Metaverse"
---| "Mikel"
---| "Morantol"
---| "Naveil"
---| "Nightshade"
---| "Orhu"
---| "Pixl"
---| "Plank"
---| "RedZ"
---| "Ryan"
---| "Secret"
---| "Secret3"
---| "Sixxy"
---| "Spoon"
---| "Tychus"
---| "UIU"
---| "X3j"
---| "alleyes"
Enums["CoffeeTranslation"] = {
    ["Astral"] = "Astral",
    ["Banzai"] = "Banzai",
    ["Beryl"] = "Beryl",
    ["Ced"] = "Ced",
    ["ChemicalWolf"] = "ChemicalWolf",
    ["CombatSwift"] = "CombatSwift",
    ["Crimson"] = "Crimson",
    ["Danstr"] = "Danstr",
    ["Decimalis"] = "Decimalis",
    ["Dellarie"] = "Dellarie",
    ["Ender"] = "Ender",
    ["EnoughCoffee"] = "EnoughCoffee",
    ["Georgia"] = "Georgia",
    ["Goop"] = "Goop",
    ["Goop2"] = "Goop2",
    ["Jegg"] = "Jegg",
    ["JesusQC"] = "JesusQC",
    ["Lion"] = "Lion",
    ["M4st3r0re"] = "M4st3r0re",
    ["MajorFox"] = "MajorFox",
    ["Meep"] = "Meep",
    ["Metaverse"] = "Metaverse",
    ["Mikel"] = "Mikel",
    ["Morantol"] = "Morantol",
    ["Naveil"] = "Naveil",
    ["Nightshade"] = "Nightshade",
    ["Orhu"] = "Orhu",
    ["Pixl"] = "Pixl",
    ["Plank"] = "Plank",
    ["RedZ"] = "RedZ",
    ["Ryan"] = "Ryan",
    ["Secret"] = "Secret",
    ["Secret3"] = "Secret3",
    ["Sixxy"] = "Sixxy",
    ["Spoon"] = "Spoon",
    ["Tychus"] = "Tychus",
    ["UIU"] = "UIU",
    ["X3j"] = "X3j",
    ["alleyes"] = "alleyes",
}
CoffeeTranslation = Enums["CoffeeTranslation"]

---@alias Enum_CustomLiteNetLib4MirrorTransport_002e_ClientType
---| "GameClient"
---| "VerificationService"
Enums["CustomLiteNetLib4MirrorTransport.ClientType"] = {
    ["GameClient"] = "GameClient",
    ["VerificationService"] = "VerificationService",
}
Enums.CustomLiteNetLib4MirrorTransport = {}
Enums.CustomLiteNetLib4MirrorTransport.ClientType = Enums["CustomLiteNetLib4MirrorTransport.ClientType"]
CustomLiteNetLib4MirrorTransport = {}
CustomLiteNetLib4MirrorTransport.ClientType = Enums["CustomLiteNetLib4MirrorTransport.ClientType"]

---@alias Enum_DebugLevel
---| "Elevated"
---| "Full"
---| "Muted"
---| "Narrow"
---| "Normal"
Enums["DebugLevel"] = {
    ["Elevated"] = "Elevated",
    ["Full"] = "Full",
    ["Muted"] = "Muted",
    ["Narrow"] = "Narrow",
    ["Normal"] = "Normal",
}
DebugLevel = Enums["DebugLevel"]

---@alias Enum_EncryptedChannelManager_002e_EncryptedChannel
---| "AdminChat"
---| "GameConsole"
---| "RemoteAdmin"
Enums["EncryptedChannelManager.EncryptedChannel"] = {
    ["AdminChat"] = "AdminChat",
    ["GameConsole"] = "GameConsole",
    ["RemoteAdmin"] = "RemoteAdmin",
}
Enums.EncryptedChannelManager = {}
Enums.EncryptedChannelManager.EncryptedChannel = Enums["EncryptedChannelManager.EncryptedChannel"]
EncryptedChannelManager = {}
EncryptedChannelManager.EncryptedChannel = Enums["EncryptedChannelManager.EncryptedChannel"]

---@alias Enum_EncryptedChannelManager_002e_SecurityLevel
---| "EncryptedAndAuthenticated"
---| "Unsecured"
Enums["EncryptedChannelManager.SecurityLevel"] = {
    ["EncryptedAndAuthenticated"] = "EncryptedAndAuthenticated",
    ["Unsecured"] = "Unsecured",
}
Enums.EncryptedChannelManager.SecurityLevel = Enums["EncryptedChannelManager.SecurityLevel"]
EncryptedChannelManager.SecurityLevel = Enums["EncryptedChannelManager.SecurityLevel"]

---@alias Enum_Escape_002e_EscapeScenarioType
---| "ClassD"
---| "CuffedClassD"
---| "CuffedScientist"
---| "Custom"
---| "None"
---| "Scientist"
Enums["Escape.EscapeScenarioType"] = {
    ["ClassD"] = "ClassD",
    ["CuffedClassD"] = "CuffedClassD",
    ["CuffedScientist"] = "CuffedScientist",
    ["Custom"] = "Custom",
    ["None"] = "None",
    ["Scientist"] = "Scientist",
}
Enums.Escape = {}
Enums.Escape.EscapeScenarioType = Enums["Escape.EscapeScenarioType"]
Escape = {}
Escape.EscapeScenarioType = Enums["Escape.EscapeScenarioType"]

---@alias Enum_ExplosionType
---| "Cola"
---| "Custom"
---| "Disruptor"
---| "Grenade"
---| "Jailbird"
---| "PinkCandy"
---| "SCP018"
Enums["ExplosionType"] = {
    ["Cola"] = "Cola",
    ["Custom"] = "Custom",
    ["Disruptor"] = "Disruptor",
    ["Grenade"] = "Grenade",
    ["Jailbird"] = "Jailbird",
    ["PinkCandy"] = "PinkCandy",
    ["SCP018"] = "SCP018",
}
ExplosionType = Enums["ExplosionType"]

---@alias Enum_FalloffType
---| "Exponential"
---| "Footstep"
---| "Linear"
Enums["FalloffType"] = {
    ["Exponential"] = "Exponential",
    ["Footstep"] = "Footstep",
    ["Linear"] = "Linear",
}
FalloffType = Enums["FalloffType"]

---@alias Enum_FavoriteAndHistory_002e_StorageLocation
---| "Favorites"
---| "History"
---| "IPHistory"
Enums["FavoriteAndHistory.StorageLocation"] = {
    ["Favorites"] = "Favorites",
    ["History"] = "History",
    ["IPHistory"] = "IPHistory",
}
Enums.FavoriteAndHistory = {}
Enums.FavoriteAndHistory.StorageLocation = Enums["FavoriteAndHistory.StorageLocation"]
FavoriteAndHistory = {}
FavoriteAndHistory.StorageLocation = Enums["FavoriteAndHistory.StorageLocation"]

---@alias Enum_FriendlyFireAction
---| "Ban"
---| "Kick"
---| "Kill"
---| "Noop"
Enums["FriendlyFireAction"] = {
    ["Ban"] = "Ban",
    ["Kick"] = "Kick",
    ["Kill"] = "Kill",
    ["Noop"] = "Noop",
}
FriendlyFireAction = Enums["FriendlyFireAction"]

---@alias Enum_GeoblockingMode
---| "Blacklist"
---| "None"
---| "Whitelist"
Enums["GeoblockingMode"] = {
    ["Blacklist"] = "Blacklist",
    ["None"] = "None",
    ["Whitelist"] = "Whitelist",
}
GeoblockingMode = Enums["GeoblockingMode"]

---@alias Enum_GraphicsApiDropdown_002e_GraphicsApi
---| "Auto"
---| "DirectX11"
---| "DirectX12"
---| "Vulkan"
Enums["GraphicsApiDropdown.GraphicsApi"] = {
    ["Auto"] = "Auto",
    ["DirectX11"] = "DirectX11",
    ["DirectX12"] = "DirectX12",
    ["Vulkan"] = "Vulkan",
}
Enums.GraphicsApiDropdown = {}
Enums.GraphicsApiDropdown.GraphicsApi = Enums["GraphicsApiDropdown.GraphicsApi"]
GraphicsApiDropdown = {}
GraphicsApiDropdown.GraphicsApi = Enums["GraphicsApiDropdown.GraphicsApi"]

---@alias Enum_HitboxType
---| "Body"
---| "Headshot"
---| "Limb"
Enums["HitboxType"] = {
    ["Body"] = "Body",
    ["Headshot"] = "Headshot",
    ["Limb"] = "Limb",
}
HitboxType = Enums["HitboxType"]

---@alias Enum_HitmarkerType
---| "Blocked"
---| "None"
---| "Regular"
Enums["HitmarkerType"] = {
    ["Blocked"] = "Blocked",
    ["None"] = "None",
    ["Regular"] = "Regular",
}
HitmarkerType = Enums["HitmarkerType"]

---@alias Enum_ItemCategory
---| "Ammo"
---| "Armor"
---| "Firearm"
---| "Grenade"
---| "Keycard"
---| "Medical"
---| "None"
---| "Radio"
---| "SCPItem"
---| "SpecialWeapon"
Enums["ItemCategory"] = {
    ["Ammo"] = "Ammo",
    ["Armor"] = "Armor",
    ["Firearm"] = "Firearm",
    ["Grenade"] = "Grenade",
    ["Keycard"] = "Keycard",
    ["Medical"] = "Medical",
    ["None"] = "None",
    ["Radio"] = "Radio",
    ["SCPItem"] = "SCPItem",
    ["SpecialWeapon"] = "SpecialWeapon",
}
ItemCategory = Enums["ItemCategory"]

---@alias Enum_ItemTierFlags
---| "Common"
---| "ExtraRare"
---| "MilitaryGrade"
---| "Rare"
Enums["ItemTierFlags"] = {
    ["Common"] = "Common",
    ["ExtraRare"] = "ExtraRare",
    ["MilitaryGrade"] = "MilitaryGrade",
    ["Rare"] = "Rare",
}
ItemTierFlags = Enums["ItemTierFlags"]

---@alias Enum_ItemType
---| "Adrenaline"
---| "Ammo12gauge"
---| "Ammo44cal"
---| "Ammo556x45"
---| "Ammo762x39"
---| "Ammo9x19"
---| "AntiSCP207"
---| "ArmorCombat"
---| "ArmorHeavy"
---| "ArmorLight"
---| "Coal"
---| "Coin"
---| "DebugRagdollMover"
---| "Flashlight"
---| "GrenadeFlash"
---| "GrenadeHE"
---| "GunA7"
---| "GunAK"
---| "GunCOM15"
---| "GunCOM18"
---| "GunCom45"
---| "GunCrossvec"
---| "GunE11SR"
---| "GunFRMG0"
---| "GunFSP9"
---| "GunLogicer"
---| "GunRevolver"
---| "GunSCP127"
---| "GunShotgun"
---| "Jailbird"
---| "KeycardChaosInsurgency"
---| "KeycardContainmentEngineer"
---| "KeycardCustomManagement"
---| "KeycardCustomMetalCase"
---| "KeycardCustomSite02"
---| "KeycardCustomTaskForce"
---| "KeycardFacilityManager"
---| "KeycardGuard"
---| "KeycardJanitor"
---| "KeycardMTFCaptain"
---| "KeycardMTFOperative"
---| "KeycardMTFPrivate"
---| "KeycardO5"
---| "KeycardResearchCoordinator"
---| "KeycardScientist"
---| "KeycardZoneManager"
---| "Lantern"
---| "MarshmallowItem"
---| "Medkit"
---| "MicroHID"
---| "None"
---| "Painkillers"
---| "ParticleDisruptor"
---| "Radio"
---| "SCP018"
---| "SCP1344"
---| "SCP1507Tape"
---| "SCP1509"
---| "SCP1576"
---| "SCP1853"
---| "SCP207"
---| "SCP2176"
---| "SCP244a"
---| "SCP244b"
---| "SCP268"
---| "SCP330"
---| "SCP500"
---| "Scp021J"
---| "Snowball"
---| "SpecialCoal"
---| "SurfaceAccessPass"
Enums["ItemType"] = {
    ["Adrenaline"] = "Adrenaline",
    ["Ammo12gauge"] = "Ammo12gauge",
    ["Ammo44cal"] = "Ammo44cal",
    ["Ammo556x45"] = "Ammo556x45",
    ["Ammo762x39"] = "Ammo762x39",
    ["Ammo9x19"] = "Ammo9x19",
    ["AntiSCP207"] = "AntiSCP207",
    ["ArmorCombat"] = "ArmorCombat",
    ["ArmorHeavy"] = "ArmorHeavy",
    ["ArmorLight"] = "ArmorLight",
    ["Coal"] = "Coal",
    ["Coin"] = "Coin",
    ["DebugRagdollMover"] = "DebugRagdollMover",
    ["Flashlight"] = "Flashlight",
    ["GrenadeFlash"] = "GrenadeFlash",
    ["GrenadeHE"] = "GrenadeHE",
    ["GunA7"] = "GunA7",
    ["GunAK"] = "GunAK",
    ["GunCOM15"] = "GunCOM15",
    ["GunCOM18"] = "GunCOM18",
    ["GunCom45"] = "GunCom45",
    ["GunCrossvec"] = "GunCrossvec",
    ["GunE11SR"] = "GunE11SR",
    ["GunFRMG0"] = "GunFRMG0",
    ["GunFSP9"] = "GunFSP9",
    ["GunLogicer"] = "GunLogicer",
    ["GunRevolver"] = "GunRevolver",
    ["GunSCP127"] = "GunSCP127",
    ["GunShotgun"] = "GunShotgun",
    ["Jailbird"] = "Jailbird",
    ["KeycardChaosInsurgency"] = "KeycardChaosInsurgency",
    ["KeycardContainmentEngineer"] = "KeycardContainmentEngineer",
    ["KeycardCustomManagement"] = "KeycardCustomManagement",
    ["KeycardCustomMetalCase"] = "KeycardCustomMetalCase",
    ["KeycardCustomSite02"] = "KeycardCustomSite02",
    ["KeycardCustomTaskForce"] = "KeycardCustomTaskForce",
    ["KeycardFacilityManager"] = "KeycardFacilityManager",
    ["KeycardGuard"] = "KeycardGuard",
    ["KeycardJanitor"] = "KeycardJanitor",
    ["KeycardMTFCaptain"] = "KeycardMTFCaptain",
    ["KeycardMTFOperative"] = "KeycardMTFOperative",
    ["KeycardMTFPrivate"] = "KeycardMTFPrivate",
    ["KeycardO5"] = "KeycardO5",
    ["KeycardResearchCoordinator"] = "KeycardResearchCoordinator",
    ["KeycardScientist"] = "KeycardScientist",
    ["KeycardZoneManager"] = "KeycardZoneManager",
    ["Lantern"] = "Lantern",
    ["MarshmallowItem"] = "MarshmallowItem",
    ["Medkit"] = "Medkit",
    ["MicroHID"] = "MicroHID",
    ["None"] = "None",
    ["Painkillers"] = "Painkillers",
    ["ParticleDisruptor"] = "ParticleDisruptor",
    ["Radio"] = "Radio",
    ["SCP018"] = "SCP018",
    ["SCP1344"] = "SCP1344",
    ["SCP1507Tape"] = "SCP1507Tape",
    ["SCP1509"] = "SCP1509",
    ["SCP1576"] = "SCP1576",
    ["SCP1853"] = "SCP1853",
    ["SCP207"] = "SCP207",
    ["SCP2176"] = "SCP2176",
    ["SCP244a"] = "SCP244a",
    ["SCP244b"] = "SCP244b",
    ["SCP268"] = "SCP268",
    ["SCP330"] = "SCP330",
    ["SCP500"] = "SCP500",
    ["Scp021J"] = "Scp021J",
    ["Snowball"] = "Snowball",
    ["SpecialCoal"] = "SpecialCoal",
    ["SurfaceAccessPass"] = "SurfaceAccessPass",
}
ItemType = Enums["ItemType"]

---@alias Enum_LegacyInterfaces
---| "AlphaWarhead"
---| "ClassDEscaped"
---| "CreateSinkhole"
---| "FollowOrders"
---| "GiveOrders"
---| "Hidden"
---| "KilledByScps"
---| "PassiveAbility"
---| "RoundRestartTimer"
---| "SameRank"
---| "ScientistsSurvived"
---| "SpecialAbility"
---| "TerminatedScps"
---| "UseSinkhole"
---| "WarheadDetonated"
---| "WarheadUnused"
---| "YouDied"
---| "YouWereKilledByNicknameLabel"
---| "YouWereKilledByRoleLabel"
Enums["LegacyInterfaces"] = {
    ["AlphaWarhead"] = "AlphaWarhead",
    ["ClassDEscaped"] = "ClassDEscaped",
    ["CreateSinkhole"] = "CreateSinkhole",
    ["FollowOrders"] = "FollowOrders",
    ["GiveOrders"] = "GiveOrders",
    ["Hidden"] = "Hidden",
    ["KilledByScps"] = "KilledByScps",
    ["PassiveAbility"] = "PassiveAbility",
    ["RoundRestartTimer"] = "RoundRestartTimer",
    ["SameRank"] = "SameRank",
    ["ScientistsSurvived"] = "ScientistsSurvived",
    ["SpecialAbility"] = "SpecialAbility",
    ["TerminatedScps"] = "TerminatedScps",
    ["UseSinkhole"] = "UseSinkhole",
    ["WarheadDetonated"] = "WarheadDetonated",
    ["WarheadUnused"] = "WarheadUnused",
    ["YouDied"] = "YouDied",
    ["YouWereKilledByNicknameLabel"] = "YouWereKilledByNicknameLabel",
    ["YouWereKilledByRoleLabel"] = "YouWereKilledByRoleLabel",
}
LegacyInterfaces = Enums["LegacyInterfaces"]

---@alias Enum_MainMenuSoundtrackController_002e_MenuSoundtrackState
---| "MenuIntensive"
---| "MenuJustLoaded"
---| "MenuLoop"
---| "Muted"
---| "PregameLobby"
---| "Retro"
Enums["MainMenuSoundtrackController.MenuSoundtrackState"] = {
    ["MenuIntensive"] = "MenuIntensive",
    ["MenuJustLoaded"] = "MenuJustLoaded",
    ["MenuLoop"] = "MenuLoop",
    ["Muted"] = "Muted",
    ["PregameLobby"] = "PregameLobby",
    ["Retro"] = "Retro",
}
Enums.MainMenuSoundtrackController = {}
Enums.MainMenuSoundtrackController.MenuSoundtrackState = Enums["MainMenuSoundtrackController.MenuSoundtrackState"]
MainMenuSoundtrackController = {}
MainMenuSoundtrackController.MenuSoundtrackState = Enums["MainMenuSoundtrackController.MenuSoundtrackState"]

---@alias Enum_MainMenuSoundtrackController_002e_MenuThemeSetting
---| "EventTheme"
---| "RegularTheme"
Enums["MainMenuSoundtrackController.MenuThemeSetting"] = {
    ["EventTheme"] = "EventTheme",
    ["RegularTheme"] = "RegularTheme",
}
Enums.MainMenuSoundtrackController.MenuThemeSetting = Enums["MainMenuSoundtrackController.MenuThemeSetting"]
MainMenuSoundtrackController.MenuThemeSetting = Enums["MainMenuSoundtrackController.MenuThemeSetting"]

---@alias Enum_MainThreadDispatcher_002e_DispatchTime
---| "FixedUpdate"
---| "LateUpdate"
---| "Update"
Enums["MainThreadDispatcher.DispatchTime"] = {
    ["FixedUpdate"] = "FixedUpdate",
    ["LateUpdate"] = "LateUpdate",
    ["Update"] = "Update",
}
Enums.MainThreadDispatcher = {}
Enums.MainThreadDispatcher.DispatchTime = Enums["MainThreadDispatcher.DispatchTime"]
MainThreadDispatcher = {}
MainThreadDispatcher.DispatchTime = Enums["MainThreadDispatcher.DispatchTime"]

---@alias Enum_MessageImportance
---| "LeastImportant"
---| "LessImportant"
---| "MostImportant"
---| "Normal"
Enums["MessageImportance"] = {
    ["LeastImportant"] = "LeastImportant",
    ["LessImportant"] = "LessImportant",
    ["MostImportant"] = "MostImportant",
    ["Normal"] = "Normal",
}
MessageImportance = Enums["MessageImportance"]

---@alias Enum_Misc_002e_CommandOperationMode
---| "Disable"
---| "Enable"
---| "Toggle"
Enums["Misc.CommandOperationMode"] = {
    ["Disable"] = "Disable",
    ["Enable"] = "Enable",
    ["Toggle"] = "Toggle",
}
Enums.Misc = {}
Enums.Misc.CommandOperationMode = Enums["Misc.CommandOperationMode"]
Misc = {}
Misc.CommandOperationMode = Enums["Misc.CommandOperationMode"]

---@alias Enum_Misc_002e_IPAddressType
---| "Hostname"
---| "IPV4"
---| "IPV6"
---| "Localhost"
---| "Unknown"
Enums["Misc.IPAddressType"] = {
    ["Hostname"] = "Hostname",
    ["IPV4"] = "IPV4",
    ["IPV6"] = "IPV6",
    ["Localhost"] = "Localhost",
    ["Unknown"] = "Unknown",
}
Enums.Misc.IPAddressType = Enums["Misc.IPAddressType"]
Misc.IPAddressType = Enums["Misc.IPAddressType"]

---@alias Enum_Misc_002e_PlayerInfoColorTypes
---| "Aqua"
---| "ArmyGreen"
---| "Black"
---| "BlueGreen"
---| "Brown"
---| "Carmine"
---| "Crimson"
---| "Cyan"
---| "DeepPink"
---| "Emerald"
---| "Green"
---| "LightGreen"
---| "Lime"
---| "Magenta"
---| "Mint"
---| "Nickel"
---| "Orange"
---| "Pink"
---| "Pumpkin"
---| "Red"
---| "Silver"
---| "Tomato"
---| "White"
---| "Yellow"
Enums["Misc.PlayerInfoColorTypes"] = {
    ["Aqua"] = "Aqua",
    ["ArmyGreen"] = "ArmyGreen",
    ["Black"] = "Black",
    ["BlueGreen"] = "BlueGreen",
    ["Brown"] = "Brown",
    ["Carmine"] = "Carmine",
    ["Crimson"] = "Crimson",
    ["Cyan"] = "Cyan",
    ["DeepPink"] = "DeepPink",
    ["Emerald"] = "Emerald",
    ["Green"] = "Green",
    ["LightGreen"] = "LightGreen",
    ["Lime"] = "Lime",
    ["Magenta"] = "Magenta",
    ["Mint"] = "Mint",
    ["Nickel"] = "Nickel",
    ["Orange"] = "Orange",
    ["Pink"] = "Pink",
    ["Pumpkin"] = "Pumpkin",
    ["Red"] = "Red",
    ["Silver"] = "Silver",
    ["Tomato"] = "Tomato",
    ["White"] = "White",
    ["Yellow"] = "Yellow",
}
Enums.Misc.PlayerInfoColorTypes = Enums["Misc.PlayerInfoColorTypes"]
Misc.PlayerInfoColorTypes = Enums["Misc.PlayerInfoColorTypes"]

---@alias Enum_NonFacilityCompatibility_002e_SceneDescription_002e_VoiceChatSupportMode
---| "FullySupported"
---| "Unsupported"
---| "WithoutIntercom"
Enums["NonFacilityCompatibility.SceneDescription.VoiceChatSupportMode"] = {
    ["FullySupported"] = "FullySupported",
    ["Unsupported"] = "Unsupported",
    ["WithoutIntercom"] = "WithoutIntercom",
}
Enums.NonFacilityCompatibility = {}
Enums.NonFacilityCompatibility.SceneDescription = {}
Enums.NonFacilityCompatibility.SceneDescription.VoiceChatSupportMode = Enums["NonFacilityCompatibility.SceneDescription.VoiceChatSupportMode"]
NonFacilityCompatibility = {}
NonFacilityCompatibility.SceneDescription = {}
NonFacilityCompatibility.SceneDescription.VoiceChatSupportMode = Enums["NonFacilityCompatibility.SceneDescription.VoiceChatSupportMode"]

---@alias Enum_PhotosensitivityWarning_002e_Photosensitivity
---| "FirstViewing"
Enums["PhotosensitivityWarning.Photosensitivity"] = {
    ["FirstViewing"] = "FirstViewing",
}
Enums.PhotosensitivityWarning = {}
Enums.PhotosensitivityWarning.Photosensitivity = Enums["PhotosensitivityWarning.Photosensitivity"]
PhotosensitivityWarning = {}
PhotosensitivityWarning.Photosensitivity = Enums["PhotosensitivityWarning.Photosensitivity"]

---@alias Enum_PlayerInfoArea
---| "Badge"
---| "CustomInfo"
---| "Nickname"
---| "PowerStatus"
---| "Role"
---| "UnitName"
Enums["PlayerInfoArea"] = {
    ["Badge"] = "Badge",
    ["CustomInfo"] = "CustomInfo",
    ["Nickname"] = "Nickname",
    ["PowerStatus"] = "PowerStatus",
    ["Role"] = "Role",
    ["UnitName"] = "UnitName",
}
PlayerInfoArea = Enums["PlayerInfoArea"]

---@alias Enum_PlayerPermissionType
---| "Misc"
---| "RaAccess"
---| "ViewAccess"
Enums["PlayerPermissionType"] = {
    ["Misc"] = "Misc",
    ["RaAccess"] = "RaAccess",
    ["ViewAccess"] = "ViewAccess",
}
PlayerPermissionType = Enums["PlayerPermissionType"]

---@alias Enum_PlayerPermissions
---| "AFKImmunity"
---| "AdminChat"
---| "Announcer"
---| "BanningUpToDay"
---| "Broadcasting"
---| "Effects"
---| "ExecuteAs"
---| "FacilityManagement"
---| "ForceclassSelf"
---| "ForceclassToSpectator"
---| "ForceclassWithoutRestrictions"
---| "FriendlyFireDetectorImmunity"
---| "FriendlyFireDetectorTempDisable"
---| "GameplayData"
---| "GivingItems"
---| "KickingAndShortTermBanning"
---| "LongTermBanning"
---| "Noclip"
---| "Overwatch"
---| "PermissionsManagement"
---| "PlayerSensitiveDataAccess"
---| "PlayersManagement"
---| "RespawnEvents"
---| "RoundEvents"
---| "ServerConfigs"
---| "ServerConsoleCommands"
---| "ServerLogLiveFeed"
---| "SetGroup"
---| "Vanish"
---| "ViewHiddenBadges"
---| "ViewHiddenGlobalBadges"
---| "WarheadEvents"
Enums["PlayerPermissions"] = {
    ["AFKImmunity"] = "AFKImmunity",
    ["AdminChat"] = "AdminChat",
    ["Announcer"] = "Announcer",
    ["BanningUpToDay"] = "BanningUpToDay",
    ["Broadcasting"] = "Broadcasting",
    ["Effects"] = "Effects",
    ["ExecuteAs"] = "ExecuteAs",
    ["FacilityManagement"] = "FacilityManagement",
    ["ForceclassSelf"] = "ForceclassSelf",
    ["ForceclassToSpectator"] = "ForceclassToSpectator",
    ["ForceclassWithoutRestrictions"] = "ForceclassWithoutRestrictions",
    ["FriendlyFireDetectorImmunity"] = "FriendlyFireDetectorImmunity",
    ["FriendlyFireDetectorTempDisable"] = "FriendlyFireDetectorTempDisable",
    ["GameplayData"] = "GameplayData",
    ["GivingItems"] = "GivingItems",
    ["KickingAndShortTermBanning"] = "KickingAndShortTermBanning",
    ["LongTermBanning"] = "LongTermBanning",
    ["Noclip"] = "Noclip",
    ["Overwatch"] = "Overwatch",
    ["PermissionsManagement"] = "PermissionsManagement",
    ["PlayerSensitiveDataAccess"] = "PlayerSensitiveDataAccess",
    ["PlayersManagement"] = "PlayersManagement",
    ["RespawnEvents"] = "RespawnEvents",
    ["RoundEvents"] = "RoundEvents",
    ["ServerConfigs"] = "ServerConfigs",
    ["ServerConsoleCommands"] = "ServerConsoleCommands",
    ["ServerLogLiveFeed"] = "ServerLogLiveFeed",
    ["SetGroup"] = "SetGroup",
    ["Vanish"] = "Vanish",
    ["ViewHiddenBadges"] = "ViewHiddenBadges",
    ["ViewHiddenGlobalBadges"] = "ViewHiddenGlobalBadges",
    ["WarheadEvents"] = "WarheadEvents",
}
PlayerPermissions = Enums["PlayerPermissions"]

---@alias Enum_PlayerPrefsSl_002e_DataType
---| "Bool"
---| "BoolArray"
---| "Byte"
---| "ByteArray"
---| "Char"
---| "CharArray"
---| "Decimal"
---| "DecimalArray"
---| "Double"
---| "DoubleArray"
---| "Float"
---| "FloatArray"
---| "Int"
---| "IntArray"
---| "Long"
---| "LongArray"
---| "Sbyte"
---| "SbyteArray"
---| "Short"
---| "ShortArray"
---| "String"
---| "StringArray"
---| "Uint"
---| "UintArray"
---| "Ulong"
---| "UlongArray"
---| "Ushort"
---| "UshortArray"
Enums["PlayerPrefsSl.DataType"] = {
    ["Bool"] = "Bool",
    ["BoolArray"] = "BoolArray",
    ["Byte"] = "Byte",
    ["ByteArray"] = "ByteArray",
    ["Char"] = "Char",
    ["CharArray"] = "CharArray",
    ["Decimal"] = "Decimal",
    ["DecimalArray"] = "DecimalArray",
    ["Double"] = "Double",
    ["DoubleArray"] = "DoubleArray",
    ["Float"] = "Float",
    ["FloatArray"] = "FloatArray",
    ["Int"] = "Int",
    ["IntArray"] = "IntArray",
    ["Long"] = "Long",
    ["LongArray"] = "LongArray",
    ["Sbyte"] = "Sbyte",
    ["SbyteArray"] = "SbyteArray",
    ["Short"] = "Short",
    ["ShortArray"] = "ShortArray",
    ["String"] = "String",
    ["StringArray"] = "StringArray",
    ["Uint"] = "Uint",
    ["UintArray"] = "UintArray",
    ["Ulong"] = "Ulong",
    ["UlongArray"] = "UlongArray",
    ["Ushort"] = "Ushort",
    ["UshortArray"] = "UshortArray",
}
Enums.PlayerPrefsSl = {}
Enums.PlayerPrefsSl.DataType = Enums["PlayerPrefsSl.DataType"]
PlayerPrefsSl = {}
PlayerPrefsSl.DataType = Enums["PlayerPrefsSl.DataType"]

---@alias Enum_PocketDimensionTeleport_002e_PDTeleportType
---| "Exit"
---| "Killer"
Enums["PocketDimensionTeleport.PDTeleportType"] = {
    ["Exit"] = "Exit",
    ["Killer"] = "Killer",
}
Enums.PocketDimensionTeleport = {}
Enums.PocketDimensionTeleport.PDTeleportType = Enums["PocketDimensionTeleport.PDTeleportType"]
PocketDimensionTeleport = {}
PocketDimensionTeleport.PDTeleportType = Enums["PocketDimensionTeleport.PDTeleportType"]

---@alias Enum_RejectionReason
---| "AuthenticationRequired"
---| "Banned"
---| "CentralServerAuthRejected"
---| "Challenge"
---| "Custom"
---| "Delay"
---| "Error"
---| "ExpiredAuth"
---| "Geoblocked"
---| "GloballyBanned"
---| "InvalidChallenge"
---| "InvalidChallengeKey"
---| "InvalidToken"
---| "NotSpecified"
---| "NotWhitelisted"
---| "RateLimit"
---| "Redirect"
---| "ServerFull"
---| "VerificationAccepted"
---| "VerificationRejected"
---| "VersionMismatch"
Enums["RejectionReason"] = {
    ["AuthenticationRequired"] = "AuthenticationRequired",
    ["Banned"] = "Banned",
    ["CentralServerAuthRejected"] = "CentralServerAuthRejected",
    ["Challenge"] = "Challenge",
    ["Custom"] = "Custom",
    ["Delay"] = "Delay",
    ["Error"] = "Error",
    ["ExpiredAuth"] = "ExpiredAuth",
    ["Geoblocked"] = "Geoblocked",
    ["GloballyBanned"] = "GloballyBanned",
    ["InvalidChallenge"] = "InvalidChallenge",
    ["InvalidChallengeKey"] = "InvalidChallengeKey",
    ["InvalidToken"] = "InvalidToken",
    ["NotSpecified"] = "NotSpecified",
    ["NotWhitelisted"] = "NotWhitelisted",
    ["RateLimit"] = "RateLimit",
    ["Redirect"] = "Redirect",
    ["ServerFull"] = "ServerFull",
    ["VerificationAccepted"] = "VerificationAccepted",
    ["VerificationRejected"] = "VerificationRejected",
    ["VersionMismatch"] = "VersionMismatch",
}
RejectionReason = Enums["RejectionReason"]

---@alias Enum_RemoteAdminResponse_002e_RemoteAdminResponseFlags
---| "LogInConsole"
---| "Successful"
Enums["RemoteAdminResponse.RemoteAdminResponseFlags"] = {
    ["LogInConsole"] = "LogInConsole",
    ["Successful"] = "Successful",
}
Enums.RemoteAdminResponse = {}
Enums.RemoteAdminResponse.RemoteAdminResponseFlags = Enums["RemoteAdminResponse.RemoteAdminResponseFlags"]
RemoteAdminResponse = {}
RemoteAdminResponse.RemoteAdminResponseFlags = Enums["RemoteAdminResponse.RemoteAdminResponseFlags"]

---@alias Enum_ReportServer_002e_CentralResponse
---| "Error"
---| "None"
---| "Success"
Enums["ReportServer.CentralResponse"] = {
    ["Error"] = "Error",
    ["None"] = "None",
    ["Success"] = "Success",
}
Enums.ReportServer = {}
Enums.ReportServer.CentralResponse = Enums["ReportServer.CentralResponse"]
ReportServer = {}
ReportServer.CentralResponse = Enums["ReportServer.CentralResponse"]

---@alias Enum_ReproProjectAssetType
---| "Asset"
---| "Prefab"
---| "Scene"
---| "Wildcard"
Enums["ReproProjectAssetType"] = {
    ["Asset"] = "Asset",
    ["Prefab"] = "Prefab",
    ["Scene"] = "Scene",
    ["Wildcard"] = "Wildcard",
}
ReproProjectAssetType = Enums["ReproProjectAssetType"]

---@alias Enum_RoundSummary_002e_LeadingTeam
---| "Anomalies"
---| "ChaosInsurgency"
---| "Draw"
---| "FacilityForces"
---| "Flamingos"
Enums["RoundSummary.LeadingTeam"] = {
    ["Anomalies"] = "Anomalies",
    ["ChaosInsurgency"] = "ChaosInsurgency",
    ["Draw"] = "Draw",
    ["FacilityForces"] = "FacilityForces",
    ["Flamingos"] = "Flamingos",
}
Enums.RoundSummary = {}
Enums.RoundSummary.LeadingTeam = Enums["RoundSummary.LeadingTeam"]
RoundSummary = {}
RoundSummary.LeadingTeam = Enums["RoundSummary.LeadingTeam"]

---@alias Enum_Scp956Target_002e_EffectReason
---| "Child"
---| "HasCandy"
---| "None"
Enums["Scp956Target.EffectReason"] = {
    ["Child"] = "Child",
    ["HasCandy"] = "HasCandy",
    ["None"] = "None",
}
Enums.Scp956Target = {}
Enums.Scp956Target.EffectReason = Enums["Scp956Target.EffectReason"]
Scp956Target = {}
Scp956Target.EffectReason = Enums["Scp956Target.EffectReason"]

---@alias Enum_ServerConfigSynchronizer_002e_MainBoolsSettings
---| "DisabledAchievements"
---| "FriendlyFire"
Enums["ServerConfigSynchronizer.MainBoolsSettings"] = {
    ["DisabledAchievements"] = "DisabledAchievements",
    ["FriendlyFire"] = "FriendlyFire",
}
Enums.ServerConfigSynchronizer = {}
Enums.ServerConfigSynchronizer.MainBoolsSettings = Enums["ServerConfigSynchronizer.MainBoolsSettings"]
ServerConfigSynchronizer = {}
ServerConfigSynchronizer.MainBoolsSettings = Enums["ServerConfigSynchronizer.MainBoolsSettings"]

---@alias Enum_ServerLogs_002e_LoggingState
---| "Off"
---| "Restart"
---| "Standby"
---| "Terminate"
---| "Write"
Enums["ServerLogs.LoggingState"] = {
    ["Off"] = "Off",
    ["Restart"] = "Restart",
    ["Standby"] = "Standby",
    ["Terminate"] = "Terminate",
    ["Write"] = "Write",
}
Enums.ServerLogs = {}
Enums.ServerLogs.LoggingState = Enums["ServerLogs.LoggingState"]
ServerLogs = {}
ServerLogs.LoggingState = Enums["ServerLogs.LoggingState"]

---@alias Enum_ServerLogs_002e_Modules
---| "Administrative"
---| "ClassChange"
---| "DataAccess"
---| "Detector"
---| "Door"
---| "Elevator"
---| "GameLogic"
---| "Networking"
---| "Permissions"
---| "Throwable"
---| "Warhead"
Enums["ServerLogs.Modules"] = {
    ["Administrative"] = "Administrative",
    ["ClassChange"] = "ClassChange",
    ["DataAccess"] = "DataAccess",
    ["Detector"] = "Detector",
    ["Door"] = "Door",
    ["Elevator"] = "Elevator",
    ["GameLogic"] = "GameLogic",
    ["Networking"] = "Networking",
    ["Permissions"] = "Permissions",
    ["Throwable"] = "Throwable",
    ["Warhead"] = "Warhead",
}
Enums.ServerLogs.Modules = Enums["ServerLogs.Modules"]
ServerLogs.Modules = Enums["ServerLogs.Modules"]

---@alias Enum_ServerLogs_002e_ServerLogType
---| "AdminChat"
---| "AuthRateLimit"
---| "ConnectionUpdate"
---| "GameEvent"
---| "InternalMessage"
---| "KillLog"
---| "Query"
---| "RemoteAdminActivity_GameChanging"
---| "RemoteAdminActivity_Misc"
---| "Suicide"
---| "Teamkill"
Enums["ServerLogs.ServerLogType"] = {
    ["AdminChat"] = "AdminChat",
    ["AuthRateLimit"] = "AuthRateLimit",
    ["ConnectionUpdate"] = "ConnectionUpdate",
    ["GameEvent"] = "GameEvent",
    ["InternalMessage"] = "InternalMessage",
    ["KillLog"] = "KillLog",
    ["Query"] = "Query",
    ["RemoteAdminActivity_GameChanging"] = "RemoteAdminActivity_GameChanging",
    ["RemoteAdminActivity_Misc"] = "RemoteAdminActivity_Misc",
    ["Suicide"] = "Suicide",
    ["Teamkill"] = "Teamkill",
}
Enums.ServerLogs.ServerLogType = Enums["ServerLogs.ServerLogType"]
ServerLogs.ServerLogType = Enums["ServerLogs.ServerLogType"]

---@alias Enum_ServerRoles_002e_BadgePreferences
---| "NoPreference"
---| "PreferGlobal"
---| "PreferLocal"
Enums["ServerRoles.BadgePreferences"] = {
    ["NoPreference"] = "NoPreference",
    ["PreferGlobal"] = "PreferGlobal",
    ["PreferLocal"] = "PreferLocal",
}
Enums.ServerRoles = {}
Enums.ServerRoles.BadgePreferences = Enums["ServerRoles.BadgePreferences"]
ServerRoles = {}
ServerRoles.BadgePreferences = Enums["ServerRoles.BadgePreferences"]

---@alias Enum_ServerRoles_002e_BadgeVisibilityPreferences
---| "Hidden"
---| "NoPreference"
---| "Visible"
Enums["ServerRoles.BadgeVisibilityPreferences"] = {
    ["Hidden"] = "Hidden",
    ["NoPreference"] = "NoPreference",
    ["Visible"] = "Visible",
}
Enums.ServerRoles.BadgeVisibilityPreferences = Enums["ServerRoles.BadgeVisibilityPreferences"]
ServerRoles.BadgeVisibilityPreferences = Enums["ServerRoles.BadgeVisibilityPreferences"]

---@alias Enum_ServerShutdown_002e_ServerShutdownState
---| "BroadcastingShutdown"
---| "Complete"
---| "NotInitiated"
---| "ShuttingDown"
Enums["ServerShutdown.ServerShutdownState"] = {
    ["BroadcastingShutdown"] = "BroadcastingShutdown",
    ["Complete"] = "Complete",
    ["NotInitiated"] = "NotInitiated",
    ["ShuttingDown"] = "ShuttingDown",
}
Enums.ServerShutdown = {}
Enums.ServerShutdown.ServerShutdownState = Enums["ServerShutdown.ServerShutdownState"]
ServerShutdown = {}
ServerShutdown.ServerShutdownState = Enums["ServerShutdown.ServerShutdownState"]

---@alias Enum_ServerStatic_002e_NextRoundAction
---| "DoNothing"
---| "Restart"
---| "Shutdown"
Enums["ServerStatic.NextRoundAction"] = {
    ["DoNothing"] = "DoNothing",
    ["Restart"] = "Restart",
    ["Shutdown"] = "Shutdown",
}
Enums.ServerStatic = {}
Enums.ServerStatic.NextRoundAction = Enums["ServerStatic.NextRoundAction"]
ServerStatic = {}
ServerStatic.NextRoundAction = Enums["ServerStatic.NextRoundAction"]

---@alias Enum_ServerTab
---| "Favorites"
---| "Friends"
---| "History"
---| "Internet"
---| "Official"
Enums["ServerTab"] = {
    ["Favorites"] = "Favorites",
    ["Friends"] = "Friends",
    ["History"] = "History",
    ["Internet"] = "Internet",
    ["Official"] = "Official",
}
ServerTab = Enums["ServerTab"]

---@alias Enum_StatusBar_002e_AutoHideType
---| "AlwaysVisible"
---| "WhenEmpty"
---| "WhenFull"
Enums["StatusBar.AutoHideType"] = {
    ["AlwaysVisible"] = "AlwaysVisible",
    ["WhenEmpty"] = "WhenEmpty",
    ["WhenFull"] = "WhenFull",
}
Enums.StatusBar = {}
Enums.StatusBar.AutoHideType = Enums["StatusBar.AutoHideType"]
StatusBar = {}
StatusBar.AutoHideType = Enums["StatusBar.AutoHideType"]

---@alias Enum_WarheadScenarioType
---| "DeadmanSwitch"
---| "Resume"
---| "Start"
Enums["WarheadScenarioType"] = {
    ["DeadmanSwitch"] = "DeadmanSwitch",
    ["Resume"] = "Resume",
    ["Start"] = "Start",
}
WarheadScenarioType = Enums["WarheadScenarioType"]

---@alias Enum_Achievements_002e_AchievementName
---| "AccessGranted"
---| "AfterlifeCommunicator"
---| "AmnesticAmbush"
---| "AnomalouslyEfficient"
---| "ArizonaRanger"
---| "ArmyOfOne"
---| "BePoliteBeEfficient"
---| "ChangeInCommand"
---| "CompleteTheMission"
---| "CrisisAverted"
---| "DeltaCommand"
---| "DidntEvenFeelThat"
---| "DontBlink"
---| "Escape207"
---| "EscapeArtist"
---| "ExecutiveAccess"
---| "FireInTheHole"
---| "ForScience"
---| "Friendship"
---| "HatsOffToYou"
---| "Hawkeye"
---| "HeWillBeBack"
---| "IllPassThanks"
---| "IsThisThingOn"
---| "ItsAlwaysLeft"
---| "JustResources"
---| "LMGG"
---| "LightsOut"
---| "Matador"
---| "MelancholyOfDecay"
---| "MicrowaveMeal"
---| "MutuallyAssuredDestruction"
---| "OnSpeakingTerms"
---| "Overcurrent"
---| "Overtime"
---| "Pacified"
---| "ProceedWithCaution"
---| "PropertyOfChaos"
---| "RuleBreaker"
---| "SecureContainProtect"
---| "SignalLost"
---| "SomethingDoneRight"
---| "TMinus"
---| "ThatCanBeUseful"
---| "ThatWasClose"
---| "TheEnemyOfMyEnemy"
---| "ThinkFast"
---| "ToothAndNail"
---| "TrilateralTermination"
---| "TurnThemAll"
---| "UndeadSpaceProgram"
---| "WalkItOff"
Enums["Achievements.AchievementName"] = {
    ["AccessGranted"] = "AccessGranted",
    ["AfterlifeCommunicator"] = "AfterlifeCommunicator",
    ["AmnesticAmbush"] = "AmnesticAmbush",
    ["AnomalouslyEfficient"] = "AnomalouslyEfficient",
    ["ArizonaRanger"] = "ArizonaRanger",
    ["ArmyOfOne"] = "ArmyOfOne",
    ["BePoliteBeEfficient"] = "BePoliteBeEfficient",
    ["ChangeInCommand"] = "ChangeInCommand",
    ["CompleteTheMission"] = "CompleteTheMission",
    ["CrisisAverted"] = "CrisisAverted",
    ["DeltaCommand"] = "DeltaCommand",
    ["DidntEvenFeelThat"] = "DidntEvenFeelThat",
    ["DontBlink"] = "DontBlink",
    ["Escape207"] = "Escape207",
    ["EscapeArtist"] = "EscapeArtist",
    ["ExecutiveAccess"] = "ExecutiveAccess",
    ["FireInTheHole"] = "FireInTheHole",
    ["ForScience"] = "ForScience",
    ["Friendship"] = "Friendship",
    ["HatsOffToYou"] = "HatsOffToYou",
    ["Hawkeye"] = "Hawkeye",
    ["HeWillBeBack"] = "HeWillBeBack",
    ["IllPassThanks"] = "IllPassThanks",
    ["IsThisThingOn"] = "IsThisThingOn",
    ["ItsAlwaysLeft"] = "ItsAlwaysLeft",
    ["JustResources"] = "JustResources",
    ["LMGG"] = "LMGG",
    ["LightsOut"] = "LightsOut",
    ["Matador"] = "Matador",
    ["MelancholyOfDecay"] = "MelancholyOfDecay",
    ["MicrowaveMeal"] = "MicrowaveMeal",
    ["MutuallyAssuredDestruction"] = "MutuallyAssuredDestruction",
    ["OnSpeakingTerms"] = "OnSpeakingTerms",
    ["Overcurrent"] = "Overcurrent",
    ["Overtime"] = "Overtime",
    ["Pacified"] = "Pacified",
    ["ProceedWithCaution"] = "ProceedWithCaution",
    ["PropertyOfChaos"] = "PropertyOfChaos",
    ["RuleBreaker"] = "RuleBreaker",
    ["SecureContainProtect"] = "SecureContainProtect",
    ["SignalLost"] = "SignalLost",
    ["SomethingDoneRight"] = "SomethingDoneRight",
    ["TMinus"] = "TMinus",
    ["ThatCanBeUseful"] = "ThatCanBeUseful",
    ["ThatWasClose"] = "ThatWasClose",
    ["TheEnemyOfMyEnemy"] = "TheEnemyOfMyEnemy",
    ["ThinkFast"] = "ThinkFast",
    ["ToothAndNail"] = "ToothAndNail",
    ["TrilateralTermination"] = "TrilateralTermination",
    ["TurnThemAll"] = "TurnThemAll",
    ["UndeadSpaceProgram"] = "UndeadSpaceProgram",
    ["WalkItOff"] = "WalkItOff",
}
Enums.Achievements = {}
Enums.Achievements.AchievementName = Enums["Achievements.AchievementName"]
Enums.AchievementName = Enums["Achievements.AchievementName"]
AchievementName = Enums["Achievements.AchievementName"]

---@alias Enum_AdminToys_002e_InvisibleInteractableToy_002e_ColliderShape
---| "Box"
---| "Capsule"
---| "Sphere"
Enums["AdminToys.InvisibleInteractableToy.ColliderShape"] = {
    ["Box"] = "Box",
    ["Capsule"] = "Capsule",
    ["Sphere"] = "Sphere",
}
Enums.AdminToys = {}
Enums.AdminToys.InvisibleInteractableToy = {}
Enums.AdminToys.InvisibleInteractableToy.ColliderShape = Enums["AdminToys.InvisibleInteractableToy.ColliderShape"]
Enums.InvisibleInteractableToy = {}
Enums.InvisibleInteractableToy.ColliderShape = Enums["AdminToys.InvisibleInteractableToy.ColliderShape"]
Enums["InvisibleInteractableToy.ColliderShape"] = Enums["AdminToys.InvisibleInteractableToy.ColliderShape"]
InvisibleInteractableToy = {}
InvisibleInteractableToy.ColliderShape = Enums["AdminToys.InvisibleInteractableToy.ColliderShape"]

---@alias Enum_AdminToys_002e_PrimitiveFlags
---| "Collidable"
---| "None"
---| "Visible"
Enums["AdminToys.PrimitiveFlags"] = {
    ["Collidable"] = "Collidable",
    ["None"] = "None",
    ["Visible"] = "Visible",
}
Enums.AdminToys.PrimitiveFlags = Enums["AdminToys.PrimitiveFlags"]
Enums.PrimitiveFlags = Enums["AdminToys.PrimitiveFlags"]
PrimitiveFlags = Enums["AdminToys.PrimitiveFlags"]

---@alias Enum_AdminToys_002e_ShootingTarget_002e_TargetButton
---| "DecreaseHP"
---| "DecreaseResetTime"
---| "GlobalResults"
---| "IncreaseHP"
---| "IncreaseResetTime"
---| "ManualReset"
---| "Remove"
Enums["AdminToys.ShootingTarget.TargetButton"] = {
    ["DecreaseHP"] = "DecreaseHP",
    ["DecreaseResetTime"] = "DecreaseResetTime",
    ["GlobalResults"] = "GlobalResults",
    ["IncreaseHP"] = "IncreaseHP",
    ["IncreaseResetTime"] = "IncreaseResetTime",
    ["ManualReset"] = "ManualReset",
    ["Remove"] = "Remove",
}
Enums.AdminToys.ShootingTarget = {}
Enums.AdminToys.ShootingTarget.TargetButton = Enums["AdminToys.ShootingTarget.TargetButton"]
Enums.ShootingTarget = {}
Enums.ShootingTarget.TargetButton = Enums["AdminToys.ShootingTarget.TargetButton"]
Enums["ShootingTarget.TargetButton"] = Enums["AdminToys.ShootingTarget.TargetButton"]
ShootingTarget = {}
ShootingTarget.TargetButton = Enums["AdminToys.ShootingTarget.TargetButton"]

---@alias Enum_AudioPooling_002e_MixerChannel
---| "DefaultSfx"
---| "Interface"
---| "NoDucking"
---| "Scp127VoiceViewmodel"
---| "VoiceChat"
---| "Weapons"
Enums["AudioPooling.MixerChannel"] = {
    ["DefaultSfx"] = "DefaultSfx",
    ["Interface"] = "Interface",
    ["NoDucking"] = "NoDucking",
    ["Scp127VoiceViewmodel"] = "Scp127VoiceViewmodel",
    ["VoiceChat"] = "VoiceChat",
    ["Weapons"] = "Weapons",
}
Enums.AudioPooling = {}
Enums.AudioPooling.MixerChannel = Enums["AudioPooling.MixerChannel"]
Enums.MixerChannel = Enums["AudioPooling.MixerChannel"]
MixerChannel = Enums["AudioPooling.MixerChannel"]

---@alias Enum_Cassie_002e_CassieClipCategory
---| "Glitch"
---| "Other"
---| "Word"
Enums["Cassie.CassieClipCategory"] = {
    ["Glitch"] = "Glitch",
    ["Other"] = "Other",
    ["Word"] = "Word",
}
Enums.Cassie = {}
Enums.Cassie.CassieClipCategory = Enums["Cassie.CassieClipCategory"]
Enums.CassieClipCategory = Enums["Cassie.CassieClipCategory"]
CassieClipCategory = Enums["Cassie.CassieClipCategory"]

---@alias Enum_Cassie_002e_CassieTtsPayload_002e_SubtitleMode
---| "Automatic"
---| "Custom"
---| "FromTranslation"
---| "None"
Enums["Cassie.CassieTtsPayload.SubtitleMode"] = {
    ["Automatic"] = "Automatic",
    ["Custom"] = "Custom",
    ["FromTranslation"] = "FromTranslation",
    ["None"] = "None",
}
Enums.Cassie.CassieTtsPayload = {}
Enums.Cassie.CassieTtsPayload.SubtitleMode = Enums["Cassie.CassieTtsPayload.SubtitleMode"]
Enums.CassieTtsPayload = {}
Enums.CassieTtsPayload.SubtitleMode = Enums["Cassie.CassieTtsPayload.SubtitleMode"]
Enums["CassieTtsPayload.SubtitleMode"] = Enums["Cassie.CassieTtsPayload.SubtitleMode"]
CassieTtsPayload = {}
CassieTtsPayload.SubtitleMode = Enums["Cassie.CassieTtsPayload.SubtitleMode"]

---@alias Enum_Cassie_002e_Interpreters_002e_CassieInterpreter_002e_NextAction
---| "Continue"
---| "Halt"
Enums["Cassie.Interpreters.CassieInterpreter.NextAction"] = {
    ["Continue"] = "Continue",
    ["Halt"] = "Halt",
}
Enums.Cassie.Interpreters = {}
Enums.Cassie.Interpreters.CassieInterpreter = {}
Enums.Cassie.Interpreters.CassieInterpreter.NextAction = Enums["Cassie.Interpreters.CassieInterpreter.NextAction"]
Enums.CassieInterpreter = {}
Enums.CassieInterpreter.NextAction = Enums["Cassie.Interpreters.CassieInterpreter.NextAction"]
Enums["CassieInterpreter.NextAction"] = Enums["Cassie.Interpreters.CassieInterpreter.NextAction"]
CassieInterpreter = {}
CassieInterpreter.NextAction = Enums["Cassie.Interpreters.CassieInterpreter.NextAction"]

---@alias Enum_CentralAuth_002e_AuthenticationResponseFunctions_002e_AuthenticationResponseFlags
---| "AuthToken"
---| "BadgeToken"
---| "DoNotTrack"
---| "HideBadge"
Enums["CentralAuth.AuthenticationResponseFunctions.AuthenticationResponseFlags"] = {
    ["AuthToken"] = "AuthToken",
    ["BadgeToken"] = "BadgeToken",
    ["DoNotTrack"] = "DoNotTrack",
    ["HideBadge"] = "HideBadge",
}
Enums.CentralAuth = {}
Enums.CentralAuth.AuthenticationResponseFunctions = {}
Enums.CentralAuth.AuthenticationResponseFunctions.AuthenticationResponseFlags = Enums["CentralAuth.AuthenticationResponseFunctions.AuthenticationResponseFlags"]
Enums.AuthenticationResponseFunctions = {}
Enums.AuthenticationResponseFunctions.AuthenticationResponseFlags = Enums["CentralAuth.AuthenticationResponseFunctions.AuthenticationResponseFlags"]
Enums["AuthenticationResponseFunctions.AuthenticationResponseFlags"] = Enums["CentralAuth.AuthenticationResponseFunctions.AuthenticationResponseFlags"]
AuthenticationResponseFunctions = {}
AuthenticationResponseFunctions.AuthenticationResponseFlags = Enums["CentralAuth.AuthenticationResponseFunctions.AuthenticationResponseFlags"]

---@alias Enum_CentralAuth_002e_ClientInstanceMode
---| "DedicatedServer"
---| "Dummy"
---| "Host"
---| "ReadyClient"
---| "Unverified"
Enums["CentralAuth.ClientInstanceMode"] = {
    ["DedicatedServer"] = "DedicatedServer",
    ["Dummy"] = "Dummy",
    ["Host"] = "Host",
    ["ReadyClient"] = "ReadyClient",
    ["Unverified"] = "Unverified",
}
Enums.CentralAuth.ClientInstanceMode = Enums["CentralAuth.ClientInstanceMode"]
Enums.ClientInstanceMode = Enums["CentralAuth.ClientInstanceMode"]
ClientInstanceMode = Enums["CentralAuth.ClientInstanceMode"]

---@alias Enum_Christmas_002e_Scp2536_002e_UrgencyLevel
---| "Exclusive"
---| "Five"
---| "Four"
---| "One"
---| "Six"
---| "Three"
---| "Two"
---| "Zero"
Enums["Christmas.Scp2536.UrgencyLevel"] = {
    ["Exclusive"] = "Exclusive",
    ["Five"] = "Five",
    ["Four"] = "Four",
    ["One"] = "One",
    ["Six"] = "Six",
    ["Three"] = "Three",
    ["Two"] = "Two",
    ["Zero"] = "Zero",
}
Enums.Christmas = {}
Enums.Christmas.Scp2536 = {}
Enums.Christmas.Scp2536.UrgencyLevel = Enums["Christmas.Scp2536.UrgencyLevel"]
Enums.UrgencyLevel = Enums["Christmas.Scp2536.UrgencyLevel"]
UrgencyLevel = Enums["Christmas.Scp2536.UrgencyLevel"]

---@alias Enum_CursorManagement_002e_CursorOverrideMode
---| "Centered"
---| "Confined"
---| "Free"
---| "NoOverride"
Enums["CursorManagement.CursorOverrideMode"] = {
    ["Centered"] = "Centered",
    ["Confined"] = "Confined",
    ["Free"] = "Free",
    ["NoOverride"] = "NoOverride",
}
Enums.CursorManagement = {}
Enums.CursorManagement.CursorOverrideMode = Enums["CursorManagement.CursorOverrideMode"]
Enums.CursorOverrideMode = Enums["CursorManagement.CursorOverrideMode"]
CursorOverrideMode = Enums["CursorManagement.CursorOverrideMode"]

---@alias Enum_CustomPlayerEffects_002e_StatusEffectBase_002e_EffectClassification
---| "Mixed"
---| "Negative"
---| "Positive"
---| "Technical"
Enums["CustomPlayerEffects.StatusEffectBase.EffectClassification"] = {
    ["Mixed"] = "Mixed",
    ["Negative"] = "Negative",
    ["Positive"] = "Positive",
    ["Technical"] = "Technical",
}
Enums.CustomPlayerEffects = {}
Enums.CustomPlayerEffects.StatusEffectBase = {}
Enums.CustomPlayerEffects.StatusEffectBase.EffectClassification = Enums["CustomPlayerEffects.StatusEffectBase.EffectClassification"]
Enums.StatusEffectBase = {}
Enums.StatusEffectBase.EffectClassification = Enums["CustomPlayerEffects.StatusEffectBase.EffectClassification"]
Enums["StatusEffectBase.EffectClassification"] = Enums["CustomPlayerEffects.StatusEffectBase.EffectClassification"]
StatusEffectBase = {}
StatusEffectBase.EffectClassification = Enums["CustomPlayerEffects.StatusEffectBase.EffectClassification"]

---@alias Enum_CustomRendering_002e_ApplyCameraGraphicSettings_002e_AntiAliasingType
---| "Disabled"
---| "FXAA"
---| "SMAA"
Enums["CustomRendering.ApplyCameraGraphicSettings.AntiAliasingType"] = {
    ["Disabled"] = "Disabled",
    ["FXAA"] = "FXAA",
    ["SMAA"] = "SMAA",
}
Enums.CustomRendering = {}
Enums.CustomRendering.ApplyCameraGraphicSettings = {}
Enums.CustomRendering.ApplyCameraGraphicSettings.AntiAliasingType = Enums["CustomRendering.ApplyCameraGraphicSettings.AntiAliasingType"]
Enums.ApplyCameraGraphicSettings = {}
Enums.ApplyCameraGraphicSettings.AntiAliasingType = Enums["CustomRendering.ApplyCameraGraphicSettings.AntiAliasingType"]
Enums["ApplyCameraGraphicSettings.AntiAliasingType"] = Enums["CustomRendering.ApplyCameraGraphicSettings.AntiAliasingType"]
ApplyCameraGraphicSettings = {}
ApplyCameraGraphicSettings.AntiAliasingType = Enums["CustomRendering.ApplyCameraGraphicSettings.AntiAliasingType"]

---@alias Enum_CustomRendering_002e_FogType
---| "Amnesia"
---| "BecomingFlamingo"
---| "ChristmasInside"
---| "ChristmasOutside"
---| "Decontamination"
---| "HalloweenInside"
---| "HalloweenOutside"
---| "Inside"
---| "None"
---| "Nuke"
---| "Outside"
---| "PocketDimension"
---| "Scp244"
Enums["CustomRendering.FogType"] = {
    ["Amnesia"] = "Amnesia",
    ["BecomingFlamingo"] = "BecomingFlamingo",
    ["ChristmasInside"] = "ChristmasInside",
    ["ChristmasOutside"] = "ChristmasOutside",
    ["Decontamination"] = "Decontamination",
    ["HalloweenInside"] = "HalloweenInside",
    ["HalloweenOutside"] = "HalloweenOutside",
    ["Inside"] = "Inside",
    ["None"] = "None",
    ["Nuke"] = "Nuke",
    ["Outside"] = "Outside",
    ["PocketDimension"] = "PocketDimension",
    ["Scp244"] = "Scp244",
}
Enums.CustomRendering.FogType = Enums["CustomRendering.FogType"]
Enums.FogType = Enums["CustomRendering.FogType"]
FogType = Enums["CustomRendering.FogType"]

---@alias Enum_Decals_002e_DecalPoolType
---| "Blood"
---| "Buckshot"
---| "Bullet"
---| "GlassCrack"
---| "None"
Enums["Decals.DecalPoolType"] = {
    ["Blood"] = "Blood",
    ["Buckshot"] = "Buckshot",
    ["Bullet"] = "Bullet",
    ["GlassCrack"] = "GlassCrack",
    ["None"] = "None",
}
Enums.Decals = {}
Enums.Decals.DecalPoolType = Enums["Decals.DecalPoolType"]
Enums.DecalPoolType = Enums["Decals.DecalPoolType"]
DecalPoolType = Enums["Decals.DecalPoolType"]

---@alias Enum_Discord_002e_DiscordGameStatus
---| "Lobby"
---| "MainMenu"
Enums["Discord.DiscordGameStatus"] = {
    ["Lobby"] = "Lobby",
    ["MainMenu"] = "MainMenu",
}
Enums.Discord = {}
Enums.Discord.DiscordGameStatus = Enums["Discord.DiscordGameStatus"]
Enums.DiscordGameStatus = Enums["Discord.DiscordGameStatus"]
DiscordGameStatus = Enums["Discord.DiscordGameStatus"]

---@alias Enum_Discord_002e_Reply
---| "Ignore"
---| "No"
---| "Yes"
Enums["Discord.Reply"] = {
    ["Ignore"] = "Ignore",
    ["No"] = "No",
    ["Yes"] = "Yes",
}
Enums.Discord.Reply = Enums["Discord.Reply"]
Enums.Reply = Enums["Discord.Reply"]
Reply = Enums["Discord.Reply"]

---@alias Enum_GameCore_002e_ConfigShareTypes
---| "Bans"
---| "GameplayDatabase"
---| "Groups"
---| "GroupsMembers"
---| "Mutes"
---| "ReservedSlots"
---| "Whitelist"
Enums["GameCore.ConfigShareTypes"] = {
    ["Bans"] = "Bans",
    ["GameplayDatabase"] = "GameplayDatabase",
    ["Groups"] = "Groups",
    ["GroupsMembers"] = "GroupsMembers",
    ["Mutes"] = "Mutes",
    ["ReservedSlots"] = "ReservedSlots",
    ["Whitelist"] = "Whitelist",
}
Enums.GameCore = {}
Enums.GameCore.ConfigShareTypes = Enums["GameCore.ConfigShareTypes"]
Enums.ConfigShareTypes = Enums["GameCore.ConfigShareTypes"]
ConfigShareTypes = Enums["GameCore.ConfigShareTypes"]

---@alias Enum_GameCore_002e_ConfigSharing_002e_ConfigShare
---| "Bans"
---| "GameplayDatabase"
---| "Groups"
---| "GroupsMembers"
---| "Mutes"
---| "ReservedSlots"
---| "Whitelist"
Enums["GameCore.ConfigSharing.ConfigShare"] = {
    ["Bans"] = "Bans",
    ["GameplayDatabase"] = "GameplayDatabase",
    ["Groups"] = "Groups",
    ["GroupsMembers"] = "GroupsMembers",
    ["Mutes"] = "Mutes",
    ["ReservedSlots"] = "ReservedSlots",
    ["Whitelist"] = "Whitelist",
}
Enums.GameCore.ConfigSharing = {}
Enums.GameCore.ConfigSharing.ConfigShare = Enums["GameCore.ConfigSharing.ConfigShare"]
Enums.ConfigSharing = {}
Enums.ConfigSharing.ConfigShare = Enums["GameCore.ConfigSharing.ConfigShare"]
Enums["ConfigSharing.ConfigShare"] = Enums["GameCore.ConfigSharing.ConfigShare"]
ConfigSharing = {}
ConfigSharing.ConfigShare = Enums["GameCore.ConfigSharing.ConfigShare"]

---@alias Enum_GameCore_002e_ConsoleLogType
---| "DoNotLog"
---| "Error"
---| "Log"
---| "Warning"
Enums["GameCore.ConsoleLogType"] = {
    ["DoNotLog"] = "DoNotLog",
    ["Error"] = "Error",
    ["Log"] = "Log",
    ["Warning"] = "Warning",
}
Enums.GameCore.ConsoleLogType = Enums["GameCore.ConsoleLogType"]
Enums.ConsoleLogType = Enums["GameCore.ConsoleLogType"]
ConsoleLogType = Enums["GameCore.ConsoleLogType"]

---@alias Enum_GameCore_002e_DistributionPlatform
---| "Dedicated"
---| "Discord"
---| "Steam"
Enums["GameCore.DistributionPlatform"] = {
    ["Dedicated"] = "Dedicated",
    ["Discord"] = "Discord",
    ["Steam"] = "Steam",
}
Enums.GameCore.DistributionPlatform = Enums["GameCore.DistributionPlatform"]
Enums.DistributionPlatform = Enums["GameCore.DistributionPlatform"]
DistributionPlatform = Enums["GameCore.DistributionPlatform"]

---@alias Enum_GameCore_002e_ResizableWindow_002e_ResizeDirection
---| "Bottom"
---| "Left"
---| "None"
---| "Right"
---| "Top"
Enums["GameCore.ResizableWindow.ResizeDirection"] = {
    ["Bottom"] = "Bottom",
    ["Left"] = "Left",
    ["None"] = "None",
    ["Right"] = "Right",
    ["Top"] = "Top",
}
Enums.GameCore.ResizableWindow = {}
Enums.GameCore.ResizableWindow.ResizeDirection = Enums["GameCore.ResizableWindow.ResizeDirection"]
Enums.ResizableWindow = {}
Enums.ResizableWindow.ResizeDirection = Enums["GameCore.ResizableWindow.ResizeDirection"]
Enums["ResizableWindow.ResizeDirection"] = Enums["GameCore.ResizableWindow.ResizeDirection"]
ResizableWindow = {}
ResizableWindow.ResizeDirection = Enums["GameCore.ResizableWindow.ResizeDirection"]

---@alias Enum_GameCore_002e_SystemCursorID
---| "AppStarting"
---| "Block"
---| "Cross"
---| "Default"
---| "Help"
---| "LinkSelect"
---| "Person"
---| "Pin"
---| "SizeAll"
---| "SizeDiagonalBottomTop"
---| "SizeDiagonalTopBottom"
---| "SizeHorizontal"
---| "SizeVertical"
---| "TextSelectBeam"
---| "UpArrow"
---| "WaitingCircle"
Enums["GameCore.SystemCursorID"] = {
    ["AppStarting"] = "AppStarting",
    ["Block"] = "Block",
    ["Cross"] = "Cross",
    ["Default"] = "Default",
    ["Help"] = "Help",
    ["LinkSelect"] = "LinkSelect",
    ["Person"] = "Person",
    ["Pin"] = "Pin",
    ["SizeAll"] = "SizeAll",
    ["SizeDiagonalBottomTop"] = "SizeDiagonalBottomTop",
    ["SizeDiagonalTopBottom"] = "SizeDiagonalTopBottom",
    ["SizeHorizontal"] = "SizeHorizontal",
    ["SizeVertical"] = "SizeVertical",
    ["TextSelectBeam"] = "TextSelectBeam",
    ["UpArrow"] = "UpArrow",
    ["WaitingCircle"] = "WaitingCircle",
}
Enums.GameCore.SystemCursorID = Enums["GameCore.SystemCursorID"]
Enums.SystemCursorID = Enums["GameCore.SystemCursorID"]
SystemCursorID = Enums["GameCore.SystemCursorID"]

---@alias Enum_GameCore_002e_Version_002e_VersionType
---| "Development"
---| "Nightly"
---| "PrivateBeta"
---| "PrivateBetaStreamingForbidden"
---| "PrivateRC"
---| "PrivateRCStreamingForbidden"
---| "PublicBeta"
---| "PublicRC"
---| "Release"
Enums["GameCore.Version.VersionType"] = {
    ["Development"] = "Development",
    ["Nightly"] = "Nightly",
    ["PrivateBeta"] = "PrivateBeta",
    ["PrivateBetaStreamingForbidden"] = "PrivateBetaStreamingForbidden",
    ["PrivateRC"] = "PrivateRC",
    ["PrivateRCStreamingForbidden"] = "PrivateRCStreamingForbidden",
    ["PublicBeta"] = "PublicBeta",
    ["PublicRC"] = "PublicRC",
    ["Release"] = "Release",
}
Enums.GameCore.Version = {}
Enums.GameCore.Version.VersionType = Enums["GameCore.Version.VersionType"]
Enums.Version = {}
Enums.Version.VersionType = Enums["GameCore.Version.VersionType"]
Enums["Version.VersionType"] = Enums["GameCore.Version.VersionType"]
Version = {}
Version.VersionType = Enums["GameCore.Version.VersionType"]

---@alias Enum_Hints_002e_HintMessageParameterFunctions_002e_HintMessageTypes
---| "TextHint"
---| "TranslationHint"
---| "Unknown"
Enums["Hints.HintMessageParameterFunctions.HintMessageTypes"] = {
    ["TextHint"] = "TextHint",
    ["TranslationHint"] = "TranslationHint",
    ["Unknown"] = "Unknown",
}
Enums.Hints = {}
Enums.Hints.HintMessageParameterFunctions = {}
Enums.Hints.HintMessageParameterFunctions.HintMessageTypes = Enums["Hints.HintMessageParameterFunctions.HintMessageTypes"]
Enums.HintMessageParameterFunctions = {}
Enums.HintMessageParameterFunctions.HintMessageTypes = Enums["Hints.HintMessageParameterFunctions.HintMessageTypes"]
Enums["HintMessageParameterFunctions.HintMessageTypes"] = Enums["Hints.HintMessageParameterFunctions.HintMessageTypes"]
HintMessageParameterFunctions = {}
HintMessageParameterFunctions.HintMessageTypes = Enums["Hints.HintMessageParameterFunctions.HintMessageTypes"]

---@alias Enum_Hints_002e_HintTranslations
---| "MaxAmmoAlreadyReached"
---| "MaxAmmoReached"
---| "MaxItemCategoryAlreadyReached"
---| "MaxItemCategoryReached"
---| "MaxItemsAlreadyReached"
---| "WarheadDetonateCoverDenied"
Enums["Hints.HintTranslations"] = {
    ["MaxAmmoAlreadyReached"] = "MaxAmmoAlreadyReached",
    ["MaxAmmoReached"] = "MaxAmmoReached",
    ["MaxItemCategoryAlreadyReached"] = "MaxItemCategoryAlreadyReached",
    ["MaxItemCategoryReached"] = "MaxItemCategoryReached",
    ["MaxItemsAlreadyReached"] = "MaxItemsAlreadyReached",
    ["WarheadDetonateCoverDenied"] = "WarheadDetonateCoverDenied",
}
Enums.Hints.HintTranslations = Enums["Hints.HintTranslations"]
Enums.HintTranslations = Enums["Hints.HintTranslations"]
HintTranslations = Enums["Hints.HintTranslations"]

---@alias Enum_Hints_002e_KeyCodeTranslations
---| "ArrowDown"
---| "ArrowLeft"
---| "ArrowRight"
---| "ArrowUp"
---| "Enter"
---| "KeyNotAssigned"
---| "LeftAlt"
---| "LeftControl"
---| "LeftShift"
---| "MouseMiddle"
---| "MouseN"
---| "MousePrimary"
---| "MouseSecondary"
---| "RightAlt"
---| "RightControl"
---| "RightShift"
---| "ServerSettingNotFound"
---| "Space"
---| "Tab"
Enums["Hints.KeyCodeTranslations"] = {
    ["ArrowDown"] = "ArrowDown",
    ["ArrowLeft"] = "ArrowLeft",
    ["ArrowRight"] = "ArrowRight",
    ["ArrowUp"] = "ArrowUp",
    ["Enter"] = "Enter",
    ["KeyNotAssigned"] = "KeyNotAssigned",
    ["LeftAlt"] = "LeftAlt",
    ["LeftControl"] = "LeftControl",
    ["LeftShift"] = "LeftShift",
    ["MouseMiddle"] = "MouseMiddle",
    ["MouseN"] = "MouseN",
    ["MousePrimary"] = "MousePrimary",
    ["MouseSecondary"] = "MouseSecondary",
    ["RightAlt"] = "RightAlt",
    ["RightControl"] = "RightControl",
    ["RightShift"] = "RightShift",
    ["ServerSettingNotFound"] = "ServerSettingNotFound",
    ["Space"] = "Space",
    ["Tab"] = "Tab",
}
Enums.Hints.KeyCodeTranslations = Enums["Hints.KeyCodeTranslations"]
Enums.KeyCodeTranslations = Enums["Hints.KeyCodeTranslations"]
KeyCodeTranslations = Enums["Hints.KeyCodeTranslations"]

---@alias Enum_Interactables_002e_Interobjects_002e_CheckpointDoor_002e_SequenceState
---| "ClosingWarning"
---| "Granted"
---| "Idle"
---| "OpenLoop"
Enums["Interactables.Interobjects.CheckpointDoor.SequenceState"] = {
    ["ClosingWarning"] = "ClosingWarning",
    ["Granted"] = "Granted",
    ["Idle"] = "Idle",
    ["OpenLoop"] = "OpenLoop",
}
Enums.Interactables = {}
Enums.Interactables.Interobjects = {}
Enums.Interactables.Interobjects.CheckpointDoor = {}
Enums.Interactables.Interobjects.CheckpointDoor.SequenceState = Enums["Interactables.Interobjects.CheckpointDoor.SequenceState"]
Enums.CheckpointDoor = {}
Enums.CheckpointDoor.SequenceState = Enums["Interactables.Interobjects.CheckpointDoor.SequenceState"]
Enums["CheckpointDoor.SequenceState"] = Enums["Interactables.Interobjects.CheckpointDoor.SequenceState"]
CheckpointDoor = {}
CheckpointDoor.SequenceState = Enums["Interactables.Interobjects.CheckpointDoor.SequenceState"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorAction
---| "AccessDenied"
---| "Closed"
---| "Destroyed"
---| "Locked"
---| "Opened"
---| "Unlocked"
Enums["Interactables.Interobjects.DoorUtils.DoorAction"] = {
    ["AccessDenied"] = "AccessDenied",
    ["Closed"] = "Closed",
    ["Destroyed"] = "Destroyed",
    ["Locked"] = "Locked",
    ["Opened"] = "Opened",
    ["Unlocked"] = "Unlocked",
}
Enums.Interactables.Interobjects.DoorUtils = {}
Enums.Interactables.Interobjects.DoorUtils.DoorAction = Enums["Interactables.Interobjects.DoorUtils.DoorAction"]
Enums.DoorAction = Enums["Interactables.Interobjects.DoorUtils.DoorAction"]
DoorAction = Enums["Interactables.Interobjects.DoorUtils.DoorAction"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorDamageType
---| "Grenade"
---| "None"
---| "ParticleDisruptor"
---| "Scp096"
---| "ServerCommand"
---| "Weapon"
Enums["Interactables.Interobjects.DoorUtils.DoorDamageType"] = {
    ["Grenade"] = "Grenade",
    ["None"] = "None",
    ["ParticleDisruptor"] = "ParticleDisruptor",
    ["Scp096"] = "Scp096",
    ["ServerCommand"] = "ServerCommand",
    ["Weapon"] = "Weapon",
}
Enums.Interactables.Interobjects.DoorUtils.DoorDamageType = Enums["Interactables.Interobjects.DoorUtils.DoorDamageType"]
Enums.DoorDamageType = Enums["Interactables.Interobjects.DoorUtils.DoorDamageType"]
DoorDamageType = Enums["Interactables.Interobjects.DoorUtils.DoorDamageType"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorEventOpenerExtension_002e_OpenerEventType
---| "DeconEvac"
---| "DeconFinish"
---| "DeconReset"
---| "WarheadCancel"
---| "WarheadStart"
Enums["Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension.OpenerEventType"] = {
    ["DeconEvac"] = "DeconEvac",
    ["DeconFinish"] = "DeconFinish",
    ["DeconReset"] = "DeconReset",
    ["WarheadCancel"] = "WarheadCancel",
    ["WarheadStart"] = "WarheadStart",
}
Enums.Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension = {}
Enums.Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension.OpenerEventType = Enums["Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension.OpenerEventType"]
Enums.DoorEventOpenerExtension = {}
Enums.DoorEventOpenerExtension.OpenerEventType = Enums["Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension.OpenerEventType"]
Enums["DoorEventOpenerExtension.OpenerEventType"] = Enums["Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension.OpenerEventType"]
DoorEventOpenerExtension = {}
DoorEventOpenerExtension.OpenerEventType = Enums["Interactables.Interobjects.DoorUtils.DoorEventOpenerExtension.OpenerEventType"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorLockMode
---| "CanClose"
---| "CanOpen"
---| "FullLock"
---| "ScpOverride"
Enums["Interactables.Interobjects.DoorUtils.DoorLockMode"] = {
    ["CanClose"] = "CanClose",
    ["CanOpen"] = "CanOpen",
    ["FullLock"] = "FullLock",
    ["ScpOverride"] = "ScpOverride",
}
Enums.Interactables.Interobjects.DoorUtils.DoorLockMode = Enums["Interactables.Interobjects.DoorUtils.DoorLockMode"]
Enums.DoorLockMode = Enums["Interactables.Interobjects.DoorUtils.DoorLockMode"]
DoorLockMode = Enums["Interactables.Interobjects.DoorUtils.DoorLockMode"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorLockReason
---| "AdminCommand"
---| "DecontEvacuate"
---| "DecontLockdown"
---| "Isolation"
---| "Lockdown079"
---| "Lockdown2176"
---| "NoPower"
---| "None"
---| "Regular079"
---| "SpecialDoorFeature"
---| "Warhead"
Enums["Interactables.Interobjects.DoorUtils.DoorLockReason"] = {
    ["AdminCommand"] = "AdminCommand",
    ["DecontEvacuate"] = "DecontEvacuate",
    ["DecontLockdown"] = "DecontLockdown",
    ["Isolation"] = "Isolation",
    ["Lockdown079"] = "Lockdown079",
    ["Lockdown2176"] = "Lockdown2176",
    ["NoPower"] = "NoPower",
    ["None"] = "None",
    ["Regular079"] = "Regular079",
    ["SpecialDoorFeature"] = "SpecialDoorFeature",
    ["Warhead"] = "Warhead",
}
Enums.Interactables.Interobjects.DoorUtils.DoorLockReason = Enums["Interactables.Interobjects.DoorUtils.DoorLockReason"]
Enums.DoorLockReason = Enums["Interactables.Interobjects.DoorUtils.DoorLockReason"]
DoorLockReason = Enums["Interactables.Interobjects.DoorUtils.DoorLockReason"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorPermissionFlags
---| "All"
---| "AlphaWarhead"
---| "ArmoryLevelOne"
---| "ArmoryLevelThree"
---| "ArmoryLevelTwo"
---| "Checkpoints"
---| "ContainmentLevelOne"
---| "ContainmentLevelThree"
---| "ContainmentLevelTwo"
---| "ExitGates"
---| "Intercom"
---| "None"
---| "ScpOverride"
Enums["Interactables.Interobjects.DoorUtils.DoorPermissionFlags"] = {
    ["All"] = "All",
    ["AlphaWarhead"] = "AlphaWarhead",
    ["ArmoryLevelOne"] = "ArmoryLevelOne",
    ["ArmoryLevelThree"] = "ArmoryLevelThree",
    ["ArmoryLevelTwo"] = "ArmoryLevelTwo",
    ["Checkpoints"] = "Checkpoints",
    ["ContainmentLevelOne"] = "ContainmentLevelOne",
    ["ContainmentLevelThree"] = "ContainmentLevelThree",
    ["ContainmentLevelTwo"] = "ContainmentLevelTwo",
    ["ExitGates"] = "ExitGates",
    ["Intercom"] = "Intercom",
    ["None"] = "None",
    ["ScpOverride"] = "ScpOverride",
}
Enums.Interactables.Interobjects.DoorUtils.DoorPermissionFlags = Enums["Interactables.Interobjects.DoorUtils.DoorPermissionFlags"]
Enums.DoorPermissionFlags = Enums["Interactables.Interobjects.DoorUtils.DoorPermissionFlags"]
DoorPermissionFlags = Enums["Interactables.Interobjects.DoorUtils.DoorPermissionFlags"]

---@alias Enum_Interactables_002e_Interobjects_002e_DoorUtils_002e_DoorVariant_002e_CollisionsDisablingReasons
---| "DoorClosing"
---| "Scp106"
Enums["Interactables.Interobjects.DoorUtils.DoorVariant.CollisionsDisablingReasons"] = {
    ["DoorClosing"] = "DoorClosing",
    ["Scp106"] = "Scp106",
}
Enums.Interactables.Interobjects.DoorUtils.DoorVariant = {}
Enums.Interactables.Interobjects.DoorUtils.DoorVariant.CollisionsDisablingReasons = Enums["Interactables.Interobjects.DoorUtils.DoorVariant.CollisionsDisablingReasons"]
Enums.DoorVariant = {}
Enums.DoorVariant.CollisionsDisablingReasons = Enums["Interactables.Interobjects.DoorUtils.DoorVariant.CollisionsDisablingReasons"]
Enums["DoorVariant.CollisionsDisablingReasons"] = Enums["Interactables.Interobjects.DoorUtils.DoorVariant.CollisionsDisablingReasons"]
DoorVariant = {}
DoorVariant.CollisionsDisablingReasons = Enums["Interactables.Interobjects.DoorUtils.DoorVariant.CollisionsDisablingReasons"]

---@alias Enum_Interactables_002e_Interobjects_002e_ElevatorChamber_002e_ElevatorSequence
---| "Arriving"
---| "DoorClosing"
---| "DoorOpening"
---| "MovingAway"
---| "Ready"
---| "StartingSequence"
Enums["Interactables.Interobjects.ElevatorChamber.ElevatorSequence"] = {
    ["Arriving"] = "Arriving",
    ["DoorClosing"] = "DoorClosing",
    ["DoorOpening"] = "DoorOpening",
    ["MovingAway"] = "MovingAway",
    ["Ready"] = "Ready",
    ["StartingSequence"] = "StartingSequence",
}
Enums.Interactables.Interobjects.ElevatorChamber = {}
Enums.Interactables.Interobjects.ElevatorChamber.ElevatorSequence = Enums["Interactables.Interobjects.ElevatorChamber.ElevatorSequence"]
Enums.ElevatorChamber = {}
Enums.ElevatorChamber.ElevatorSequence = Enums["Interactables.Interobjects.ElevatorChamber.ElevatorSequence"]
Enums["ElevatorChamber.ElevatorSequence"] = Enums["Interactables.Interobjects.ElevatorChamber.ElevatorSequence"]
ElevatorChamber = {}
ElevatorChamber.ElevatorSequence = Enums["Interactables.Interobjects.ElevatorChamber.ElevatorSequence"]

---@alias Enum_Interactables_002e_Interobjects_002e_ElevatorGroup
---| "GateA01"
---| "GateA02"
---| "GateB"
---| "LczA01"
---| "LczA02"
---| "LczB01"
---| "LczB02"
---| "Nuke01"
---| "Nuke02"
---| "Scp049"
---| "ServerRoom"
Enums["Interactables.Interobjects.ElevatorGroup"] = {
    ["GateA01"] = "GateA01",
    ["GateA02"] = "GateA02",
    ["GateB"] = "GateB",
    ["LczA01"] = "LczA01",
    ["LczA02"] = "LczA02",
    ["LczB01"] = "LczB01",
    ["LczB02"] = "LczB02",
    ["Nuke01"] = "Nuke01",
    ["Nuke02"] = "Nuke02",
    ["Scp049"] = "Scp049",
    ["ServerRoom"] = "ServerRoom",
}
Enums.Interactables.Interobjects.ElevatorGroup = Enums["Interactables.Interobjects.ElevatorGroup"]
Enums.ElevatorGroup = Enums["Interactables.Interobjects.ElevatorGroup"]
ElevatorGroup = Enums["Interactables.Interobjects.ElevatorGroup"]

---@alias Enum_Interactables_002e_Interobjects_002e_PopupInterobject_002e_PopupState
---| "Disabling"
---| "Enabling"
---| "Off"
---| "On"
Enums["Interactables.Interobjects.PopupInterobject.PopupState"] = {
    ["Disabling"] = "Disabling",
    ["Enabling"] = "Enabling",
    ["Off"] = "Off",
    ["On"] = "On",
}
Enums.Interactables.Interobjects.PopupInterobject = {}
Enums.Interactables.Interobjects.PopupInterobject.PopupState = Enums["Interactables.Interobjects.PopupInterobject.PopupState"]
Enums.PopupInterobject = {}
Enums.PopupInterobject.PopupState = Enums["Interactables.Interobjects.PopupInterobject.PopupState"]
Enums["PopupInterobject.PopupState"] = Enums["Interactables.Interobjects.PopupInterobject.PopupState"]
PopupInterobject = {}
PopupInterobject.PopupState = Enums["Interactables.Interobjects.PopupInterobject.PopupState"]

---@alias Enum_InventorySystem_002e_Drawers_002e_AlertContent_002e_ColorMode
---| "Accented"
---| "Role"
---| "White"
Enums["InventorySystem.Drawers.AlertContent.ColorMode"] = {
    ["Accented"] = "Accented",
    ["Role"] = "Role",
    ["White"] = "White",
}
Enums.InventorySystem = {}
Enums.InventorySystem.Drawers = {}
Enums.InventorySystem.Drawers.AlertContent = {}
Enums.InventorySystem.Drawers.AlertContent.ColorMode = Enums["InventorySystem.Drawers.AlertContent.ColorMode"]
Enums.AlertContent = {}
Enums.AlertContent.ColorMode = Enums["InventorySystem.Drawers.AlertContent.ColorMode"]
Enums["AlertContent.ColorMode"] = Enums["InventorySystem.Drawers.AlertContent.ColorMode"]
AlertContent = {}
AlertContent.ColorMode = Enums["InventorySystem.Drawers.AlertContent.ColorMode"]

---@alias Enum_InventorySystem_002e_GUI_002e_InventoryGuiAction
---| "Drop"
---| "None"
---| "Select"
Enums["InventorySystem.GUI.InventoryGuiAction"] = {
    ["Drop"] = "Drop",
    ["None"] = "None",
    ["Select"] = "Select",
}
Enums.InventorySystem.GUI = {}
Enums.InventorySystem.GUI.InventoryGuiAction = Enums["InventorySystem.GUI.InventoryGuiAction"]
Enums.InventoryGuiAction = Enums["InventorySystem.GUI.InventoryGuiAction"]
InventoryGuiAction = Enums["InventorySystem.GUI.InventoryGuiAction"]

---@alias Enum_InventorySystem_002e_GUI_002e_InventoryGuiTranslation
---| "ArmorAmmoLimits"
---| "ArmorHelmetEfficacy"
---| "ArmorItemLimits"
---| "ArmorMovementSpeed"
---| "ArmorPenetration"
---| "ArmorStaminaUsage"
---| "ArmorTotal"
---| "ArmorVestEfficacy"
---| "DisruptorToggleModeHint"
---| "DropAmmo"
---| "FirearmAdsAcc"
---| "FirearmAmmo"
---| "FirearmDamage"
---| "FirearmFireRate"
---| "FirearmHipAcc"
---| "FirearmSprintSpeed"
---| "JailbirdAttackHint"
---| "JailbirdChargeHint"
---| "Length"
---| "MarshmallowCackleHint"
---| "MicroHidDamaged"
---| "MicroHidPrimaryHint"
---| "MicroHidReadyToDischarge"
---| "MicroHidSecondaryHint"
---| "RemainingAmmo"
---| "RemainingCharge"
---| "RunningInaccuracy"
---| "Scp127OnEquip"
---| "SnakeHint"
---| "Weight"
Enums["InventorySystem.GUI.InventoryGuiTranslation"] = {
    ["ArmorAmmoLimits"] = "ArmorAmmoLimits",
    ["ArmorHelmetEfficacy"] = "ArmorHelmetEfficacy",
    ["ArmorItemLimits"] = "ArmorItemLimits",
    ["ArmorMovementSpeed"] = "ArmorMovementSpeed",
    ["ArmorPenetration"] = "ArmorPenetration",
    ["ArmorStaminaUsage"] = "ArmorStaminaUsage",
    ["ArmorTotal"] = "ArmorTotal",
    ["ArmorVestEfficacy"] = "ArmorVestEfficacy",
    ["DisruptorToggleModeHint"] = "DisruptorToggleModeHint",
    ["DropAmmo"] = "DropAmmo",
    ["FirearmAdsAcc"] = "FirearmAdsAcc",
    ["FirearmAmmo"] = "FirearmAmmo",
    ["FirearmDamage"] = "FirearmDamage",
    ["FirearmFireRate"] = "FirearmFireRate",
    ["FirearmHipAcc"] = "FirearmHipAcc",
    ["FirearmSprintSpeed"] = "FirearmSprintSpeed",
    ["JailbirdAttackHint"] = "JailbirdAttackHint",
    ["JailbirdChargeHint"] = "JailbirdChargeHint",
    ["Length"] = "Length",
    ["MarshmallowCackleHint"] = "MarshmallowCackleHint",
    ["MicroHidDamaged"] = "MicroHidDamaged",
    ["MicroHidPrimaryHint"] = "MicroHidPrimaryHint",
    ["MicroHidReadyToDischarge"] = "MicroHidReadyToDischarge",
    ["MicroHidSecondaryHint"] = "MicroHidSecondaryHint",
    ["RemainingAmmo"] = "RemainingAmmo",
    ["RemainingCharge"] = "RemainingCharge",
    ["RunningInaccuracy"] = "RunningInaccuracy",
    ["Scp127OnEquip"] = "Scp127OnEquip",
    ["SnakeHint"] = "SnakeHint",
    ["Weight"] = "Weight",
}
Enums.InventorySystem.GUI.InventoryGuiTranslation = Enums["InventorySystem.GUI.InventoryGuiTranslation"]
Enums.InventoryGuiTranslation = Enums["InventorySystem.GUI.InventoryGuiTranslation"]
InventoryGuiTranslation = Enums["InventorySystem.GUI.InventoryGuiTranslation"]

---@alias Enum_InventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeyApperance
---| "Hidden"
---| "NormalIcon"
---| "SmallIcon"
Enums["InventorySystem.Hotkeys.Customization.HotkeyApperance"] = {
    ["Hidden"] = "Hidden",
    ["NormalIcon"] = "NormalIcon",
    ["SmallIcon"] = "SmallIcon",
}
Enums.InventorySystem.Hotkeys = {}
Enums.InventorySystem.Hotkeys.Customization = {}
Enums.InventorySystem.Hotkeys.Customization.HotkeyApperance = Enums["InventorySystem.Hotkeys.Customization.HotkeyApperance"]
Enums.HotkeyApperance = Enums["InventorySystem.Hotkeys.Customization.HotkeyApperance"]
HotkeyApperance = Enums["InventorySystem.Hotkeys.Customization.HotkeyApperance"]

---@alias Enum_InventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeyItemGroup
---| "AnyFirearm"
---| "AnyKeycard"
---| "AnyScpItem"
---| "AnyThrowable"
Enums["InventorySystem.Hotkeys.Customization.HotkeyItemGroup"] = {
    ["AnyFirearm"] = "AnyFirearm",
    ["AnyKeycard"] = "AnyKeycard",
    ["AnyScpItem"] = "AnyScpItem",
    ["AnyThrowable"] = "AnyThrowable",
}
Enums.InventorySystem.Hotkeys.Customization.HotkeyItemGroup = Enums["InventorySystem.Hotkeys.Customization.HotkeyItemGroup"]
Enums.HotkeyItemGroup = Enums["InventorySystem.Hotkeys.Customization.HotkeyItemGroup"]
HotkeyItemGroup = Enums["InventorySystem.Hotkeys.Customization.HotkeyItemGroup"]

---@alias Enum_InventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeyOverrideOption
---| "BestMedical"
---| "NoOverride"
---| "PrioritizeNearbyDoors"
---| "RememberPrevious"
---| "SkipEmptyFirearms"
Enums["InventorySystem.Hotkeys.Customization.HotkeyOverrideOption"] = {
    ["BestMedical"] = "BestMedical",
    ["NoOverride"] = "NoOverride",
    ["PrioritizeNearbyDoors"] = "PrioritizeNearbyDoors",
    ["RememberPrevious"] = "RememberPrevious",
    ["SkipEmptyFirearms"] = "SkipEmptyFirearms",
}
Enums.InventorySystem.Hotkeys.Customization.HotkeyOverrideOption = Enums["InventorySystem.Hotkeys.Customization.HotkeyOverrideOption"]
Enums.HotkeyOverrideOption = Enums["InventorySystem.Hotkeys.Customization.HotkeyOverrideOption"]
HotkeyOverrideOption = Enums["InventorySystem.Hotkeys.Customization.HotkeyOverrideOption"]

---@alias Enum_InventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeySortMode
---| "InventoryOrder"
---| "ItemPoolOrder"
Enums["InventorySystem.Hotkeys.Customization.HotkeySortMode"] = {
    ["InventoryOrder"] = "InventoryOrder",
    ["ItemPoolOrder"] = "ItemPoolOrder",
}
Enums.InventorySystem.Hotkeys.Customization.HotkeySortMode = Enums["InventorySystem.Hotkeys.Customization.HotkeySortMode"]
Enums.HotkeySortMode = Enums["InventorySystem.Hotkeys.Customization.HotkeySortMode"]
HotkeySortMode = Enums["InventorySystem.Hotkeys.Customization.HotkeySortMode"]

---@alias Enum_InventorySystem_002e_Hotkeys_002e_Customization_002e_PoolElementData_002e_ElementType
---| "Group"
---| "Order"
---| "SpecificItem"
Enums["InventorySystem.Hotkeys.Customization.PoolElementData.ElementType"] = {
    ["Group"] = "Group",
    ["Order"] = "Order",
    ["SpecificItem"] = "SpecificItem",
}
Enums.InventorySystem.Hotkeys.Customization.PoolElementData = {}
Enums.InventorySystem.Hotkeys.Customization.PoolElementData.ElementType = Enums["InventorySystem.Hotkeys.Customization.PoolElementData.ElementType"]
Enums.PoolElementData = {}
Enums.PoolElementData.ElementType = Enums["InventorySystem.Hotkeys.Customization.PoolElementData.ElementType"]
Enums["PoolElementData.ElementType"] = Enums["InventorySystem.Hotkeys.Customization.PoolElementData.ElementType"]
PoolElementData = {}
PoolElementData.ElementType = Enums["InventorySystem.Hotkeys.Customization.PoolElementData.ElementType"]

---@alias Enum_InventorySystem_002e_Hotkeys_002e_HotkeysTranslation
---| "ApperanceHidden"
---| "ApperanceLabel"
---| "ApperanceNormal"
---| "ApperanceSmall"
---| "CreateNewLabel"
---| "GrenadePredefinedName"
---| "GroupFirearms"
---| "GroupKeycards"
---| "GroupScpItems"
---| "GroupThrowable"
---| "KeyLabel"
---| "KeycardPredefinedName"
---| "MedicalPredefinedName"
---| "NameLabel"
---| "NewHotkeyNamePlaceholder"
---| "OverrideBestMedical"
---| "OverrideBestMedicalHint"
---| "OverrideHint"
---| "OverrideLabel"
---| "OverrideNearbyDoor"
---| "OverrideNearbyDoorHint"
---| "OverrideNone"
---| "OverrideRememberPrev"
---| "OverrideRememberPrevHint"
---| "OverrideSkipEmpty"
---| "OverrideSkipEmptyHint"
---| "PoolItemsLabel"
---| "PoolOrderHint"
---| "PoolOrderLabel"
---| "PrimaryWeaponPredefinedName"
---| "RestoreDefaultButton"
---| "RestoreDefaultLabel"
---| "SecondaryWeaponPredefinedName"
---| "SettingsHeader"
---| "SettingsHeaderHint"
---| "SortInventoryOrder"
---| "SortInventoryOrderHint"
---| "SortItemQueue"
---| "SortItemQueueHint"
---| "SortLabel"
---| "TitlePrefix"
Enums["InventorySystem.Hotkeys.HotkeysTranslation"] = {
    ["ApperanceHidden"] = "ApperanceHidden",
    ["ApperanceLabel"] = "ApperanceLabel",
    ["ApperanceNormal"] = "ApperanceNormal",
    ["ApperanceSmall"] = "ApperanceSmall",
    ["CreateNewLabel"] = "CreateNewLabel",
    ["GrenadePredefinedName"] = "GrenadePredefinedName",
    ["GroupFirearms"] = "GroupFirearms",
    ["GroupKeycards"] = "GroupKeycards",
    ["GroupScpItems"] = "GroupScpItems",
    ["GroupThrowable"] = "GroupThrowable",
    ["KeyLabel"] = "KeyLabel",
    ["KeycardPredefinedName"] = "KeycardPredefinedName",
    ["MedicalPredefinedName"] = "MedicalPredefinedName",
    ["NameLabel"] = "NameLabel",
    ["NewHotkeyNamePlaceholder"] = "NewHotkeyNamePlaceholder",
    ["OverrideBestMedical"] = "OverrideBestMedical",
    ["OverrideBestMedicalHint"] = "OverrideBestMedicalHint",
    ["OverrideHint"] = "OverrideHint",
    ["OverrideLabel"] = "OverrideLabel",
    ["OverrideNearbyDoor"] = "OverrideNearbyDoor",
    ["OverrideNearbyDoorHint"] = "OverrideNearbyDoorHint",
    ["OverrideNone"] = "OverrideNone",
    ["OverrideRememberPrev"] = "OverrideRememberPrev",
    ["OverrideRememberPrevHint"] = "OverrideRememberPrevHint",
    ["OverrideSkipEmpty"] = "OverrideSkipEmpty",
    ["OverrideSkipEmptyHint"] = "OverrideSkipEmptyHint",
    ["PoolItemsLabel"] = "PoolItemsLabel",
    ["PoolOrderHint"] = "PoolOrderHint",
    ["PoolOrderLabel"] = "PoolOrderLabel",
    ["PrimaryWeaponPredefinedName"] = "PrimaryWeaponPredefinedName",
    ["RestoreDefaultButton"] = "RestoreDefaultButton",
    ["RestoreDefaultLabel"] = "RestoreDefaultLabel",
    ["SecondaryWeaponPredefinedName"] = "SecondaryWeaponPredefinedName",
    ["SettingsHeader"] = "SettingsHeader",
    ["SettingsHeaderHint"] = "SettingsHeaderHint",
    ["SortInventoryOrder"] = "SortInventoryOrder",
    ["SortInventoryOrderHint"] = "SortInventoryOrderHint",
    ["SortItemQueue"] = "SortItemQueue",
    ["SortItemQueueHint"] = "SortItemQueueHint",
    ["SortLabel"] = "SortLabel",
    ["TitlePrefix"] = "TitlePrefix",
}
Enums.InventorySystem.Hotkeys.HotkeysTranslation = Enums["InventorySystem.Hotkeys.HotkeysTranslation"]
Enums.HotkeysTranslation = Enums["InventorySystem.Hotkeys.HotkeysTranslation"]
HotkeysTranslation = Enums["InventorySystem.Hotkeys.HotkeysTranslation"]

---@alias Enum_InventorySystem_002e_Items_002e_Autosync_002e_AutosyncInstantiationStatus
---| "InventoryInstance"
---| "SimulatedInstance"
---| "Template"
---| "Unspecified"
Enums["InventorySystem.Items.Autosync.AutosyncInstantiationStatus"] = {
    ["InventoryInstance"] = "InventoryInstance",
    ["SimulatedInstance"] = "SimulatedInstance",
    ["Template"] = "Template",
    ["Unspecified"] = "Unspecified",
}
Enums.InventorySystem.Items = {}
Enums.InventorySystem.Items.Autosync = {}
Enums.InventorySystem.Items.Autosync.AutosyncInstantiationStatus = Enums["InventorySystem.Items.Autosync.AutosyncInstantiationStatus"]
Enums.AutosyncInstantiationStatus = Enums["InventorySystem.Items.Autosync.AutosyncInstantiationStatus"]
AutosyncInstantiationStatus = Enums["InventorySystem.Items.Autosync.AutosyncInstantiationStatus"]

---@alias Enum_InventorySystem_002e_Items_002e_Autosync_002e_AutosyncMessageType
---| "Cmd"
---| "RpcInstance"
---| "RpcTemplate"
Enums["InventorySystem.Items.Autosync.AutosyncMessageType"] = {
    ["Cmd"] = "Cmd",
    ["RpcInstance"] = "RpcInstance",
    ["RpcTemplate"] = "RpcTemplate",
}
Enums.InventorySystem.Items.Autosync.AutosyncMessageType = Enums["InventorySystem.Items.Autosync.AutosyncMessageType"]
Enums.AutosyncMessageType = Enums["InventorySystem.Items.Autosync.AutosyncMessageType"]
AutosyncMessageType = Enums["InventorySystem.Items.Autosync.AutosyncMessageType"]

---@alias Enum_InventorySystem_002e_Items_002e_Autosync_002e_AutosyncRpc_002e_Mode
---| "AllClients"
---| "Conditional"
---| "SpecificClient"
Enums["InventorySystem.Items.Autosync.AutosyncRpc.Mode"] = {
    ["AllClients"] = "AllClients",
    ["Conditional"] = "Conditional",
    ["SpecificClient"] = "SpecificClient",
}
Enums.InventorySystem.Items.Autosync.AutosyncRpc = {}
Enums.InventorySystem.Items.Autosync.AutosyncRpc.Mode = Enums["InventorySystem.Items.Autosync.AutosyncRpc.Mode"]
Enums.AutosyncRpc = {}
Enums.AutosyncRpc.Mode = Enums["InventorySystem.Items.Autosync.AutosyncRpc.Mode"]
Enums["AutosyncRpc.Mode"] = Enums["InventorySystem.Items.Autosync.AutosyncRpc.Mode"]
AutosyncRpc = {}
AutosyncRpc.Mode = Enums["InventorySystem.Items.Autosync.AutosyncRpc.Mode"]

---@alias Enum_InventorySystem_002e_Items_002e_Autosync_002e_MeleeAutoSync_002e_HitResult
---| "HitPlayer"
---| "HitWall"
---| "KilledPlayer"
---| "Missed"
Enums["InventorySystem.Items.Autosync.MeleeAutoSync.HitResult"] = {
    ["HitPlayer"] = "HitPlayer",
    ["HitWall"] = "HitWall",
    ["KilledPlayer"] = "KilledPlayer",
    ["Missed"] = "Missed",
}
Enums.InventorySystem.Items.Autosync.MeleeAutoSync = {}
Enums.InventorySystem.Items.Autosync.MeleeAutoSync.HitResult = Enums["InventorySystem.Items.Autosync.MeleeAutoSync.HitResult"]
Enums.MeleeAutoSync = {}
Enums.MeleeAutoSync.HitResult = Enums["InventorySystem.Items.Autosync.MeleeAutoSync.HitResult"]
Enums["MeleeAutoSync.HitResult"] = Enums["InventorySystem.Items.Autosync.MeleeAutoSync.HitResult"]
MeleeAutoSync = {}
MeleeAutoSync.HitResult = Enums["InventorySystem.Items.Autosync.MeleeAutoSync.HitResult"]

---@alias Enum_InventorySystem_002e_Items_002e_Autosync_002e_MessageInterceptionResult
---| "Continue"
---| "ResetAndContinue"
---| "Stop"
Enums["InventorySystem.Items.Autosync.MessageInterceptionResult"] = {
    ["Continue"] = "Continue",
    ["ResetAndContinue"] = "ResetAndContinue",
    ["Stop"] = "Stop",
}
Enums.InventorySystem.Items.Autosync.MessageInterceptionResult = Enums["InventorySystem.Items.Autosync.MessageInterceptionResult"]
Enums.MessageInterceptionResult = Enums["InventorySystem.Items.Autosync.MessageInterceptionResult"]
MessageInterceptionResult = Enums["InventorySystem.Items.Autosync.MessageInterceptionResult"]

---@alias Enum_InventorySystem_002e_Items_002e_BlockedInteraction
---| "All"
---| "BeDisarmed"
---| "GeneralInteractions"
---| "GrabItems"
---| "ItemPrimaryAction"
---| "ItemUsage"
---| "OpenInventory"
---| "UndisarmPlayers"
Enums["InventorySystem.Items.BlockedInteraction"] = {
    ["All"] = "All",
    ["BeDisarmed"] = "BeDisarmed",
    ["GeneralInteractions"] = "GeneralInteractions",
    ["GrabItems"] = "GrabItems",
    ["ItemPrimaryAction"] = "ItemPrimaryAction",
    ["ItemUsage"] = "ItemUsage",
    ["OpenInventory"] = "OpenInventory",
    ["UndisarmPlayers"] = "UndisarmPlayers",
}
Enums.InventorySystem.Items.BlockedInteraction = Enums["InventorySystem.Items.BlockedInteraction"]
Enums.BlockedInteraction = Enums["InventorySystem.Items.BlockedInteraction"]
BlockedInteraction = Enums["InventorySystem.Items.BlockedInteraction"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentDescriptiveAdvantages
---| "AmmoCounter"
---| "FlashSuppression"
---| "Flashlight"
---| "NightVision"
---| "None"
---| "ShotgunShells"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"] = {
    ["AmmoCounter"] = "AmmoCounter",
    ["FlashSuppression"] = "FlashSuppression",
    ["Flashlight"] = "Flashlight",
    ["NightVision"] = "NightVision",
    ["None"] = "None",
    ["ShotgunShells"] = "ShotgunShells",
}
Enums.InventorySystem.Items.Firearms = {}
Enums.InventorySystem.Items.Firearms.Attachments = {}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"]
Enums.AttachmentDescriptiveAdvantages = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"]
AttachmentDescriptiveAdvantages = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentDescriptiveDownsides
---| "Laser"
---| "NoHeadshots"
---| "None"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"] = {
    ["Laser"] = "Laser",
    ["NoHeadshots"] = "NoHeadshots",
    ["None"] = "None",
}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"]
Enums.AttachmentDescriptiveDownsides = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"]
AttachmentDescriptiveDownsides = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentEditorsTranslation
---| "AttachmentCustomization"
---| "ClearAttachments"
---| "CloseStatsSummary"
---| "Custom"
---| "DefaultAttachments"
---| "PresetId"
---| "Presets"
---| "SaveAttachments"
---| "ShowStatsSummary"
---| "SpectatorEditorTip"
---| "WorkstationBooting"
---| "WorkstationEquipGun"
---| "WorkstationOffline"
---| "WorkstationReady"
---| "WorkstationShuttingDownDescription"
---| "WorkstationShuttingDownHeader"
---| "WorkstationStandby"
---| "WorkstationWelcome"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"] = {
    ["AttachmentCustomization"] = "AttachmentCustomization",
    ["ClearAttachments"] = "ClearAttachments",
    ["CloseStatsSummary"] = "CloseStatsSummary",
    ["Custom"] = "Custom",
    ["DefaultAttachments"] = "DefaultAttachments",
    ["PresetId"] = "PresetId",
    ["Presets"] = "Presets",
    ["SaveAttachments"] = "SaveAttachments",
    ["ShowStatsSummary"] = "ShowStatsSummary",
    ["SpectatorEditorTip"] = "SpectatorEditorTip",
    ["WorkstationBooting"] = "WorkstationBooting",
    ["WorkstationEquipGun"] = "WorkstationEquipGun",
    ["WorkstationOffline"] = "WorkstationOffline",
    ["WorkstationReady"] = "WorkstationReady",
    ["WorkstationShuttingDownDescription"] = "WorkstationShuttingDownDescription",
    ["WorkstationShuttingDownHeader"] = "WorkstationShuttingDownHeader",
    ["WorkstationStandby"] = "WorkstationStandby",
    ["WorkstationWelcome"] = "WorkstationWelcome",
}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"]
Enums.AttachmentEditorsTranslation = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"]
AttachmentEditorsTranslation = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentName
---| "AmmoCounter"
---| "AmmoSight"
---| "CarbineBody"
---| "CylinderMag5"
---| "CylinderMag6"
---| "CylinderMag7"
---| "DotSight"
---| "DrumMagAP"
---| "DrumMagFMJ"
---| "DrumMagJHP"
---| "ExtendedBarrel"
---| "ExtendedMagAP"
---| "ExtendedMagFMJ"
---| "ExtendedMagJHP"
---| "ExtendedStock"
---| "FlashHider"
---| "Flashlight"
---| "Foregrip"
---| "HeavyStock"
---| "HoloSight"
---| "IronSights"
---| "Laser"
---| "LightweightStock"
---| "LowcapMagAP"
---| "LowcapMagFMJ"
---| "LowcapMagJHP"
---| "MuzzleBooster"
---| "MuzzleBrake"
---| "NightVisionSight"
---| "NoRifleStock"
---| "None"
---| "RecoilReducingStock"
---| "RetractedStock"
---| "RifleBody"
---| "ScopeSight"
---| "ShortBarrel"
---| "ShotgunChoke"
---| "ShotgunDoubleShot"
---| "ShotgunExtendedBarrel"
---| "ShotgunSingleShot"
---| "SoundSuppressor"
---| "StandardBarrel"
---| "StandardMagAP"
---| "StandardMagFMJ"
---| "StandardMagJHP"
---| "StandardStock"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentName"] = {
    ["AmmoCounter"] = "AmmoCounter",
    ["AmmoSight"] = "AmmoSight",
    ["CarbineBody"] = "CarbineBody",
    ["CylinderMag5"] = "CylinderMag5",
    ["CylinderMag6"] = "CylinderMag6",
    ["CylinderMag7"] = "CylinderMag7",
    ["DotSight"] = "DotSight",
    ["DrumMagAP"] = "DrumMagAP",
    ["DrumMagFMJ"] = "DrumMagFMJ",
    ["DrumMagJHP"] = "DrumMagJHP",
    ["ExtendedBarrel"] = "ExtendedBarrel",
    ["ExtendedMagAP"] = "ExtendedMagAP",
    ["ExtendedMagFMJ"] = "ExtendedMagFMJ",
    ["ExtendedMagJHP"] = "ExtendedMagJHP",
    ["ExtendedStock"] = "ExtendedStock",
    ["FlashHider"] = "FlashHider",
    ["Flashlight"] = "Flashlight",
    ["Foregrip"] = "Foregrip",
    ["HeavyStock"] = "HeavyStock",
    ["HoloSight"] = "HoloSight",
    ["IronSights"] = "IronSights",
    ["Laser"] = "Laser",
    ["LightweightStock"] = "LightweightStock",
    ["LowcapMagAP"] = "LowcapMagAP",
    ["LowcapMagFMJ"] = "LowcapMagFMJ",
    ["LowcapMagJHP"] = "LowcapMagJHP",
    ["MuzzleBooster"] = "MuzzleBooster",
    ["MuzzleBrake"] = "MuzzleBrake",
    ["NightVisionSight"] = "NightVisionSight",
    ["NoRifleStock"] = "NoRifleStock",
    ["None"] = "None",
    ["RecoilReducingStock"] = "RecoilReducingStock",
    ["RetractedStock"] = "RetractedStock",
    ["RifleBody"] = "RifleBody",
    ["ScopeSight"] = "ScopeSight",
    ["ShortBarrel"] = "ShortBarrel",
    ["ShotgunChoke"] = "ShotgunChoke",
    ["ShotgunDoubleShot"] = "ShotgunDoubleShot",
    ["ShotgunExtendedBarrel"] = "ShotgunExtendedBarrel",
    ["ShotgunSingleShot"] = "ShotgunSingleShot",
    ["SoundSuppressor"] = "SoundSuppressor",
    ["StandardBarrel"] = "StandardBarrel",
    ["StandardMagAP"] = "StandardMagAP",
    ["StandardMagFMJ"] = "StandardMagFMJ",
    ["StandardMagJHP"] = "StandardMagJHP",
    ["StandardStock"] = "StandardStock",
}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentName = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentName"]
Enums.AttachmentName = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentName"]
AttachmentName = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentName"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentParam
---| "AdsInaccuracyMultiplier"
---| "AdsMouseSensitivityMultiplier"
---| "AdsRecoilMultiplier"
---| "AdsSpeedMultiplier"
---| "AdsZoomMultiplier"
---| "AmmoConsumptionMultiplier"
---| "BulletInaccuracyMultiplier"
---| "DamageMultiplier"
---| "DoubleActionSpeedMultiplier"
---| "DrawSpeedMultiplier"
---| "DrawTimeModifier"
---| "FireRateMultiplier"
---| "GunshotLoudnessMultiplier"
---| "HipInaccuracyMultiplier"
---| "MagazineCapacityModifier"
---| "OverallRecoilMultiplier"
---| "PenetrationMultiplier"
---| "PreventReload"
---| "ReloadSpeedMultiplier"
---| "ReloadTimeModifier"
---| "RunningInaccuracyMultiplier"
---| "ShotClipIdOverride"
---| "SpreadMultiplier"
---| "SpreadPredictability"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParam"] = {
    ["AdsInaccuracyMultiplier"] = "AdsInaccuracyMultiplier",
    ["AdsMouseSensitivityMultiplier"] = "AdsMouseSensitivityMultiplier",
    ["AdsRecoilMultiplier"] = "AdsRecoilMultiplier",
    ["AdsSpeedMultiplier"] = "AdsSpeedMultiplier",
    ["AdsZoomMultiplier"] = "AdsZoomMultiplier",
    ["AmmoConsumptionMultiplier"] = "AmmoConsumptionMultiplier",
    ["BulletInaccuracyMultiplier"] = "BulletInaccuracyMultiplier",
    ["DamageMultiplier"] = "DamageMultiplier",
    ["DoubleActionSpeedMultiplier"] = "DoubleActionSpeedMultiplier",
    ["DrawSpeedMultiplier"] = "DrawSpeedMultiplier",
    ["DrawTimeModifier"] = "DrawTimeModifier",
    ["FireRateMultiplier"] = "FireRateMultiplier",
    ["GunshotLoudnessMultiplier"] = "GunshotLoudnessMultiplier",
    ["HipInaccuracyMultiplier"] = "HipInaccuracyMultiplier",
    ["MagazineCapacityModifier"] = "MagazineCapacityModifier",
    ["OverallRecoilMultiplier"] = "OverallRecoilMultiplier",
    ["PenetrationMultiplier"] = "PenetrationMultiplier",
    ["PreventReload"] = "PreventReload",
    ["ReloadSpeedMultiplier"] = "ReloadSpeedMultiplier",
    ["ReloadTimeModifier"] = "ReloadTimeModifier",
    ["RunningInaccuracyMultiplier"] = "RunningInaccuracyMultiplier",
    ["ShotClipIdOverride"] = "ShotClipIdOverride",
    ["SpreadMultiplier"] = "SpreadMultiplier",
    ["SpreadPredictability"] = "SpreadPredictability",
}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentParam = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParam"]
Enums.AttachmentParam = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParam"]
AttachmentParam = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParam"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentParamState
---| "ActiveAndDisplayed"
---| "Disabled"
---| "SilentlyActive"
---| "UserInterface"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParamState"] = {
    ["ActiveAndDisplayed"] = "ActiveAndDisplayed",
    ["Disabled"] = "Disabled",
    ["SilentlyActive"] = "SilentlyActive",
    ["UserInterface"] = "UserInterface",
}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentParamState = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParamState"]
Enums.AttachmentParamState = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParamState"]
AttachmentParamState = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentParamState"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentSlot
---| "Ammunition"
---| "Barrel"
---| "Body"
---| "BottomRail"
---| "SideRail"
---| "Sight"
---| "Stability"
---| "Stock"
---| "Unassigned"
Enums["InventorySystem.Items.Firearms.Attachments.AttachmentSlot"] = {
    ["Ammunition"] = "Ammunition",
    ["Barrel"] = "Barrel",
    ["Body"] = "Body",
    ["BottomRail"] = "BottomRail",
    ["SideRail"] = "SideRail",
    ["Sight"] = "Sight",
    ["Stability"] = "Stability",
    ["Stock"] = "Stock",
    ["Unassigned"] = "Unassigned",
}
Enums.InventorySystem.Items.Firearms.Attachments.AttachmentSlot = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentSlot"]
Enums.AttachmentSlot = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentSlot"]
AttachmentSlot = Enums["InventorySystem.Items.Firearms.Attachments.AttachmentSlot"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_FlashlightAttachment_002e_RpcType
---| "Disabled"
---| "Enabled"
---| "FullResync"
Enums["InventorySystem.Items.Firearms.Attachments.FlashlightAttachment.RpcType"] = {
    ["Disabled"] = "Disabled",
    ["Enabled"] = "Enabled",
    ["FullResync"] = "FullResync",
}
Enums.InventorySystem.Items.Firearms.Attachments.FlashlightAttachment = {}
Enums.InventorySystem.Items.Firearms.Attachments.FlashlightAttachment.RpcType = Enums["InventorySystem.Items.Firearms.Attachments.FlashlightAttachment.RpcType"]
Enums.FlashlightAttachment = {}
Enums.FlashlightAttachment.RpcType = Enums["InventorySystem.Items.Firearms.Attachments.FlashlightAttachment.RpcType"]
Enums["FlashlightAttachment.RpcType"] = Enums["InventorySystem.Items.Firearms.Attachments.FlashlightAttachment.RpcType"]
FlashlightAttachment = {}
FlashlightAttachment.RpcType = Enums["InventorySystem.Items.Firearms.Attachments.FlashlightAttachment.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_ParameterMixingMode
---| "Additive"
---| "Override"
---| "Percent"
Enums["InventorySystem.Items.Firearms.Attachments.ParameterMixingMode"] = {
    ["Additive"] = "Additive",
    ["Override"] = "Override",
    ["Percent"] = "Percent",
}
Enums.InventorySystem.Items.Firearms.Attachments.ParameterMixingMode = Enums["InventorySystem.Items.Firearms.Attachments.ParameterMixingMode"]
Enums.ParameterMixingMode = Enums["InventorySystem.Items.Firearms.Attachments.ParameterMixingMode"]
ParameterMixingMode = Enums["InventorySystem.Items.Firearms.Attachments.ParameterMixingMode"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_WorkstationController_002e_WorkstationStatus
---| "Offline"
---| "Online"
---| "PoweringDown"
---| "PoweringUp"
Enums["InventorySystem.Items.Firearms.Attachments.WorkstationController.WorkstationStatus"] = {
    ["Offline"] = "Offline",
    ["Online"] = "Online",
    ["PoweringDown"] = "PoweringDown",
    ["PoweringUp"] = "PoweringUp",
}
Enums.InventorySystem.Items.Firearms.Attachments.WorkstationController = {}
Enums.InventorySystem.Items.Firearms.Attachments.WorkstationController.WorkstationStatus = Enums["InventorySystem.Items.Firearms.Attachments.WorkstationController.WorkstationStatus"]
Enums.WorkstationController = {}
Enums.WorkstationController.WorkstationStatus = Enums["InventorySystem.Items.Firearms.Attachments.WorkstationController.WorkstationStatus"]
Enums["WorkstationController.WorkstationStatus"] = Enums["InventorySystem.Items.Firearms.Attachments.WorkstationController.WorkstationStatus"]
WorkstationController = {}
WorkstationController.WorkstationStatus = Enums["InventorySystem.Items.Firearms.Attachments.WorkstationController.WorkstationStatus"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_ConditionalEvaluator_002e_AttachmentStatus
---| "Disabled"
---| "Enabled"
---| "EnabledAndNotReady"
---| "EnabledAndReady"
Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.AttachmentStatus"] = {
    ["Disabled"] = "Disabled",
    ["Enabled"] = "Enabled",
    ["EnabledAndNotReady"] = "EnabledAndNotReady",
    ["EnabledAndReady"] = "EnabledAndReady",
}
Enums.InventorySystem.Items.Firearms.Extensions = {}
Enums.InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator = {}
Enums.InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.AttachmentStatus = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.AttachmentStatus"]
Enums.ConditionalEvaluator = {}
Enums.ConditionalEvaluator.AttachmentStatus = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.AttachmentStatus"]
Enums["ConditionalEvaluator.AttachmentStatus"] = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.AttachmentStatus"]
ConditionalEvaluator = {}
ConditionalEvaluator.AttachmentStatus = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.AttachmentStatus"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_ConditionalEvaluator_002e_OtherCondition
---| "Equipped"
---| "Firstperson"
---| "Pickup"
---| "Thirdperson"
Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.OtherCondition"] = {
    ["Equipped"] = "Equipped",
    ["Firstperson"] = "Firstperson",
    ["Pickup"] = "Pickup",
    ["Thirdperson"] = "Thirdperson",
}
Enums.InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.OtherCondition = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.OtherCondition"]
Enums.ConditionalEvaluator.OtherCondition = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.OtherCondition"]
Enums["ConditionalEvaluator.OtherCondition"] = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.OtherCondition"]
ConditionalEvaluator.OtherCondition = Enums["InventorySystem.Items.Firearms.Extensions.ConditionalEvaluator.OtherCondition"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_DisruptorDualCamExtension_002e_IconType
---| "BurstMode"
---| "CrashWarn"
---| "CriticalWarn"
---| "DesintegratorMode"
---| "None"
Enums["InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension.IconType"] = {
    ["BurstMode"] = "BurstMode",
    ["CrashWarn"] = "CrashWarn",
    ["CriticalWarn"] = "CriticalWarn",
    ["DesintegratorMode"] = "DesintegratorMode",
    ["None"] = "None",
}
Enums.InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension = {}
Enums.InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension.IconType = Enums["InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension.IconType"]
Enums.DisruptorDualCamExtension = {}
Enums.DisruptorDualCamExtension.IconType = Enums["InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension.IconType"]
Enums["DisruptorDualCamExtension.IconType"] = Enums["InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension.IconType"]
DisruptorDualCamExtension = {}
DisruptorDualCamExtension.IconType = Enums["InventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension.IconType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_ViewmodelReloadOnlyLayerExtension_002e_Mode
---| "DuringReloadsAndUnloads"
---| "OutsideReloadsAndUnloads"
Enums["InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension.Mode"] = {
    ["DuringReloadsAndUnloads"] = "DuringReloadsAndUnloads",
    ["OutsideReloadsAndUnloads"] = "OutsideReloadsAndUnloads",
}
Enums.InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension = {}
Enums.InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension.Mode = Enums["InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension.Mode"]
Enums.ViewmodelReloadOnlyLayerExtension = {}
Enums.ViewmodelReloadOnlyLayerExtension.Mode = Enums["InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension.Mode"]
Enums["ViewmodelReloadOnlyLayerExtension.Mode"] = Enums["InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension.Mode"]
ViewmodelReloadOnlyLayerExtension = {}
ViewmodelReloadOnlyLayerExtension.Mode = Enums["InventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension.Mode"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_WorldmodelMagazineExtension_002e_RemovalMode
---| "DeactivateObject"
---| "SetScaleZero"
Enums["InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension.RemovalMode"] = {
    ["DeactivateObject"] = "DeactivateObject",
    ["SetScaleZero"] = "SetScaleZero",
}
Enums.InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension = {}
Enums.InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension.RemovalMode = Enums["InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension.RemovalMode"]
Enums.WorldmodelMagazineExtension = {}
Enums.WorldmodelMagazineExtension.RemovalMode = Enums["InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension.RemovalMode"]
Enums["WorldmodelMagazineExtension.RemovalMode"] = Enums["InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension.RemovalMode"]
WorldmodelMagazineExtension = {}
WorldmodelMagazineExtension.RemovalMode = Enums["InventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension.RemovalMode"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_FirearmCategory
---| "LightMachineGun"
---| "Pistol"
---| "Revolver"
---| "Rifle"
---| "Shotgun"
---| "SubmachineGun"
---| "Unclassified"
Enums["InventorySystem.Items.Firearms.FirearmCategory"] = {
    ["LightMachineGun"] = "LightMachineGun",
    ["Pistol"] = "Pistol",
    ["Revolver"] = "Revolver",
    ["Rifle"] = "Rifle",
    ["Shotgun"] = "Shotgun",
    ["SubmachineGun"] = "SubmachineGun",
    ["Unclassified"] = "Unclassified",
}
Enums.InventorySystem.Items.Firearms.FirearmCategory = Enums["InventorySystem.Items.Firearms.FirearmCategory"]
Enums.FirearmCategory = Enums["InventorySystem.Items.Firearms.FirearmCategory"]
FirearmCategory = Enums["InventorySystem.Items.Firearms.FirearmCategory"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_FirearmWorldmodelType
---| "Pickup"
---| "Presentation"
---| "Thirdperson"
Enums["InventorySystem.Items.Firearms.FirearmWorldmodelType"] = {
    ["Pickup"] = "Pickup",
    ["Presentation"] = "Presentation",
    ["Thirdperson"] = "Thirdperson",
}
Enums.InventorySystem.Items.Firearms.FirearmWorldmodelType = Enums["InventorySystem.Items.Firearms.FirearmWorldmodelType"]
Enums.FirearmWorldmodelType = Enums["InventorySystem.Items.Firearms.FirearmWorldmodelType"]
FirearmWorldmodelType = Enums["InventorySystem.Items.Firearms.FirearmWorldmodelType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AnimationToggleableReloaderModule_002e_RpcType
---| "LoadableAmmoSync"
---| "StopAnimations"
Enums["InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule.RpcType"] = {
    ["LoadableAmmoSync"] = "LoadableAmmoSync",
    ["StopAnimations"] = "StopAnimations",
}
Enums.InventorySystem.Items.Firearms.Modules = {}
Enums.InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule = {}
Enums.InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule.RpcType"]
Enums.AnimationToggleableReloaderModule = {}
Enums.AnimationToggleableReloaderModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule.RpcType"]
Enums["AnimationToggleableReloaderModule.RpcType"] = Enums["InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule.RpcType"]
AnimationToggleableReloaderModule = {}
AnimationToggleableReloaderModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AnimatorReloaderModuleBase_002e_ReloaderMessageHeader
---| "Custom"
---| "Reload"
---| "RequestRejected"
---| "Stop"
---| "Unload"
Enums["InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase.ReloaderMessageHeader"] = {
    ["Custom"] = "Custom",
    ["Reload"] = "Reload",
    ["RequestRejected"] = "RequestRejected",
    ["Stop"] = "Stop",
    ["Unload"] = "Unload",
}
Enums.InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase = {}
Enums.InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase.ReloaderMessageHeader = Enums["InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase.ReloaderMessageHeader"]
Enums.AnimatorReloaderModuleBase = {}
Enums.AnimatorReloaderModuleBase.ReloaderMessageHeader = Enums["InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase.ReloaderMessageHeader"]
Enums["AnimatorReloaderModuleBase.ReloaderMessageHeader"] = Enums["InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase.ReloaderMessageHeader"]
AnimatorReloaderModuleBase = {}
AnimatorReloaderModuleBase.ReloaderMessageHeader = Enums["InventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase.ReloaderMessageHeader"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AutomaticActionModule_002e_MessageHeader
---| "CmdShoot"
---| "RpcDryFire"
---| "RpcFire"
---| "RpcNewPlayerSync"
---| "RpcPublicSync"
---| "RpcRejectionReason"
---| "RpcResponse"
Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.MessageHeader"] = {
    ["CmdShoot"] = "CmdShoot",
    ["RpcDryFire"] = "RpcDryFire",
    ["RpcFire"] = "RpcFire",
    ["RpcNewPlayerSync"] = "RpcNewPlayerSync",
    ["RpcPublicSync"] = "RpcPublicSync",
    ["RpcRejectionReason"] = "RpcRejectionReason",
    ["RpcResponse"] = "RpcResponse",
}
Enums.InventorySystem.Items.Firearms.Modules.AutomaticActionModule = {}
Enums.InventorySystem.Items.Firearms.Modules.AutomaticActionModule.MessageHeader = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.MessageHeader"]
Enums.AutomaticActionModule = {}
Enums.AutomaticActionModule.MessageHeader = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.MessageHeader"]
Enums["AutomaticActionModule.MessageHeader"] = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.MessageHeader"]
AutomaticActionModule = {}
AutomaticActionModule.MessageHeader = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.MessageHeader"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AutomaticActionModule_002e_RejectionReason
---| "BoltLocked"
---| "ModuleBusy"
---| "NotCocked"
---| "TimedOut"
Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.RejectionReason"] = {
    ["BoltLocked"] = "BoltLocked",
    ["ModuleBusy"] = "ModuleBusy",
    ["NotCocked"] = "NotCocked",
    ["TimedOut"] = "TimedOut",
}
Enums.InventorySystem.Items.Firearms.Modules.AutomaticActionModule.RejectionReason = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.RejectionReason"]
Enums.AutomaticActionModule.RejectionReason = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.RejectionReason"]
Enums["AutomaticActionModule.RejectionReason"] = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.RejectionReason"]
AutomaticActionModule.RejectionReason = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.RejectionReason"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AutomaticActionModule_002e_SyncDataFlags
---| "AmmoChamberedBit0"
---| "AmmoChamberedBit1"
---| "AmmoChamberedBit2"
---| "AmmoChamberedBit3"
---| "AmmoChamberedFilter"
---| "BoltLocked"
---| "Cocked"
---| "None"
Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.SyncDataFlags"] = {
    ["AmmoChamberedBit0"] = "AmmoChamberedBit0",
    ["AmmoChamberedBit1"] = "AmmoChamberedBit1",
    ["AmmoChamberedBit2"] = "AmmoChamberedBit2",
    ["AmmoChamberedBit3"] = "AmmoChamberedBit3",
    ["AmmoChamberedFilter"] = "AmmoChamberedFilter",
    ["BoltLocked"] = "BoltLocked",
    ["Cocked"] = "Cocked",
    ["None"] = "None",
}
Enums.InventorySystem.Items.Firearms.Modules.AutomaticActionModule.SyncDataFlags = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.SyncDataFlags"]
Enums.AutomaticActionModule.SyncDataFlags = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.SyncDataFlags"]
Enums["AutomaticActionModule.SyncDataFlags"] = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.SyncDataFlags"]
AutomaticActionModule.SyncDataFlags = Enums["InventorySystem.Items.Firearms.Modules.AutomaticActionModule.SyncDataFlags"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_CylinderAmmoModule_002e_ChamberState
---| "Discharged"
---| "Empty"
---| "Live"
Enums["InventorySystem.Items.Firearms.Modules.CylinderAmmoModule.ChamberState"] = {
    ["Discharged"] = "Discharged",
    ["Empty"] = "Empty",
    ["Live"] = "Live",
}
Enums.InventorySystem.Items.Firearms.Modules.CylinderAmmoModule = {}
Enums.InventorySystem.Items.Firearms.Modules.CylinderAmmoModule.ChamberState = Enums["InventorySystem.Items.Firearms.Modules.CylinderAmmoModule.ChamberState"]
Enums.CylinderAmmoModule = {}
Enums.CylinderAmmoModule.ChamberState = Enums["InventorySystem.Items.Firearms.Modules.CylinderAmmoModule.ChamberState"]
Enums["CylinderAmmoModule.ChamberState"] = Enums["InventorySystem.Items.Firearms.Modules.CylinderAmmoModule.ChamberState"]
CylinderAmmoModule = {}
CylinderAmmoModule.ChamberState = Enums["InventorySystem.Items.Firearms.Modules.CylinderAmmoModule.ChamberState"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_DisruptorActionModule_002e_FiringState
---| "FiringRapid"
---| "FiringSingle"
---| "None"
Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.FiringState"] = {
    ["FiringRapid"] = "FiringRapid",
    ["FiringSingle"] = "FiringSingle",
    ["None"] = "None",
}
Enums.InventorySystem.Items.Firearms.Modules.DisruptorActionModule = {}
Enums.InventorySystem.Items.Firearms.Modules.DisruptorActionModule.FiringState = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.FiringState"]
Enums.DisruptorActionModule = {}
Enums.DisruptorActionModule.FiringState = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.FiringState"]
Enums["DisruptorActionModule.FiringState"] = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.FiringState"]
DisruptorActionModule = {}
DisruptorActionModule.FiringState = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.FiringState"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_DisruptorActionModule_002e_MessageType
---| "CmdConfirmDischarge"
---| "CmdRequestStartFiring"
---| "RpcOnShot"
---| "RpcRequireReloadFalse"
---| "RpcRequireReloadFullResync"
---| "RpcRequireReloadTrue"
---| "RpcStartFiring"
---| "RpcStopFiring"
Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.MessageType"] = {
    ["CmdConfirmDischarge"] = "CmdConfirmDischarge",
    ["CmdRequestStartFiring"] = "CmdRequestStartFiring",
    ["RpcOnShot"] = "RpcOnShot",
    ["RpcRequireReloadFalse"] = "RpcRequireReloadFalse",
    ["RpcRequireReloadFullResync"] = "RpcRequireReloadFullResync",
    ["RpcRequireReloadTrue"] = "RpcRequireReloadTrue",
    ["RpcStartFiring"] = "RpcStartFiring",
    ["RpcStopFiring"] = "RpcStopFiring",
}
Enums.InventorySystem.Items.Firearms.Modules.DisruptorActionModule.MessageType = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.MessageType"]
Enums.DisruptorActionModule.MessageType = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.MessageType"]
Enums["DisruptorActionModule.MessageType"] = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.MessageType"]
DisruptorActionModule.MessageType = Enums["InventorySystem.Items.Firearms.Modules.DisruptorActionModule.MessageType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_DoubleActionModule_002e_MessageType
---| "CmdShoot"
---| "CmdUpdatePulling"
---| "RpcDryFire"
---| "RpcFire"
---| "RpcNewPlayerResync"
---| "RpcSetCockedFalse"
---| "RpcSetCockedTrue"
---| "StartCocking"
---| "StartPulling"
Enums["InventorySystem.Items.Firearms.Modules.DoubleActionModule.MessageType"] = {
    ["CmdShoot"] = "CmdShoot",
    ["CmdUpdatePulling"] = "CmdUpdatePulling",
    ["RpcDryFire"] = "RpcDryFire",
    ["RpcFire"] = "RpcFire",
    ["RpcNewPlayerResync"] = "RpcNewPlayerResync",
    ["RpcSetCockedFalse"] = "RpcSetCockedFalse",
    ["RpcSetCockedTrue"] = "RpcSetCockedTrue",
    ["StartCocking"] = "StartCocking",
    ["StartPulling"] = "StartPulling",
}
Enums.InventorySystem.Items.Firearms.Modules.DoubleActionModule = {}
Enums.InventorySystem.Items.Firearms.Modules.DoubleActionModule.MessageType = Enums["InventorySystem.Items.Firearms.Modules.DoubleActionModule.MessageType"]
Enums.DoubleActionModule = {}
Enums.DoubleActionModule.MessageType = Enums["InventorySystem.Items.Firearms.Modules.DoubleActionModule.MessageType"]
Enums["DoubleActionModule.MessageType"] = Enums["InventorySystem.Items.Firearms.Modules.DoubleActionModule.MessageType"]
DoubleActionModule = {}
DoubleActionModule.MessageType = Enums["InventorySystem.Items.Firearms.Modules.DoubleActionModule.MessageType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_EventBasedEquipperModule_002e_RpcType
---| "FirstTimeFalse"
---| "FirstTimeTrue"
---| "ResyncAllFirstTime"
---| "SeedSync"
Enums["InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule.RpcType"] = {
    ["FirstTimeFalse"] = "FirstTimeFalse",
    ["FirstTimeTrue"] = "FirstTimeTrue",
    ["ResyncAllFirstTime"] = "ResyncAllFirstTime",
    ["SeedSync"] = "SeedSync",
}
Enums.InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule = {}
Enums.InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule.RpcType"]
Enums.EventBasedEquipperModule = {}
Enums.EventBasedEquipperModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule.RpcType"]
Enums["EventBasedEquipperModule.RpcType"] = Enums["InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule.RpcType"]
EventBasedEquipperModule = {}
EventBasedEquipperModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.EventBasedEquipperModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_IReloadUnloadValidatorModule_002e_Authorization
---| "Allowed"
---| "Idle"
---| "Vetoed"
Enums["InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule.Authorization"] = {
    ["Allowed"] = "Allowed",
    ["Idle"] = "Idle",
    ["Vetoed"] = "Vetoed",
}
Enums.InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule = {}
Enums.InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule.Authorization = Enums["InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule.Authorization"]
Enums.IReloadUnloadValidatorModule = {}
Enums.IReloadUnloadValidatorModule.Authorization = Enums["InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule.Authorization"]
Enums["IReloadUnloadValidatorModule.Authorization"] = Enums["InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule.Authorization"]
IReloadUnloadValidatorModule = {}
IReloadUnloadValidatorModule.Authorization = Enums["InventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule.Authorization"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_ImpactEffectsModule_002e_RpcType
---| "ImpactDecal"
---| "PlayerHit"
---| "TracerDefault"
---| "TracerOverride"
Enums["InventorySystem.Items.Firearms.Modules.ImpactEffectsModule.RpcType"] = {
    ["ImpactDecal"] = "ImpactDecal",
    ["PlayerHit"] = "PlayerHit",
    ["TracerDefault"] = "TracerDefault",
    ["TracerOverride"] = "TracerOverride",
}
Enums.InventorySystem.Items.Firearms.Modules.ImpactEffectsModule = {}
Enums.InventorySystem.Items.Firearms.Modules.ImpactEffectsModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.ImpactEffectsModule.RpcType"]
Enums.ImpactEffectsModule = {}
Enums.ImpactEffectsModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.ImpactEffectsModule.RpcType"]
Enums["ImpactEffectsModule.RpcType"] = Enums["InventorySystem.Items.Firearms.Modules.ImpactEffectsModule.RpcType"]
ImpactEffectsModule = {}
ImpactEffectsModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.ImpactEffectsModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Misc_002e_StatModifier_002e_ModifierMode
---| "Add"
---| "Inactive"
---| "Multiply"
---| "Override"
Enums["InventorySystem.Items.Firearms.Modules.Misc.StatModifier.ModifierMode"] = {
    ["Add"] = "Add",
    ["Inactive"] = "Inactive",
    ["Multiply"] = "Multiply",
    ["Override"] = "Override",
}
Enums.InventorySystem.Items.Firearms.Modules.Misc = {}
Enums.InventorySystem.Items.Firearms.Modules.Misc.StatModifier = {}
Enums.InventorySystem.Items.Firearms.Modules.Misc.StatModifier.ModifierMode = Enums["InventorySystem.Items.Firearms.Modules.Misc.StatModifier.ModifierMode"]
Enums.StatModifier = {}
Enums.StatModifier.ModifierMode = Enums["InventorySystem.Items.Firearms.Modules.Misc.StatModifier.ModifierMode"]
Enums["StatModifier.ModifierMode"] = Enums["InventorySystem.Items.Firearms.Modules.Misc.StatModifier.ModifierMode"]
StatModifier = {}
StatModifier.ModifierMode = Enums["InventorySystem.Items.Firearms.Modules.Misc.StatModifier.ModifierMode"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_PumpActionModule_002e_RpcType
---| "ResyncAll"
---| "ResyncOne"
---| "SchedulePump"
---| "Shoot"
Enums["InventorySystem.Items.Firearms.Modules.PumpActionModule.RpcType"] = {
    ["ResyncAll"] = "ResyncAll",
    ["ResyncOne"] = "ResyncOne",
    ["SchedulePump"] = "SchedulePump",
    ["Shoot"] = "Shoot",
}
Enums.InventorySystem.Items.Firearms.Modules.PumpActionModule = {}
Enums.InventorySystem.Items.Firearms.Modules.PumpActionModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.PumpActionModule.RpcType"]
Enums.PumpActionModule = {}
Enums.PumpActionModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.PumpActionModule.RpcType"]
Enums["PumpActionModule.RpcType"] = Enums["InventorySystem.Items.Firearms.Modules.PumpActionModule.RpcType"]
PumpActionModule = {}
PumpActionModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.PumpActionModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127Tier
---| "Tier1"
---| "Tier2"
---| "Tier3"
Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"] = {
    ["Tier1"] = "Tier1",
    ["Tier2"] = "Tier2",
    ["Tier3"] = "Tier3",
}
Enums.InventorySystem.Items.Firearms.Modules.Scp127 = {}
Enums.InventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"]
Enums.Scp127Tier = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"]
Scp127Tier = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127VoiceLineManagerModule_002e_RpcType
---| "OwnerRegistered"
---| "PlayLine"
Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule.RpcType"] = {
    ["OwnerRegistered"] = "OwnerRegistered",
    ["PlayLine"] = "PlayLine",
}
Enums.InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule = {}
Enums.InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule.RpcType"]
Enums.Scp127VoiceLineManagerModule = {}
Enums.Scp127VoiceLineManagerModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule.RpcType"]
Enums["Scp127VoiceLineManagerModule.RpcType"] = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule.RpcType"]
Scp127VoiceLineManagerModule = {}
Scp127VoiceLineManagerModule.RpcType = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127VoiceLinesTranslation
---| "ChamberingDammitThatHurts"
---| "ChamberingFeelThatTmrw"
---| "ChamberingGrunt1"
---| "ChamberingGrunt2"
---| "ChamberingGrunt3"
---| "ChamberingGrunt4"
---| "ChamberingHateThatPart"
---| "ChamberingNopeNeverGettinBetter"
---| "ChamberingThatStings"
---| "ChaosSpawnDrowningConstantly"
---| "ChaosSpawnIDontTrustEm"
---| "ChaosSpawnNotHereNotAgain"
---| "ChaosSpawnNotJustMe"
---| "ChaosSpawnThemAgain"
---| "ChaosSpawnThisIsntGonnaEndWell"
---| "DrawGoodToSeeYa"
---| "DrawHello"
---| "DrawHelloAgain"
---| "DrawHelloThere"
---| "DrawHeyHey"
---| "DrawHowsItGoin"
---| "DroppedDontForgetAboutMe"
---| "DroppedGuessIllWaitHere"
---| "DroppedIllCatchUp"
---| "DroppedSeeyaLaterBoss"
---| "DroppedTakeCareBoss"
---| "DroppedWatchThePaint"
---| "HolsterBeHereIfYouNeedMe"
---| "HolsterByeBye"
---| "HolsterFairEnough"
---| "HolsterGoodbye"
---| "HolsterIsTheSafetyOn"
---| "HolsterIsTheSafetyOn2"
---| "HolsterOffIGo"
---| "HolsterYouKnowWhereToFindMeBoss"
---| "IdleChatterBarDownInQueens"
---| "IdleChatterDifferentFacility"
---| "IdleChatterGettingPaidForThisRight"
---| "IdleChatterSoundedLikeDeath"
---| "IdleChatterThoughtIHeardSomething"
---| "IdleChatterWhatWentWrong"
---| "IdleChatterYouGotAnyPlans"
---| "IdleChatterYouKnowWhereYoureGoin"
---| "MissedAlmostHitSomething"
---| "MissedILikeYourStyleBoss"
---| "MissedInvisHatFellaAround"
---| "MissedUhYouGoodBoss"
---| "MtfSpawnAhaReinforcements"
---| "MtfSpawnDontLetEmTakeMeAgain"
---| "MtfSpawnDontSayAnything"
---| "MtfSpawnFriendsOfYours"
---| "MtfSpawnGotAPlan"
---| "MtfSpawnThatsNotGood"
---| "MtfSpawnThingsAreGettingInterestingNow"
---| "MtfSpawnWereGettingBackup"
---| "OnKillBang"
---| "OnKillBeatItBopIt"
---| "OnKillGoodOneBoss"
---| "OnKillGoodStuff"
---| "OnKillGoodWorkBoss"
---| "OnKillNiceOneBoss"
---| "OnKillNiceShot"
---| "OnKillOooNice"
---| "OnKillRattleHim"
---| "OnKillScram"
---| "OnKillWhamo"
---| "PickupChamberNecksnapMcgee"
---| "PickupChamberPleasedToMeetYa"
---| "PickupChamberRoomForOneMore"
---| "PickupChamberSayWhatYearIsIt"
---| "PickupChamberSweetOxygen"
---| "PickupChamberWaterboarding"
---| "PickupChaosGasMask"
---| "PickupChaosSickGladRagsBoss"
---| "PickupChaosYouFellasFromOuttaTown"
---| "PickupDclassCouplaCorpses"
---| "PickupDclassNeedAHand"
---| "PickupDclassPartnersInCrime"
---| "PickupDclassTagEmBoss"
---| "PickupDclassUsedToOwnAJumpsuit"
---| "PickupDclassWooBreakout"
---| "PickupGenericEasyOnTheGrip"
---| "PickupGenericHeyBossHowsItGoin"
---| "PickupGenericSobbing"
---| "PickupGenericTakeMeWith"
---| "PickupMtfAnyChanceICanGo"
---| "PickupMtfAwJeezRick"
---| "PickupScientistDidntYouStickMeInThatTank"
---| "PickupScientistThoseGlassesBetterBe"
---| "PickupScientistYouDoKnowHowToShootRight"
---| "PickupTutorialHereToRescueMe"
---| "PickupTutorialOutOfPlaceHereBoss"
---| "RankupBetterThanExpected"
---| "RankupDoinPrettyGood"
---| "RankupFeelsGoodToBeBack"
---| "RankupIThinkWereWinning"
---| "RankupManicLaughter"
---| "RankupPartnersInCrimeOrJustice"
---| "ScpKilled049AppleADay"
---| "ScpKilled049BehindThatBeak"
---| "ScpKilled049IAmTheBlackDeath"
---| "ScpKilled049MsBirdface"
---| "ScpKilled049OverdosedOnShells"
---| "ScpKilled049ResurrectThis"
---| "ScpKilled049TakeGoodCareOfTheKids"
---| "ScpKilled0790101Moron"
---| "ScpKilled079AintDyinToAMinifridge"
---| "ScpKilled079KeepTheCamera"
---| "ScpKilled079NeverLikedThatToaster"
---| "ScpKilled079WarrantyRunOut"
---| "ScpKilled0967FeetTall"
---| "ScpKilled096AllTheRage"
---| "ScpKilled096IMayNotHaveEyes"
---| "ScpKilled096IMayNotHaveEyes2"
---| "ScpKilled096LikedTheStatue"
---| "ScpKilled096ThoughtHedNeverShutUp"
---| "ScpKilled106HaveFunInTheCage"
---| "ScpKilled106OldDudesGone"
---| "ScpKilled106PocketThisDimension"
---| "ScpKilled106SinkingFeeling"
---| "ScpKilled106StayForABit"
---| "ScpKilled106SunkIntoTheFloor"
---| "ScpKilled173EyeOpener"
---| "ScpKilled173PeiceThatBackTogether"
---| "ScpKilled173ScrapingWasGettingOnMyNerves"
---| "ScpKilled173ShatteredLikeGlass"
---| "ScpKilled3114NoMoreBones"
---| "ScpKilled3114OneLessSkeleton"
---| "ScpKilled3114WeGotTheSkinwalker"
---| "ScpKilled939NothingToSayNow"
---| "ScpKilled939StayDownFreakshow"
---| "ScpKilled939YouAintTheFirst"
---| "ScpKilled939YouAintTheFirst2"
---| "ScpKilledAllThoseFreakyPowers"
---| "ScpKilledCoupleOfRounds"
---| "ScpKilledGunBeatsWhateverYouAre"
---| "ScpKilledGunBeatsWhateverYouAre2"
---| "ScpKilledNotSoTough"
---| "ScpKilledWhyBuildAContainer"
---| "ScpKilledZombieAintNoPill"
---| "ScpKilledZombieGetHisGoons"
---| "ScpKilledZombieMr12FeetUnder"
---| "ScpKilledZombieSayGoodbyeToUrGoons"
---| "ScpKilledZombieStayDown"
---| "ScpKilledZombieTwoLivesTooMany"
---| "ScpKilledZombieWhatGetsUp"
---| "UserKilledAttacthmentIssues"
---| "UserKilledBooooosss"
---| "UserKilledHeyHeyGetUp"
---| "UserKilledJustStartingToLikeYou"
---| "UserKilledNoNotNow"
---| "UserKilledNoNotNow2"
---| "UserKilledNotAgain"
---| "UserKilledNotAgain2"
Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"] = {
    ["ChamberingDammitThatHurts"] = "ChamberingDammitThatHurts",
    ["ChamberingFeelThatTmrw"] = "ChamberingFeelThatTmrw",
    ["ChamberingGrunt1"] = "ChamberingGrunt1",
    ["ChamberingGrunt2"] = "ChamberingGrunt2",
    ["ChamberingGrunt3"] = "ChamberingGrunt3",
    ["ChamberingGrunt4"] = "ChamberingGrunt4",
    ["ChamberingHateThatPart"] = "ChamberingHateThatPart",
    ["ChamberingNopeNeverGettinBetter"] = "ChamberingNopeNeverGettinBetter",
    ["ChamberingThatStings"] = "ChamberingThatStings",
    ["ChaosSpawnDrowningConstantly"] = "ChaosSpawnDrowningConstantly",
    ["ChaosSpawnIDontTrustEm"] = "ChaosSpawnIDontTrustEm",
    ["ChaosSpawnNotHereNotAgain"] = "ChaosSpawnNotHereNotAgain",
    ["ChaosSpawnNotJustMe"] = "ChaosSpawnNotJustMe",
    ["ChaosSpawnThemAgain"] = "ChaosSpawnThemAgain",
    ["ChaosSpawnThisIsntGonnaEndWell"] = "ChaosSpawnThisIsntGonnaEndWell",
    ["DrawGoodToSeeYa"] = "DrawGoodToSeeYa",
    ["DrawHello"] = "DrawHello",
    ["DrawHelloAgain"] = "DrawHelloAgain",
    ["DrawHelloThere"] = "DrawHelloThere",
    ["DrawHeyHey"] = "DrawHeyHey",
    ["DrawHowsItGoin"] = "DrawHowsItGoin",
    ["DroppedDontForgetAboutMe"] = "DroppedDontForgetAboutMe",
    ["DroppedGuessIllWaitHere"] = "DroppedGuessIllWaitHere",
    ["DroppedIllCatchUp"] = "DroppedIllCatchUp",
    ["DroppedSeeyaLaterBoss"] = "DroppedSeeyaLaterBoss",
    ["DroppedTakeCareBoss"] = "DroppedTakeCareBoss",
    ["DroppedWatchThePaint"] = "DroppedWatchThePaint",
    ["HolsterBeHereIfYouNeedMe"] = "HolsterBeHereIfYouNeedMe",
    ["HolsterByeBye"] = "HolsterByeBye",
    ["HolsterFairEnough"] = "HolsterFairEnough",
    ["HolsterGoodbye"] = "HolsterGoodbye",
    ["HolsterIsTheSafetyOn"] = "HolsterIsTheSafetyOn",
    ["HolsterIsTheSafetyOn2"] = "HolsterIsTheSafetyOn2",
    ["HolsterOffIGo"] = "HolsterOffIGo",
    ["HolsterYouKnowWhereToFindMeBoss"] = "HolsterYouKnowWhereToFindMeBoss",
    ["IdleChatterBarDownInQueens"] = "IdleChatterBarDownInQueens",
    ["IdleChatterDifferentFacility"] = "IdleChatterDifferentFacility",
    ["IdleChatterGettingPaidForThisRight"] = "IdleChatterGettingPaidForThisRight",
    ["IdleChatterSoundedLikeDeath"] = "IdleChatterSoundedLikeDeath",
    ["IdleChatterThoughtIHeardSomething"] = "IdleChatterThoughtIHeardSomething",
    ["IdleChatterWhatWentWrong"] = "IdleChatterWhatWentWrong",
    ["IdleChatterYouGotAnyPlans"] = "IdleChatterYouGotAnyPlans",
    ["IdleChatterYouKnowWhereYoureGoin"] = "IdleChatterYouKnowWhereYoureGoin",
    ["MissedAlmostHitSomething"] = "MissedAlmostHitSomething",
    ["MissedILikeYourStyleBoss"] = "MissedILikeYourStyleBoss",
    ["MissedInvisHatFellaAround"] = "MissedInvisHatFellaAround",
    ["MissedUhYouGoodBoss"] = "MissedUhYouGoodBoss",
    ["MtfSpawnAhaReinforcements"] = "MtfSpawnAhaReinforcements",
    ["MtfSpawnDontLetEmTakeMeAgain"] = "MtfSpawnDontLetEmTakeMeAgain",
    ["MtfSpawnDontSayAnything"] = "MtfSpawnDontSayAnything",
    ["MtfSpawnFriendsOfYours"] = "MtfSpawnFriendsOfYours",
    ["MtfSpawnGotAPlan"] = "MtfSpawnGotAPlan",
    ["MtfSpawnThatsNotGood"] = "MtfSpawnThatsNotGood",
    ["MtfSpawnThingsAreGettingInterestingNow"] = "MtfSpawnThingsAreGettingInterestingNow",
    ["MtfSpawnWereGettingBackup"] = "MtfSpawnWereGettingBackup",
    ["OnKillBang"] = "OnKillBang",
    ["OnKillBeatItBopIt"] = "OnKillBeatItBopIt",
    ["OnKillGoodOneBoss"] = "OnKillGoodOneBoss",
    ["OnKillGoodStuff"] = "OnKillGoodStuff",
    ["OnKillGoodWorkBoss"] = "OnKillGoodWorkBoss",
    ["OnKillNiceOneBoss"] = "OnKillNiceOneBoss",
    ["OnKillNiceShot"] = "OnKillNiceShot",
    ["OnKillOooNice"] = "OnKillOooNice",
    ["OnKillRattleHim"] = "OnKillRattleHim",
    ["OnKillScram"] = "OnKillScram",
    ["OnKillWhamo"] = "OnKillWhamo",
    ["PickupChamberNecksnapMcgee"] = "PickupChamberNecksnapMcgee",
    ["PickupChamberPleasedToMeetYa"] = "PickupChamberPleasedToMeetYa",
    ["PickupChamberRoomForOneMore"] = "PickupChamberRoomForOneMore",
    ["PickupChamberSayWhatYearIsIt"] = "PickupChamberSayWhatYearIsIt",
    ["PickupChamberSweetOxygen"] = "PickupChamberSweetOxygen",
    ["PickupChamberWaterboarding"] = "PickupChamberWaterboarding",
    ["PickupChaosGasMask"] = "PickupChaosGasMask",
    ["PickupChaosSickGladRagsBoss"] = "PickupChaosSickGladRagsBoss",
    ["PickupChaosYouFellasFromOuttaTown"] = "PickupChaosYouFellasFromOuttaTown",
    ["PickupDclassCouplaCorpses"] = "PickupDclassCouplaCorpses",
    ["PickupDclassNeedAHand"] = "PickupDclassNeedAHand",
    ["PickupDclassPartnersInCrime"] = "PickupDclassPartnersInCrime",
    ["PickupDclassTagEmBoss"] = "PickupDclassTagEmBoss",
    ["PickupDclassUsedToOwnAJumpsuit"] = "PickupDclassUsedToOwnAJumpsuit",
    ["PickupDclassWooBreakout"] = "PickupDclassWooBreakout",
    ["PickupGenericEasyOnTheGrip"] = "PickupGenericEasyOnTheGrip",
    ["PickupGenericHeyBossHowsItGoin"] = "PickupGenericHeyBossHowsItGoin",
    ["PickupGenericSobbing"] = "PickupGenericSobbing",
    ["PickupGenericTakeMeWith"] = "PickupGenericTakeMeWith",
    ["PickupMtfAnyChanceICanGo"] = "PickupMtfAnyChanceICanGo",
    ["PickupMtfAwJeezRick"] = "PickupMtfAwJeezRick",
    ["PickupScientistDidntYouStickMeInThatTank"] = "PickupScientistDidntYouStickMeInThatTank",
    ["PickupScientistThoseGlassesBetterBe"] = "PickupScientistThoseGlassesBetterBe",
    ["PickupScientistYouDoKnowHowToShootRight"] = "PickupScientistYouDoKnowHowToShootRight",
    ["PickupTutorialHereToRescueMe"] = "PickupTutorialHereToRescueMe",
    ["PickupTutorialOutOfPlaceHereBoss"] = "PickupTutorialOutOfPlaceHereBoss",
    ["RankupBetterThanExpected"] = "RankupBetterThanExpected",
    ["RankupDoinPrettyGood"] = "RankupDoinPrettyGood",
    ["RankupFeelsGoodToBeBack"] = "RankupFeelsGoodToBeBack",
    ["RankupIThinkWereWinning"] = "RankupIThinkWereWinning",
    ["RankupManicLaughter"] = "RankupManicLaughter",
    ["RankupPartnersInCrimeOrJustice"] = "RankupPartnersInCrimeOrJustice",
    ["ScpKilled049AppleADay"] = "ScpKilled049AppleADay",
    ["ScpKilled049BehindThatBeak"] = "ScpKilled049BehindThatBeak",
    ["ScpKilled049IAmTheBlackDeath"] = "ScpKilled049IAmTheBlackDeath",
    ["ScpKilled049MsBirdface"] = "ScpKilled049MsBirdface",
    ["ScpKilled049OverdosedOnShells"] = "ScpKilled049OverdosedOnShells",
    ["ScpKilled049ResurrectThis"] = "ScpKilled049ResurrectThis",
    ["ScpKilled049TakeGoodCareOfTheKids"] = "ScpKilled049TakeGoodCareOfTheKids",
    ["ScpKilled0790101Moron"] = "ScpKilled0790101Moron",
    ["ScpKilled079AintDyinToAMinifridge"] = "ScpKilled079AintDyinToAMinifridge",
    ["ScpKilled079KeepTheCamera"] = "ScpKilled079KeepTheCamera",
    ["ScpKilled079NeverLikedThatToaster"] = "ScpKilled079NeverLikedThatToaster",
    ["ScpKilled079WarrantyRunOut"] = "ScpKilled079WarrantyRunOut",
    ["ScpKilled0967FeetTall"] = "ScpKilled0967FeetTall",
    ["ScpKilled096AllTheRage"] = "ScpKilled096AllTheRage",
    ["ScpKilled096IMayNotHaveEyes"] = "ScpKilled096IMayNotHaveEyes",
    ["ScpKilled096IMayNotHaveEyes2"] = "ScpKilled096IMayNotHaveEyes2",
    ["ScpKilled096LikedTheStatue"] = "ScpKilled096LikedTheStatue",
    ["ScpKilled096ThoughtHedNeverShutUp"] = "ScpKilled096ThoughtHedNeverShutUp",
    ["ScpKilled106HaveFunInTheCage"] = "ScpKilled106HaveFunInTheCage",
    ["ScpKilled106OldDudesGone"] = "ScpKilled106OldDudesGone",
    ["ScpKilled106PocketThisDimension"] = "ScpKilled106PocketThisDimension",
    ["ScpKilled106SinkingFeeling"] = "ScpKilled106SinkingFeeling",
    ["ScpKilled106StayForABit"] = "ScpKilled106StayForABit",
    ["ScpKilled106SunkIntoTheFloor"] = "ScpKilled106SunkIntoTheFloor",
    ["ScpKilled173EyeOpener"] = "ScpKilled173EyeOpener",
    ["ScpKilled173PeiceThatBackTogether"] = "ScpKilled173PeiceThatBackTogether",
    ["ScpKilled173ScrapingWasGettingOnMyNerves"] = "ScpKilled173ScrapingWasGettingOnMyNerves",
    ["ScpKilled173ShatteredLikeGlass"] = "ScpKilled173ShatteredLikeGlass",
    ["ScpKilled3114NoMoreBones"] = "ScpKilled3114NoMoreBones",
    ["ScpKilled3114OneLessSkeleton"] = "ScpKilled3114OneLessSkeleton",
    ["ScpKilled3114WeGotTheSkinwalker"] = "ScpKilled3114WeGotTheSkinwalker",
    ["ScpKilled939NothingToSayNow"] = "ScpKilled939NothingToSayNow",
    ["ScpKilled939StayDownFreakshow"] = "ScpKilled939StayDownFreakshow",
    ["ScpKilled939YouAintTheFirst"] = "ScpKilled939YouAintTheFirst",
    ["ScpKilled939YouAintTheFirst2"] = "ScpKilled939YouAintTheFirst2",
    ["ScpKilledAllThoseFreakyPowers"] = "ScpKilledAllThoseFreakyPowers",
    ["ScpKilledCoupleOfRounds"] = "ScpKilledCoupleOfRounds",
    ["ScpKilledGunBeatsWhateverYouAre"] = "ScpKilledGunBeatsWhateverYouAre",
    ["ScpKilledGunBeatsWhateverYouAre2"] = "ScpKilledGunBeatsWhateverYouAre2",
    ["ScpKilledNotSoTough"] = "ScpKilledNotSoTough",
    ["ScpKilledWhyBuildAContainer"] = "ScpKilledWhyBuildAContainer",
    ["ScpKilledZombieAintNoPill"] = "ScpKilledZombieAintNoPill",
    ["ScpKilledZombieGetHisGoons"] = "ScpKilledZombieGetHisGoons",
    ["ScpKilledZombieMr12FeetUnder"] = "ScpKilledZombieMr12FeetUnder",
    ["ScpKilledZombieSayGoodbyeToUrGoons"] = "ScpKilledZombieSayGoodbyeToUrGoons",
    ["ScpKilledZombieStayDown"] = "ScpKilledZombieStayDown",
    ["ScpKilledZombieTwoLivesTooMany"] = "ScpKilledZombieTwoLivesTooMany",
    ["ScpKilledZombieWhatGetsUp"] = "ScpKilledZombieWhatGetsUp",
    ["UserKilledAttacthmentIssues"] = "UserKilledAttacthmentIssues",
    ["UserKilledBooooosss"] = "UserKilledBooooosss",
    ["UserKilledHeyHeyGetUp"] = "UserKilledHeyHeyGetUp",
    ["UserKilledJustStartingToLikeYou"] = "UserKilledJustStartingToLikeYou",
    ["UserKilledNoNotNow"] = "UserKilledNoNotNow",
    ["UserKilledNoNotNow2"] = "UserKilledNoNotNow2",
    ["UserKilledNotAgain"] = "UserKilledNotAgain",
    ["UserKilledNotAgain2"] = "UserKilledNotAgain2",
}
Enums.InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"]
Enums.Scp127VoiceLinesTranslation = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"]
Scp127VoiceLinesTranslation = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127VoiceTriggerBase_002e_VoiceLinePriority
---| "High"
---| "Low"
---| "Normal"
---| "VeryHigh"
Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority"] = {
    ["High"] = "High",
    ["Low"] = "Low",
    ["Normal"] = "Normal",
    ["VeryHigh"] = "VeryHigh",
}
Enums.InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase = {}
Enums.InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority"]
Enums.Scp127VoiceTriggerBase = {}
Enums.Scp127VoiceTriggerBase.VoiceLinePriority = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority"]
Enums["Scp127VoiceTriggerBase.VoiceLinePriority"] = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority"]
Scp127VoiceTriggerBase = {}
Scp127VoiceTriggerBase.VoiceLinePriority = Enums["InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority"]

---@alias Enum_InventorySystem_002e_Items_002e_Firearms_002e_Thirdperson_002e_FirearmThirdpersonItem_002e_FirearmAnim
---| "Ads"
---| "Hip"
---| "Reload"
Enums["InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem.FirearmAnim"] = {
    ["Ads"] = "Ads",
    ["Hip"] = "Hip",
    ["Reload"] = "Reload",
}
Enums.InventorySystem.Items.Firearms.Thirdperson = {}
Enums.InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem = {}
Enums.InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem.FirearmAnim = Enums["InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem.FirearmAnim"]
Enums.FirearmThirdpersonItem = {}
Enums.FirearmThirdpersonItem.FirearmAnim = Enums["InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem.FirearmAnim"]
Enums["FirearmThirdpersonItem.FirearmAnim"] = Enums["InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem.FirearmAnim"]
FirearmThirdpersonItem = {}
FirearmThirdpersonItem.FirearmAnim = Enums["InventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem.FirearmAnim"]

---@alias Enum_InventorySystem_002e_Items_002e_ItemAddReason
---| "AdminCommand"
---| "PickedUp"
---| "Scp2536"
---| "Scp914Upgrade"
---| "StartingItem"
---| "StatusEffect"
---| "Undefined"
Enums["InventorySystem.Items.ItemAddReason"] = {
    ["AdminCommand"] = "AdminCommand",
    ["PickedUp"] = "PickedUp",
    ["Scp2536"] = "Scp2536",
    ["Scp914Upgrade"] = "Scp914Upgrade",
    ["StartingItem"] = "StartingItem",
    ["StatusEffect"] = "StatusEffect",
    ["Undefined"] = "Undefined",
}
Enums.InventorySystem.Items.ItemAddReason = Enums["InventorySystem.Items.ItemAddReason"]
Enums.ItemAddReason = Enums["InventorySystem.Items.ItemAddReason"]
ItemAddReason = Enums["InventorySystem.Items.ItemAddReason"]

---@alias Enum_InventorySystem_002e_Items_002e_ItemDescriptionType
---| "Armor"
---| "Custom"
---| "Default"
---| "Firearm"
---| "Keycard"
---| "None"
---| "Scp330Bag"
Enums["InventorySystem.Items.ItemDescriptionType"] = {
    ["Armor"] = "Armor",
    ["Custom"] = "Custom",
    ["Default"] = "Default",
    ["Firearm"] = "Firearm",
    ["Keycard"] = "Keycard",
    ["None"] = "None",
    ["Scp330Bag"] = "Scp330Bag",
}
Enums.InventorySystem.Items.ItemDescriptionType = Enums["InventorySystem.Items.ItemDescriptionType"]
Enums.ItemDescriptionType = Enums["InventorySystem.Items.ItemDescriptionType"]
ItemDescriptionType = Enums["InventorySystem.Items.ItemDescriptionType"]

---@alias Enum_InventorySystem_002e_Items_002e_Jailbird_002e_JailbirdMessageType
---| "AttackPerformed"
---| "AttackTriggered"
---| "Broken"
---| "ChargeFailed"
---| "ChargeLoadTriggered"
---| "ChargeStarted"
---| "Holstered"
---| "Inspect"
---| "UpdateState"
Enums["InventorySystem.Items.Jailbird.JailbirdMessageType"] = {
    ["AttackPerformed"] = "AttackPerformed",
    ["AttackTriggered"] = "AttackTriggered",
    ["Broken"] = "Broken",
    ["ChargeFailed"] = "ChargeFailed",
    ["ChargeLoadTriggered"] = "ChargeLoadTriggered",
    ["ChargeStarted"] = "ChargeStarted",
    ["Holstered"] = "Holstered",
    ["Inspect"] = "Inspect",
    ["UpdateState"] = "UpdateState",
}
Enums.InventorySystem.Items.Jailbird = {}
Enums.InventorySystem.Items.Jailbird.JailbirdMessageType = Enums["InventorySystem.Items.Jailbird.JailbirdMessageType"]
Enums.JailbirdMessageType = Enums["InventorySystem.Items.Jailbird.JailbirdMessageType"]
JailbirdMessageType = Enums["InventorySystem.Items.Jailbird.JailbirdMessageType"]

---@alias Enum_InventorySystem_002e_Items_002e_Jailbird_002e_JailbirdWearState
---| "AlmostBroken"
---| "Broken"
---| "Healthy"
---| "HighWear"
---| "LowWear"
---| "MediumWear"
Enums["InventorySystem.Items.Jailbird.JailbirdWearState"] = {
    ["AlmostBroken"] = "AlmostBroken",
    ["Broken"] = "Broken",
    ["Healthy"] = "Healthy",
    ["HighWear"] = "HighWear",
    ["LowWear"] = "LowWear",
    ["MediumWear"] = "MediumWear",
}
Enums.InventorySystem.Items.Jailbird.JailbirdWearState = Enums["InventorySystem.Items.Jailbird.JailbirdWearState"]
Enums.JailbirdWearState = Enums["InventorySystem.Items.Jailbird.JailbirdWearState"]
JailbirdWearState = Enums["InventorySystem.Items.Jailbird.JailbirdWearState"]

---@alias Enum_InventorySystem_002e_Items_002e_Keycards_002e_ChaosKeycardItem_002e_ChaosMsgType
---| "MovementSwitch"
---| "NewConnectionFullSync"
---| "SnakeMsgSync"
---| "UseDetails"
Enums["InventorySystem.Items.Keycards.ChaosKeycardItem.ChaosMsgType"] = {
    ["MovementSwitch"] = "MovementSwitch",
    ["NewConnectionFullSync"] = "NewConnectionFullSync",
    ["SnakeMsgSync"] = "SnakeMsgSync",
    ["UseDetails"] = "UseDetails",
}
Enums.InventorySystem.Items.Keycards = {}
Enums.InventorySystem.Items.Keycards.ChaosKeycardItem = {}
Enums.InventorySystem.Items.Keycards.ChaosKeycardItem.ChaosMsgType = Enums["InventorySystem.Items.Keycards.ChaosKeycardItem.ChaosMsgType"]
Enums.ChaosKeycardItem = {}
Enums.ChaosKeycardItem.ChaosMsgType = Enums["InventorySystem.Items.Keycards.ChaosKeycardItem.ChaosMsgType"]
Enums["ChaosKeycardItem.ChaosMsgType"] = Enums["InventorySystem.Items.Keycards.ChaosKeycardItem.ChaosMsgType"]
ChaosKeycardItem = {}
ChaosKeycardItem.ChaosMsgType = Enums["InventorySystem.Items.Keycards.ChaosKeycardItem.ChaosMsgType"]

---@alias Enum_InventorySystem_002e_Items_002e_Keycards_002e_KeycardItem_002e_MsgType
---| "Custom"
---| "Inspect"
---| "NewPlayerFullResync"
---| "OnKeycardUsed"
Enums["InventorySystem.Items.Keycards.KeycardItem.MsgType"] = {
    ["Custom"] = "Custom",
    ["Inspect"] = "Inspect",
    ["NewPlayerFullResync"] = "NewPlayerFullResync",
    ["OnKeycardUsed"] = "OnKeycardUsed",
}
Enums.InventorySystem.Items.Keycards.KeycardItem = {}
Enums.InventorySystem.Items.Keycards.KeycardItem.MsgType = Enums["InventorySystem.Items.Keycards.KeycardItem.MsgType"]
Enums.KeycardItem = {}
Enums.KeycardItem.MsgType = Enums["InventorySystem.Items.Keycards.KeycardItem.MsgType"]
Enums["KeycardItem.MsgType"] = Enums["InventorySystem.Items.Keycards.KeycardItem.MsgType"]
KeycardItem = {}
KeycardItem.MsgType = Enums["InventorySystem.Items.Keycards.KeycardItem.MsgType"]

---@alias Enum_InventorySystem_002e_Items_002e_Keycards_002e_TranslatedLabelDetail_002e_KeycardLabelTranslation
---| "ContEngineer"
---| "FacilityManager"
---| "Janitor"
---| "ResearchSupervisor"
---| "Scientist"
---| "SecurityGuard"
---| "SurfaceAccessPassNormal"
---| "SurfaceAccessPassUsed"
---| "ZoneManager"
Enums["InventorySystem.Items.Keycards.TranslatedLabelDetail.KeycardLabelTranslation"] = {
    ["ContEngineer"] = "ContEngineer",
    ["FacilityManager"] = "FacilityManager",
    ["Janitor"] = "Janitor",
    ["ResearchSupervisor"] = "ResearchSupervisor",
    ["Scientist"] = "Scientist",
    ["SecurityGuard"] = "SecurityGuard",
    ["SurfaceAccessPassNormal"] = "SurfaceAccessPassNormal",
    ["SurfaceAccessPassUsed"] = "SurfaceAccessPassUsed",
    ["ZoneManager"] = "ZoneManager",
}
Enums.InventorySystem.Items.Keycards.TranslatedLabelDetail = {}
Enums.InventorySystem.Items.Keycards.TranslatedLabelDetail.KeycardLabelTranslation = Enums["InventorySystem.Items.Keycards.TranslatedLabelDetail.KeycardLabelTranslation"]
Enums.TranslatedLabelDetail = {}
Enums.TranslatedLabelDetail.KeycardLabelTranslation = Enums["InventorySystem.Items.Keycards.TranslatedLabelDetail.KeycardLabelTranslation"]
Enums["TranslatedLabelDetail.KeycardLabelTranslation"] = Enums["InventorySystem.Items.Keycards.TranslatedLabelDetail.KeycardLabelTranslation"]
TranslatedLabelDetail = {}
TranslatedLabelDetail.KeycardLabelTranslation = Enums["InventorySystem.Items.Keycards.TranslatedLabelDetail.KeycardLabelTranslation"]

---@alias Enum_InventorySystem_002e_Items_002e_MarshmallowMan_002e_MarshmallowItem_002e_RpcType
---| "AttackStart"
---| "Cackle"
---| "Evil"
---| "Hit"
---| "Holster"
Enums["InventorySystem.Items.MarshmallowMan.MarshmallowItem.RpcType"] = {
    ["AttackStart"] = "AttackStart",
    ["Cackle"] = "Cackle",
    ["Evil"] = "Evil",
    ["Hit"] = "Hit",
    ["Holster"] = "Holster",
}
Enums.InventorySystem.Items.MarshmallowMan = {}
Enums.InventorySystem.Items.MarshmallowMan.MarshmallowItem = {}
Enums.InventorySystem.Items.MarshmallowMan.MarshmallowItem.RpcType = Enums["InventorySystem.Items.MarshmallowMan.MarshmallowItem.RpcType"]
Enums.MarshmallowItem = {}
Enums.MarshmallowItem.RpcType = Enums["InventorySystem.Items.MarshmallowMan.MarshmallowItem.RpcType"]
Enums["MarshmallowItem.RpcType"] = Enums["InventorySystem.Items.MarshmallowMan.MarshmallowItem.RpcType"]
MarshmallowItem = {}
MarshmallowItem.RpcType = Enums["InventorySystem.Items.MarshmallowMan.MarshmallowItem.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_MicroHID_002e_MicroHIDMonitor_002e_LcdElementType
---| "Broken"
---| "Firing"
---| "JeffConcerned"
---| "JeffHappy"
---| "JeffSad"
---| "ReadyToFire"
---| "Standby"
---| "Winddown"
---| "Windup"
Enums["InventorySystem.Items.MicroHID.MicroHIDMonitor.LcdElementType"] = {
    ["Broken"] = "Broken",
    ["Firing"] = "Firing",
    ["JeffConcerned"] = "JeffConcerned",
    ["JeffHappy"] = "JeffHappy",
    ["JeffSad"] = "JeffSad",
    ["ReadyToFire"] = "ReadyToFire",
    ["Standby"] = "Standby",
    ["Winddown"] = "Winddown",
    ["Windup"] = "Windup",
}
Enums.InventorySystem.Items.MicroHID = {}
Enums.InventorySystem.Items.MicroHID.MicroHIDMonitor = {}
Enums.InventorySystem.Items.MicroHID.MicroHIDMonitor.LcdElementType = Enums["InventorySystem.Items.MicroHID.MicroHIDMonitor.LcdElementType"]
Enums.MicroHIDMonitor = {}
Enums.MicroHIDMonitor.LcdElementType = Enums["InventorySystem.Items.MicroHID.MicroHIDMonitor.LcdElementType"]
Enums["MicroHIDMonitor.LcdElementType"] = Enums["InventorySystem.Items.MicroHID.MicroHIDMonitor.LcdElementType"]
MicroHIDMonitor = {}
MicroHIDMonitor.LcdElementType = Enums["InventorySystem.Items.MicroHID.MicroHIDMonitor.LcdElementType"]

---@alias Enum_InventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_BrokenSyncModule_002e_RpcType
---| "BreakSpecific"
---| "ResyncAll"
---| "UnbreakSpecific"
Enums["InventorySystem.Items.MicroHID.Modules.BrokenSyncModule.RpcType"] = {
    ["BreakSpecific"] = "BreakSpecific",
    ["ResyncAll"] = "ResyncAll",
    ["UnbreakSpecific"] = "UnbreakSpecific",
}
Enums.InventorySystem.Items.MicroHID.Modules = {}
Enums.InventorySystem.Items.MicroHID.Modules.BrokenSyncModule = {}
Enums.InventorySystem.Items.MicroHID.Modules.BrokenSyncModule.RpcType = Enums["InventorySystem.Items.MicroHID.Modules.BrokenSyncModule.RpcType"]
Enums.BrokenSyncModule = {}
Enums.BrokenSyncModule.RpcType = Enums["InventorySystem.Items.MicroHID.Modules.BrokenSyncModule.RpcType"]
Enums["BrokenSyncModule.RpcType"] = Enums["InventorySystem.Items.MicroHID.Modules.BrokenSyncModule.RpcType"]
BrokenSyncModule = {}
BrokenSyncModule.RpcType = Enums["InventorySystem.Items.MicroHID.Modules.BrokenSyncModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_DrawAndInspectorModule_002e_RpcType
---| "AddPickup"
---| "InspectRequested"
---| "OnEquipped"
---| "OnHolstered"
Enums["InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule.RpcType"] = {
    ["AddPickup"] = "AddPickup",
    ["InspectRequested"] = "InspectRequested",
    ["OnEquipped"] = "OnEquipped",
    ["OnHolstered"] = "OnHolstered",
}
Enums.InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule = {}
Enums.InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule.RpcType = Enums["InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule.RpcType"]
Enums.DrawAndInspectorModule = {}
Enums.DrawAndInspectorModule.RpcType = Enums["InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule.RpcType"]
Enums["DrawAndInspectorModule.RpcType"] = Enums["InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule.RpcType"]
DrawAndInspectorModule = {}
DrawAndInspectorModule.RpcType = Enums["InventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule.RpcType"]

---@alias Enum_InventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_InputSyncModule_002e_SyncData
---| "None"
---| "Primary"
---| "Secondary"
Enums["InventorySystem.Items.MicroHID.Modules.InputSyncModule.SyncData"] = {
    ["None"] = "None",
    ["Primary"] = "Primary",
    ["Secondary"] = "Secondary",
}
Enums.InventorySystem.Items.MicroHID.Modules.InputSyncModule = {}
Enums.InventorySystem.Items.MicroHID.Modules.InputSyncModule.SyncData = Enums["InventorySystem.Items.MicroHID.Modules.InputSyncModule.SyncData"]
Enums.InputSyncModule = {}
Enums.InputSyncModule.SyncData = Enums["InventorySystem.Items.MicroHID.Modules.InputSyncModule.SyncData"]
Enums["InputSyncModule.SyncData"] = Enums["InventorySystem.Items.MicroHID.Modules.InputSyncModule.SyncData"]
InputSyncModule = {}
InputSyncModule.SyncData = Enums["InventorySystem.Items.MicroHID.Modules.InputSyncModule.SyncData"]

---@alias Enum_InventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_MicroHidFiringMode
---| "BrokenFire"
---| "ChargeFire"
---| "PrimaryFire"
Enums["InventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"] = {
    ["BrokenFire"] = "BrokenFire",
    ["ChargeFire"] = "ChargeFire",
    ["PrimaryFire"] = "PrimaryFire",
}
Enums.InventorySystem.Items.MicroHID.Modules.MicroHidFiringMode = Enums["InventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"]
Enums.MicroHidFiringMode = Enums["InventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"]
MicroHidFiringMode = Enums["InventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"]

---@alias Enum_InventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_MicroHidPhase
---| "Firing"
---| "Standby"
---| "WindingDown"
---| "WindingUp"
---| "WoundUpSustain"
Enums["InventorySystem.Items.MicroHID.Modules.MicroHidPhase"] = {
    ["Firing"] = "Firing",
    ["Standby"] = "Standby",
    ["WindingDown"] = "WindingDown",
    ["WindingUp"] = "WindingUp",
    ["WoundUpSustain"] = "WoundUpSustain",
}
Enums.InventorySystem.Items.MicroHID.Modules.MicroHidPhase = Enums["InventorySystem.Items.MicroHID.Modules.MicroHidPhase"]
Enums.MicroHidPhase = Enums["InventorySystem.Items.MicroHID.Modules.MicroHidPhase"]
MicroHidPhase = Enums["InventorySystem.Items.MicroHID.Modules.MicroHidPhase"]

---@alias Enum_InventorySystem_002e_Items_002e_Pickups_002e_PickupStandardPhysics_002e_FreezingMode
---| "Default"
---| "FreezeWhenSleeping"
---| "NeverFreeze"
Enums["InventorySystem.Items.Pickups.PickupStandardPhysics.FreezingMode"] = {
    ["Default"] = "Default",
    ["FreezeWhenSleeping"] = "FreezeWhenSleeping",
    ["NeverFreeze"] = "NeverFreeze",
}
Enums.InventorySystem.Items.Pickups = {}
Enums.InventorySystem.Items.Pickups.PickupStandardPhysics = {}
Enums.InventorySystem.Items.Pickups.PickupStandardPhysics.FreezingMode = Enums["InventorySystem.Items.Pickups.PickupStandardPhysics.FreezingMode"]
Enums.PickupStandardPhysics = {}
Enums.PickupStandardPhysics.FreezingMode = Enums["InventorySystem.Items.Pickups.PickupStandardPhysics.FreezingMode"]
Enums["PickupStandardPhysics.FreezingMode"] = Enums["InventorySystem.Items.Pickups.PickupStandardPhysics.FreezingMode"]
PickupStandardPhysics = {}
PickupStandardPhysics.FreezingMode = Enums["InventorySystem.Items.Pickups.PickupStandardPhysics.FreezingMode"]

---@alias Enum_InventorySystem_002e_Items_002e_Pickups_002e_PickupSyncInfo_002e_PickupFlags
---| "InUse"
---| "Locked"
Enums["InventorySystem.Items.Pickups.PickupSyncInfo.PickupFlags"] = {
    ["InUse"] = "InUse",
    ["Locked"] = "Locked",
}
Enums.InventorySystem.Items.Pickups.PickupSyncInfo = {}
Enums.InventorySystem.Items.Pickups.PickupSyncInfo.PickupFlags = Enums["InventorySystem.Items.Pickups.PickupSyncInfo.PickupFlags"]
Enums.PickupSyncInfo = {}
Enums.PickupSyncInfo.PickupFlags = Enums["InventorySystem.Items.Pickups.PickupSyncInfo.PickupFlags"]
Enums["PickupSyncInfo.PickupFlags"] = Enums["InventorySystem.Items.Pickups.PickupSyncInfo.PickupFlags"]
PickupSyncInfo = {}
PickupSyncInfo.PickupFlags = Enums["InventorySystem.Items.Pickups.PickupSyncInfo.PickupFlags"]

---@alias Enum_InventorySystem_002e_Items_002e_Radio_002e_RadioMessages_002e_RadioCommand
---| "ChangeRange"
---| "Disable"
---| "Enable"
Enums["InventorySystem.Items.Radio.RadioMessages.RadioCommand"] = {
    ["ChangeRange"] = "ChangeRange",
    ["Disable"] = "Disable",
    ["Enable"] = "Enable",
}
Enums.InventorySystem.Items.Radio = {}
Enums.InventorySystem.Items.Radio.RadioMessages = {}
Enums.InventorySystem.Items.Radio.RadioMessages.RadioCommand = Enums["InventorySystem.Items.Radio.RadioMessages.RadioCommand"]
Enums.RadioMessages = {}
Enums.RadioMessages.RadioCommand = Enums["InventorySystem.Items.Radio.RadioMessages.RadioCommand"]
Enums["RadioMessages.RadioCommand"] = Enums["InventorySystem.Items.Radio.RadioMessages.RadioCommand"]
RadioMessages = {}
RadioMessages.RadioCommand = Enums["InventorySystem.Items.Radio.RadioMessages.RadioCommand"]

---@alias Enum_InventorySystem_002e_Items_002e_Radio_002e_RadioMessages_002e_RadioRangeLevel
---| "HighRange"
---| "LowRange"
---| "MediumRange"
---| "RadioDisabled"
---| "UltraRange"
Enums["InventorySystem.Items.Radio.RadioMessages.RadioRangeLevel"] = {
    ["HighRange"] = "HighRange",
    ["LowRange"] = "LowRange",
    ["MediumRange"] = "MediumRange",
    ["RadioDisabled"] = "RadioDisabled",
    ["UltraRange"] = "UltraRange",
}
Enums.InventorySystem.Items.Radio.RadioMessages.RadioRangeLevel = Enums["InventorySystem.Items.Radio.RadioMessages.RadioRangeLevel"]
Enums.RadioMessages.RadioRangeLevel = Enums["InventorySystem.Items.Radio.RadioMessages.RadioRangeLevel"]
Enums["RadioMessages.RadioRangeLevel"] = Enums["InventorySystem.Items.Radio.RadioMessages.RadioRangeLevel"]
RadioMessages.RadioRangeLevel = Enums["InventorySystem.Items.Radio.RadioMessages.RadioRangeLevel"]

---@alias Enum_InventorySystem_002e_Items_002e_Scp1509_002e_Scp1509MessageType
---| "AttackPerformed"
---| "AttackPreformedHitFlesh"
---| "AttackPreformedHitMissed"
---| "AttackPreformedHitWall"
---| "AttackTriggered"
---| "CanNotResurrect"
---| "CanResurrect"
---| "Holstered"
---| "Inspect"
---| "KilledPlayer"
---| "NewPlayerFullResync"
---| "SpawnResurrectParticles"
Enums["InventorySystem.Items.Scp1509.Scp1509MessageType"] = {
    ["AttackPerformed"] = "AttackPerformed",
    ["AttackPreformedHitFlesh"] = "AttackPreformedHitFlesh",
    ["AttackPreformedHitMissed"] = "AttackPreformedHitMissed",
    ["AttackPreformedHitWall"] = "AttackPreformedHitWall",
    ["AttackTriggered"] = "AttackTriggered",
    ["CanNotResurrect"] = "CanNotResurrect",
    ["CanResurrect"] = "CanResurrect",
    ["Holstered"] = "Holstered",
    ["Inspect"] = "Inspect",
    ["KilledPlayer"] = "KilledPlayer",
    ["NewPlayerFullResync"] = "NewPlayerFullResync",
    ["SpawnResurrectParticles"] = "SpawnResurrectParticles",
}
Enums.InventorySystem.Items.Scp1509 = {}
Enums.InventorySystem.Items.Scp1509.Scp1509MessageType = Enums["InventorySystem.Items.Scp1509.Scp1509MessageType"]
Enums.Scp1509MessageType = Enums["InventorySystem.Items.Scp1509.Scp1509MessageType"]
Scp1509MessageType = Enums["InventorySystem.Items.Scp1509.Scp1509MessageType"]

---@alias Enum_InventorySystem_002e_Items_002e_Thirdperson_002e_AnimItemLayer3p
---| "Additive"
---| "Left"
---| "Middle"
---| "PreMovement"
---| "Right"
Enums["InventorySystem.Items.Thirdperson.AnimItemLayer3p"] = {
    ["Additive"] = "Additive",
    ["Left"] = "Left",
    ["Middle"] = "Middle",
    ["PreMovement"] = "PreMovement",
    ["Right"] = "Right",
}
Enums.InventorySystem.Items.Thirdperson = {}
Enums.InventorySystem.Items.Thirdperson.AnimItemLayer3p = Enums["InventorySystem.Items.Thirdperson.AnimItemLayer3p"]
Enums.AnimItemLayer3p = Enums["InventorySystem.Items.Thirdperson.AnimItemLayer3p"]
AnimItemLayer3p = Enums["InventorySystem.Items.Thirdperson.AnimItemLayer3p"]

---@alias Enum_InventorySystem_002e_Items_002e_Thirdperson_002e_AnimState3p
---| "Additive0"
---| "Additive1"
---| "Additive2"
---| "Override0"
---| "Override1"
---| "Override2"
---| "RunForward"
---| "SprintForward"
Enums["InventorySystem.Items.Thirdperson.AnimState3p"] = {
    ["Additive0"] = "Additive0",
    ["Additive1"] = "Additive1",
    ["Additive2"] = "Additive2",
    ["Override0"] = "Override0",
    ["Override1"] = "Override1",
    ["Override2"] = "Override2",
    ["RunForward"] = "RunForward",
    ["SprintForward"] = "SprintForward",
}
Enums.InventorySystem.Items.Thirdperson.AnimState3p = Enums["InventorySystem.Items.Thirdperson.AnimState3p"]
Enums.AnimState3p = Enums["InventorySystem.Items.Thirdperson.AnimState3p"]
AnimState3p = Enums["InventorySystem.Items.Thirdperson.AnimState3p"]

---@alias Enum_InventorySystem_002e_Items_002e_ThrowableProjectiles_002e_FlybyDetectorProjectile_002e_FriendlyFireInteraction
---| "CollideWithEveryone"
---| "CollideWithOnlyEnemies"
---| "UseFriendlyFireConfig"
Enums["InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile.FriendlyFireInteraction"] = {
    ["CollideWithEveryone"] = "CollideWithEveryone",
    ["CollideWithOnlyEnemies"] = "CollideWithOnlyEnemies",
    ["UseFriendlyFireConfig"] = "UseFriendlyFireConfig",
}
Enums.InventorySystem.Items.ThrowableProjectiles = {}
Enums.InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile = {}
Enums.InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile.FriendlyFireInteraction = Enums["InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile.FriendlyFireInteraction"]
Enums.FlybyDetectorProjectile = {}
Enums.FlybyDetectorProjectile.FriendlyFireInteraction = Enums["InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile.FriendlyFireInteraction"]
Enums["FlybyDetectorProjectile.FriendlyFireInteraction"] = Enums["InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile.FriendlyFireInteraction"]
FlybyDetectorProjectile = {}
FlybyDetectorProjectile.FriendlyFireInteraction = Enums["InventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile.FriendlyFireInteraction"]

---@alias Enum_InventorySystem_002e_Items_002e_ThrowableProjectiles_002e_ThrowableNetworkHandler_002e_RequestType
---| "BeginThrow"
---| "CancelThrow"
---| "ConfirmThrowFullForce"
---| "ConfirmThrowWeak"
Enums["InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler.RequestType"] = {
    ["BeginThrow"] = "BeginThrow",
    ["CancelThrow"] = "CancelThrow",
    ["ConfirmThrowFullForce"] = "ConfirmThrowFullForce",
    ["ConfirmThrowWeak"] = "ConfirmThrowWeak",
}
Enums.InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler = {}
Enums.InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler.RequestType = Enums["InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler.RequestType"]
Enums.ThrowableNetworkHandler = {}
Enums.ThrowableNetworkHandler.RequestType = Enums["InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler.RequestType"]
Enums["ThrowableNetworkHandler.RequestType"] = Enums["InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler.RequestType"]
ThrowableNetworkHandler = {}
ThrowableNetworkHandler.RequestType = Enums["InventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler.RequestType"]

---@alias Enum_InventorySystem_002e_Items_002e_Usables_002e_Scp1344_002e_Scp1344Status
---| "Activating"
---| "Active"
---| "CancelingDeactivation"
---| "Deactivating"
---| "Dropping"
---| "Equipping"
---| "Idle"
---| "Inspecting"
---| "Stabbing"
Enums["InventorySystem.Items.Usables.Scp1344.Scp1344Status"] = {
    ["Activating"] = "Activating",
    ["Active"] = "Active",
    ["CancelingDeactivation"] = "CancelingDeactivation",
    ["Deactivating"] = "Deactivating",
    ["Dropping"] = "Dropping",
    ["Equipping"] = "Equipping",
    ["Idle"] = "Idle",
    ["Inspecting"] = "Inspecting",
    ["Stabbing"] = "Stabbing",
}
Enums.InventorySystem.Items.Usables = {}
Enums.InventorySystem.Items.Usables.Scp1344 = {}
Enums.InventorySystem.Items.Usables.Scp1344.Scp1344Status = Enums["InventorySystem.Items.Usables.Scp1344.Scp1344Status"]
Enums.Scp1344Status = Enums["InventorySystem.Items.Usables.Scp1344.Scp1344Status"]
Scp1344Status = Enums["InventorySystem.Items.Usables.Scp1344.Scp1344Status"]

---@alias Enum_InventorySystem_002e_Items_002e_Usables_002e_Scp244_002e_Scp244State
---| "Active"
---| "Destroyed"
---| "Idle"
---| "PickedUp"
Enums["InventorySystem.Items.Usables.Scp244.Scp244State"] = {
    ["Active"] = "Active",
    ["Destroyed"] = "Destroyed",
    ["Idle"] = "Idle",
    ["PickedUp"] = "PickedUp",
}
Enums.InventorySystem.Items.Usables.Scp244 = {}
Enums.InventorySystem.Items.Usables.Scp244.Scp244State = Enums["InventorySystem.Items.Usables.Scp244.Scp244State"]
Enums.Scp244State = Enums["InventorySystem.Items.Usables.Scp244.Scp244State"]
Scp244State = Enums["InventorySystem.Items.Usables.Scp244.Scp244State"]

---@alias Enum_InventorySystem_002e_Items_002e_Usables_002e_Scp330_002e_CandyKindID
---| "Black"
---| "Blue"
---| "Brown"
---| "Evil"
---| "Gray"
---| "Green"
---| "None"
---| "Orange"
---| "Pink"
---| "Purple"
---| "Rainbow"
---| "Red"
---| "White"
---| "Yellow"
Enums["InventorySystem.Items.Usables.Scp330.CandyKindID"] = {
    ["Black"] = "Black",
    ["Blue"] = "Blue",
    ["Brown"] = "Brown",
    ["Evil"] = "Evil",
    ["Gray"] = "Gray",
    ["Green"] = "Green",
    ["None"] = "None",
    ["Orange"] = "Orange",
    ["Pink"] = "Pink",
    ["Purple"] = "Purple",
    ["Rainbow"] = "Rainbow",
    ["Red"] = "Red",
    ["White"] = "White",
    ["Yellow"] = "Yellow",
}
Enums.InventorySystem.Items.Usables.Scp330 = {}
Enums.InventorySystem.Items.Usables.Scp330.CandyKindID = Enums["InventorySystem.Items.Usables.Scp330.CandyKindID"]
Enums.CandyKindID = Enums["InventorySystem.Items.Usables.Scp330.CandyKindID"]
CandyKindID = Enums["InventorySystem.Items.Usables.Scp330.CandyKindID"]

---@alias Enum_InventorySystem_002e_Items_002e_Usables_002e_Scp330_002e_Scp330Translations_002e_Entry
---| "Candies"
Enums["InventorySystem.Items.Usables.Scp330.Scp330Translations.Entry"] = {
    ["Candies"] = "Candies",
}
Enums.InventorySystem.Items.Usables.Scp330.Scp330Translations = {}
Enums.InventorySystem.Items.Usables.Scp330.Scp330Translations.Entry = Enums["InventorySystem.Items.Usables.Scp330.Scp330Translations.Entry"]
Enums.Scp330Translations = {}
Enums.Scp330Translations.Entry = Enums["InventorySystem.Items.Usables.Scp330.Scp330Translations.Entry"]
Enums["Scp330Translations.Entry"] = Enums["InventorySystem.Items.Usables.Scp330.Scp330Translations.Entry"]
Scp330Translations = {}
Scp330Translations.Entry = Enums["InventorySystem.Items.Usables.Scp330.Scp330Translations.Entry"]

---@alias Enum_InventorySystem_002e_Items_002e_Usables_002e_StatusMessage_002e_StatusType
---| "Cancel"
---| "Start"
Enums["InventorySystem.Items.Usables.StatusMessage.StatusType"] = {
    ["Cancel"] = "Cancel",
    ["Start"] = "Start",
}
Enums.InventorySystem.Items.Usables.StatusMessage = {}
Enums.InventorySystem.Items.Usables.StatusMessage.StatusType = Enums["InventorySystem.Items.Usables.StatusMessage.StatusType"]
Enums.StatusMessage = {}
Enums.StatusMessage.StatusType = Enums["InventorySystem.Items.Usables.StatusMessage.StatusType"]
Enums["StatusMessage.StatusType"] = Enums["InventorySystem.Items.Usables.StatusMessage.StatusType"]
StatusMessage = {}
StatusMessage.StatusType = Enums["InventorySystem.Items.Usables.StatusMessage.StatusType"]

---@alias Enum_InventorySystem_002e_Items_002e_WearableSlot
---| "Body"
---| "Eyes"
---| "Hat"
Enums["InventorySystem.Items.WearableSlot"] = {
    ["Body"] = "Body",
    ["Eyes"] = "Eyes",
    ["Hat"] = "Hat",
}
Enums.InventorySystem.Items.WearableSlot = Enums["InventorySystem.Items.WearableSlot"]
Enums.WearableSlot = Enums["InventorySystem.Items.WearableSlot"]
WearableSlot = Enums["InventorySystem.Items.WearableSlot"]

---@alias Enum_InventorySystem_002e_Searching_002e_SearchSessionPipe_002e_Activity
---| "Idle"
---| "Promised"
---| "Requested"
Enums["InventorySystem.Searching.SearchSessionPipe.Activity"] = {
    ["Idle"] = "Idle",
    ["Promised"] = "Promised",
    ["Requested"] = "Requested",
}
Enums.InventorySystem.Searching = {}
Enums.InventorySystem.Searching.SearchSessionPipe = {}
Enums.InventorySystem.Searching.SearchSessionPipe.Activity = Enums["InventorySystem.Searching.SearchSessionPipe.Activity"]
Enums.SearchSessionPipe = {}
Enums.SearchSessionPipe.Activity = Enums["InventorySystem.Searching.SearchSessionPipe.Activity"]
Enums["SearchSessionPipe.Activity"] = Enums["InventorySystem.Searching.SearchSessionPipe.Activity"]
SearchSessionPipe = {}
SearchSessionPipe.Activity = Enums["InventorySystem.Searching.SearchSessionPipe.Activity"]

---@alias Enum_LightContainmentZoneDecontamination_002e_DecontaminationController_002e_DecontaminationPhase_002e_PhaseFunction
---| "Final"
---| "GloballyAudible"
---| "None"
---| "OpenCheckpoints"
Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase.PhaseFunction"] = {
    ["Final"] = "Final",
    ["GloballyAudible"] = "GloballyAudible",
    ["None"] = "None",
    ["OpenCheckpoints"] = "OpenCheckpoints",
}
Enums.LightContainmentZoneDecontamination = {}
Enums.LightContainmentZoneDecontamination.DecontaminationController = {}
Enums.LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase = {}
Enums.LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase.PhaseFunction = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase.PhaseFunction"]
Enums.DecontaminationController = {}
Enums.DecontaminationController.DecontaminationPhase = {}
Enums.DecontaminationController.DecontaminationPhase.PhaseFunction = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase.PhaseFunction"]
Enums["DecontaminationController.DecontaminationPhase.PhaseFunction"] = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase.PhaseFunction"]
DecontaminationController = {}
DecontaminationController.DecontaminationPhase = {}
DecontaminationController.DecontaminationPhase.PhaseFunction = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationPhase.PhaseFunction"]

---@alias Enum_LightContainmentZoneDecontamination_002e_DecontaminationController_002e_DecontaminationStatus
---| "Disabled"
---| "Forced"
---| "None"
Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus"] = {
    ["Disabled"] = "Disabled",
    ["Forced"] = "Forced",
    ["None"] = "None",
}
Enums.LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus"]
Enums.DecontaminationController.DecontaminationStatus = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus"]
Enums["DecontaminationController.DecontaminationStatus"] = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus"]
DecontaminationController.DecontaminationStatus = Enums["LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus"]

---@alias Enum_LiteNetLib_002e_ConnectRequestResult
---| "NewConnection"
---| "None"
---| "P2PLose"
---| "Reconnection"
Enums["LiteNetLib.ConnectRequestResult"] = {
    ["NewConnection"] = "NewConnection",
    ["None"] = "None",
    ["P2PLose"] = "P2PLose",
    ["Reconnection"] = "Reconnection",
}
Enums.LiteNetLib = {}
Enums.LiteNetLib.ConnectRequestResult = Enums["LiteNetLib.ConnectRequestResult"]
Enums.ConnectRequestResult = Enums["LiteNetLib.ConnectRequestResult"]
ConnectRequestResult = Enums["LiteNetLib.ConnectRequestResult"]

---@alias Enum_LiteNetLib_002e_ConnectionRequestResult
---| "Accept"
---| "None"
---| "Reject"
---| "RejectForce"
Enums["LiteNetLib.ConnectionRequestResult"] = {
    ["Accept"] = "Accept",
    ["None"] = "None",
    ["Reject"] = "Reject",
    ["RejectForce"] = "RejectForce",
}
Enums.LiteNetLib.ConnectionRequestResult = Enums["LiteNetLib.ConnectionRequestResult"]
Enums.ConnectionRequestResult = Enums["LiteNetLib.ConnectionRequestResult"]
ConnectionRequestResult = Enums["LiteNetLib.ConnectionRequestResult"]

---@alias Enum_LiteNetLib_002e_ConnectionState
---| "Any"
---| "Connected"
---| "Disconnected"
---| "EndPointChange"
---| "Outgoing"
---| "ShutdownRequested"
Enums["LiteNetLib.ConnectionState"] = {
    ["Any"] = "Any",
    ["Connected"] = "Connected",
    ["Disconnected"] = "Disconnected",
    ["EndPointChange"] = "EndPointChange",
    ["Outgoing"] = "Outgoing",
    ["ShutdownRequested"] = "ShutdownRequested",
}
Enums.LiteNetLib.ConnectionState = Enums["LiteNetLib.ConnectionState"]
Enums.ConnectionState = Enums["LiteNetLib.ConnectionState"]
ConnectionState = Enums["LiteNetLib.ConnectionState"]

---@alias Enum_LiteNetLib_002e_DeliveryMethod
---| "ReliableOrdered"
---| "ReliableSequenced"
---| "ReliableUnordered"
---| "Sequenced"
---| "Unreliable"
Enums["LiteNetLib.DeliveryMethod"] = {
    ["ReliableOrdered"] = "ReliableOrdered",
    ["ReliableSequenced"] = "ReliableSequenced",
    ["ReliableUnordered"] = "ReliableUnordered",
    ["Sequenced"] = "Sequenced",
    ["Unreliable"] = "Unreliable",
}
Enums.LiteNetLib.DeliveryMethod = Enums["LiteNetLib.DeliveryMethod"]
Enums.DeliveryMethod = Enums["LiteNetLib.DeliveryMethod"]
DeliveryMethod = Enums["LiteNetLib.DeliveryMethod"]

---@alias Enum_LiteNetLib_002e_DisconnectReason
---| "ConnectionFailed"
---| "ConnectionRejected"
---| "DisconnectPeerCalled"
---| "HostUnreachable"
---| "InvalidProtocol"
---| "NetworkUnreachable"
---| "PeerNotFound"
---| "PeerToPeerConnection"
---| "Reconnect"
---| "RemoteConnectionClose"
---| "Timeout"
---| "UnknownHost"
Enums["LiteNetLib.DisconnectReason"] = {
    ["ConnectionFailed"] = "ConnectionFailed",
    ["ConnectionRejected"] = "ConnectionRejected",
    ["DisconnectPeerCalled"] = "DisconnectPeerCalled",
    ["HostUnreachable"] = "HostUnreachable",
    ["InvalidProtocol"] = "InvalidProtocol",
    ["NetworkUnreachable"] = "NetworkUnreachable",
    ["PeerNotFound"] = "PeerNotFound",
    ["PeerToPeerConnection"] = "PeerToPeerConnection",
    ["Reconnect"] = "Reconnect",
    ["RemoteConnectionClose"] = "RemoteConnectionClose",
    ["Timeout"] = "Timeout",
    ["UnknownHost"] = "UnknownHost",
}
Enums.LiteNetLib.DisconnectReason = Enums["LiteNetLib.DisconnectReason"]
Enums.DisconnectReason = Enums["LiteNetLib.DisconnectReason"]
DisconnectReason = Enums["LiteNetLib.DisconnectReason"]

---@alias Enum_LiteNetLib_002e_DisconnectResult
---| "Disconnect"
---| "None"
---| "Reject"
Enums["LiteNetLib.DisconnectResult"] = {
    ["Disconnect"] = "Disconnect",
    ["None"] = "None",
    ["Reject"] = "Reject",
}
Enums.LiteNetLib.DisconnectResult = Enums["LiteNetLib.DisconnectResult"]
Enums.DisconnectResult = Enums["LiteNetLib.DisconnectResult"]
DisconnectResult = Enums["LiteNetLib.DisconnectResult"]

---@alias Enum_LiteNetLib_002e_LocalAddrType
---| "All"
---| "IPv4"
---| "IPv6"
Enums["LiteNetLib.LocalAddrType"] = {
    ["All"] = "All",
    ["IPv4"] = "IPv4",
    ["IPv6"] = "IPv6",
}
Enums.LiteNetLib.LocalAddrType = Enums["LiteNetLib.LocalAddrType"]
Enums.LocalAddrType = Enums["LiteNetLib.LocalAddrType"]
LocalAddrType = Enums["LiteNetLib.LocalAddrType"]

---@alias Enum_LiteNetLib_002e_NatAddressType
---| "External"
---| "Internal"
Enums["LiteNetLib.NatAddressType"] = {
    ["External"] = "External",
    ["Internal"] = "Internal",
}
Enums.LiteNetLib.NatAddressType = Enums["LiteNetLib.NatAddressType"]
Enums.NatAddressType = Enums["LiteNetLib.NatAddressType"]
NatAddressType = Enums["LiteNetLib.NatAddressType"]

---@alias Enum_LiteNetLib_002e_NetEvent_002e_EType
---| "Broadcast"
---| "Connect"
---| "ConnectionLatencyUpdated"
---| "ConnectionRequest"
---| "Disconnect"
---| "Error"
---| "MessageDelivered"
---| "PeerAddressChanged"
---| "Receive"
---| "ReceiveUnconnected"
Enums["LiteNetLib.NetEvent.EType"] = {
    ["Broadcast"] = "Broadcast",
    ["Connect"] = "Connect",
    ["ConnectionLatencyUpdated"] = "ConnectionLatencyUpdated",
    ["ConnectionRequest"] = "ConnectionRequest",
    ["Disconnect"] = "Disconnect",
    ["Error"] = "Error",
    ["MessageDelivered"] = "MessageDelivered",
    ["PeerAddressChanged"] = "PeerAddressChanged",
    ["Receive"] = "Receive",
    ["ReceiveUnconnected"] = "ReceiveUnconnected",
}
Enums.LiteNetLib.NetEvent = {}
Enums.LiteNetLib.NetEvent.EType = Enums["LiteNetLib.NetEvent.EType"]
Enums.NetEvent = {}
Enums.NetEvent.EType = Enums["LiteNetLib.NetEvent.EType"]
Enums["NetEvent.EType"] = Enums["LiteNetLib.NetEvent.EType"]
NetEvent = {}
NetEvent.EType = Enums["LiteNetLib.NetEvent.EType"]

---@alias Enum_LiteNetLib_002e_NetLogLevel
---| "Error"
---| "Info"
---| "Trace"
---| "Warning"
Enums["LiteNetLib.NetLogLevel"] = {
    ["Error"] = "Error",
    ["Info"] = "Info",
    ["Trace"] = "Trace",
    ["Warning"] = "Warning",
}
Enums.LiteNetLib.NetLogLevel = Enums["LiteNetLib.NetLogLevel"]
Enums.NetLogLevel = Enums["LiteNetLib.NetLogLevel"]
NetLogLevel = Enums["LiteNetLib.NetLogLevel"]

---@alias Enum_LiteNetLib_002e_PacketProperty
---| "Ack"
---| "Broadcast"
---| "Channeled"
---| "ConnectAccept"
---| "ConnectRequest"
---| "Disconnect"
---| "Empty"
---| "InvalidProtocol"
---| "Merged"
---| "MtuCheck"
---| "MtuOk"
---| "NatMessage"
---| "PeerNotFound"
---| "Ping"
---| "Pong"
---| "ReliableMerged"
---| "ShutdownOk"
---| "Total"
---| "UnconnectedMessage"
---| "Unreliable"
Enums["LiteNetLib.PacketProperty"] = {
    ["Ack"] = "Ack",
    ["Broadcast"] = "Broadcast",
    ["Channeled"] = "Channeled",
    ["ConnectAccept"] = "ConnectAccept",
    ["ConnectRequest"] = "ConnectRequest",
    ["Disconnect"] = "Disconnect",
    ["Empty"] = "Empty",
    ["InvalidProtocol"] = "InvalidProtocol",
    ["Merged"] = "Merged",
    ["MtuCheck"] = "MtuCheck",
    ["MtuOk"] = "MtuOk",
    ["NatMessage"] = "NatMessage",
    ["PeerNotFound"] = "PeerNotFound",
    ["Ping"] = "Ping",
    ["Pong"] = "Pong",
    ["ReliableMerged"] = "ReliableMerged",
    ["ShutdownOk"] = "ShutdownOk",
    ["Total"] = "Total",
    ["UnconnectedMessage"] = "UnconnectedMessage",
    ["Unreliable"] = "Unreliable",
}
Enums.LiteNetLib.PacketProperty = Enums["LiteNetLib.PacketProperty"]
Enums.PacketProperty = Enums["LiteNetLib.PacketProperty"]
PacketProperty = Enums["LiteNetLib.PacketProperty"]

---@alias Enum_LiteNetLib_002e_ShutdownResult
---| "None"
---| "Success"
---| "WasConnected"
Enums["LiteNetLib.ShutdownResult"] = {
    ["None"] = "None",
    ["Success"] = "Success",
    ["WasConnected"] = "WasConnected",
}
Enums.LiteNetLib.ShutdownResult = Enums["LiteNetLib.ShutdownResult"]
Enums.ShutdownResult = Enums["LiteNetLib.ShutdownResult"]
ShutdownResult = Enums["LiteNetLib.ShutdownResult"]

---@alias Enum_LiteNetLib_002e_UnconnectedMessageType
---| "BasicMessage"
---| "Broadcast"
Enums["LiteNetLib.UnconnectedMessageType"] = {
    ["BasicMessage"] = "BasicMessage",
    ["Broadcast"] = "Broadcast",
}
Enums.LiteNetLib.UnconnectedMessageType = Enums["LiteNetLib.UnconnectedMessageType"]
Enums.UnconnectedMessageType = Enums["LiteNetLib.UnconnectedMessageType"]
UnconnectedMessageType = Enums["LiteNetLib.UnconnectedMessageType"]

---@alias Enum_LiteNetLib_002e_Utils_002e_NetSerializer_002e_CallType
---| "Array"
---| "Basic"
---| "List"
Enums["LiteNetLib.Utils.NetSerializer.CallType"] = {
    ["Array"] = "Array",
    ["Basic"] = "Basic",
    ["List"] = "List",
}
Enums.LiteNetLib.Utils = {}
Enums.LiteNetLib.Utils.NetSerializer = {}
Enums.LiteNetLib.Utils.NetSerializer.CallType = Enums["LiteNetLib.Utils.NetSerializer.CallType"]
Enums.NetSerializer = {}
Enums.NetSerializer.CallType = Enums["LiteNetLib.Utils.NetSerializer.CallType"]
Enums["NetSerializer.CallType"] = Enums["LiteNetLib.Utils.NetSerializer.CallType"]
NetSerializer = {}
NetSerializer.CallType = Enums["LiteNetLib.Utils.NetSerializer.CallType"]

---@alias Enum_LiteNetLib_002e_Utils_002e_NtpLeapIndicator
---| "AlarmCondition"
---| "LastMinuteHas59Seconds"
---| "LastMinuteHas61Seconds"
---| "NoWarning"
Enums["LiteNetLib.Utils.NtpLeapIndicator"] = {
    ["AlarmCondition"] = "AlarmCondition",
    ["LastMinuteHas59Seconds"] = "LastMinuteHas59Seconds",
    ["LastMinuteHas61Seconds"] = "LastMinuteHas61Seconds",
    ["NoWarning"] = "NoWarning",
}
Enums.LiteNetLib.Utils.NtpLeapIndicator = Enums["LiteNetLib.Utils.NtpLeapIndicator"]
Enums.NtpLeapIndicator = Enums["LiteNetLib.Utils.NtpLeapIndicator"]
NtpLeapIndicator = Enums["LiteNetLib.Utils.NtpLeapIndicator"]

---@alias Enum_LiteNetLib_002e_Utils_002e_NtpMode
---| "Client"
---| "Server"
Enums["LiteNetLib.Utils.NtpMode"] = {
    ["Client"] = "Client",
    ["Server"] = "Server",
}
Enums.LiteNetLib.Utils.NtpMode = Enums["LiteNetLib.Utils.NtpMode"]
Enums.NtpMode = Enums["LiteNetLib.Utils.NtpMode"]
NtpMode = Enums["LiteNetLib.Utils.NtpMode"]

---@alias Enum_LiteNetLib4Mirror_002e_Open_002e_Nat_002e_MappingLifetime
---| "ForcedSession"
---| "Manual"
---| "Permanent"
---| "Session"
Enums["LiteNetLib4Mirror.Open.Nat.MappingLifetime"] = {
    ["ForcedSession"] = "ForcedSession",
    ["Manual"] = "Manual",
    ["Permanent"] = "Permanent",
    ["Session"] = "Session",
}
Enums.LiteNetLib4Mirror = {}
Enums.LiteNetLib4Mirror.Open = {}
Enums.LiteNetLib4Mirror.Open.Nat = {}
Enums.LiteNetLib4Mirror.Open.Nat.MappingLifetime = Enums["LiteNetLib4Mirror.Open.Nat.MappingLifetime"]
Enums.MappingLifetime = Enums["LiteNetLib4Mirror.Open.Nat.MappingLifetime"]
MappingLifetime = Enums["LiteNetLib4Mirror.Open.Nat.MappingLifetime"]

---@alias Enum_LiteNetLib4Mirror_002e_Open_002e_Nat_002e_NetworkProtocolType
---| "Tcp"
---| "Udp"
Enums["LiteNetLib4Mirror.Open.Nat.NetworkProtocolType"] = {
    ["Tcp"] = "Tcp",
    ["Udp"] = "Udp",
}
Enums.LiteNetLib4Mirror.Open.Nat.NetworkProtocolType = Enums["LiteNetLib4Mirror.Open.Nat.NetworkProtocolType"]
Enums.NetworkProtocolType = Enums["LiteNetLib4Mirror.Open.Nat.NetworkProtocolType"]
NetworkProtocolType = Enums["LiteNetLib4Mirror.Open.Nat.NetworkProtocolType"]

---@alias Enum_LiteNetLib4Mirror_002e_Open_002e_Nat_002e_PortMapper
---| "Pmp"
---| "Upnp"
Enums["LiteNetLib4Mirror.Open.Nat.PortMapper"] = {
    ["Pmp"] = "Pmp",
    ["Upnp"] = "Upnp",
}
Enums.LiteNetLib4Mirror.Open.Nat.PortMapper = Enums["LiteNetLib4Mirror.Open.Nat.PortMapper"]
Enums.PortMapper = Enums["LiteNetLib4Mirror.Open.Nat.PortMapper"]
PortMapper = Enums["LiteNetLib4Mirror.Open.Nat.PortMapper"]

---@alias Enum_MapGeneration_002e_Distributors_002e_Scp079Generator_002e_GeneratorColliderId
---| "CancelButton"
---| "Door"
---| "Switch"
Enums["MapGeneration.Distributors.Scp079Generator.GeneratorColliderId"] = {
    ["CancelButton"] = "CancelButton",
    ["Door"] = "Door",
    ["Switch"] = "Switch",
}
Enums.MapGeneration = {}
Enums.MapGeneration.Distributors = {}
Enums.MapGeneration.Distributors.Scp079Generator = {}
Enums.MapGeneration.Distributors.Scp079Generator.GeneratorColliderId = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorColliderId"]
Enums.Scp079Generator = {}
Enums.Scp079Generator.GeneratorColliderId = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorColliderId"]
Enums["Scp079Generator.GeneratorColliderId"] = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorColliderId"]
Scp079Generator = {}
Scp079Generator.GeneratorColliderId = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorColliderId"]

---@alias Enum_MapGeneration_002e_Distributors_002e_Scp079Generator_002e_GeneratorFlags
---| "Activating"
---| "Engaged"
---| "None"
---| "Open"
---| "Unlocked"
Enums["MapGeneration.Distributors.Scp079Generator.GeneratorFlags"] = {
    ["Activating"] = "Activating",
    ["Engaged"] = "Engaged",
    ["None"] = "None",
    ["Open"] = "Open",
    ["Unlocked"] = "Unlocked",
}
Enums.MapGeneration.Distributors.Scp079Generator.GeneratorFlags = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorFlags"]
Enums.Scp079Generator.GeneratorFlags = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorFlags"]
Enums["Scp079Generator.GeneratorFlags"] = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorFlags"]
Scp079Generator.GeneratorFlags = Enums["MapGeneration.Distributors.Scp079Generator.GeneratorFlags"]

---@alias Enum_MapGeneration_002e_Distributors_002e_StructureType
---| "ExperimentalWeaponLocker"
---| "LargeGunLocker"
---| "Scp079Generator"
---| "ScpPedestal"
---| "SmallWallCabinet"
---| "StandardLocker"
---| "Workstation"
Enums["MapGeneration.Distributors.StructureType"] = {
    ["ExperimentalWeaponLocker"] = "ExperimentalWeaponLocker",
    ["LargeGunLocker"] = "LargeGunLocker",
    ["Scp079Generator"] = "Scp079Generator",
    ["ScpPedestal"] = "ScpPedestal",
    ["SmallWallCabinet"] = "SmallWallCabinet",
    ["StandardLocker"] = "StandardLocker",
    ["Workstation"] = "Workstation",
}
Enums.MapGeneration.Distributors.StructureType = Enums["MapGeneration.Distributors.StructureType"]
Enums.StructureType = Enums["MapGeneration.Distributors.StructureType"]
StructureType = Enums["MapGeneration.Distributors.StructureType"]

---@alias Enum_MapGeneration_002e_FacilityZone
---| "Entrance"
---| "HeavyContainment"
---| "LightContainment"
---| "None"
---| "Other"
---| "Surface"
Enums["MapGeneration.FacilityZone"] = {
    ["Entrance"] = "Entrance",
    ["HeavyContainment"] = "HeavyContainment",
    ["LightContainment"] = "LightContainment",
    ["None"] = "None",
    ["Other"] = "Other",
    ["Surface"] = "Surface",
}
Enums.MapGeneration.FacilityZone = Enums["MapGeneration.FacilityZone"]
Enums.FacilityZone = Enums["MapGeneration.FacilityZone"]
FacilityZone = Enums["MapGeneration.FacilityZone"]

---@alias Enum_MapGeneration_002e_Holidays_002e_HolidayType
---| "AprilFools"
---| "Christmas"
---| "Halloween"
---| "None"
Enums["MapGeneration.Holidays.HolidayType"] = {
    ["AprilFools"] = "AprilFools",
    ["Christmas"] = "Christmas",
    ["Halloween"] = "Halloween",
    ["None"] = "None",
}
Enums.MapGeneration.Holidays = {}
Enums.MapGeneration.Holidays.HolidayType = Enums["MapGeneration.Holidays.HolidayType"]
Enums.HolidayType = Enums["MapGeneration.Holidays.HolidayType"]
HolidayType = Enums["MapGeneration.Holidays.HolidayType"]

---@alias Enum_MapGeneration_002e_MapGenerationPhase
---| "ComplexDecorationsAndClutter"
---| "CullingCaching"
---| "ParentRoomRegistration"
---| "RelativePositioningWaypoints"
---| "RoomCoordsRegistrations"
---| "SimpleDecorations"
---| "SpawnableStructures"
---| "StaticBatching"
Enums["MapGeneration.MapGenerationPhase"] = {
    ["ComplexDecorationsAndClutter"] = "ComplexDecorationsAndClutter",
    ["CullingCaching"] = "CullingCaching",
    ["ParentRoomRegistration"] = "ParentRoomRegistration",
    ["RelativePositioningWaypoints"] = "RelativePositioningWaypoints",
    ["RoomCoordsRegistrations"] = "RoomCoordsRegistrations",
    ["SimpleDecorations"] = "SimpleDecorations",
    ["SpawnableStructures"] = "SpawnableStructures",
    ["StaticBatching"] = "StaticBatching",
}
Enums.MapGeneration.MapGenerationPhase = Enums["MapGeneration.MapGenerationPhase"]
Enums.MapGenerationPhase = Enums["MapGeneration.MapGenerationPhase"]
MapGenerationPhase = Enums["MapGeneration.MapGenerationPhase"]

---@alias Enum_MapGeneration_002e_RoomConnectors_002e_SpawnableRoomConnectorType
---| "ClutterAngledFences"
---| "ClutterBoxesLadder"
---| "ClutterBrokenElectricalBox"
---| "ClutterHugeOrangePipes"
---| "ClutterPipesLong"
---| "ClutterPipesShort"
---| "ClutterSimpleBoxes"
---| "ClutterTankSupportedShelf"
---| "EzStandardDoor"
---| "HczBulkDoor"
---| "HczStandardDoor"
---| "LczStandardDoor"
---| "None"
---| "OpenHallway"
Enums["MapGeneration.RoomConnectors.SpawnableRoomConnectorType"] = {
    ["ClutterAngledFences"] = "ClutterAngledFences",
    ["ClutterBoxesLadder"] = "ClutterBoxesLadder",
    ["ClutterBrokenElectricalBox"] = "ClutterBrokenElectricalBox",
    ["ClutterHugeOrangePipes"] = "ClutterHugeOrangePipes",
    ["ClutterPipesLong"] = "ClutterPipesLong",
    ["ClutterPipesShort"] = "ClutterPipesShort",
    ["ClutterSimpleBoxes"] = "ClutterSimpleBoxes",
    ["ClutterTankSupportedShelf"] = "ClutterTankSupportedShelf",
    ["EzStandardDoor"] = "EzStandardDoor",
    ["HczBulkDoor"] = "HczBulkDoor",
    ["HczStandardDoor"] = "HczStandardDoor",
    ["LczStandardDoor"] = "LczStandardDoor",
    ["None"] = "None",
    ["OpenHallway"] = "OpenHallway",
}
Enums.MapGeneration.RoomConnectors = {}
Enums.MapGeneration.RoomConnectors.SpawnableRoomConnectorType = Enums["MapGeneration.RoomConnectors.SpawnableRoomConnectorType"]
Enums.SpawnableRoomConnectorType = Enums["MapGeneration.RoomConnectors.SpawnableRoomConnectorType"]
SpawnableRoomConnectorType = Enums["MapGeneration.RoomConnectors.SpawnableRoomConnectorType"]

---@alias Enum_MapGeneration_002e_RoomName
---| "EzCollapsedTunnel"
---| "EzEvacShelter"
---| "EzGateA"
---| "EzGateB"
---| "EzIntercom"
---| "EzOfficeLarge"
---| "EzOfficeSmall"
---| "EzOfficeStoried"
---| "EzRedroom"
---| "Hcz049"
---| "Hcz079"
---| "Hcz096"
---| "Hcz106"
---| "Hcz127"
---| "Hcz939"
---| "HczAcroamaticAbatement"
---| "HczArmory"
---| "HczCheckpointA"
---| "HczCheckpointB"
---| "HczCheckpointToEntranceZone"
---| "HczMicroHID"
---| "HczRampTunnel"
---| "HczServers"
---| "HczTesla"
---| "HczTestroom"
---| "HczWarhead"
---| "HczWaysideIncinerator"
---| "Lcz173"
---| "Lcz330"
---| "Lcz914"
---| "LczAirlock"
---| "LczArmory"
---| "LczCheckpointA"
---| "LczCheckpointB"
---| "LczClassDSpawn"
---| "LczComputerRoom"
---| "LczGlassroom"
---| "LczGreenhouse"
---| "LczToilets"
---| "Outside"
---| "Pocket"
---| "Unnamed"
Enums["MapGeneration.RoomName"] = {
    ["EzCollapsedTunnel"] = "EzCollapsedTunnel",
    ["EzEvacShelter"] = "EzEvacShelter",
    ["EzGateA"] = "EzGateA",
    ["EzGateB"] = "EzGateB",
    ["EzIntercom"] = "EzIntercom",
    ["EzOfficeLarge"] = "EzOfficeLarge",
    ["EzOfficeSmall"] = "EzOfficeSmall",
    ["EzOfficeStoried"] = "EzOfficeStoried",
    ["EzRedroom"] = "EzRedroom",
    ["Hcz049"] = "Hcz049",
    ["Hcz079"] = "Hcz079",
    ["Hcz096"] = "Hcz096",
    ["Hcz106"] = "Hcz106",
    ["Hcz127"] = "Hcz127",
    ["Hcz939"] = "Hcz939",
    ["HczAcroamaticAbatement"] = "HczAcroamaticAbatement",
    ["HczArmory"] = "HczArmory",
    ["HczCheckpointA"] = "HczCheckpointA",
    ["HczCheckpointB"] = "HczCheckpointB",
    ["HczCheckpointToEntranceZone"] = "HczCheckpointToEntranceZone",
    ["HczMicroHID"] = "HczMicroHID",
    ["HczRampTunnel"] = "HczRampTunnel",
    ["HczServers"] = "HczServers",
    ["HczTesla"] = "HczTesla",
    ["HczTestroom"] = "HczTestroom",
    ["HczWarhead"] = "HczWarhead",
    ["HczWaysideIncinerator"] = "HczWaysideIncinerator",
    ["Lcz173"] = "Lcz173",
    ["Lcz330"] = "Lcz330",
    ["Lcz914"] = "Lcz914",
    ["LczAirlock"] = "LczAirlock",
    ["LczArmory"] = "LczArmory",
    ["LczCheckpointA"] = "LczCheckpointA",
    ["LczCheckpointB"] = "LczCheckpointB",
    ["LczClassDSpawn"] = "LczClassDSpawn",
    ["LczComputerRoom"] = "LczComputerRoom",
    ["LczGlassroom"] = "LczGlassroom",
    ["LczGreenhouse"] = "LczGreenhouse",
    ["LczToilets"] = "LczToilets",
    ["Outside"] = "Outside",
    ["Pocket"] = "Pocket",
    ["Unnamed"] = "Unnamed",
}
Enums.MapGeneration.RoomName = Enums["MapGeneration.RoomName"]
Enums.RoomName = Enums["MapGeneration.RoomName"]
RoomName = Enums["MapGeneration.RoomName"]

---@alias Enum_MapGeneration_002e_RoomShape
---| "Curve"
---| "Endroom"
---| "Straight"
---| "TShape"
---| "Undefined"
---| "XShape"
Enums["MapGeneration.RoomShape"] = {
    ["Curve"] = "Curve",
    ["Endroom"] = "Endroom",
    ["Straight"] = "Straight",
    ["TShape"] = "TShape",
    ["Undefined"] = "Undefined",
    ["XShape"] = "XShape",
}
Enums.MapGeneration.RoomShape = Enums["MapGeneration.RoomShape"]
Enums.RoomShape = Enums["MapGeneration.RoomShape"]
RoomShape = Enums["MapGeneration.RoomShape"]

---@alias Enum_MapGeneration_002e_Rooms_002e_RoomLevelName
---| "Hcz049Sublevel"
---| "Main"
---| "NukeSilo"
Enums["MapGeneration.Rooms.RoomLevelName"] = {
    ["Hcz049Sublevel"] = "Hcz049Sublevel",
    ["Main"] = "Main",
    ["NukeSilo"] = "NukeSilo",
}
Enums.MapGeneration.Rooms = {}
Enums.MapGeneration.Rooms.RoomLevelName = Enums["MapGeneration.Rooms.RoomLevelName"]
Enums.RoomLevelName = Enums["MapGeneration.Rooms.RoomLevelName"]
RoomLevelName = Enums["MapGeneration.Rooms.RoomLevelName"]

---@alias Enum_Metrics_002e_DeathsCollector_002e_ExportPreset
---| "DeathReasons"
Enums["Metrics.DeathsCollector.ExportPreset"] = {
    ["DeathReasons"] = "DeathReasons",
}
Enums.Metrics = {}
Enums.Metrics.DeathsCollector = {}
Enums.Metrics.DeathsCollector.ExportPreset = Enums["Metrics.DeathsCollector.ExportPreset"]
Enums.DeathsCollector = {}
Enums.DeathsCollector.ExportPreset = Enums["Metrics.DeathsCollector.ExportPreset"]
Enums["DeathsCollector.ExportPreset"] = Enums["Metrics.DeathsCollector.ExportPreset"]
DeathsCollector = {}
DeathsCollector.ExportPreset = Enums["Metrics.DeathsCollector.ExportPreset"]

---@alias Enum_Metrics_002e_ScpPreferencesCollector_002e_OptOutExportBehavior
---| "Exclude"
---| "Include"
---| "TreatAsZero"
Enums["Metrics.ScpPreferencesCollector.OptOutExportBehavior"] = {
    ["Exclude"] = "Exclude",
    ["Include"] = "Include",
    ["TreatAsZero"] = "TreatAsZero",
}
Enums.Metrics.ScpPreferencesCollector = {}
Enums.Metrics.ScpPreferencesCollector.OptOutExportBehavior = Enums["Metrics.ScpPreferencesCollector.OptOutExportBehavior"]
Enums.ScpPreferencesCollector = {}
Enums.ScpPreferencesCollector.OptOutExportBehavior = Enums["Metrics.ScpPreferencesCollector.OptOutExportBehavior"]
Enums["ScpPreferencesCollector.OptOutExportBehavior"] = Enums["Metrics.ScpPreferencesCollector.OptOutExportBehavior"]
ScpPreferencesCollector = {}
ScpPreferencesCollector.OptOutExportBehavior = Enums["Metrics.ScpPreferencesCollector.OptOutExportBehavior"]

---@alias Enum_Mirror_002e_LiteNetLib4Mirror_002e_LiteNetLib4MirrorCore_002e_States
---| "ClientConnected"
---| "ClientConnecting"
---| "Discovery"
---| "Idle"
---| "NonInitialized"
---| "Server"
Enums["Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.States"] = {
    ["ClientConnected"] = "ClientConnected",
    ["ClientConnecting"] = "ClientConnecting",
    ["Discovery"] = "Discovery",
    ["Idle"] = "Idle",
    ["NonInitialized"] = "NonInitialized",
    ["Server"] = "Server",
}
Enums.Mirror = {}
Enums.Mirror.LiteNetLib4Mirror = {}
Enums.Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore = {}
Enums.Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.States = Enums["Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.States"]
Enums.LiteNetLib4MirrorCore = {}
Enums.LiteNetLib4MirrorCore.States = Enums["Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.States"]
Enums["LiteNetLib4MirrorCore.States"] = Enums["Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.States"]
LiteNetLib4MirrorCore = {}
LiteNetLib4MirrorCore.States = Enums["Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore.States"]

---@alias Enum_PlayerRoles_002e_Faction
---| "Flamingos"
---| "FoundationEnemy"
---| "FoundationStaff"
---| "SCP"
---| "Unclassified"
Enums["PlayerRoles.Faction"] = {
    ["Flamingos"] = "Flamingos",
    ["FoundationEnemy"] = "FoundationEnemy",
    ["FoundationStaff"] = "FoundationStaff",
    ["SCP"] = "SCP",
    ["Unclassified"] = "Unclassified",
}
Enums.PlayerRoles = {}
Enums.PlayerRoles.Faction = Enums["PlayerRoles.Faction"]
Enums.Faction = Enums["PlayerRoles.Faction"]
Faction = Enums["PlayerRoles.Faction"]

---@alias Enum_PlayerRoles_002e_Filmmaker_002e_FilmmakerBlendPreset
---| "FetchNext"
---| "Hold"
---| "Linear"
---| "Smooth"
Enums["PlayerRoles.Filmmaker.FilmmakerBlendPreset"] = {
    ["FetchNext"] = "FetchNext",
    ["Hold"] = "Hold",
    ["Linear"] = "Linear",
    ["Smooth"] = "Smooth",
}
Enums.PlayerRoles.Filmmaker = {}
Enums.PlayerRoles.Filmmaker.FilmmakerBlendPreset = Enums["PlayerRoles.Filmmaker.FilmmakerBlendPreset"]
Enums.FilmmakerBlendPreset = Enums["PlayerRoles.Filmmaker.FilmmakerBlendPreset"]
FilmmakerBlendPreset = Enums["PlayerRoles.Filmmaker.FilmmakerBlendPreset"]

---@alias Enum_PlayerRoles_002e_Filmmaker_002e_FilmmakerUI_002e_FilmmakerKey
---| "JumpBackward"
---| "JumpForward"
---| "JumpToBeginning"
---| "JumpToEnd"
---| "RemoveKeyframe"
---| "UpdatePlayback"
Enums["PlayerRoles.Filmmaker.FilmmakerUI.FilmmakerKey"] = {
    ["JumpBackward"] = "JumpBackward",
    ["JumpForward"] = "JumpForward",
    ["JumpToBeginning"] = "JumpToBeginning",
    ["JumpToEnd"] = "JumpToEnd",
    ["RemoveKeyframe"] = "RemoveKeyframe",
    ["UpdatePlayback"] = "UpdatePlayback",
}
Enums.PlayerRoles.Filmmaker.FilmmakerUI = {}
Enums.PlayerRoles.Filmmaker.FilmmakerUI.FilmmakerKey = Enums["PlayerRoles.Filmmaker.FilmmakerUI.FilmmakerKey"]
Enums.FilmmakerUI = {}
Enums.FilmmakerUI.FilmmakerKey = Enums["PlayerRoles.Filmmaker.FilmmakerUI.FilmmakerKey"]
Enums["FilmmakerUI.FilmmakerKey"] = Enums["PlayerRoles.Filmmaker.FilmmakerUI.FilmmakerKey"]
FilmmakerUI = {}
FilmmakerUI.FilmmakerKey = Enums["PlayerRoles.Filmmaker.FilmmakerUI.FilmmakerKey"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_FpcMotor_002e_FpcViewMode
---| "LocalPlayer"
---| "Server"
---| "Thirdperson"
Enums["PlayerRoles.FirstPersonControl.FpcMotor.FpcViewMode"] = {
    ["LocalPlayer"] = "LocalPlayer",
    ["Server"] = "Server",
    ["Thirdperson"] = "Thirdperson",
}
Enums.PlayerRoles.FirstPersonControl = {}
Enums.PlayerRoles.FirstPersonControl.FpcMotor = {}
Enums.PlayerRoles.FirstPersonControl.FpcMotor.FpcViewMode = Enums["PlayerRoles.FirstPersonControl.FpcMotor.FpcViewMode"]
Enums.FpcMotor = {}
Enums.FpcMotor.FpcViewMode = Enums["PlayerRoles.FirstPersonControl.FpcMotor.FpcViewMode"]
Enums["FpcMotor.FpcViewMode"] = Enums["PlayerRoles.FirstPersonControl.FpcMotor.FpcViewMode"]
FpcMotor = {}
FpcMotor.FpcViewMode = Enums["PlayerRoles.FirstPersonControl.FpcMotor.FpcViewMode"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_PlayerMovementState
---| "Crouching"
---| "Sneaking"
---| "Sprinting"
---| "Walking"
Enums["PlayerRoles.FirstPersonControl.PlayerMovementState"] = {
    ["Crouching"] = "Crouching",
    ["Sneaking"] = "Sneaking",
    ["Sprinting"] = "Sprinting",
    ["Walking"] = "Walking",
}
Enums.PlayerRoles.FirstPersonControl.PlayerMovementState = Enums["PlayerRoles.FirstPersonControl.PlayerMovementState"]
Enums.PlayerMovementState = Enums["PlayerRoles.FirstPersonControl.PlayerMovementState"]
PlayerMovementState = Enums["PlayerRoles.FirstPersonControl.PlayerMovementState"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_AnimatedCharacterModel_002e_FootstepLoudness
---| "Chaos"
---| "Civilian"
---| "FoundationForces"
---| "Scp"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel.FootstepLoudness"] = {
    ["Chaos"] = "Chaos",
    ["Civilian"] = "Civilian",
    ["FoundationForces"] = "FoundationForces",
    ["Scp"] = "Scp",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel.FootstepLoudness = Enums["PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel.FootstepLoudness"]
Enums.AnimatedCharacterModel = {}
Enums.AnimatedCharacterModel.FootstepLoudness = Enums["PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel.FootstepLoudness"]
Enums["AnimatedCharacterModel.FootstepLoudness"] = Enums["PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel.FootstepLoudness"]
AnimatedCharacterModel = {}
AnimatedCharacterModel.FootstepLoudness = Enums["PlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel.FootstepLoudness"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_RefpointType
---| "Hitbox"
---| "LineOfSightPoint"
---| "None"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.RefpointType"] = {
    ["Hitbox"] = "Hitbox",
    ["LineOfSightPoint"] = "LineOfSightPoint",
    ["None"] = "None",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.RefpointType = Enums["PlayerRoles.FirstPersonControl.Thirdperson.RefpointType"]
Enums.RefpointType = Enums["PlayerRoles.FirstPersonControl.Thirdperson.RefpointType"]
RefpointType = Enums["PlayerRoles.FirstPersonControl.Thirdperson.RefpointType"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_CullingSubcontroller_002e_AnimationQuality
---| "High"
---| "Low"
---| "Medium"
---| "Ultra"
---| "VeryLow"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller.AnimationQuality"] = {
    ["High"] = "High",
    ["Low"] = "Low",
    ["Medium"] = "Medium",
    ["Ultra"] = "Ultra",
    ["VeryLow"] = "VeryLow",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller.AnimationQuality = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller.AnimationQuality"]
Enums.CullingSubcontroller = {}
Enums.CullingSubcontroller.AnimationQuality = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller.AnimationQuality"]
Enums["CullingSubcontroller.AnimationQuality"] = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller.AnimationQuality"]
CullingSubcontroller = {}
CullingSubcontroller.AnimationQuality = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller.AnimationQuality"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_EmotionBlendshape
---| "Angry"
---| "BrowRaise"
---| "Chad"
---| "Disgusted"
---| "EyeCloseLeft"
---| "EyeCloseRight"
---| "EyeOpenLeft"
---| "EyeOpenRight"
---| "EyeTapLeft"
---| "EyeTapRight"
---| "EyesBottom"
---| "Frown"
---| "Grin"
---| "JawDrop"
---| "LipsNormalBottom"
---| "LipsStretchBottom"
---| "LipsTop"
---| "MixClosedEyes"
---| "MouthOpen"
---| "Ogre"
---| "SmileOpen"
---| "Undefined"
---| "Worried"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"] = {
    ["Angry"] = "Angry",
    ["BrowRaise"] = "BrowRaise",
    ["Chad"] = "Chad",
    ["Disgusted"] = "Disgusted",
    ["EyeCloseLeft"] = "EyeCloseLeft",
    ["EyeCloseRight"] = "EyeCloseRight",
    ["EyeOpenLeft"] = "EyeOpenLeft",
    ["EyeOpenRight"] = "EyeOpenRight",
    ["EyeTapLeft"] = "EyeTapLeft",
    ["EyeTapRight"] = "EyeTapRight",
    ["EyesBottom"] = "EyesBottom",
    ["Frown"] = "Frown",
    ["Grin"] = "Grin",
    ["JawDrop"] = "JawDrop",
    ["LipsNormalBottom"] = "LipsNormalBottom",
    ["LipsStretchBottom"] = "LipsStretchBottom",
    ["LipsTop"] = "LipsTop",
    ["MixClosedEyes"] = "MixClosedEyes",
    ["MouthOpen"] = "MouthOpen",
    ["Ogre"] = "Ogre",
    ["SmileOpen"] = "SmileOpen",
    ["Undefined"] = "Undefined",
    ["Worried"] = "Worried",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"]
Enums.EmotionBlendshape = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"]
EmotionBlendshape = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_EmotionPresetType
---| "Angry"
---| "AwkwardSmile"
---| "Chad"
---| "Happy"
---| "Neutral"
---| "Ogre"
---| "Scared"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"] = {
    ["Angry"] = "Angry",
    ["AwkwardSmile"] = "AwkwardSmile",
    ["Chad"] = "Chad",
    ["Happy"] = "Happy",
    ["Neutral"] = "Neutral",
    ["Ogre"] = "Ogre",
    ["Scared"] = "Scared",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"]
Enums.EmotionPresetType = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"]
EmotionPresetType = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_GlowInTheDarkSubcontroller_002e_ForceCondition
---| "ForceWhenEnemy"
---| "ForceWhenFriendly"
---| "NeverForce"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceCondition"] = {
    ["ForceWhenEnemy"] = "ForceWhenEnemy",
    ["ForceWhenFriendly"] = "ForceWhenFriendly",
    ["NeverForce"] = "NeverForce",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceCondition = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceCondition"]
Enums.GlowInTheDarkSubcontroller = {}
Enums.GlowInTheDarkSubcontroller.ForceCondition = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceCondition"]
Enums["GlowInTheDarkSubcontroller.ForceCondition"] = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceCondition"]
GlowInTheDarkSubcontroller = {}
GlowInTheDarkSubcontroller.ForceCondition = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceCondition"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_GlowInTheDarkSubcontroller_002e_ForceResult
---| "ForceDarkened"
---| "ForceNormal"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceResult"] = {
    ["ForceDarkened"] = "ForceDarkened",
    ["ForceNormal"] = "ForceNormal",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceResult = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceResult"]
Enums.GlowInTheDarkSubcontroller.ForceResult = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceResult"]
Enums["GlowInTheDarkSubcontroller.ForceResult"] = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceResult"]
GlowInTheDarkSubcontroller.ForceResult = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller.ForceResult"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_IdlePoseRetainerSubcontroller_002e_TurnStatus
---| "Idle"
---| "TurningLeft"
---| "TurningRight"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller.TurnStatus"] = {
    ["Idle"] = "Idle",
    ["TurningLeft"] = "TurningLeft",
    ["TurningRight"] = "TurningRight",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller.TurnStatus = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller.TurnStatus"]
Enums.IdlePoseRetainerSubcontroller = {}
Enums.IdlePoseRetainerSubcontroller.TurnStatus = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller.TurnStatus"]
Enums["IdlePoseRetainerSubcontroller.TurnStatus"] = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller.TurnStatus"]
IdlePoseRetainerSubcontroller = {}
IdlePoseRetainerSubcontroller.TurnStatus = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller.TurnStatus"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_InventorySubcontroller_002e_TransitionStatus
---| "Done"
---| "EquippingNew"
---| "RetractingPrevious"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller.TransitionStatus"] = {
    ["Done"] = "Done",
    ["EquippingNew"] = "EquippingNew",
    ["RetractingPrevious"] = "RetractingPrevious",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller.TransitionStatus = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller.TransitionStatus"]
Enums.InventorySubcontroller = {}
Enums.InventorySubcontroller.TransitionStatus = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller.TransitionStatus"]
Enums["InventorySubcontroller.TransitionStatus"] = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller.TransitionStatus"]
InventorySubcontroller = {}
InventorySubcontroller.TransitionStatus = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller.TransitionStatus"]

---@alias Enum_PlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_Wearables_002e_WearableElements
---| "Armor"
---| "None"
---| "Scp1344Goggles"
---| "Scp268Hat"
Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"] = {
    ["Armor"] = "Armor",
    ["None"] = "None",
    ["Scp1344Goggles"] = "Scp1344Goggles",
    ["Scp268Hat"] = "Scp268Hat",
}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables = {}
Enums.PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"]
Enums.WearableElements = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"]
WearableElements = Enums["PlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp049_002e_Scp049ResurrectAbility_002e_ResurrectError
---| "Expired"
---| "MaxReached"
---| "None"
---| "Refused"
---| "TargetInvalid"
---| "TargetNull"
Enums["PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility.ResurrectError"] = {
    ["Expired"] = "Expired",
    ["MaxReached"] = "MaxReached",
    ["None"] = "None",
    ["Refused"] = "Refused",
    ["TargetInvalid"] = "TargetInvalid",
    ["TargetNull"] = "TargetNull",
}
Enums.PlayerRoles.PlayableScps = {}
Enums.PlayerRoles.PlayableScps.Scp049 = {}
Enums.PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility = {}
Enums.PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility.ResurrectError = Enums["PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility.ResurrectError"]
Enums.Scp049ResurrectAbility = {}
Enums.Scp049ResurrectAbility.ResurrectError = Enums["PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility.ResurrectError"]
Enums["Scp049ResurrectAbility.ResurrectError"] = Enums["PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility.ResurrectError"]
Scp049ResurrectAbility = {}
Scp049ResurrectAbility.ResurrectError = Enums["PlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility.ResurrectError"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp049_002e_Zombies_002e_ZombieAudioPlayer_002e_Scp0492SoundId
---| "AngryGrowl"
---| "Attack"
---| "Growl"
Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer.Scp0492SoundId"] = {
    ["AngryGrowl"] = "AngryGrowl",
    ["Attack"] = "Attack",
    ["Growl"] = "Growl",
}
Enums.PlayerRoles.PlayableScps.Scp049.Zombies = {}
Enums.PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer = {}
Enums.PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer.Scp0492SoundId = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer.Scp0492SoundId"]
Enums.ZombieAudioPlayer = {}
Enums.ZombieAudioPlayer.Scp0492SoundId = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer.Scp0492SoundId"]
Enums["ZombieAudioPlayer.Scp0492SoundId"] = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer.Scp0492SoundId"]
ZombieAudioPlayer = {}
ZombieAudioPlayer.Scp0492SoundId = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer.Scp0492SoundId"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp049_002e_Zombies_002e_ZombieConsumeAbility_002e_ConsumeError
---| "AlreadyConsumed"
---| "BeingConsumed"
---| "CannotCancel"
---| "FullHealth"
---| "None"
---| "TargetNotValid"
Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility.ConsumeError"] = {
    ["AlreadyConsumed"] = "AlreadyConsumed",
    ["BeingConsumed"] = "BeingConsumed",
    ["CannotCancel"] = "CannotCancel",
    ["FullHealth"] = "FullHealth",
    ["None"] = "None",
    ["TargetNotValid"] = "TargetNotValid",
}
Enums.PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility = {}
Enums.PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility.ConsumeError = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility.ConsumeError"]
Enums.ZombieConsumeAbility = {}
Enums.ZombieConsumeAbility.ConsumeError = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility.ConsumeError"]
Enums["ZombieConsumeAbility.ConsumeError"] = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility.ConsumeError"]
ZombieConsumeAbility = {}
ZombieConsumeAbility.ConsumeError = Enums["PlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility.ConsumeError"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Cameras_002e_Scp079CurrentCameraSync_002e_ClientSwitchState
---| "None"
---| "SwitchingRoom"
---| "SwitchingZone"
Enums["PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync.ClientSwitchState"] = {
    ["None"] = "None",
    ["SwitchingRoom"] = "SwitchingRoom",
    ["SwitchingZone"] = "SwitchingZone",
}
Enums.PlayerRoles.PlayableScps.Scp079 = {}
Enums.PlayerRoles.PlayableScps.Scp079.Cameras = {}
Enums.PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync = {}
Enums.PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync.ClientSwitchState = Enums["PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync.ClientSwitchState"]
Enums.Scp079CurrentCameraSync = {}
Enums.Scp079CurrentCameraSync.ClientSwitchState = Enums["PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync.ClientSwitchState"]
Enums["Scp079CurrentCameraSync.ClientSwitchState"] = Enums["PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync.ClientSwitchState"]
Scp079CurrentCameraSync = {}
Scp079CurrentCameraSync.ClientSwitchState = Enums["PlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync.ClientSwitchState"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_GUI_002e_NotificationSound
---| "None"
---| "Standard"
Enums["PlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"] = {
    ["None"] = "None",
    ["Standard"] = "Standard",
}
Enums.PlayerRoles.PlayableScps.Scp079.GUI = {}
Enums.PlayerRoles.PlayableScps.Scp079.GUI.NotificationSound = Enums["PlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"]
Enums.NotificationSound = Enums["PlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"]
NotificationSound = Enums["PlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Map_002e_Scp079TeammateIndicators_002e_IndicatorType
---| "High"
---| "Low"
---| "Medium"
Enums["PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators.IndicatorType"] = {
    ["High"] = "High",
    ["Low"] = "Low",
    ["Medium"] = "Medium",
}
Enums.PlayerRoles.PlayableScps.Scp079.Map = {}
Enums.PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators = {}
Enums.PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators.IndicatorType = Enums["PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators.IndicatorType"]
Enums.Scp079TeammateIndicators = {}
Enums.Scp079TeammateIndicators.IndicatorType = Enums["PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators.IndicatorType"]
Enums["Scp079TeammateIndicators.IndicatorType"] = Enums["PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators.IndicatorType"]
Scp079TeammateIndicators = {}
Scp079TeammateIndicators.IndicatorType = Enums["PlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators.IndicatorType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079BlackoutRoomAbility_002e_ValidationError
---| "AlreadyBlackedOut"
---| "MaxCapacityReached"
---| "NoController"
---| "None"
---| "NotEnoughAux"
---| "RoomOnCooldown"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility.ValidationError"] = {
    ["AlreadyBlackedOut"] = "AlreadyBlackedOut",
    ["MaxCapacityReached"] = "MaxCapacityReached",
    ["NoController"] = "NoController",
    ["None"] = "None",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["RoomOnCooldown"] = "RoomOnCooldown",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility = {}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility.ValidationError"]
Enums.Scp079BlackoutRoomAbility = {}
Enums.Scp079BlackoutRoomAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility.ValidationError"]
Enums["Scp079BlackoutRoomAbility.ValidationError"] = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility.ValidationError"]
Scp079BlackoutRoomAbility = {}
Scp079BlackoutRoomAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility.ValidationError"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079BlackoutZoneAbility_002e_ValidationError
---| "Cooldown"
---| "None"
---| "NotEnoughAux"
---| "Unavailable"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility.ValidationError"] = {
    ["Cooldown"] = "Cooldown",
    ["None"] = "None",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["Unavailable"] = "Unavailable",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility = {}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility.ValidationError"]
Enums.Scp079BlackoutZoneAbility = {}
Enums.Scp079BlackoutZoneAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility.ValidationError"]
Enums["Scp079BlackoutZoneAbility.ValidationError"] = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility.ValidationError"]
Scp079BlackoutZoneAbility = {}
Scp079BlackoutZoneAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility.ValidationError"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079HudTranslation
---| "AccessTier"
---| "AccessTierUnlocked"
---| "ActivateRoomBlackout"
---| "ActivateZoneBlackout"
---| "AuxPower"
---| "AuxPowerLimitIncreased"
---| "AuxRegenReduced"
---| "AuxRegenSuspended"
---| "BlackoutAlreadyActive"
---| "BlackoutCapacityIncreased"
---| "BlackoutRoomAvailable"
---| "BlackoutRoomCooldown"
---| "BlackoutRoomLimit"
---| "BlackoutRoomUnavailable"
---| "BreachScannerAvailable"
---| "CameraSwitchFailed"
---| "CloseBreachScanner"
---| "CloseDoor"
---| "CloseMap"
---| "CounterCivils"
---| "CounterMilitary"
---| "CounterScps"
---| "CounterTargets"
---| "CounterTotalLifeforms"
---| "DoorAccessDenied"
---| "DoorLockAbilityAvailable"
---| "DoorLockAlreadyActive"
---| "DoorLockAuxPause"
---| "DoorLockCooldown"
---| "ElevatorAccessDenied"
---| "EntranceZone"
---| "EtaTimer"
---| "ExpGainAdminCommand"
---| "ExpGainBlockingHuman"
---| "ExpGainHidStopped"
---| "ExpGainTeammateProtection"
---| "ExpGainTerminationAssist"
---| "ExpGainTerminationDirect"
---| "ExpGainTerminationWitness"
---| "Experience"
---| "FireTeslaGate"
---| "GoToCamera"
---| "HeavyContZone"
---| "HelpMenuDeepLearning"
---| "HelpMenuOverview"
---| "HelpMenuOverwatch"
---| "HelpMenuUnlimitedAccess"
---| "HelpMenuVirtuallyIndestructible"
---| "HigherTierRequired"
---| "InvalidCamera"
---| "LightContZone"
---| "LockDoor"
---| "Lockdown"
---| "LockdownAuxPause"
---| "LockdownAvailable"
---| "LockdownCooldown"
---| "LockdownNoDoorsError"
---| "MaxTierReached"
---| "NoCamera"
---| "NotEnoughAux"
---| "OpenBreachScanner"
---| "OpenDoor"
---| "OpenMap"
---| "PingLocation"
---| "PingRateLimited"
---| "ReconnectingEta"
---| "ReleaseDoorLock"
---| "ScanResultNoneFound"
---| "ScanResultPlayerDetected"
---| "ScanResultRetryingMessage"
---| "ScanStatusDisabled"
---| "ScanStatusScanning"
---| "ScanStatusWaiting"
---| "ScannerAuxPause"
---| "ScannerDescription"
---| "ScannerFilters"
---| "ScannerNoFiltersError"
---| "ScannerNoZoneSelectedLabel"
---| "ScannerSelectedZoneCntLabel"
---| "SendElevator"
---| "SignalLost"
---| "SpeakerAuxPause"
---| "SurfaceZone"
---| "TeslaGateCooldown"
---| "UnlockDoor"
---| "UseSpeaker"
---| "YouAreBeingAttacked"
---| "ZoneBlackoutAvailable"
---| "ZoneBlackoutCooldown"
---| "ZoneBlackoutUnavailable"
---| "Zoom"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"] = {
    ["AccessTier"] = "AccessTier",
    ["AccessTierUnlocked"] = "AccessTierUnlocked",
    ["ActivateRoomBlackout"] = "ActivateRoomBlackout",
    ["ActivateZoneBlackout"] = "ActivateZoneBlackout",
    ["AuxPower"] = "AuxPower",
    ["AuxPowerLimitIncreased"] = "AuxPowerLimitIncreased",
    ["AuxRegenReduced"] = "AuxRegenReduced",
    ["AuxRegenSuspended"] = "AuxRegenSuspended",
    ["BlackoutAlreadyActive"] = "BlackoutAlreadyActive",
    ["BlackoutCapacityIncreased"] = "BlackoutCapacityIncreased",
    ["BlackoutRoomAvailable"] = "BlackoutRoomAvailable",
    ["BlackoutRoomCooldown"] = "BlackoutRoomCooldown",
    ["BlackoutRoomLimit"] = "BlackoutRoomLimit",
    ["BlackoutRoomUnavailable"] = "BlackoutRoomUnavailable",
    ["BreachScannerAvailable"] = "BreachScannerAvailable",
    ["CameraSwitchFailed"] = "CameraSwitchFailed",
    ["CloseBreachScanner"] = "CloseBreachScanner",
    ["CloseDoor"] = "CloseDoor",
    ["CloseMap"] = "CloseMap",
    ["CounterCivils"] = "CounterCivils",
    ["CounterMilitary"] = "CounterMilitary",
    ["CounterScps"] = "CounterScps",
    ["CounterTargets"] = "CounterTargets",
    ["CounterTotalLifeforms"] = "CounterTotalLifeforms",
    ["DoorAccessDenied"] = "DoorAccessDenied",
    ["DoorLockAbilityAvailable"] = "DoorLockAbilityAvailable",
    ["DoorLockAlreadyActive"] = "DoorLockAlreadyActive",
    ["DoorLockAuxPause"] = "DoorLockAuxPause",
    ["DoorLockCooldown"] = "DoorLockCooldown",
    ["ElevatorAccessDenied"] = "ElevatorAccessDenied",
    ["EntranceZone"] = "EntranceZone",
    ["EtaTimer"] = "EtaTimer",
    ["ExpGainAdminCommand"] = "ExpGainAdminCommand",
    ["ExpGainBlockingHuman"] = "ExpGainBlockingHuman",
    ["ExpGainHidStopped"] = "ExpGainHidStopped",
    ["ExpGainTeammateProtection"] = "ExpGainTeammateProtection",
    ["ExpGainTerminationAssist"] = "ExpGainTerminationAssist",
    ["ExpGainTerminationDirect"] = "ExpGainTerminationDirect",
    ["ExpGainTerminationWitness"] = "ExpGainTerminationWitness",
    ["Experience"] = "Experience",
    ["FireTeslaGate"] = "FireTeslaGate",
    ["GoToCamera"] = "GoToCamera",
    ["HeavyContZone"] = "HeavyContZone",
    ["HelpMenuDeepLearning"] = "HelpMenuDeepLearning",
    ["HelpMenuOverview"] = "HelpMenuOverview",
    ["HelpMenuOverwatch"] = "HelpMenuOverwatch",
    ["HelpMenuUnlimitedAccess"] = "HelpMenuUnlimitedAccess",
    ["HelpMenuVirtuallyIndestructible"] = "HelpMenuVirtuallyIndestructible",
    ["HigherTierRequired"] = "HigherTierRequired",
    ["InvalidCamera"] = "InvalidCamera",
    ["LightContZone"] = "LightContZone",
    ["LockDoor"] = "LockDoor",
    ["Lockdown"] = "Lockdown",
    ["LockdownAuxPause"] = "LockdownAuxPause",
    ["LockdownAvailable"] = "LockdownAvailable",
    ["LockdownCooldown"] = "LockdownCooldown",
    ["LockdownNoDoorsError"] = "LockdownNoDoorsError",
    ["MaxTierReached"] = "MaxTierReached",
    ["NoCamera"] = "NoCamera",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["OpenBreachScanner"] = "OpenBreachScanner",
    ["OpenDoor"] = "OpenDoor",
    ["OpenMap"] = "OpenMap",
    ["PingLocation"] = "PingLocation",
    ["PingRateLimited"] = "PingRateLimited",
    ["ReconnectingEta"] = "ReconnectingEta",
    ["ReleaseDoorLock"] = "ReleaseDoorLock",
    ["ScanResultNoneFound"] = "ScanResultNoneFound",
    ["ScanResultPlayerDetected"] = "ScanResultPlayerDetected",
    ["ScanResultRetryingMessage"] = "ScanResultRetryingMessage",
    ["ScanStatusDisabled"] = "ScanStatusDisabled",
    ["ScanStatusScanning"] = "ScanStatusScanning",
    ["ScanStatusWaiting"] = "ScanStatusWaiting",
    ["ScannerAuxPause"] = "ScannerAuxPause",
    ["ScannerDescription"] = "ScannerDescription",
    ["ScannerFilters"] = "ScannerFilters",
    ["ScannerNoFiltersError"] = "ScannerNoFiltersError",
    ["ScannerNoZoneSelectedLabel"] = "ScannerNoZoneSelectedLabel",
    ["ScannerSelectedZoneCntLabel"] = "ScannerSelectedZoneCntLabel",
    ["SendElevator"] = "SendElevator",
    ["SignalLost"] = "SignalLost",
    ["SpeakerAuxPause"] = "SpeakerAuxPause",
    ["SurfaceZone"] = "SurfaceZone",
    ["TeslaGateCooldown"] = "TeslaGateCooldown",
    ["UnlockDoor"] = "UnlockDoor",
    ["UseSpeaker"] = "UseSpeaker",
    ["YouAreBeingAttacked"] = "YouAreBeingAttacked",
    ["ZoneBlackoutAvailable"] = "ZoneBlackoutAvailable",
    ["ZoneBlackoutCooldown"] = "ZoneBlackoutCooldown",
    ["ZoneBlackoutUnavailable"] = "ZoneBlackoutUnavailable",
    ["Zoom"] = "Zoom",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079HudTranslation = Enums["PlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"]
Enums.Scp079HudTranslation = Enums["PlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"]
Scp079HudTranslation = Enums["PlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079KeyAbilityBase_002e_Category
---| "Movement"
---| "OverconInteraction"
---| "SpecialAbility"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase.Category"] = {
    ["Movement"] = "Movement",
    ["OverconInteraction"] = "OverconInteraction",
    ["SpecialAbility"] = "SpecialAbility",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase = {}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase.Category = Enums["PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase.Category"]
Enums.Scp079KeyAbilityBase = {}
Enums.Scp079KeyAbilityBase.Category = Enums["PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase.Category"]
Enums["Scp079KeyAbilityBase.Category"] = Enums["PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase.Category"]
Scp079KeyAbilityBase = {}
Scp079KeyAbilityBase.Category = Enums["PlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase.Category"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079LockdownRoomAbility_002e_ValidationError
---| "Cooldown"
---| "NoDoors"
---| "None"
---| "NotEnoughAux"
---| "TierTooLow"
---| "Unknown"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility.ValidationError"] = {
    ["Cooldown"] = "Cooldown",
    ["NoDoors"] = "NoDoors",
    ["None"] = "None",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["TierTooLow"] = "TierTooLow",
    ["Unknown"] = "Unknown",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility = {}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility.ValidationError"]
Enums.Scp079LockdownRoomAbility = {}
Enums.Scp079LockdownRoomAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility.ValidationError"]
Enums["Scp079LockdownRoomAbility.ValidationError"] = Enums["PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility.ValidationError"]
Scp079LockdownRoomAbility = {}
Scp079LockdownRoomAbility.ValidationError = Enums["PlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility.ValidationError"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079ScannerSequence_002e_ScanSequenceStep
---| "CountingDown"
---| "Init"
---| "ScanningFailedCooldown"
---| "ScanningFindNewTarget"
---| "ScanningUpdateTarget"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.ScanSequenceStep"] = {
    ["CountingDown"] = "CountingDown",
    ["Init"] = "Init",
    ["ScanningFailedCooldown"] = "ScanningFailedCooldown",
    ["ScanningFindNewTarget"] = "ScanningFindNewTarget",
    ["ScanningUpdateTarget"] = "ScanningUpdateTarget",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence = {}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.ScanSequenceStep = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.ScanSequenceStep"]
Enums.Scp079ScannerSequence = {}
Enums.Scp079ScannerSequence.ScanSequenceStep = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.ScanSequenceStep"]
Enums["Scp079ScannerSequence.ScanSequenceStep"] = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.ScanSequenceStep"]
Scp079ScannerSequence = {}
Scp079ScannerSequence.ScanSequenceStep = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.ScanSequenceStep"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079ScannerSequence_002e_TrackerMessage
---| "None"
---| "ScanNoResults"
---| "ScanSuccessful"
---| "ScanTimeSync"
---| "ScannerDisabled"
Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.TrackerMessage"] = {
    ["None"] = "None",
    ["ScanNoResults"] = "ScanNoResults",
    ["ScanSuccessful"] = "ScanSuccessful",
    ["ScanTimeSync"] = "ScanTimeSync",
    ["ScannerDisabled"] = "ScannerDisabled",
}
Enums.PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.TrackerMessage = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.TrackerMessage"]
Enums.Scp079ScannerSequence.TrackerMessage = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.TrackerMessage"]
Enums["Scp079ScannerSequence.TrackerMessage"] = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.TrackerMessage"]
Scp079ScannerSequence.TrackerMessage = Enums["PlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence.TrackerMessage"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096AbilityState
---| "Attacking"
---| "Charging"
---| "None"
---| "PryingGate"
---| "TryingNotToCry"
Enums["PlayerRoles.PlayableScps.Scp096.Scp096AbilityState"] = {
    ["Attacking"] = "Attacking",
    ["Charging"] = "Charging",
    ["None"] = "None",
    ["PryingGate"] = "PryingGate",
    ["TryingNotToCry"] = "TryingNotToCry",
}
Enums.PlayerRoles.PlayableScps.Scp096 = {}
Enums.PlayerRoles.PlayableScps.Scp096.Scp096AbilityState = Enums["PlayerRoles.PlayableScps.Scp096.Scp096AbilityState"]
Enums.Scp096AbilityState = Enums["PlayerRoles.PlayableScps.Scp096.Scp096AbilityState"]
Scp096AbilityState = Enums["PlayerRoles.PlayableScps.Scp096.Scp096AbilityState"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096HitResult
---| "Door"
---| "Human"
---| "Lethal"
---| "None"
---| "Window"
Enums["PlayerRoles.PlayableScps.Scp096.Scp096HitResult"] = {
    ["Door"] = "Door",
    ["Human"] = "Human",
    ["Lethal"] = "Lethal",
    ["None"] = "None",
    ["Window"] = "Window",
}
Enums.PlayerRoles.PlayableScps.Scp096.Scp096HitResult = Enums["PlayerRoles.PlayableScps.Scp096.Scp096HitResult"]
Enums.Scp096HitResult = Enums["PlayerRoles.PlayableScps.Scp096.Scp096HitResult"]
Scp096HitResult = Enums["PlayerRoles.PlayableScps.Scp096.Scp096HitResult"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096HudTranslation
---| "EnterRageKeyInfo"
---| "ExitRageKeyInfo"
---| "HelpMenuCharge"
---| "HelpMenuDescription"
---| "HelpMenuDocileOnly"
---| "HelpMenuMeeleAttack"
---| "HelpMenuRageOnly"
---| "HelpMenuTryNotToCry"
Enums["PlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"] = {
    ["EnterRageKeyInfo"] = "EnterRageKeyInfo",
    ["ExitRageKeyInfo"] = "ExitRageKeyInfo",
    ["HelpMenuCharge"] = "HelpMenuCharge",
    ["HelpMenuDescription"] = "HelpMenuDescription",
    ["HelpMenuDocileOnly"] = "HelpMenuDocileOnly",
    ["HelpMenuMeeleAttack"] = "HelpMenuMeeleAttack",
    ["HelpMenuRageOnly"] = "HelpMenuRageOnly",
    ["HelpMenuTryNotToCry"] = "HelpMenuTryNotToCry",
}
Enums.PlayerRoles.PlayableScps.Scp096.Scp096HudTranslation = Enums["PlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"]
Enums.Scp096HudTranslation = Enums["PlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"]
Scp096HudTranslation = Enums["PlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096RageState
---| "Calming"
---| "Distressed"
---| "Docile"
---| "Enraged"
Enums["PlayerRoles.PlayableScps.Scp096.Scp096RageState"] = {
    ["Calming"] = "Calming",
    ["Distressed"] = "Distressed",
    ["Docile"] = "Docile",
    ["Enraged"] = "Enraged",
}
Enums.PlayerRoles.PlayableScps.Scp096.Scp096RageState = Enums["PlayerRoles.PlayableScps.Scp096.Scp096RageState"]
Enums.Scp096RageState = Enums["PlayerRoles.PlayableScps.Scp096.Scp096RageState"]
Scp096RageState = Enums["PlayerRoles.PlayableScps.Scp096.Scp096RageState"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp106_002e_Scp106StalkAbility_002e_RpcType
---| "NotEnoughVigor"
---| "StalkActive"
---| "StalkInactive"
Enums["PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility.RpcType"] = {
    ["NotEnoughVigor"] = "NotEnoughVigor",
    ["StalkActive"] = "StalkActive",
    ["StalkInactive"] = "StalkInactive",
}
Enums.PlayerRoles.PlayableScps.Scp106 = {}
Enums.PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility = {}
Enums.PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility.RpcType = Enums["PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility.RpcType"]
Enums.Scp106StalkAbility = {}
Enums.Scp106StalkAbility.RpcType = Enums["PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility.RpcType"]
Enums["Scp106StalkAbility.RpcType"] = Enums["PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility.RpcType"]
Scp106StalkAbility = {}
Scp106StalkAbility.RpcType = Enums["PlayerRoles.PlayableScps.Scp106.Scp106StalkAbility.RpcType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp1507_002e_Scp1507Spawner_002e_State
---| "Idle"
---| "Spawned"
---| "Spawning"
---| "WaitForRespawnCycle"
---| "WaitForSpectators"
Enums["PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner.State"] = {
    ["Idle"] = "Idle",
    ["Spawned"] = "Spawned",
    ["Spawning"] = "Spawning",
    ["WaitForRespawnCycle"] = "WaitForRespawnCycle",
    ["WaitForSpectators"] = "WaitForSpectators",
}
Enums.PlayerRoles.PlayableScps.Scp1507 = {}
Enums.PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner = {}
Enums.PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner.State = Enums["PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner.State"]
Enums.Scp1507Spawner = {}
Enums.Scp1507Spawner.State = Enums["PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner.State"]
Enums["Scp1507Spawner.State"] = Enums["PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner.State"]
Scp1507Spawner = {}
Scp1507Spawner.State = Enums["PlayerRoles.PlayableScps.Scp1507.Scp1507Spawner.State"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp173_002e_Scp173AudioPlayer_002e_Scp173SoundId
---| "Hit"
---| "Snap"
---| "Teleport"
Enums["PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer.Scp173SoundId"] = {
    ["Hit"] = "Hit",
    ["Snap"] = "Snap",
    ["Teleport"] = "Teleport",
}
Enums.PlayerRoles.PlayableScps.Scp173 = {}
Enums.PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer = {}
Enums.PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer.Scp173SoundId = Enums["PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer.Scp173SoundId"]
Enums.Scp173AudioPlayer = {}
Enums.Scp173AudioPlayer.Scp173SoundId = Enums["PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer.Scp173SoundId"]
Enums["Scp173AudioPlayer.Scp173SoundId"] = Enums["PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer.Scp173SoundId"]
Scp173AudioPlayer = {}
Scp173AudioPlayer.Scp173SoundId = Enums["PlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer.Scp173SoundId"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp173_002e_Scp173TeleportAbility_002e_CmdTeleportData
---| "Aiming"
---| "WantsToTeleport"
Enums["PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility.CmdTeleportData"] = {
    ["Aiming"] = "Aiming",
    ["WantsToTeleport"] = "WantsToTeleport",
}
Enums.PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility = {}
Enums.PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility.CmdTeleportData = Enums["PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility.CmdTeleportData"]
Enums.Scp173TeleportAbility = {}
Enums.Scp173TeleportAbility.CmdTeleportData = Enums["PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility.CmdTeleportData"]
Enums["Scp173TeleportAbility.CmdTeleportData"] = Enums["PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility.CmdTeleportData"]
Scp173TeleportAbility = {}
Scp173TeleportAbility.CmdTeleportData = Enums["PlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility.CmdTeleportData"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114DamageHandler_002e_HandlerType
---| "SkinSteal"
---| "Slap"
---| "Strangulation"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler.HandlerType"] = {
    ["SkinSteal"] = "SkinSteal",
    ["Slap"] = "Slap",
    ["Strangulation"] = "Strangulation",
}
Enums.PlayerRoles.PlayableScps.Scp3114 = {}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler = {}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler.HandlerType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler.HandlerType"]
Enums.Scp3114DamageHandler = {}
Enums.Scp3114DamageHandler.HandlerType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler.HandlerType"]
Enums["Scp3114DamageHandler.HandlerType"] = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler.HandlerType"]
Scp3114DamageHandler = {}
Scp3114DamageHandler.HandlerType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler.HandlerType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114HudTranslation
---| "EquipItemWarning"
---| "HelpMenuCatchTheseHands"
---| "HelpMenuSkeletonsInYourCLoset"
---| "HelpMenuStrangulation"
---| "HelpMenuUnderTheirSkin"
---| "IdentityStolenWarning"
---| "InspectRagdollOverride"
---| "ProximityVoiceHint"
---| "RagdollErrorAlreadyDisguised"
---| "RagdollErrorNotHuman"
---| "RagdollErrorPreviouslyUsed"
---| "RevealKeyHoldLongerWarning"
---| "StartingRagdollHint"
---| "StrangulationCooldown"
---| "UnableToAttackWarning"
---| "ValidRagdollIndicator"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"] = {
    ["EquipItemWarning"] = "EquipItemWarning",
    ["HelpMenuCatchTheseHands"] = "HelpMenuCatchTheseHands",
    ["HelpMenuSkeletonsInYourCLoset"] = "HelpMenuSkeletonsInYourCLoset",
    ["HelpMenuStrangulation"] = "HelpMenuStrangulation",
    ["HelpMenuUnderTheirSkin"] = "HelpMenuUnderTheirSkin",
    ["IdentityStolenWarning"] = "IdentityStolenWarning",
    ["InspectRagdollOverride"] = "InspectRagdollOverride",
    ["ProximityVoiceHint"] = "ProximityVoiceHint",
    ["RagdollErrorAlreadyDisguised"] = "RagdollErrorAlreadyDisguised",
    ["RagdollErrorNotHuman"] = "RagdollErrorNotHuman",
    ["RagdollErrorPreviouslyUsed"] = "RagdollErrorPreviouslyUsed",
    ["RevealKeyHoldLongerWarning"] = "RevealKeyHoldLongerWarning",
    ["StartingRagdollHint"] = "StartingRagdollHint",
    ["StrangulationCooldown"] = "StrangulationCooldown",
    ["UnableToAttackWarning"] = "UnableToAttackWarning",
    ["ValidRagdollIndicator"] = "ValidRagdollIndicator",
}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"]
Enums.Scp3114HudTranslation = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"]
Scp3114HudTranslation = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114Identity_002e_DisguiseStatus
---| "Active"
---| "Equipping"
---| "None"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Identity.DisguiseStatus"] = {
    ["Active"] = "Active",
    ["Equipping"] = "Equipping",
    ["None"] = "None",
}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114Identity = {}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114Identity.DisguiseStatus = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Identity.DisguiseStatus"]
Enums.Scp3114Identity = {}
Enums.Scp3114Identity.DisguiseStatus = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Identity.DisguiseStatus"]
Enums["Scp3114Identity.DisguiseStatus"] = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Identity.DisguiseStatus"]
Scp3114Identity = {}
Scp3114Identity.DisguiseStatus = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Identity.DisguiseStatus"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114MaterialType
---| "Disguise"
---| "Original"
---| "Reveal"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"] = {
    ["Disguise"] = "Disguise",
    ["Original"] = "Original",
    ["Reveal"] = "Reveal",
}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"]
Enums.Scp3114MaterialType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"]
Scp3114MaterialType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114Strangle_002e_RpcType
---| "AttackCancelled"
---| "AttackInterrupted"
---| "OutOfRange"
---| "StrangleTimer"
---| "TargetKilled"
---| "TargetResync"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle.RpcType"] = {
    ["AttackCancelled"] = "AttackCancelled",
    ["AttackInterrupted"] = "AttackInterrupted",
    ["OutOfRange"] = "OutOfRange",
    ["StrangleTimer"] = "StrangleTimer",
    ["TargetKilled"] = "TargetKilled",
    ["TargetResync"] = "TargetResync",
}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle = {}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle.RpcType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle.RpcType"]
Enums.Scp3114Strangle = {}
Enums.Scp3114Strangle.RpcType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle.RpcType"]
Enums["Scp3114Strangle.RpcType"] = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle.RpcType"]
Scp3114Strangle = {}
Scp3114Strangle.RpcType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114Strangle.RpcType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114StrangleAudio_002e_RpcType
---| "ChokeSync"
---| "Kill"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio.RpcType"] = {
    ["ChokeSync"] = "ChokeSync",
    ["Kill"] = "Kill",
}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio = {}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio.RpcType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio.RpcType"]
Enums.Scp3114StrangleAudio = {}
Enums.Scp3114StrangleAudio.RpcType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio.RpcType"]
Enums["Scp3114StrangleAudio.RpcType"] = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio.RpcType"]
Scp3114StrangleAudio = {}
Scp3114StrangleAudio.RpcType = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio.RpcType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114VoiceLines_002e_VoiceLinesName
---| "EquipStart"
---| "KillSlap"
---| "KillStrangle"
---| "RandomIdle"
---| "Reveal"
---| "Slap"
---| "StartStrangle"
Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines.VoiceLinesName"] = {
    ["EquipStart"] = "EquipStart",
    ["KillSlap"] = "KillSlap",
    ["KillStrangle"] = "KillStrangle",
    ["RandomIdle"] = "RandomIdle",
    ["Reveal"] = "Reveal",
    ["Slap"] = "Slap",
    ["StartStrangle"] = "StartStrangle",
}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines = {}
Enums.PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines.VoiceLinesName = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines.VoiceLinesName"]
Enums.Scp3114VoiceLines = {}
Enums.Scp3114VoiceLines.VoiceLinesName = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines.VoiceLinesName"]
Enums["Scp3114VoiceLines.VoiceLinesName"] = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines.VoiceLinesName"]
Scp3114VoiceLines = {}
Scp3114VoiceLines.VoiceLinesName = Enums["PlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines.VoiceLinesName"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp939_002e_Mimicry_002e_MimicPointController_002e_RpcStateMsg
---| "DestroyedByDistance"
---| "None"
---| "PlacedByUser"
---| "RemovedByUser"
Enums["PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController.RpcStateMsg"] = {
    ["DestroyedByDistance"] = "DestroyedByDistance",
    ["None"] = "None",
    ["PlacedByUser"] = "PlacedByUser",
    ["RemovedByUser"] = "RemovedByUser",
}
Enums.PlayerRoles.PlayableScps.Scp939 = {}
Enums.PlayerRoles.PlayableScps.Scp939.Mimicry = {}
Enums.PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController = {}
Enums.PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController.RpcStateMsg = Enums["PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController.RpcStateMsg"]
Enums.MimicPointController = {}
Enums.MimicPointController.RpcStateMsg = Enums["PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController.RpcStateMsg"]
Enums["MimicPointController.RpcStateMsg"] = Enums["PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController.RpcStateMsg"]
MimicPointController = {}
MimicPointController.RpcStateMsg = Enums["PlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController.RpcStateMsg"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939AmnesticCloudInstance_002e_CloudState
---| "Created"
---| "Destroyed"
---| "Spawning"
Enums["PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance.CloudState"] = {
    ["Created"] = "Created",
    ["Destroyed"] = "Destroyed",
    ["Spawning"] = "Spawning",
}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance = {}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance.CloudState = Enums["PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance.CloudState"]
Enums.Scp939AmnesticCloudInstance = {}
Enums.Scp939AmnesticCloudInstance.CloudState = Enums["PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance.CloudState"]
Enums["Scp939AmnesticCloudInstance.CloudState"] = Enums["PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance.CloudState"]
Scp939AmnesticCloudInstance = {}
Scp939AmnesticCloudInstance.CloudState = Enums["PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance.CloudState"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939ChaseThemeProvider_002e_TrackType
---| "Detected"
---| "MadeSound"
---| "None"
---| "Quiet"
Enums["PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider.TrackType"] = {
    ["Detected"] = "Detected",
    ["MadeSound"] = "MadeSound",
    ["None"] = "None",
    ["Quiet"] = "Quiet",
}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider = {}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider.TrackType = Enums["PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider.TrackType"]
Enums.Scp939ChaseThemeProvider = {}
Enums.Scp939ChaseThemeProvider.TrackType = Enums["PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider.TrackType"]
Enums["Scp939ChaseThemeProvider.TrackType"] = Enums["PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider.TrackType"]
Scp939ChaseThemeProvider = {}
Scp939ChaseThemeProvider.TrackType = Enums["PlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider.TrackType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939DamageType
---| "Claw"
---| "LungeSecondary"
---| "LungeTarget"
---| "None"
Enums["PlayerRoles.PlayableScps.Scp939.Scp939DamageType"] = {
    ["Claw"] = "Claw",
    ["LungeSecondary"] = "LungeSecondary",
    ["LungeTarget"] = "LungeTarget",
    ["None"] = "None",
}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939DamageType = Enums["PlayerRoles.PlayableScps.Scp939.Scp939DamageType"]
Enums.Scp939DamageType = Enums["PlayerRoles.PlayableScps.Scp939.Scp939DamageType"]
Scp939DamageType = Enums["PlayerRoles.PlayableScps.Scp939.Scp939DamageType"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939HudTranslation
---| "CloudFailedPositionInvalid"
---| "CloudFailedSizeInsufficient"
---| "EnvMimicryCooldown"
---| "EnvMimicryDescription"
---| "EnvMimicryHeader"
---| "HelpMenuClaw"
---| "HelpMenuCloud"
---| "HelpMenuDescription"
---| "HelpMenuFocus"
---| "HelpMenuUltrasensitive"
---| "HelpMenuWithManyVoices"
---| "HintAssignedHotkey"
---| "HintPlayEnvMimicry"
---| "HintPreviewVoice"
---| "HintRemoveVoice"
---| "HintReorganizeRecordings"
---| "HintStopPlayback"
---| "HintToggleFavorite"
---| "HintTransmitVoice"
---| "HintTrimRecording"
---| "LocalPreviewHeader"
---| "MimicFootsteps"
---| "MimicOther"
---| "MimicPointDestroyedDistance"
---| "MimicPointPlaceButton"
---| "MimicPointPlaceConfirmation"
---| "MimicPointPlaceDescription"
---| "MimicPointRemoveButton"
---| "MimicPointRemoveConfirmation"
---| "MimicPointRemoveDescription"
---| "MimicRoles"
---| "MimicShooting"
---| "MimicryPrivacyAcceptButton"
---| "MimicryPrivacyDeleteButton"
---| "MimicryPrivacyDescription"
---| "MimicryPrivacyMoreInfoButton"
---| "MimicryPrivacyRememberChoice"
---| "MimicryPrivacySubtitle"
---| "NextMenu"
---| "PressKeyToLunge"
---| "PrevMenu"
---| "PrivacyHeader"
---| "ReturnButton"
---| "Roles"
---| "StolenVoicesDescription"
---| "StolenVoicesHeader"
---| "YouGotAVoicelinePopup"
Enums["PlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"] = {
    ["CloudFailedPositionInvalid"] = "CloudFailedPositionInvalid",
    ["CloudFailedSizeInsufficient"] = "CloudFailedSizeInsufficient",
    ["EnvMimicryCooldown"] = "EnvMimicryCooldown",
    ["EnvMimicryDescription"] = "EnvMimicryDescription",
    ["EnvMimicryHeader"] = "EnvMimicryHeader",
    ["HelpMenuClaw"] = "HelpMenuClaw",
    ["HelpMenuCloud"] = "HelpMenuCloud",
    ["HelpMenuDescription"] = "HelpMenuDescription",
    ["HelpMenuFocus"] = "HelpMenuFocus",
    ["HelpMenuUltrasensitive"] = "HelpMenuUltrasensitive",
    ["HelpMenuWithManyVoices"] = "HelpMenuWithManyVoices",
    ["HintAssignedHotkey"] = "HintAssignedHotkey",
    ["HintPlayEnvMimicry"] = "HintPlayEnvMimicry",
    ["HintPreviewVoice"] = "HintPreviewVoice",
    ["HintRemoveVoice"] = "HintRemoveVoice",
    ["HintReorganizeRecordings"] = "HintReorganizeRecordings",
    ["HintStopPlayback"] = "HintStopPlayback",
    ["HintToggleFavorite"] = "HintToggleFavorite",
    ["HintTransmitVoice"] = "HintTransmitVoice",
    ["HintTrimRecording"] = "HintTrimRecording",
    ["LocalPreviewHeader"] = "LocalPreviewHeader",
    ["MimicFootsteps"] = "MimicFootsteps",
    ["MimicOther"] = "MimicOther",
    ["MimicPointDestroyedDistance"] = "MimicPointDestroyedDistance",
    ["MimicPointPlaceButton"] = "MimicPointPlaceButton",
    ["MimicPointPlaceConfirmation"] = "MimicPointPlaceConfirmation",
    ["MimicPointPlaceDescription"] = "MimicPointPlaceDescription",
    ["MimicPointRemoveButton"] = "MimicPointRemoveButton",
    ["MimicPointRemoveConfirmation"] = "MimicPointRemoveConfirmation",
    ["MimicPointRemoveDescription"] = "MimicPointRemoveDescription",
    ["MimicRoles"] = "MimicRoles",
    ["MimicShooting"] = "MimicShooting",
    ["MimicryPrivacyAcceptButton"] = "MimicryPrivacyAcceptButton",
    ["MimicryPrivacyDeleteButton"] = "MimicryPrivacyDeleteButton",
    ["MimicryPrivacyDescription"] = "MimicryPrivacyDescription",
    ["MimicryPrivacyMoreInfoButton"] = "MimicryPrivacyMoreInfoButton",
    ["MimicryPrivacyRememberChoice"] = "MimicryPrivacyRememberChoice",
    ["MimicryPrivacySubtitle"] = "MimicryPrivacySubtitle",
    ["NextMenu"] = "NextMenu",
    ["PressKeyToLunge"] = "PressKeyToLunge",
    ["PrevMenu"] = "PrevMenu",
    ["PrivacyHeader"] = "PrivacyHeader",
    ["ReturnButton"] = "ReturnButton",
    ["Roles"] = "Roles",
    ["StolenVoicesDescription"] = "StolenVoicesDescription",
    ["StolenVoicesHeader"] = "StolenVoicesHeader",
    ["YouGotAVoicelinePopup"] = "YouGotAVoicelinePopup",
}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939HudTranslation = Enums["PlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"]
Enums.Scp939HudTranslation = Enums["PlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"]
Scp939HudTranslation = Enums["PlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939LungeState
---| "LandHarsh"
---| "LandHit"
---| "LandRegular"
---| "None"
---| "Triggered"
Enums["PlayerRoles.PlayableScps.Scp939.Scp939LungeState"] = {
    ["LandHarsh"] = "LandHarsh",
    ["LandHit"] = "LandHit",
    ["LandRegular"] = "LandRegular",
    ["None"] = "None",
    ["Triggered"] = "Triggered",
}
Enums.PlayerRoles.PlayableScps.Scp939.Scp939LungeState = Enums["PlayerRoles.PlayableScps.Scp939.Scp939LungeState"]
Enums.Scp939LungeState = Enums["PlayerRoles.PlayableScps.Scp939.Scp939LungeState"]
Scp939LungeState = Enums["PlayerRoles.PlayableScps.Scp939.Scp939LungeState"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_Subroutines_002e_AttackResult
---| "AttackedObject"
---| "AttackedPlayer"
---| "KilledPlayer"
---| "None"
Enums["PlayerRoles.PlayableScps.Subroutines.AttackResult"] = {
    ["AttackedObject"] = "AttackedObject",
    ["AttackedPlayer"] = "AttackedPlayer",
    ["KilledPlayer"] = "KilledPlayer",
    ["None"] = "None",
}
Enums.PlayerRoles.PlayableScps.Subroutines = {}
Enums.PlayerRoles.PlayableScps.Subroutines.AttackResult = Enums["PlayerRoles.PlayableScps.Subroutines.AttackResult"]
Enums.AttackResult = Enums["PlayerRoles.PlayableScps.Subroutines.AttackResult"]
AttackResult = Enums["PlayerRoles.PlayableScps.Subroutines.AttackResult"]

---@alias Enum_PlayerRoles_002e_PlayableScps_002e_VisionInformation_002e_FailReason
---| "InDarkRoom"
---| "IsLooking"
---| "NotInDistance"
---| "NotInLineOfSight"
---| "NotInView"
---| "NotOnSameFloor"
---| "UnkownReason"
Enums["PlayerRoles.PlayableScps.VisionInformation.FailReason"] = {
    ["InDarkRoom"] = "InDarkRoom",
    ["IsLooking"] = "IsLooking",
    ["NotInDistance"] = "NotInDistance",
    ["NotInLineOfSight"] = "NotInLineOfSight",
    ["NotInView"] = "NotInView",
    ["NotOnSameFloor"] = "NotOnSameFloor",
    ["UnkownReason"] = "UnkownReason",
}
Enums.PlayerRoles.PlayableScps.VisionInformation = {}
Enums.PlayerRoles.PlayableScps.VisionInformation.FailReason = Enums["PlayerRoles.PlayableScps.VisionInformation.FailReason"]
Enums.VisionInformation = {}
Enums.VisionInformation.FailReason = Enums["PlayerRoles.PlayableScps.VisionInformation.FailReason"]
Enums["VisionInformation.FailReason"] = Enums["PlayerRoles.PlayableScps.VisionInformation.FailReason"]
VisionInformation = {}
VisionInformation.FailReason = Enums["PlayerRoles.PlayableScps.VisionInformation.FailReason"]

---@alias Enum_PlayerRoles_002e_RoleChangeReason
---| "Destroyed"
---| "Died"
---| "Escaped"
---| "ItemUsage"
---| "LateJoin"
---| "None"
---| "RemoteAdmin"
---| "Respawn"
---| "RespawnMiniwave"
---| "Resurrected"
---| "Revived"
---| "RoundStart"
Enums["PlayerRoles.RoleChangeReason"] = {
    ["Destroyed"] = "Destroyed",
    ["Died"] = "Died",
    ["Escaped"] = "Escaped",
    ["ItemUsage"] = "ItemUsage",
    ["LateJoin"] = "LateJoin",
    ["None"] = "None",
    ["RemoteAdmin"] = "RemoteAdmin",
    ["Respawn"] = "Respawn",
    ["RespawnMiniwave"] = "RespawnMiniwave",
    ["Resurrected"] = "Resurrected",
    ["Revived"] = "Revived",
    ["RoundStart"] = "RoundStart",
}
Enums.PlayerRoles.RoleChangeReason = Enums["PlayerRoles.RoleChangeReason"]
Enums.RoleChangeReason = Enums["PlayerRoles.RoleChangeReason"]
RoleChangeReason = Enums["PlayerRoles.RoleChangeReason"]

---@alias Enum_PlayerRoles_002e_RoleSpawnFlags
---| "All"
---| "AssignInventory"
---| "None"
---| "UseSpawnpoint"
Enums["PlayerRoles.RoleSpawnFlags"] = {
    ["All"] = "All",
    ["AssignInventory"] = "AssignInventory",
    ["None"] = "None",
    ["UseSpawnpoint"] = "UseSpawnpoint",
}
Enums.PlayerRoles.RoleSpawnFlags = Enums["PlayerRoles.RoleSpawnFlags"]
Enums.RoleSpawnFlags = Enums["PlayerRoles.RoleSpawnFlags"]
RoleSpawnFlags = Enums["PlayerRoles.RoleSpawnFlags"]

---@alias Enum_PlayerRoles_002e_RoleTypeId
---| "AlphaFlamingo"
---| "ChaosConscript"
---| "ChaosFlamingo"
---| "ChaosMarauder"
---| "ChaosRepressor"
---| "ChaosRifleman"
---| "ClassD"
---| "CustomRole"
---| "Destroyed"
---| "FacilityGuard"
---| "Filmmaker"
---| "Flamingo"
---| "None"
---| "NtfCaptain"
---| "NtfFlamingo"
---| "NtfPrivate"
---| "NtfSergeant"
---| "NtfSpecialist"
---| "Overwatch"
---| "Scientist"
---| "Scp049"
---| "Scp0492"
---| "Scp079"
---| "Scp096"
---| "Scp106"
---| "Scp173"
---| "Scp3114"
---| "Scp939"
---| "Spectator"
---| "Tutorial"
---| "ZombieFlamingo"
Enums["PlayerRoles.RoleTypeId"] = {
    ["AlphaFlamingo"] = "AlphaFlamingo",
    ["ChaosConscript"] = "ChaosConscript",
    ["ChaosFlamingo"] = "ChaosFlamingo",
    ["ChaosMarauder"] = "ChaosMarauder",
    ["ChaosRepressor"] = "ChaosRepressor",
    ["ChaosRifleman"] = "ChaosRifleman",
    ["ClassD"] = "ClassD",
    ["CustomRole"] = "CustomRole",
    ["Destroyed"] = "Destroyed",
    ["FacilityGuard"] = "FacilityGuard",
    ["Filmmaker"] = "Filmmaker",
    ["Flamingo"] = "Flamingo",
    ["None"] = "None",
    ["NtfCaptain"] = "NtfCaptain",
    ["NtfFlamingo"] = "NtfFlamingo",
    ["NtfPrivate"] = "NtfPrivate",
    ["NtfSergeant"] = "NtfSergeant",
    ["NtfSpecialist"] = "NtfSpecialist",
    ["Overwatch"] = "Overwatch",
    ["Scientist"] = "Scientist",
    ["Scp049"] = "Scp049",
    ["Scp0492"] = "Scp0492",
    ["Scp079"] = "Scp079",
    ["Scp096"] = "Scp096",
    ["Scp106"] = "Scp106",
    ["Scp173"] = "Scp173",
    ["Scp3114"] = "Scp3114",
    ["Scp939"] = "Scp939",
    ["Spectator"] = "Spectator",
    ["Tutorial"] = "Tutorial",
    ["ZombieFlamingo"] = "ZombieFlamingo",
}
Enums.PlayerRoles.RoleTypeId = Enums["PlayerRoles.RoleTypeId"]
Enums.RoleTypeId = Enums["PlayerRoles.RoleTypeId"]
RoleTypeId = Enums["PlayerRoles.RoleTypeId"]

---@alias Enum_PlayerRoles_002e_Spectating_002e_OverwatchSettings
---| "DebugLines"
---| "HitRegisterToggle"
---| "PlayerInfoToggle"
Enums["PlayerRoles.Spectating.OverwatchSettings"] = {
    ["DebugLines"] = "DebugLines",
    ["HitRegisterToggle"] = "HitRegisterToggle",
    ["PlayerInfoToggle"] = "PlayerInfoToggle",
}
Enums.PlayerRoles.Spectating = {}
Enums.PlayerRoles.Spectating.OverwatchSettings = Enums["PlayerRoles.Spectating.OverwatchSettings"]
Enums.OverwatchSettings = Enums["PlayerRoles.Spectating.OverwatchSettings"]
OverwatchSettings = Enums["PlayerRoles.Spectating.OverwatchSettings"]

---@alias Enum_PlayerRoles_002e_Spectating_002e_SpectatableListElementType
---| "Scp079"
---| "Scp3114"
---| "Standard"
Enums["PlayerRoles.Spectating.SpectatableListElementType"] = {
    ["Scp079"] = "Scp079",
    ["Scp3114"] = "Scp3114",
    ["Standard"] = "Standard",
}
Enums.PlayerRoles.Spectating.SpectatableListElementType = Enums["PlayerRoles.Spectating.SpectatableListElementType"]
Enums.SpectatableListElementType = Enums["PlayerRoles.Spectating.SpectatableListElementType"]
SpectatableListElementType = Enums["PlayerRoles.Spectating.SpectatableListElementType"]

---@alias Enum_PlayerRoles_002e_Spectating_002e_SpectatorSpawnReason
---| "KilledByPlayer"
---| "None"
---| "Other"
Enums["PlayerRoles.Spectating.SpectatorSpawnReason"] = {
    ["KilledByPlayer"] = "KilledByPlayer",
    ["None"] = "None",
    ["Other"] = "Other",
}
Enums.PlayerRoles.Spectating.SpectatorSpawnReason = Enums["PlayerRoles.Spectating.SpectatorSpawnReason"]
Enums.SpectatorSpawnReason = Enums["PlayerRoles.Spectating.SpectatorSpawnReason"]
SpectatorSpawnReason = Enums["PlayerRoles.Spectating.SpectatorSpawnReason"]

---@alias Enum_PlayerRoles_002e_Team
---| "ChaosInsurgency"
---| "ClassD"
---| "Dead"
---| "Flamingos"
---| "FoundationForces"
---| "OtherAlive"
---| "SCPs"
---| "Scientists"
Enums["PlayerRoles.Team"] = {
    ["ChaosInsurgency"] = "ChaosInsurgency",
    ["ClassD"] = "ClassD",
    ["Dead"] = "Dead",
    ["Flamingos"] = "Flamingos",
    ["FoundationForces"] = "FoundationForces",
    ["OtherAlive"] = "OtherAlive",
    ["SCPs"] = "SCPs",
    ["Scientists"] = "Scientists",
}
Enums.PlayerRoles.Team = Enums["PlayerRoles.Team"]
Enums.Team = Enums["PlayerRoles.Team"]
Team = Enums["PlayerRoles.Team"]

---@alias Enum_PlayerRoles_002e_Visibility_002e_InvisibilityFlags
---| "None"
---| "OutOfRange"
---| "Scp106Sinkhole"
---| "Scp268"
Enums["PlayerRoles.Visibility.InvisibilityFlags"] = {
    ["None"] = "None",
    ["OutOfRange"] = "OutOfRange",
    ["Scp106Sinkhole"] = "Scp106Sinkhole",
    ["Scp268"] = "Scp268",
}
Enums.PlayerRoles.Visibility = {}
Enums.PlayerRoles.Visibility.InvisibilityFlags = Enums["PlayerRoles.Visibility.InvisibilityFlags"]
Enums.InvisibilityFlags = Enums["PlayerRoles.Visibility.InvisibilityFlags"]
InvisibilityFlags = Enums["PlayerRoles.Visibility.InvisibilityFlags"]

---@alias Enum_PlayerRoles_002e_Voice_002e_GroupMuteFlags
---| "Alive"
---| "Lobby"
---| "None"
---| "Spectators"
---| "Summary"
Enums["PlayerRoles.Voice.GroupMuteFlags"] = {
    ["Alive"] = "Alive",
    ["Lobby"] = "Lobby",
    ["None"] = "None",
    ["Spectators"] = "Spectators",
    ["Summary"] = "Summary",
}
Enums.PlayerRoles.Voice = {}
Enums.PlayerRoles.Voice.GroupMuteFlags = Enums["PlayerRoles.Voice.GroupMuteFlags"]
Enums.GroupMuteFlags = Enums["PlayerRoles.Voice.GroupMuteFlags"]
GroupMuteFlags = Enums["PlayerRoles.Voice.GroupMuteFlags"]

---@alias Enum_PlayerRoles_002e_Voice_002e_IntercomDisplay_002e_IcomText
---| "AdminUsing"
---| "Muted"
---| "Ready"
---| "Restarting"
---| "Transmitting"
---| "TrasmittingBypass"
---| "Unknown"
---| "Wait"
Enums["PlayerRoles.Voice.IntercomDisplay.IcomText"] = {
    ["AdminUsing"] = "AdminUsing",
    ["Muted"] = "Muted",
    ["Ready"] = "Ready",
    ["Restarting"] = "Restarting",
    ["Transmitting"] = "Transmitting",
    ["TrasmittingBypass"] = "TrasmittingBypass",
    ["Unknown"] = "Unknown",
    ["Wait"] = "Wait",
}
Enums.PlayerRoles.Voice.IntercomDisplay = {}
Enums.PlayerRoles.Voice.IntercomDisplay.IcomText = Enums["PlayerRoles.Voice.IntercomDisplay.IcomText"]
Enums.IntercomDisplay = {}
Enums.IntercomDisplay.IcomText = Enums["PlayerRoles.Voice.IntercomDisplay.IcomText"]
Enums["IntercomDisplay.IcomText"] = Enums["PlayerRoles.Voice.IntercomDisplay.IcomText"]
IntercomDisplay = {}
IntercomDisplay.IcomText = Enums["PlayerRoles.Voice.IntercomDisplay.IcomText"]

---@alias Enum_PlayerRoles_002e_Voice_002e_IntercomState
---| "Cooldown"
---| "InUse"
---| "NotFound"
---| "Ready"
---| "Starting"
Enums["PlayerRoles.Voice.IntercomState"] = {
    ["Cooldown"] = "Cooldown",
    ["InUse"] = "InUse",
    ["NotFound"] = "NotFound",
    ["Ready"] = "Ready",
    ["Starting"] = "Starting",
}
Enums.PlayerRoles.Voice.IntercomState = Enums["PlayerRoles.Voice.IntercomState"]
Enums.IntercomState = Enums["PlayerRoles.Voice.IntercomState"]
IntercomState = Enums["PlayerRoles.Voice.IntercomState"]

---@alias Enum_PlayerStatsSystem_002e_AdminFlags
---| "BypassMode"
---| "GodMode"
---| "Noclip"
---| "None"
Enums["PlayerStatsSystem.AdminFlags"] = {
    ["BypassMode"] = "BypassMode",
    ["GodMode"] = "GodMode",
    ["Noclip"] = "Noclip",
    ["None"] = "None",
}
Enums.PlayerStatsSystem = {}
Enums.PlayerStatsSystem.AdminFlags = Enums["PlayerStatsSystem.AdminFlags"]
Enums.AdminFlags = Enums["PlayerStatsSystem.AdminFlags"]
AdminFlags = Enums["PlayerStatsSystem.AdminFlags"]

---@alias Enum_PlayerStatsSystem_002e_DamageHandlerBase_002e_HandlerOutput
---| "Damaged"
---| "Death"
---| "Nothing"
Enums["PlayerStatsSystem.DamageHandlerBase.HandlerOutput"] = {
    ["Damaged"] = "Damaged",
    ["Death"] = "Death",
    ["Nothing"] = "Nothing",
}
Enums.PlayerStatsSystem.DamageHandlerBase = {}
Enums.PlayerStatsSystem.DamageHandlerBase.HandlerOutput = Enums["PlayerStatsSystem.DamageHandlerBase.HandlerOutput"]
Enums.DamageHandlerBase = {}
Enums.DamageHandlerBase.HandlerOutput = Enums["PlayerStatsSystem.DamageHandlerBase.HandlerOutput"]
Enums["DamageHandlerBase.HandlerOutput"] = Enums["PlayerStatsSystem.DamageHandlerBase.HandlerOutput"]
DamageHandlerBase = {}
DamageHandlerBase.HandlerOutput = Enums["PlayerStatsSystem.DamageHandlerBase.HandlerOutput"]

---@alias Enum_PlayerStatsSystem_002e_Scp049DamageHandler_002e_AttackType
---| "CardiacArrest"
---| "Instakill"
---| "Scp0492"
Enums["PlayerStatsSystem.Scp049DamageHandler.AttackType"] = {
    ["CardiacArrest"] = "CardiacArrest",
    ["Instakill"] = "Instakill",
    ["Scp0492"] = "Scp0492",
}
Enums.PlayerStatsSystem.Scp049DamageHandler = {}
Enums.PlayerStatsSystem.Scp049DamageHandler.AttackType = Enums["PlayerStatsSystem.Scp049DamageHandler.AttackType"]
Enums.Scp049DamageHandler = {}
Enums.Scp049DamageHandler.AttackType = Enums["PlayerStatsSystem.Scp049DamageHandler.AttackType"]
Enums["Scp049DamageHandler.AttackType"] = Enums["PlayerStatsSystem.Scp049DamageHandler.AttackType"]
Scp049DamageHandler = {}
Scp049DamageHandler.AttackType = Enums["PlayerStatsSystem.Scp049DamageHandler.AttackType"]

---@alias Enum_PlayerStatsSystem_002e_Scp096DamageHandler_002e_AttackType
---| "Charge"
---| "GateKill"
---| "SlapLeft"
---| "SlapRight"
Enums["PlayerStatsSystem.Scp096DamageHandler.AttackType"] = {
    ["Charge"] = "Charge",
    ["GateKill"] = "GateKill",
    ["SlapLeft"] = "SlapLeft",
    ["SlapRight"] = "SlapRight",
}
Enums.PlayerStatsSystem.Scp096DamageHandler = {}
Enums.PlayerStatsSystem.Scp096DamageHandler.AttackType = Enums["PlayerStatsSystem.Scp096DamageHandler.AttackType"]
Enums.Scp096DamageHandler = {}
Enums.Scp096DamageHandler.AttackType = Enums["PlayerStatsSystem.Scp096DamageHandler.AttackType"]
Enums["Scp096DamageHandler.AttackType"] = Enums["PlayerStatsSystem.Scp096DamageHandler.AttackType"]
Scp096DamageHandler = {}
Scp096DamageHandler.AttackType = Enums["PlayerStatsSystem.Scp096DamageHandler.AttackType"]

---@alias Enum_PlayerStatsSystem_002e_StatSlider_002e_DisplayExactMode
---| "AlwaysExact"
---| "AlwaysPercent"
---| "PreferenceBased"
---| "ValueHidden"
Enums["PlayerStatsSystem.StatSlider.DisplayExactMode"] = {
    ["AlwaysExact"] = "AlwaysExact",
    ["AlwaysPercent"] = "AlwaysPercent",
    ["PreferenceBased"] = "PreferenceBased",
    ["ValueHidden"] = "ValueHidden",
}
Enums.PlayerStatsSystem.StatSlider = {}
Enums.PlayerStatsSystem.StatSlider.DisplayExactMode = Enums["PlayerStatsSystem.StatSlider.DisplayExactMode"]
Enums.StatSlider = {}
Enums.StatSlider.DisplayExactMode = Enums["PlayerStatsSystem.StatSlider.DisplayExactMode"]
Enums["StatSlider.DisplayExactMode"] = Enums["PlayerStatsSystem.StatSlider.DisplayExactMode"]
StatSlider = {}
StatSlider.DisplayExactMode = Enums["PlayerStatsSystem.StatSlider.DisplayExactMode"]

---@alias Enum_PlayerStatsSystem_002e_SyncedStatBase_002e_SyncMode
---| "Private"
---| "PrivateAndSpectators"
---| "Public"
Enums["PlayerStatsSystem.SyncedStatBase.SyncMode"] = {
    ["Private"] = "Private",
    ["PrivateAndSpectators"] = "PrivateAndSpectators",
    ["Public"] = "Public",
}
Enums.PlayerStatsSystem.SyncedStatBase = {}
Enums.PlayerStatsSystem.SyncedStatBase.SyncMode = Enums["PlayerStatsSystem.SyncedStatBase.SyncMode"]
Enums.SyncedStatBase = {}
Enums.SyncedStatBase.SyncMode = Enums["PlayerStatsSystem.SyncedStatBase.SyncMode"]
Enums["SyncedStatBase.SyncMode"] = Enums["PlayerStatsSystem.SyncedStatBase.SyncMode"]
SyncedStatBase = {}
SyncedStatBase.SyncMode = Enums["PlayerStatsSystem.SyncedStatBase.SyncMode"]

---@alias Enum_PlayerStatsSystem_002e_SyncedStatMessages_002e_StatMessageType
---| "CurrentValue"
---| "MaxValue"
Enums["PlayerStatsSystem.SyncedStatMessages.StatMessageType"] = {
    ["CurrentValue"] = "CurrentValue",
    ["MaxValue"] = "MaxValue",
}
Enums.PlayerStatsSystem.SyncedStatMessages = {}
Enums.PlayerStatsSystem.SyncedStatMessages.StatMessageType = Enums["PlayerStatsSystem.SyncedStatMessages.StatMessageType"]
Enums.SyncedStatMessages = {}
Enums.SyncedStatMessages.StatMessageType = Enums["PlayerStatsSystem.SyncedStatMessages.StatMessageType"]
Enums["SyncedStatMessages.StatMessageType"] = Enums["PlayerStatsSystem.SyncedStatMessages.StatMessageType"]
SyncedStatMessages = {}
SyncedStatMessages.StatMessageType = Enums["PlayerStatsSystem.SyncedStatMessages.StatMessageType"]

---@alias Enum_ProgressiveCulling_002e_RootCullablePriority
---| "Dynamic"
---| "PreCull"
---| "Rooms"
Enums["ProgressiveCulling.RootCullablePriority"] = {
    ["Dynamic"] = "Dynamic",
    ["PreCull"] = "PreCull",
    ["Rooms"] = "Rooms",
}
Enums.ProgressiveCulling = {}
Enums.ProgressiveCulling.RootCullablePriority = Enums["ProgressiveCulling.RootCullablePriority"]
Enums.RootCullablePriority = Enums["ProgressiveCulling.RootCullablePriority"]
RootCullablePriority = Enums["ProgressiveCulling.RootCullablePriority"]

---@alias Enum_Query_002e_QueryHandshake_002e_ClientFlags
---| "None"
---| "RemoteAdminMetadata"
---| "RestrictPermissions"
---| "SpecifyLogUsername"
---| "SubscribeServerConsole"
---| "SubscribeServerLogs"
---| "SuppressRaResponses"
Enums["Query.QueryHandshake.ClientFlags"] = {
    ["None"] = "None",
    ["RemoteAdminMetadata"] = "RemoteAdminMetadata",
    ["RestrictPermissions"] = "RestrictPermissions",
    ["SpecifyLogUsername"] = "SpecifyLogUsername",
    ["SubscribeServerConsole"] = "SubscribeServerConsole",
    ["SubscribeServerLogs"] = "SubscribeServerLogs",
    ["SuppressRaResponses"] = "SuppressRaResponses",
}
Enums.Query = {}
Enums.Query.QueryHandshake = {}
Enums.Query.QueryHandshake.ClientFlags = Enums["Query.QueryHandshake.ClientFlags"]
Enums.QueryHandshake = {}
Enums.QueryHandshake.ClientFlags = Enums["Query.QueryHandshake.ClientFlags"]
Enums["QueryHandshake.ClientFlags"] = Enums["Query.QueryHandshake.ClientFlags"]
QueryHandshake = {}
QueryHandshake.ClientFlags = Enums["Query.QueryHandshake.ClientFlags"]

---@alias Enum_Query_002e_QueryMessage_002e_ClientReceivedContentType
---| "CommandException"
---| "ConsoleString"
---| "QueryMessage"
---| "RemoteAdminPlaintextResponse"
---| "RemoteAdminSerializedResponse"
---| "RemoteAdminUnsuccessfulPlaintextResponse"
Enums["Query.QueryMessage.ClientReceivedContentType"] = {
    ["CommandException"] = "CommandException",
    ["ConsoleString"] = "ConsoleString",
    ["QueryMessage"] = "QueryMessage",
    ["RemoteAdminPlaintextResponse"] = "RemoteAdminPlaintextResponse",
    ["RemoteAdminSerializedResponse"] = "RemoteAdminSerializedResponse",
    ["RemoteAdminUnsuccessfulPlaintextResponse"] = "RemoteAdminUnsuccessfulPlaintextResponse",
}
Enums.Query.QueryMessage = {}
Enums.Query.QueryMessage.ClientReceivedContentType = Enums["Query.QueryMessage.ClientReceivedContentType"]
Enums.QueryMessage = {}
Enums.QueryMessage.ClientReceivedContentType = Enums["Query.QueryMessage.ClientReceivedContentType"]
Enums["QueryMessage.ClientReceivedContentType"] = Enums["Query.QueryMessage.ClientReceivedContentType"]
QueryMessage = {}
QueryMessage.ClientReceivedContentType = Enums["Query.QueryMessage.ClientReceivedContentType"]

---@alias Enum_Query_002e_QueryMessage_002e_ServerReceivedContentType
---| "Command"
Enums["Query.QueryMessage.ServerReceivedContentType"] = {
    ["Command"] = "Command",
}
Enums.Query.QueryMessage.ServerReceivedContentType = Enums["Query.QueryMessage.ServerReceivedContentType"]
Enums.QueryMessage.ServerReceivedContentType = Enums["Query.QueryMessage.ServerReceivedContentType"]
Enums["QueryMessage.ServerReceivedContentType"] = Enums["Query.QueryMessage.ServerReceivedContentType"]
QueryMessage.ServerReceivedContentType = Enums["Query.QueryMessage.ServerReceivedContentType"]

---@alias Enum_RemoteAdmin_002e_ColorSlider_002e_ModifiedColorValue
---| "A"
---| "B"
---| "G"
---| "R"
Enums["RemoteAdmin.ColorSlider.ModifiedColorValue"] = {
    ["A"] = "A",
    ["B"] = "B",
    ["G"] = "G",
    ["R"] = "R",
}
Enums.RemoteAdmin = {}
Enums.RemoteAdmin.ColorSlider = {}
Enums.RemoteAdmin.ColorSlider.ModifiedColorValue = Enums["RemoteAdmin.ColorSlider.ModifiedColorValue"]
Enums.ColorSlider = {}
Enums.ColorSlider.ModifiedColorValue = Enums["RemoteAdmin.ColorSlider.ModifiedColorValue"]
Enums["ColorSlider.ModifiedColorValue"] = Enums["RemoteAdmin.ColorSlider.ModifiedColorValue"]
ColorSlider = {}
ColorSlider.ModifiedColorValue = Enums["RemoteAdmin.ColorSlider.ModifiedColorValue"]

---@alias Enum_RemoteAdmin_002e_Communication_002e_RaClipboard_002e_RaClipBoardType
---| "Ip"
---| "PlayerId"
---| "UserId"
Enums["RemoteAdmin.Communication.RaClipboard.RaClipBoardType"] = {
    ["Ip"] = "Ip",
    ["PlayerId"] = "PlayerId",
    ["UserId"] = "UserId",
}
Enums.RemoteAdmin.Communication = {}
Enums.RemoteAdmin.Communication.RaClipboard = {}
Enums.RemoteAdmin.Communication.RaClipboard.RaClipBoardType = Enums["RemoteAdmin.Communication.RaClipboard.RaClipBoardType"]
Enums.RaClipboard = {}
Enums.RaClipboard.RaClipBoardType = Enums["RemoteAdmin.Communication.RaClipboard.RaClipBoardType"]
Enums["RaClipboard.RaClipBoardType"] = Enums["RemoteAdmin.Communication.RaClipboard.RaClipBoardType"]
RaClipboard = {}
RaClipboard.RaClipBoardType = Enums["RemoteAdmin.Communication.RaClipboard.RaClipBoardType"]

---@alias Enum_RemoteAdmin_002e_Communication_002e_RaPlayerList_002e_PlayerSorting
---| "Alphabetical"
---| "Class"
---| "Ids"
---| "Team"
Enums["RemoteAdmin.Communication.RaPlayerList.PlayerSorting"] = {
    ["Alphabetical"] = "Alphabetical",
    ["Class"] = "Class",
    ["Ids"] = "Ids",
    ["Team"] = "Team",
}
Enums.RemoteAdmin.Communication.RaPlayerList = {}
Enums.RemoteAdmin.Communication.RaPlayerList.PlayerSorting = Enums["RemoteAdmin.Communication.RaPlayerList.PlayerSorting"]
Enums.RaPlayerList = {}
Enums.RaPlayerList.PlayerSorting = Enums["RemoteAdmin.Communication.RaPlayerList.PlayerSorting"]
Enums["RaPlayerList.PlayerSorting"] = Enums["RemoteAdmin.Communication.RaPlayerList.PlayerSorting"]
RaPlayerList = {}
RaPlayerList.PlayerSorting = Enums["RemoteAdmin.Communication.RaPlayerList.PlayerSorting"]

---@alias Enum_RemoteAdmin_002e_Menus_002e_DataRequestMenu_002e_RequestType
---| "Auth"
---| "ExternalLookup"
---| "Info"
---| "ShortInfo"
Enums["RemoteAdmin.Menus.DataRequestMenu.RequestType"] = {
    ["Auth"] = "Auth",
    ["ExternalLookup"] = "ExternalLookup",
    ["Info"] = "Info",
    ["ShortInfo"] = "ShortInfo",
}
Enums.RemoteAdmin.Menus = {}
Enums.RemoteAdmin.Menus.DataRequestMenu = {}
Enums.RemoteAdmin.Menus.DataRequestMenu.RequestType = Enums["RemoteAdmin.Menus.DataRequestMenu.RequestType"]
Enums.DataRequestMenu = {}
Enums.DataRequestMenu.RequestType = Enums["RemoteAdmin.Menus.DataRequestMenu.RequestType"]
Enums["DataRequestMenu.RequestType"] = Enums["RemoteAdmin.Menus.DataRequestMenu.RequestType"]
DataRequestMenu = {}
DataRequestMenu.RequestType = Enums["RemoteAdmin.Menus.DataRequestMenu.RequestType"]

---@alias Enum_RemoteAdmin_002e_Menus_002e_RoundMenu_002e_Panel
---| "ServerEvents"
---| "TicketSystem"
Enums["RemoteAdmin.Menus.RoundMenu.Panel"] = {
    ["ServerEvents"] = "ServerEvents",
    ["TicketSystem"] = "TicketSystem",
}
Enums.RemoteAdmin.Menus.RoundMenu = {}
Enums.RemoteAdmin.Menus.RoundMenu.Panel = Enums["RemoteAdmin.Menus.RoundMenu.Panel"]
Enums.RoundMenu = {}
Enums.RoundMenu.Panel = Enums["RemoteAdmin.Menus.RoundMenu.Panel"]
Enums["RoundMenu.Panel"] = Enums["RemoteAdmin.Menus.RoundMenu.Panel"]
RoundMenu = {}
RoundMenu.Panel = Enums["RemoteAdmin.Menus.RoundMenu.Panel"]

---@alias Enum_Respawning_002e_Graphics_002e_RespawnSetting
---| "ObjectiveFeedVisible"
---| "RespawnInterfaceVisible"
Enums["Respawning.Graphics.RespawnSetting"] = {
    ["ObjectiveFeedVisible"] = "ObjectiveFeedVisible",
    ["RespawnInterfaceVisible"] = "RespawnInterfaceVisible",
}
Enums.Respawning = {}
Enums.Respawning.Graphics = {}
Enums.Respawning.Graphics.RespawnSetting = Enums["Respawning.Graphics.RespawnSetting"]
Enums.RespawnSetting = Enums["Respawning.Graphics.RespawnSetting"]
RespawnSetting = Enums["Respawning.Graphics.RespawnSetting"]

---@alias Enum_Respawning_002e_Graphics_002e_RespawnTooltipTranslation
---| "InfluenceTranslation"
---| "RespawnsTokensTranslation"
---| "TimerBarTranslation"
Enums["Respawning.Graphics.RespawnTooltipTranslation"] = {
    ["InfluenceTranslation"] = "InfluenceTranslation",
    ["RespawnsTokensTranslation"] = "RespawnsTokensTranslation",
    ["TimerBarTranslation"] = "TimerBarTranslation",
}
Enums.Respawning.Graphics.RespawnTooltipTranslation = Enums["Respawning.Graphics.RespawnTooltipTranslation"]
Enums.RespawnTooltipTranslation = Enums["Respawning.Graphics.RespawnTooltipTranslation"]
RespawnTooltipTranslation = Enums["Respawning.Graphics.RespawnTooltipTranslation"]

---@alias Enum_Respawning_002e_Objectives_002e_FootprintsTranslation
---| "DamageObjective"
---| "EscapeObjective"
---| "GeneratorObjective"
---| "GenericRewardsDisplay"
---| "InfluenceRewardDisplay"
---| "ItemPickupObjective"
---| "KillObjective"
---| "ObjectiveFeedExample"
---| "TimerRewardDisplay"
Enums["Respawning.Objectives.FootprintsTranslation"] = {
    ["DamageObjective"] = "DamageObjective",
    ["EscapeObjective"] = "EscapeObjective",
    ["GeneratorObjective"] = "GeneratorObjective",
    ["GenericRewardsDisplay"] = "GenericRewardsDisplay",
    ["InfluenceRewardDisplay"] = "InfluenceRewardDisplay",
    ["ItemPickupObjective"] = "ItemPickupObjective",
    ["KillObjective"] = "KillObjective",
    ["ObjectiveFeedExample"] = "ObjectiveFeedExample",
    ["TimerRewardDisplay"] = "TimerRewardDisplay",
}
Enums.Respawning.Objectives = {}
Enums.Respawning.Objectives.FootprintsTranslation = Enums["Respawning.Objectives.FootprintsTranslation"]
Enums.FootprintsTranslation = Enums["Respawning.Objectives.FootprintsTranslation"]
FootprintsTranslation = Enums["Respawning.Objectives.FootprintsTranslation"]

---@alias Enum_Respawning_002e_UpdateMessageFlags
---| "All"
---| "Max"
---| "None"
---| "Pause"
---| "Spawn"
---| "Timer"
---| "Tokens"
---| "Trigger"
Enums["Respawning.UpdateMessageFlags"] = {
    ["All"] = "All",
    ["Max"] = "Max",
    ["None"] = "None",
    ["Pause"] = "Pause",
    ["Spawn"] = "Spawn",
    ["Timer"] = "Timer",
    ["Tokens"] = "Tokens",
    ["Trigger"] = "Trigger",
}
Enums.Respawning.UpdateMessageFlags = Enums["Respawning.UpdateMessageFlags"]
Enums.UpdateMessageFlags = Enums["Respawning.UpdateMessageFlags"]
UpdateMessageFlags = Enums["Respawning.UpdateMessageFlags"]

---@alias Enum_Respawning_002e_WaveQueueState
---| "Idle"
---| "WaveSelected"
---| "WaveSpawned"
---| "WaveSpawning"
Enums["Respawning.WaveQueueState"] = {
    ["Idle"] = "Idle",
    ["WaveSelected"] = "WaveSelected",
    ["WaveSpawned"] = "WaveSpawned",
    ["WaveSpawning"] = "WaveSpawning",
}
Enums.Respawning.WaveQueueState = Enums["Respawning.WaveQueueState"]
Enums.WaveQueueState = Enums["Respawning.WaveQueueState"]
WaveQueueState = Enums["Respawning.WaveQueueState"]

---@alias Enum_RoundRestarting_002e_RoundRestartType
---| "FastRestart"
---| "FullRestart"
---| "RedirectRestart"
Enums["RoundRestarting.RoundRestartType"] = {
    ["FastRestart"] = "FastRestart",
    ["FullRestart"] = "FullRestart",
    ["RedirectRestart"] = "RedirectRestart",
}
Enums.RoundRestarting = {}
Enums.RoundRestarting.RoundRestartType = Enums["RoundRestarting.RoundRestartType"]
Enums.RoundRestartType = Enums["RoundRestarting.RoundRestartType"]
RoundRestartType = Enums["RoundRestarting.RoundRestartType"]

---@alias Enum_Scp914_002e_Scp914InteractCode
---| "Activate"
---| "ChangeMode"
Enums["Scp914.Scp914InteractCode"] = {
    ["Activate"] = "Activate",
    ["ChangeMode"] = "ChangeMode",
}
Enums.Scp914 = {}
Enums.Scp914.Scp914InteractCode = Enums["Scp914.Scp914InteractCode"]
Enums.Scp914InteractCode = Enums["Scp914.Scp914InteractCode"]
Scp914InteractCode = Enums["Scp914.Scp914InteractCode"]

---@alias Enum_Scp914_002e_Scp914KnobSetting
---| "Coarse"
---| "Fine"
---| "OneToOne"
---| "Rough"
---| "VeryFine"
Enums["Scp914.Scp914KnobSetting"] = {
    ["Coarse"] = "Coarse",
    ["Fine"] = "Fine",
    ["OneToOne"] = "OneToOne",
    ["Rough"] = "Rough",
    ["VeryFine"] = "VeryFine",
}
Enums.Scp914.Scp914KnobSetting = Enums["Scp914.Scp914KnobSetting"]
Enums.Scp914KnobSetting = Enums["Scp914.Scp914KnobSetting"]
Scp914KnobSetting = Enums["Scp914.Scp914KnobSetting"]

---@alias Enum_Scp914_002e_Scp914Mode
---| "Dropped"
---| "DroppedAndHeld"
---| "DroppedAndInventory"
---| "DroppedAndPlayerTeleport"
---| "Held"
---| "Inventory"
Enums["Scp914.Scp914Mode"] = {
    ["Dropped"] = "Dropped",
    ["DroppedAndHeld"] = "DroppedAndHeld",
    ["DroppedAndInventory"] = "DroppedAndInventory",
    ["DroppedAndPlayerTeleport"] = "DroppedAndPlayerTeleport",
    ["Held"] = "Held",
    ["Inventory"] = "Inventory",
}
Enums.Scp914.Scp914Mode = Enums["Scp914.Scp914Mode"]
Enums.Scp914Mode = Enums["Scp914.Scp914Mode"]
Scp914Mode = Enums["Scp914.Scp914Mode"]

---@alias Enum_Scp914_002e_Scp914Sound
---| "KnobChange"
---| "Upgrading"
Enums["Scp914.Scp914Sound"] = {
    ["KnobChange"] = "KnobChange",
    ["Upgrading"] = "Upgrading",
}
Enums.Scp914.Scp914Sound = Enums["Scp914.Scp914Sound"]
Enums.Scp914Sound = Enums["Scp914.Scp914Sound"]
Scp914Sound = Enums["Scp914.Scp914Sound"]

---@alias Enum_Security_002e_ServerRateLimit
---| "commands"
---| "playerInteract"
Enums["Security.ServerRateLimit"] = {
    ["commands"] = "commands",
    ["playerInteract"] = "playerInteract",
}
Enums.Security = {}
Enums.Security.ServerRateLimit = Enums["Security.ServerRateLimit"]
Enums.ServerRateLimit = Enums["Security.ServerRateLimit"]
ServerRateLimit = Enums["Security.ServerRateLimit"]

---@alias Enum_ServerOutput_002e_OutputCodes
---| "ExitActionReset"
---| "ExitActionRestart"
---| "ExitActionShutdown"
---| "ExitActionSilentShutdown"
---| "Heartbeat"
---| "IdleEnter"
---| "IdleExit"
---| "RoundRestart"
Enums["ServerOutput.OutputCodes"] = {
    ["ExitActionReset"] = "ExitActionReset",
    ["ExitActionRestart"] = "ExitActionRestart",
    ["ExitActionShutdown"] = "ExitActionShutdown",
    ["ExitActionSilentShutdown"] = "ExitActionSilentShutdown",
    ["Heartbeat"] = "Heartbeat",
    ["IdleEnter"] = "IdleEnter",
    ["IdleExit"] = "IdleExit",
    ["RoundRestart"] = "RoundRestart",
}
Enums.ServerOutput = {}
Enums.ServerOutput.OutputCodes = Enums["ServerOutput.OutputCodes"]
Enums.OutputCodes = Enums["ServerOutput.OutputCodes"]
OutputCodes = Enums["ServerOutput.OutputCodes"]

---@alias Enum_Steam_002e_ServerOperativeSystem
---| "Linux"
---| "Windows"
Enums["Steam.ServerOperativeSystem"] = {
    ["Linux"] = "Linux",
    ["Windows"] = "Windows",
}
Enums.Steam = {}
Enums.Steam.ServerOperativeSystem = Enums["Steam.ServerOperativeSystem"]
Enums.ServerOperativeSystem = Enums["Steam.ServerOperativeSystem"]
ServerOperativeSystem = Enums["Steam.ServerOperativeSystem"]

---@alias Enum_Steam_002e_ServerType
---| "Dedicated"
---| "Listen"
---| "SourceTV"
Enums["Steam.ServerType"] = {
    ["Dedicated"] = "Dedicated",
    ["Listen"] = "Listen",
    ["SourceTV"] = "SourceTV",
}
Enums.Steam.ServerType = Enums["Steam.ServerType"]
Enums.ServerType = Enums["Steam.ServerType"]
ServerType = Enums["Steam.ServerType"]

---@alias Enum_Subtitles_002e_CassieAnnouncementType
---| "Decontamination"
---| "Detonation"
---| "None"
---| "Normal"
Enums["Subtitles.CassieAnnouncementType"] = {
    ["Decontamination"] = "Decontamination",
    ["Detonation"] = "Detonation",
    ["None"] = "None",
    ["Normal"] = "Normal",
}
Enums.Subtitles = {}
Enums.Subtitles.CassieAnnouncementType = Enums["Subtitles.CassieAnnouncementType"]
Enums.CassieAnnouncementType = Enums["Subtitles.CassieAnnouncementType"]
CassieAnnouncementType = Enums["Subtitles.CassieAnnouncementType"]

---@alias Enum_Subtitles_002e_SubtitleType
---| "AllGeneratorsEngaged"
---| "AlphaWarheadCancelled"
---| "AlphaWarheadEngage"
---| "AlphaWarheadResumed"
---| "AwaitContainPlural"
---| "AwaitContainSingle"
---| "ChaosEntrance"
---| "ChaosMiniwaveEntrance"
---| "ContainUnit"
---| "ContainUnitUnknown"
---| "ContainedByChaos"
---| "ContainedByClassD"
---| "ContainedByScientist"
---| "Custom"
---| "DeadMansSwitch"
---| "Decontamination1Minute"
---| "DecontaminationCountdown"
---| "DecontaminationLockdown"
---| "DecontaminationMinutes"
---| "DecontaminationStart"
---| "GeneratorsActivated"
---| "LostInDecontamination"
---| "NTFEntrance"
---| "NTFMiniwaveEntrance"
---| "None"
---| "OperationalMode"
---| "OverchargeIn"
---| "SCP"
---| "TerminatedByMarshmallowMan"
---| "TerminatedBySCP"
---| "TerminatedBySecuritySystem"
---| "TerminatedByWarhead"
---| "TerminationCauseUnspecified"
---| "ThreatRemains"
Enums["Subtitles.SubtitleType"] = {
    ["AllGeneratorsEngaged"] = "AllGeneratorsEngaged",
    ["AlphaWarheadCancelled"] = "AlphaWarheadCancelled",
    ["AlphaWarheadEngage"] = "AlphaWarheadEngage",
    ["AlphaWarheadResumed"] = "AlphaWarheadResumed",
    ["AwaitContainPlural"] = "AwaitContainPlural",
    ["AwaitContainSingle"] = "AwaitContainSingle",
    ["ChaosEntrance"] = "ChaosEntrance",
    ["ChaosMiniwaveEntrance"] = "ChaosMiniwaveEntrance",
    ["ContainUnit"] = "ContainUnit",
    ["ContainUnitUnknown"] = "ContainUnitUnknown",
    ["ContainedByChaos"] = "ContainedByChaos",
    ["ContainedByClassD"] = "ContainedByClassD",
    ["ContainedByScientist"] = "ContainedByScientist",
    ["Custom"] = "Custom",
    ["DeadMansSwitch"] = "DeadMansSwitch",
    ["Decontamination1Minute"] = "Decontamination1Minute",
    ["DecontaminationCountdown"] = "DecontaminationCountdown",
    ["DecontaminationLockdown"] = "DecontaminationLockdown",
    ["DecontaminationMinutes"] = "DecontaminationMinutes",
    ["DecontaminationStart"] = "DecontaminationStart",
    ["GeneratorsActivated"] = "GeneratorsActivated",
    ["LostInDecontamination"] = "LostInDecontamination",
    ["NTFEntrance"] = "NTFEntrance",
    ["NTFMiniwaveEntrance"] = "NTFMiniwaveEntrance",
    ["None"] = "None",
    ["OperationalMode"] = "OperationalMode",
    ["OverchargeIn"] = "OverchargeIn",
    ["SCP"] = "SCP",
    ["TerminatedByMarshmallowMan"] = "TerminatedByMarshmallowMan",
    ["TerminatedBySCP"] = "TerminatedBySCP",
    ["TerminatedBySecuritySystem"] = "TerminatedBySecuritySystem",
    ["TerminatedByWarhead"] = "TerminatedByWarhead",
    ["TerminationCauseUnspecified"] = "TerminationCauseUnspecified",
    ["ThreatRemains"] = "ThreatRemains",
}
Enums.Subtitles.SubtitleType = Enums["Subtitles.SubtitleType"]
Enums.SubtitleType = Enums["Subtitles.SubtitleType"]
SubtitleType = Enums["Subtitles.SubtitleType"]

---@alias Enum_UserSettings_002e_AudioSettings_002e_MixerAudioSettings_002e_VolumeSliderSetting
---| "ChaseThemes"
---| "Master"
---| "MenuMusic"
---| "MenuUI"
---| "Scp127Voice"
---| "Scp3114Voice"
---| "SoundEffects"
---| "SoundEffectsWhileSpectating"
---| "VoiceChat"
Enums["UserSettings.AudioSettings.MixerAudioSettings.VolumeSliderSetting"] = {
    ["ChaseThemes"] = "ChaseThemes",
    ["Master"] = "Master",
    ["MenuMusic"] = "MenuMusic",
    ["MenuUI"] = "MenuUI",
    ["Scp127Voice"] = "Scp127Voice",
    ["Scp3114Voice"] = "Scp3114Voice",
    ["SoundEffects"] = "SoundEffects",
    ["SoundEffectsWhileSpectating"] = "SoundEffectsWhileSpectating",
    ["VoiceChat"] = "VoiceChat",
}
Enums.UserSettings = {}
Enums.UserSettings.AudioSettings = {}
Enums.UserSettings.AudioSettings.MixerAudioSettings = {}
Enums.UserSettings.AudioSettings.MixerAudioSettings.VolumeSliderSetting = Enums["UserSettings.AudioSettings.MixerAudioSettings.VolumeSliderSetting"]
Enums.MixerAudioSettings = {}
Enums.MixerAudioSettings.VolumeSliderSetting = Enums["UserSettings.AudioSettings.MixerAudioSettings.VolumeSliderSetting"]
Enums["MixerAudioSettings.VolumeSliderSetting"] = Enums["UserSettings.AudioSettings.MixerAudioSettings.VolumeSliderSetting"]
MixerAudioSettings = {}
MixerAudioSettings.VolumeSliderSetting = Enums["UserSettings.AudioSettings.MixerAudioSettings.VolumeSliderSetting"]

---@alias Enum_UserSettings_002e_AudioSettings_002e_OtherAudioSetting
---| "NoiseReduction"
---| "ProxVcReverbIntensity"
---| "SpatialAnnouncements"
Enums["UserSettings.AudioSettings.OtherAudioSetting"] = {
    ["NoiseReduction"] = "NoiseReduction",
    ["ProxVcReverbIntensity"] = "ProxVcReverbIntensity",
    ["SpatialAnnouncements"] = "SpatialAnnouncements",
}
Enums.UserSettings.AudioSettings.OtherAudioSetting = Enums["UserSettings.AudioSettings.OtherAudioSetting"]
Enums.OtherAudioSetting = Enums["UserSettings.AudioSettings.OtherAudioSetting"]
OtherAudioSetting = Enums["UserSettings.AudioSettings.OtherAudioSetting"]

---@alias Enum_UserSettings_002e_ControlsSettings_002e_MiscControlsSetting
---| "AdsToggle"
---| "AutomaticSpectatorSwitch"
---| "InventoryToggle"
---| "RightClickToDrop"
---| "Scp079KeybindZoneSwitching"
---| "Scp079MouseLookToggle"
---| "SearchToggle"
---| "SneakToggle"
---| "SprintToggle"
Enums["UserSettings.ControlsSettings.MiscControlsSetting"] = {
    ["AdsToggle"] = "AdsToggle",
    ["AutomaticSpectatorSwitch"] = "AutomaticSpectatorSwitch",
    ["InventoryToggle"] = "InventoryToggle",
    ["RightClickToDrop"] = "RightClickToDrop",
    ["Scp079KeybindZoneSwitching"] = "Scp079KeybindZoneSwitching",
    ["Scp079MouseLookToggle"] = "Scp079MouseLookToggle",
    ["SearchToggle"] = "SearchToggle",
    ["SneakToggle"] = "SneakToggle",
    ["SprintToggle"] = "SprintToggle",
}
Enums.UserSettings.ControlsSettings = {}
Enums.UserSettings.ControlsSettings.MiscControlsSetting = Enums["UserSettings.ControlsSettings.MiscControlsSetting"]
Enums.MiscControlsSetting = Enums["UserSettings.ControlsSettings.MiscControlsSetting"]
MiscControlsSetting = Enums["UserSettings.ControlsSettings.MiscControlsSetting"]

---@alias Enum_UserSettings_002e_ControlsSettings_002e_SensitivitySetting
---| "AdsReductionMultiplier"
---| "Invert"
---| "SensMultiplier"
Enums["UserSettings.ControlsSettings.SensitivitySetting"] = {
    ["AdsReductionMultiplier"] = "AdsReductionMultiplier",
    ["Invert"] = "Invert",
    ["SensMultiplier"] = "SensMultiplier",
}
Enums.UserSettings.ControlsSettings.SensitivitySetting = Enums["UserSettings.ControlsSettings.SensitivitySetting"]
Enums.SensitivitySetting = Enums["UserSettings.ControlsSettings.SensitivitySetting"]
SensitivitySetting = Enums["UserSettings.ControlsSettings.SensitivitySetting"]

---@alias Enum_UserSettings_002e_GUIElements_002e_UserSettingDependency_002e_Dependency_002e_Condition
---| "EqualsTo"
---| "GreaterThan"
---| "LessThan"
Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.Condition"] = {
    ["EqualsTo"] = "EqualsTo",
    ["GreaterThan"] = "GreaterThan",
    ["LessThan"] = "LessThan",
}
Enums.UserSettings.GUIElements = {}
Enums.UserSettings.GUIElements.UserSettingDependency = {}
Enums.UserSettings.GUIElements.UserSettingDependency.Dependency = {}
Enums.UserSettings.GUIElements.UserSettingDependency.Dependency.Condition = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.Condition"]
Enums.UserSettingDependency = {}
Enums.UserSettingDependency.Dependency = {}
Enums.UserSettingDependency.Dependency.Condition = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.Condition"]
Enums["UserSettingDependency.Dependency.Condition"] = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.Condition"]
UserSettingDependency = {}
UserSettingDependency.Dependency = {}
UserSettingDependency.Dependency.Condition = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.Condition"]

---@alias Enum_UserSettings_002e_GUIElements_002e_UserSettingDependency_002e_Dependency_002e_SettingType
---| "Dropdown"
---| "Slider"
---| "Toggle"
Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.SettingType"] = {
    ["Dropdown"] = "Dropdown",
    ["Slider"] = "Slider",
    ["Toggle"] = "Toggle",
}
Enums.UserSettings.GUIElements.UserSettingDependency.Dependency.SettingType = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.SettingType"]
Enums.UserSettingDependency.Dependency.SettingType = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.SettingType"]
Enums["UserSettingDependency.Dependency.SettingType"] = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.SettingType"]
UserSettingDependency.Dependency.SettingType = Enums["UserSettings.GUIElements.UserSettingDependency.Dependency.SettingType"]

---@alias Enum_UserSettings_002e_OtherSettings_002e_MiscPrivacySetting
---| "BadgePreferences"
---| "DoNotTrack"
---| "GlobalBadgeVisibility"
---| "HideSteamProfile"
---| "LocalBadgeVisibility"
---| "RichPresence"
---| "SteamLobbyPrivacy"
Enums["UserSettings.OtherSettings.MiscPrivacySetting"] = {
    ["BadgePreferences"] = "BadgePreferences",
    ["DoNotTrack"] = "DoNotTrack",
    ["GlobalBadgeVisibility"] = "GlobalBadgeVisibility",
    ["HideSteamProfile"] = "HideSteamProfile",
    ["LocalBadgeVisibility"] = "LocalBadgeVisibility",
    ["RichPresence"] = "RichPresence",
    ["SteamLobbyPrivacy"] = "SteamLobbyPrivacy",
}
Enums.UserSettings.OtherSettings = {}
Enums.UserSettings.OtherSettings.MiscPrivacySetting = Enums["UserSettings.OtherSettings.MiscPrivacySetting"]
Enums.MiscPrivacySetting = Enums["UserSettings.OtherSettings.MiscPrivacySetting"]
MiscPrivacySetting = Enums["UserSettings.OtherSettings.MiscPrivacySetting"]

---@alias Enum_UserSettings_002e_OtherSettings_002e_ScpSetting
---| "ScpOptOut"
Enums["UserSettings.OtherSettings.ScpSetting"] = {
    ["ScpOptOut"] = "ScpOptOut",
}
Enums.UserSettings.OtherSettings.ScpSetting = Enums["UserSettings.OtherSettings.ScpSetting"]
Enums.ScpSetting = Enums["UserSettings.OtherSettings.ScpSetting"]
ScpSetting = Enums["UserSettings.OtherSettings.ScpSetting"]

---@alias Enum_UserSettings_002e_OtherSettings_002e_SteamLobbyPrivacy
---| "Friends"
---| "Private"
---| "Public"
Enums["UserSettings.OtherSettings.SteamLobbyPrivacy"] = {
    ["Friends"] = "Friends",
    ["Private"] = "Private",
    ["Public"] = "Public",
}
Enums.UserSettings.OtherSettings.SteamLobbyPrivacy = Enums["UserSettings.OtherSettings.SteamLobbyPrivacy"]
Enums.SteamLobbyPrivacy = Enums["UserSettings.OtherSettings.SteamLobbyPrivacy"]
SteamLobbyPrivacy = Enums["UserSettings.OtherSettings.SteamLobbyPrivacy"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_Examples_002e_SSAbilitiesExample_002e_ExampleId
---| "HealAlly"
---| "SpeedBoostKey"
---| "SpeedBoostToggle"
Enums["UserSettings.ServerSpecific.Examples.SSAbilitiesExample.ExampleId"] = {
    ["HealAlly"] = "HealAlly",
    ["SpeedBoostKey"] = "SpeedBoostKey",
    ["SpeedBoostToggle"] = "SpeedBoostToggle",
}
Enums.UserSettings.ServerSpecific = {}
Enums.UserSettings.ServerSpecific.Examples = {}
Enums.UserSettings.ServerSpecific.Examples.SSAbilitiesExample = {}
Enums.UserSettings.ServerSpecific.Examples.SSAbilitiesExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSAbilitiesExample.ExampleId"]
Enums.SSAbilitiesExample = {}
Enums.SSAbilitiesExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSAbilitiesExample.ExampleId"]
Enums["SSAbilitiesExample.ExampleId"] = Enums["UserSettings.ServerSpecific.Examples.SSAbilitiesExample.ExampleId"]
SSAbilitiesExample = {}
SSAbilitiesExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSAbilitiesExample.ExampleId"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_Examples_002e_SSLightSpawnerExample_002e_ExampleId
---| "ColorInfo"
---| "ColorPresetDropdown"
---| "ConfirmButton"
---| "CustomColor"
---| "DestroyAllButton"
---| "InnerSpotAngle"
---| "IntensitySlider"
---| "LightShape"
---| "LightType"
---| "RangeSlider"
---| "ShadowStrength"
---| "ShadowType"
---| "SpotAngle"
Enums["UserSettings.ServerSpecific.Examples.SSLightSpawnerExample.ExampleId"] = {
    ["ColorInfo"] = "ColorInfo",
    ["ColorPresetDropdown"] = "ColorPresetDropdown",
    ["ConfirmButton"] = "ConfirmButton",
    ["CustomColor"] = "CustomColor",
    ["DestroyAllButton"] = "DestroyAllButton",
    ["InnerSpotAngle"] = "InnerSpotAngle",
    ["IntensitySlider"] = "IntensitySlider",
    ["LightShape"] = "LightShape",
    ["LightType"] = "LightType",
    ["RangeSlider"] = "RangeSlider",
    ["ShadowStrength"] = "ShadowStrength",
    ["ShadowType"] = "ShadowType",
    ["SpotAngle"] = "SpotAngle",
}
Enums.UserSettings.ServerSpecific.Examples.SSLightSpawnerExample = {}
Enums.UserSettings.ServerSpecific.Examples.SSLightSpawnerExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSLightSpawnerExample.ExampleId"]
Enums.SSLightSpawnerExample = {}
Enums.SSLightSpawnerExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSLightSpawnerExample.ExampleId"]
Enums["SSLightSpawnerExample.ExampleId"] = Enums["UserSettings.ServerSpecific.Examples.SSLightSpawnerExample.ExampleId"]
SSLightSpawnerExample = {}
SSLightSpawnerExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSLightSpawnerExample.ExampleId"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_Examples_002e_SSPrimitiveSpawnerExample_002e_ExampleId
---| "CollisionsToggle"
---| "ColorAlphaSlider"
---| "ColorField"
---| "ColorInfo"
---| "ColorPresetDropdown"
---| "ConfirmButton"
---| "DestroyAllButton"
---| "RendererToggle"
---| "ScaleSliderX"
---| "ScaleSliderY"
---| "ScaleSliderZ"
---| "TypeDropdown"
Enums["UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample.ExampleId"] = {
    ["CollisionsToggle"] = "CollisionsToggle",
    ["ColorAlphaSlider"] = "ColorAlphaSlider",
    ["ColorField"] = "ColorField",
    ["ColorInfo"] = "ColorInfo",
    ["ColorPresetDropdown"] = "ColorPresetDropdown",
    ["ConfirmButton"] = "ConfirmButton",
    ["DestroyAllButton"] = "DestroyAllButton",
    ["RendererToggle"] = "RendererToggle",
    ["ScaleSliderX"] = "ScaleSliderX",
    ["ScaleSliderY"] = "ScaleSliderY",
    ["ScaleSliderZ"] = "ScaleSliderZ",
    ["TypeDropdown"] = "TypeDropdown",
}
Enums.UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample = {}
Enums.UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample.ExampleId"]
Enums.SSPrimitiveSpawnerExample = {}
Enums.SSPrimitiveSpawnerExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample.ExampleId"]
Enums["SSPrimitiveSpawnerExample.ExampleId"] = Enums["UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample.ExampleId"]
SSPrimitiveSpawnerExample = {}
SSPrimitiveSpawnerExample.ExampleId = Enums["UserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample.ExampleId"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_SSDropdownSetting_002e_DropdownEntryType
---| "Hybrid"
---| "HybridLoop"
---| "Regular"
---| "Scrollable"
---| "ScrollableLoop"
Enums["UserSettings.ServerSpecific.SSDropdownSetting.DropdownEntryType"] = {
    ["Hybrid"] = "Hybrid",
    ["HybridLoop"] = "HybridLoop",
    ["Regular"] = "Regular",
    ["Scrollable"] = "Scrollable",
    ["ScrollableLoop"] = "ScrollableLoop",
}
Enums.UserSettings.ServerSpecific.SSDropdownSetting = {}
Enums.UserSettings.ServerSpecific.SSDropdownSetting.DropdownEntryType = Enums["UserSettings.ServerSpecific.SSDropdownSetting.DropdownEntryType"]
Enums.SSDropdownSetting = {}
Enums.SSDropdownSetting.DropdownEntryType = Enums["UserSettings.ServerSpecific.SSDropdownSetting.DropdownEntryType"]
Enums["SSDropdownSetting.DropdownEntryType"] = Enums["UserSettings.ServerSpecific.SSDropdownSetting.DropdownEntryType"]
SSDropdownSetting = {}
SSDropdownSetting.DropdownEntryType = Enums["UserSettings.ServerSpecific.SSDropdownSetting.DropdownEntryType"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_SSDropdownSetting_002e_UpdateType
---| "None"
---| "Options"
---| "Selection"
Enums["UserSettings.ServerSpecific.SSDropdownSetting.UpdateType"] = {
    ["None"] = "None",
    ["Options"] = "Options",
    ["Selection"] = "Selection",
}
Enums.UserSettings.ServerSpecific.SSDropdownSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSDropdownSetting.UpdateType"]
Enums.SSDropdownSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSDropdownSetting.UpdateType"]
Enums["SSDropdownSetting.UpdateType"] = Enums["UserSettings.ServerSpecific.SSDropdownSetting.UpdateType"]
SSDropdownSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSDropdownSetting.UpdateType"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_SSPlaintextSetting_002e_UpdateType
---| "Clear"
---| "None"
---| "Settings"
---| "Text"
Enums["UserSettings.ServerSpecific.SSPlaintextSetting.UpdateType"] = {
    ["Clear"] = "Clear",
    ["None"] = "None",
    ["Settings"] = "Settings",
    ["Text"] = "Text",
}
Enums.UserSettings.ServerSpecific.SSPlaintextSetting = {}
Enums.UserSettings.ServerSpecific.SSPlaintextSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSPlaintextSetting.UpdateType"]
Enums.SSPlaintextSetting = {}
Enums.SSPlaintextSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSPlaintextSetting.UpdateType"]
Enums["SSPlaintextSetting.UpdateType"] = Enums["UserSettings.ServerSpecific.SSPlaintextSetting.UpdateType"]
SSPlaintextSetting = {}
SSPlaintextSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSPlaintextSetting.UpdateType"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_SSSliderSetting_002e_UpdateType
---| "Constraints"
---| "None"
---| "Value"
Enums["UserSettings.ServerSpecific.SSSliderSetting.UpdateType"] = {
    ["Constraints"] = "Constraints",
    ["None"] = "None",
    ["Value"] = "Value",
}
Enums.UserSettings.ServerSpecific.SSSliderSetting = {}
Enums.UserSettings.ServerSpecific.SSSliderSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSSliderSetting.UpdateType"]
Enums.SSSliderSetting = {}
Enums.SSSliderSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSSliderSetting.UpdateType"]
Enums["SSSliderSetting.UpdateType"] = Enums["UserSettings.ServerSpecific.SSSliderSetting.UpdateType"]
SSSliderSetting = {}
SSSliderSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSSliderSetting.UpdateType"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_SSTextArea_002e_FoldoutMode
---| "CollapseOnEntry"
---| "CollapsedByDefault"
---| "ExtendOnEntry"
---| "ExtendedByDefault"
---| "NotCollapsable"
Enums["UserSettings.ServerSpecific.SSTextArea.FoldoutMode"] = {
    ["CollapseOnEntry"] = "CollapseOnEntry",
    ["CollapsedByDefault"] = "CollapsedByDefault",
    ["ExtendOnEntry"] = "ExtendOnEntry",
    ["ExtendedByDefault"] = "ExtendedByDefault",
    ["NotCollapsable"] = "NotCollapsable",
}
Enums.UserSettings.ServerSpecific.SSTextArea = {}
Enums.UserSettings.ServerSpecific.SSTextArea.FoldoutMode = Enums["UserSettings.ServerSpecific.SSTextArea.FoldoutMode"]
Enums.SSTextArea = {}
Enums.SSTextArea.FoldoutMode = Enums["UserSettings.ServerSpecific.SSTextArea.FoldoutMode"]
Enums["SSTextArea.FoldoutMode"] = Enums["UserSettings.ServerSpecific.SSTextArea.FoldoutMode"]
SSTextArea = {}
SSTextArea.FoldoutMode = Enums["UserSettings.ServerSpecific.SSTextArea.FoldoutMode"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_SSTwoButtonsSetting_002e_UpdateType
---| "None"
---| "Options"
---| "Value"
Enums["UserSettings.ServerSpecific.SSTwoButtonsSetting.UpdateType"] = {
    ["None"] = "None",
    ["Options"] = "Options",
    ["Value"] = "Value",
}
Enums.UserSettings.ServerSpecific.SSTwoButtonsSetting = {}
Enums.UserSettings.ServerSpecific.SSTwoButtonsSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSTwoButtonsSetting.UpdateType"]
Enums.SSTwoButtonsSetting = {}
Enums.SSTwoButtonsSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSTwoButtonsSetting.UpdateType"]
Enums["SSTwoButtonsSetting.UpdateType"] = Enums["UserSettings.ServerSpecific.SSTwoButtonsSetting.UpdateType"]
SSTwoButtonsSetting = {}
SSTwoButtonsSetting.UpdateType = Enums["UserSettings.ServerSpecific.SSTwoButtonsSetting.UpdateType"]

---@alias Enum_UserSettings_002e_ServerSpecific_002e_ServerSpecificSettingBase_002e_UserResponseMode
---| "AcquisitionAndChange"
---| "ChangeOnly"
---| "None"
Enums["UserSettings.ServerSpecific.ServerSpecificSettingBase.UserResponseMode"] = {
    ["AcquisitionAndChange"] = "AcquisitionAndChange",
    ["ChangeOnly"] = "ChangeOnly",
    ["None"] = "None",
}
Enums.UserSettings.ServerSpecific.ServerSpecificSettingBase = {}
Enums.UserSettings.ServerSpecific.ServerSpecificSettingBase.UserResponseMode = Enums["UserSettings.ServerSpecific.ServerSpecificSettingBase.UserResponseMode"]
Enums.ServerSpecificSettingBase = {}
Enums.ServerSpecificSettingBase.UserResponseMode = Enums["UserSettings.ServerSpecific.ServerSpecificSettingBase.UserResponseMode"]
Enums["ServerSpecificSettingBase.UserResponseMode"] = Enums["UserSettings.ServerSpecific.ServerSpecificSettingBase.UserResponseMode"]
ServerSpecificSettingBase = {}
ServerSpecificSettingBase.UserResponseMode = Enums["UserSettings.ServerSpecific.ServerSpecificSettingBase.UserResponseMode"]

---@alias Enum_UserSettings_002e_ToggleOrHoldInput_002e_InputActivationMode
---| "Hold"
---| "Toggle"
Enums["UserSettings.ToggleOrHoldInput.InputActivationMode"] = {
    ["Hold"] = "Hold",
    ["Toggle"] = "Toggle",
}
Enums.UserSettings.ToggleOrHoldInput = {}
Enums.UserSettings.ToggleOrHoldInput.InputActivationMode = Enums["UserSettings.ToggleOrHoldInput.InputActivationMode"]
Enums.ToggleOrHoldInput = {}
Enums.ToggleOrHoldInput.InputActivationMode = Enums["UserSettings.ToggleOrHoldInput.InputActivationMode"]
Enums["ToggleOrHoldInput.InputActivationMode"] = Enums["UserSettings.ToggleOrHoldInput.InputActivationMode"]
ToggleOrHoldInput = {}
ToggleOrHoldInput.InputActivationMode = Enums["UserSettings.ToggleOrHoldInput.InputActivationMode"]

---@alias Enum_UserSettings_002e_UserInterfaceSettings_002e_UISetting
---| "EnableSubtitles"
---| "FastRoleIntroMode"
---| "FlashTaskbar"
---| "HealthbarMode"
---| "HideIP"
---| "ImperialUnits"
---| "InaccuracyAsDispersion"
---| "Language"
---| "PhotosensitivityDoNotShow"
Enums["UserSettings.UserInterfaceSettings.UISetting"] = {
    ["EnableSubtitles"] = "EnableSubtitles",
    ["FastRoleIntroMode"] = "FastRoleIntroMode",
    ["FlashTaskbar"] = "FlashTaskbar",
    ["HealthbarMode"] = "HealthbarMode",
    ["HideIP"] = "HideIP",
    ["ImperialUnits"] = "ImperialUnits",
    ["InaccuracyAsDispersion"] = "InaccuracyAsDispersion",
    ["Language"] = "Language",
    ["PhotosensitivityDoNotShow"] = "PhotosensitivityDoNotShow",
}
Enums.UserSettings.UserInterfaceSettings = {}
Enums.UserSettings.UserInterfaceSettings.UISetting = Enums["UserSettings.UserInterfaceSettings.UISetting"]
Enums.UISetting = Enums["UserSettings.UserInterfaceSettings.UISetting"]
UISetting = Enums["UserSettings.UserInterfaceSettings.UISetting"]

---@alias Enum_UserSettings_002e_VideoSettings_002e_DisplayVideoSetting
---| "AspectRatio"
---| "FpsLimiter"
---| "FullscreenMode"
---| "GraphicsAPI"
---| "Resolution"
---| "VSyncCount"
Enums["UserSettings.VideoSettings.DisplayVideoSetting"] = {
    ["AspectRatio"] = "AspectRatio",
    ["FpsLimiter"] = "FpsLimiter",
    ["FullscreenMode"] = "FullscreenMode",
    ["GraphicsAPI"] = "GraphicsAPI",
    ["Resolution"] = "Resolution",
    ["VSyncCount"] = "VSyncCount",
}
Enums.UserSettings.VideoSettings = {}
Enums.UserSettings.VideoSettings.DisplayVideoSetting = Enums["UserSettings.VideoSettings.DisplayVideoSetting"]
Enums.DisplayVideoSetting = Enums["UserSettings.VideoSettings.DisplayVideoSetting"]
DisplayVideoSetting = Enums["UserSettings.VideoSettings.DisplayVideoSetting"]

---@alias Enum_UserSettings_002e_VideoSettings_002e_LightingVideoSetting
---| "RenderLights"
---| "RenderShadows"
---| "ShadowResolution"
Enums["UserSettings.VideoSettings.LightingVideoSetting"] = {
    ["RenderLights"] = "RenderLights",
    ["RenderShadows"] = "RenderShadows",
    ["ShadowResolution"] = "ShadowResolution",
}
Enums.UserSettings.VideoSettings.LightingVideoSetting = Enums["UserSettings.VideoSettings.LightingVideoSetting"]
Enums.LightingVideoSetting = Enums["UserSettings.VideoSettings.LightingVideoSetting"]
LightingVideoSetting = Enums["UserSettings.VideoSettings.LightingVideoSetting"]

---@alias Enum_UserSettings_002e_VideoSettings_002e_MiscVideoSetting
---| "BrightnessBoost"
---| "ExplosionShake"
---| "FlashbangDarkMode"
---| "HeadBobbing"
---| "Scp939VisionBlur"
---| "ShowNeedles"
Enums["UserSettings.VideoSettings.MiscVideoSetting"] = {
    ["BrightnessBoost"] = "BrightnessBoost",
    ["ExplosionShake"] = "ExplosionShake",
    ["FlashbangDarkMode"] = "FlashbangDarkMode",
    ["HeadBobbing"] = "HeadBobbing",
    ["Scp939VisionBlur"] = "Scp939VisionBlur",
    ["ShowNeedles"] = "ShowNeedles",
}
Enums.UserSettings.VideoSettings.MiscVideoSetting = Enums["UserSettings.VideoSettings.MiscVideoSetting"]
Enums.MiscVideoSetting = Enums["UserSettings.VideoSettings.MiscVideoSetting"]
MiscVideoSetting = Enums["UserSettings.VideoSettings.MiscVideoSetting"]

---@alias Enum_UserSettings_002e_VideoSettings_002e_PerformanceVideoSetting
---| "AOQuality"
---| "AnimationQuality"
---| "AntiAliasingQuality"
---| "AntiAliasingType"
---| "BloodDecalsEnabled"
---| "BloodDecalsLimit"
---| "BloomQuality"
---| "BulletDecalsEnabled"
---| "BulletDecalsLimits"
---| "RagdollFreeze"
---| "TextureResolution"
Enums["UserSettings.VideoSettings.PerformanceVideoSetting"] = {
    ["AOQuality"] = "AOQuality",
    ["AnimationQuality"] = "AnimationQuality",
    ["AntiAliasingQuality"] = "AntiAliasingQuality",
    ["AntiAliasingType"] = "AntiAliasingType",
    ["BloodDecalsEnabled"] = "BloodDecalsEnabled",
    ["BloodDecalsLimit"] = "BloodDecalsLimit",
    ["BloomQuality"] = "BloomQuality",
    ["BulletDecalsEnabled"] = "BulletDecalsEnabled",
    ["BulletDecalsLimits"] = "BulletDecalsLimits",
    ["RagdollFreeze"] = "RagdollFreeze",
    ["TextureResolution"] = "TextureResolution",
}
Enums.UserSettings.VideoSettings.PerformanceVideoSetting = Enums["UserSettings.VideoSettings.PerformanceVideoSetting"]
Enums.PerformanceVideoSetting = Enums["UserSettings.VideoSettings.PerformanceVideoSetting"]
PerformanceVideoSetting = Enums["UserSettings.VideoSettings.PerformanceVideoSetting"]

---@alias Enum_Utf8Json_002e_CollectionDeserializeToBehaviour
---| "Add"
---| "OverwriteReplace"
Enums["Utf8Json.CollectionDeserializeToBehaviour"] = {
    ["Add"] = "Add",
    ["OverwriteReplace"] = "OverwriteReplace",
}
Enums.Utf8Json = {}
Enums.Utf8Json.CollectionDeserializeToBehaviour = Enums["Utf8Json.CollectionDeserializeToBehaviour"]
Enums.CollectionDeserializeToBehaviour = Enums["Utf8Json.CollectionDeserializeToBehaviour"]
CollectionDeserializeToBehaviour = Enums["Utf8Json.CollectionDeserializeToBehaviour"]

---@alias Enum_Utf8Json_002e_Internal_002e_DoubleConversion_002e_DoubleToStringConverter_002e_DtoaMode
---| "SHORTEST"
---| "SHORTEST_SINGLE"
Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.DtoaMode"] = {
    ["SHORTEST"] = "SHORTEST",
    ["SHORTEST_SINGLE"] = "SHORTEST_SINGLE",
}
Enums.Utf8Json.Internal = {}
Enums.Utf8Json.Internal.DoubleConversion = {}
Enums.Utf8Json.Internal.DoubleConversion.DoubleToStringConverter = {}
Enums.Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.DtoaMode = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.DtoaMode"]
Enums.DoubleToStringConverter = {}
Enums.DoubleToStringConverter.DtoaMode = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.DtoaMode"]
Enums["DoubleToStringConverter.DtoaMode"] = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.DtoaMode"]
DoubleToStringConverter = {}
DoubleToStringConverter.DtoaMode = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.DtoaMode"]

---@alias Enum_Utf8Json_002e_Internal_002e_DoubleConversion_002e_DoubleToStringConverter_002e_FastDtoaMode
---| "FAST_DTOA_SHORTEST"
---| "FAST_DTOA_SHORTEST_SINGLE"
Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.FastDtoaMode"] = {
    ["FAST_DTOA_SHORTEST"] = "FAST_DTOA_SHORTEST",
    ["FAST_DTOA_SHORTEST_SINGLE"] = "FAST_DTOA_SHORTEST_SINGLE",
}
Enums.Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.FastDtoaMode = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.FastDtoaMode"]
Enums.DoubleToStringConverter.FastDtoaMode = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.FastDtoaMode"]
Enums["DoubleToStringConverter.FastDtoaMode"] = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.FastDtoaMode"]
DoubleToStringConverter.FastDtoaMode = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.FastDtoaMode"]

---@alias Enum_Utf8Json_002e_Internal_002e_DoubleConversion_002e_DoubleToStringConverter_002e_Flags
---| "EMIT_POSITIVE_EXPONENT_SIGN"
---| "EMIT_TRAILING_DECIMAL_POINT"
---| "EMIT_TRAILING_ZERO_AFTER_POINT"
---| "NO_FLAGS"
---| "UNIQUE_ZERO"
Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.Flags"] = {
    ["EMIT_POSITIVE_EXPONENT_SIGN"] = "EMIT_POSITIVE_EXPONENT_SIGN",
    ["EMIT_TRAILING_DECIMAL_POINT"] = "EMIT_TRAILING_DECIMAL_POINT",
    ["EMIT_TRAILING_ZERO_AFTER_POINT"] = "EMIT_TRAILING_ZERO_AFTER_POINT",
    ["NO_FLAGS"] = "NO_FLAGS",
    ["UNIQUE_ZERO"] = "UNIQUE_ZERO",
}
Enums.Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.Flags = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.Flags"]
Enums.DoubleToStringConverter.Flags = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.Flags"]
Enums["DoubleToStringConverter.Flags"] = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.Flags"]
DoubleToStringConverter.Flags = Enums["Utf8Json.Internal.DoubleConversion.DoubleToStringConverter.Flags"]

---@alias Enum_Utf8Json_002e_Internal_002e_DoubleConversion_002e_StringToDoubleConverter_002e_Flags
---| "ALLOW_CASE_INSENSIBILITY"
---| "ALLOW_HEX"
---| "ALLOW_LEADING_SPACES"
---| "ALLOW_OCTALS"
---| "ALLOW_SPACES_AFTER_SIGN"
---| "ALLOW_TRAILING_JUNK"
---| "ALLOW_TRAILING_SPACES"
---| "NO_FLAGS"
Enums["Utf8Json.Internal.DoubleConversion.StringToDoubleConverter.Flags"] = {
    ["ALLOW_CASE_INSENSIBILITY"] = "ALLOW_CASE_INSENSIBILITY",
    ["ALLOW_HEX"] = "ALLOW_HEX",
    ["ALLOW_LEADING_SPACES"] = "ALLOW_LEADING_SPACES",
    ["ALLOW_OCTALS"] = "ALLOW_OCTALS",
    ["ALLOW_SPACES_AFTER_SIGN"] = "ALLOW_SPACES_AFTER_SIGN",
    ["ALLOW_TRAILING_JUNK"] = "ALLOW_TRAILING_JUNK",
    ["ALLOW_TRAILING_SPACES"] = "ALLOW_TRAILING_SPACES",
    ["NO_FLAGS"] = "NO_FLAGS",
}
Enums.Utf8Json.Internal.DoubleConversion.StringToDoubleConverter = {}
Enums.Utf8Json.Internal.DoubleConversion.StringToDoubleConverter.Flags = Enums["Utf8Json.Internal.DoubleConversion.StringToDoubleConverter.Flags"]
Enums.StringToDoubleConverter = {}
Enums.StringToDoubleConverter.Flags = Enums["Utf8Json.Internal.DoubleConversion.StringToDoubleConverter.Flags"]
Enums["StringToDoubleConverter.Flags"] = Enums["Utf8Json.Internal.DoubleConversion.StringToDoubleConverter.Flags"]
StringToDoubleConverter = {}
StringToDoubleConverter.Flags = Enums["Utf8Json.Internal.DoubleConversion.StringToDoubleConverter.Flags"]

---@alias Enum_Utf8Json_002e_JsonToken
---| "BeginArray"
---| "BeginObject"
---| "EndArray"
---| "EndObject"
---| "False"
---| "NameSeparator"
---| "None"
---| "Null"
---| "Number"
---| "String"
---| "True"
---| "ValueSeparator"
Enums["Utf8Json.JsonToken"] = {
    ["BeginArray"] = "BeginArray",
    ["BeginObject"] = "BeginObject",
    ["EndArray"] = "EndArray",
    ["EndObject"] = "EndObject",
    ["False"] = "False",
    ["NameSeparator"] = "NameSeparator",
    ["None"] = "None",
    ["Null"] = "Null",
    ["Number"] = "Number",
    ["String"] = "String",
    ["True"] = "True",
    ["ValueSeparator"] = "ValueSeparator",
}
Enums.Utf8Json.JsonToken = Enums["Utf8Json.JsonToken"]
Enums.JsonToken = Enums["Utf8Json.JsonToken"]
JsonToken = Enums["Utf8Json.JsonToken"]

---@alias Enum_Utils_002e_CustomParser_002e_ParseResult
---| "Failed"
---| "FullSuccess"
---| "PartialSuccess"
Enums["Utils.CustomParser.ParseResult"] = {
    ["Failed"] = "Failed",
    ["FullSuccess"] = "FullSuccess",
    ["PartialSuccess"] = "PartialSuccess",
}
Enums.Utils = {}
Enums.Utils.CustomParser = {}
Enums.Utils.CustomParser.ParseResult = Enums["Utils.CustomParser.ParseResult"]
Enums.CustomParser = {}
Enums.CustomParser.ParseResult = Enums["Utils.CustomParser.ParseResult"]
Enums["CustomParser.ParseResult"] = Enums["Utils.CustomParser.ParseResult"]
CustomParser = {}
CustomParser.ParseResult = Enums["Utils.CustomParser.ParseResult"]

---@alias Enum_Utils_002e_Networking_002e_HintEffectReaderWriter_002e_HintEffectType
---| "Alpha"
---| "AlphaCurve"
---| "Outline"
Enums["Utils.Networking.HintEffectReaderWriter.HintEffectType"] = {
    ["Alpha"] = "Alpha",
    ["AlphaCurve"] = "AlphaCurve",
    ["Outline"] = "Outline",
}
Enums.Utils.Networking = {}
Enums.Utils.Networking.HintEffectReaderWriter = {}
Enums.Utils.Networking.HintEffectReaderWriter.HintEffectType = Enums["Utils.Networking.HintEffectReaderWriter.HintEffectType"]
Enums.HintEffectReaderWriter = {}
Enums.HintEffectReaderWriter.HintEffectType = Enums["Utils.Networking.HintEffectReaderWriter.HintEffectType"]
Enums["HintEffectReaderWriter.HintEffectType"] = Enums["Utils.Networking.HintEffectReaderWriter.HintEffectType"]
HintEffectReaderWriter = {}
HintEffectReaderWriter.HintEffectType = Enums["Utils.Networking.HintEffectReaderWriter.HintEffectType"]

---@alias Enum_Utils_002e_Networking_002e_HintParameterReaderWriter_002e_HintParameterType
---| "Ammo"
---| "AnimationCurve"
---| "Byte"
---| "Double"
---| "Float"
---| "Int"
---| "Item"
---| "ItemCategory"
---| "Long"
---| "PackedLong"
---| "PackedULong"
---| "SByte"
---| "SSKeybind"
---| "Scp330Hint"
---| "Short"
---| "Text"
---| "Timespan"
---| "UInt"
---| "ULong"
---| "UShort"
Enums["Utils.Networking.HintParameterReaderWriter.HintParameterType"] = {
    ["Ammo"] = "Ammo",
    ["AnimationCurve"] = "AnimationCurve",
    ["Byte"] = "Byte",
    ["Double"] = "Double",
    ["Float"] = "Float",
    ["Int"] = "Int",
    ["Item"] = "Item",
    ["ItemCategory"] = "ItemCategory",
    ["Long"] = "Long",
    ["PackedLong"] = "PackedLong",
    ["PackedULong"] = "PackedULong",
    ["SByte"] = "SByte",
    ["SSKeybind"] = "SSKeybind",
    ["Scp330Hint"] = "Scp330Hint",
    ["Short"] = "Short",
    ["Text"] = "Text",
    ["Timespan"] = "Timespan",
    ["UInt"] = "UInt",
    ["ULong"] = "ULong",
    ["UShort"] = "UShort",
}
Enums.Utils.Networking.HintParameterReaderWriter = {}
Enums.Utils.Networking.HintParameterReaderWriter.HintParameterType = Enums["Utils.Networking.HintParameterReaderWriter.HintParameterType"]
Enums.HintParameterReaderWriter = {}
Enums.HintParameterReaderWriter.HintParameterType = Enums["Utils.Networking.HintParameterReaderWriter.HintParameterType"]
Enums["HintParameterReaderWriter.HintParameterType"] = Enums["Utils.Networking.HintParameterReaderWriter.HintParameterType"]
HintParameterReaderWriter = {}
HintParameterReaderWriter.HintParameterType = Enums["Utils.Networking.HintParameterReaderWriter.HintParameterType"]

---@alias Enum_Utils_002e_Networking_002e_HintReaderWriter_002e_HintType
---| "Text"
---| "Translation"
Enums["Utils.Networking.HintReaderWriter.HintType"] = {
    ["Text"] = "Text",
    ["Translation"] = "Translation",
}
Enums.Utils.Networking.HintReaderWriter = {}
Enums.Utils.Networking.HintReaderWriter.HintType = Enums["Utils.Networking.HintReaderWriter.HintType"]
Enums.HintReaderWriter = {}
Enums.HintReaderWriter.HintType = Enums["Utils.Networking.HintReaderWriter.HintType"]
Enums["HintReaderWriter.HintType"] = Enums["Utils.Networking.HintReaderWriter.HintType"]
HintReaderWriter = {}
HintReaderWriter.HintType = Enums["Utils.Networking.HintReaderWriter.HintType"]

---@alias Enum_Utils_002e_Networking_002e_NullableBoolReaderWriter_002e_NullableBoolValue
---| "False"
---| "Null"
---| "True"
Enums["Utils.Networking.NullableBoolReaderWriter.NullableBoolValue"] = {
    ["False"] = "False",
    ["Null"] = "Null",
    ["True"] = "True",
}
Enums.Utils.Networking.NullableBoolReaderWriter = {}
Enums.Utils.Networking.NullableBoolReaderWriter.NullableBoolValue = Enums["Utils.Networking.NullableBoolReaderWriter.NullableBoolValue"]
Enums.NullableBoolReaderWriter = {}
Enums.NullableBoolReaderWriter.NullableBoolValue = Enums["Utils.Networking.NullableBoolReaderWriter.NullableBoolValue"]
Enums["NullableBoolReaderWriter.NullableBoolValue"] = Enums["Utils.Networking.NullableBoolReaderWriter.NullableBoolValue"]
NullableBoolReaderWriter = {}
NullableBoolReaderWriter.NullableBoolValue = Enums["Utils.Networking.NullableBoolReaderWriter.NullableBoolValue"]

---@alias Enum_VoiceChat_002e_Codec_002e_Enums_002e_OpusApplicationType
---| "Audio"
---| "RestrictedLowDelay"
---| "Voip"
Enums["VoiceChat.Codec.Enums.OpusApplicationType"] = {
    ["Audio"] = "Audio",
    ["RestrictedLowDelay"] = "RestrictedLowDelay",
    ["Voip"] = "Voip",
}
Enums.VoiceChat = {}
Enums.VoiceChat.Codec = {}
Enums.VoiceChat.Codec.Enums = {}
Enums.VoiceChat.Codec.Enums.OpusApplicationType = Enums["VoiceChat.Codec.Enums.OpusApplicationType"]
Enums.OpusApplicationType = Enums["VoiceChat.Codec.Enums.OpusApplicationType"]
OpusApplicationType = Enums["VoiceChat.Codec.Enums.OpusApplicationType"]

---@alias Enum_VoiceChat_002e_Codec_002e_Enums_002e_OpusCtlGetRequest
---| "Application"
---| "Bandwidth"
---| "Bitrate"
---| "Complexity"
---| "Dtx"
---| "FinalRange"
---| "ForceChannels"
---| "Gain"
---| "InbandFec"
---| "LookAhead"
---| "LsbDepth"
---| "MaxBandwidth"
---| "PacketLossPercentage"
---| "Pitch"
---| "SampleRate"
---| "Signal"
---| "VBR"
---| "VBRConstraint"
Enums["VoiceChat.Codec.Enums.OpusCtlGetRequest"] = {
    ["Application"] = "Application",
    ["Bandwidth"] = "Bandwidth",
    ["Bitrate"] = "Bitrate",
    ["Complexity"] = "Complexity",
    ["Dtx"] = "Dtx",
    ["FinalRange"] = "FinalRange",
    ["ForceChannels"] = "ForceChannels",
    ["Gain"] = "Gain",
    ["InbandFec"] = "InbandFec",
    ["LookAhead"] = "LookAhead",
    ["LsbDepth"] = "LsbDepth",
    ["MaxBandwidth"] = "MaxBandwidth",
    ["PacketLossPercentage"] = "PacketLossPercentage",
    ["Pitch"] = "Pitch",
    ["SampleRate"] = "SampleRate",
    ["Signal"] = "Signal",
    ["VBR"] = "VBR",
    ["VBRConstraint"] = "VBRConstraint",
}
Enums.VoiceChat.Codec.Enums.OpusCtlGetRequest = Enums["VoiceChat.Codec.Enums.OpusCtlGetRequest"]
Enums.OpusCtlGetRequest = Enums["VoiceChat.Codec.Enums.OpusCtlGetRequest"]
OpusCtlGetRequest = Enums["VoiceChat.Codec.Enums.OpusCtlGetRequest"]

---@alias Enum_VoiceChat_002e_Codec_002e_Enums_002e_OpusCtlSetRequest
---| "Application"
---| "Bandwidth"
---| "Bitrate"
---| "Complexity"
---| "Dtx"
---| "ForceChannels"
---| "Gain"
---| "InbandFec"
---| "LsbDepth"
---| "MaxBandwidth"
---| "PacketLossPercentage"
---| "Signal"
---| "VBR"
---| "VBRConstraint"
Enums["VoiceChat.Codec.Enums.OpusCtlSetRequest"] = {
    ["Application"] = "Application",
    ["Bandwidth"] = "Bandwidth",
    ["Bitrate"] = "Bitrate",
    ["Complexity"] = "Complexity",
    ["Dtx"] = "Dtx",
    ["ForceChannels"] = "ForceChannels",
    ["Gain"] = "Gain",
    ["InbandFec"] = "InbandFec",
    ["LsbDepth"] = "LsbDepth",
    ["MaxBandwidth"] = "MaxBandwidth",
    ["PacketLossPercentage"] = "PacketLossPercentage",
    ["Signal"] = "Signal",
    ["VBR"] = "VBR",
    ["VBRConstraint"] = "VBRConstraint",
}
Enums.VoiceChat.Codec.Enums.OpusCtlSetRequest = Enums["VoiceChat.Codec.Enums.OpusCtlSetRequest"]
Enums.OpusCtlSetRequest = Enums["VoiceChat.Codec.Enums.OpusCtlSetRequest"]
OpusCtlSetRequest = Enums["VoiceChat.Codec.Enums.OpusCtlSetRequest"]

---@alias Enum_VoiceChat_002e_Codec_002e_Enums_002e_OpusStatusCode
---| "AllocFail"
---| "BadArguments"
---| "BufferTooSmall"
---| "InternalError"
---| "InvalidPacket"
---| "InvalidState"
---| "OK"
---| "Unimplemented"
Enums["VoiceChat.Codec.Enums.OpusStatusCode"] = {
    ["AllocFail"] = "AllocFail",
    ["BadArguments"] = "BadArguments",
    ["BufferTooSmall"] = "BufferTooSmall",
    ["InternalError"] = "InternalError",
    ["InvalidPacket"] = "InvalidPacket",
    ["InvalidState"] = "InvalidState",
    ["OK"] = "OK",
    ["Unimplemented"] = "Unimplemented",
}
Enums.VoiceChat.Codec.Enums.OpusStatusCode = Enums["VoiceChat.Codec.Enums.OpusStatusCode"]
Enums.OpusStatusCode = Enums["VoiceChat.Codec.Enums.OpusStatusCode"]
OpusStatusCode = Enums["VoiceChat.Codec.Enums.OpusStatusCode"]

---@alias Enum_VoiceChat_002e_Playbacks_002e_GlobalChatIconType
---| "Avatar"
---| "Intercom"
---| "None"
---| "Radio"
Enums["VoiceChat.Playbacks.GlobalChatIconType"] = {
    ["Avatar"] = "Avatar",
    ["Intercom"] = "Intercom",
    ["None"] = "None",
    ["Radio"] = "Radio",
}
Enums.VoiceChat.Playbacks = {}
Enums.VoiceChat.Playbacks.GlobalChatIconType = Enums["VoiceChat.Playbacks.GlobalChatIconType"]
Enums.GlobalChatIconType = Enums["VoiceChat.Playbacks.GlobalChatIconType"]
GlobalChatIconType = Enums["VoiceChat.Playbacks.GlobalChatIconType"]

---@alias Enum_VoiceChat_002e_VcMuteFlags
---| "GlobalIntercom"
---| "GlobalRegular"
---| "LocalIntercom"
---| "LocalRegular"
---| "None"
Enums["VoiceChat.VcMuteFlags"] = {
    ["GlobalIntercom"] = "GlobalIntercom",
    ["GlobalRegular"] = "GlobalRegular",
    ["LocalIntercom"] = "LocalIntercom",
    ["LocalRegular"] = "LocalRegular",
    ["None"] = "None",
}
Enums.VoiceChat.VcMuteFlags = Enums["VoiceChat.VcMuteFlags"]
Enums.VcMuteFlags = Enums["VoiceChat.VcMuteFlags"]
VcMuteFlags = Enums["VoiceChat.VcMuteFlags"]

---@alias Enum_VoiceChat_002e_VcPrivacyFlags
---| "AllowMicCapture"
---| "AllowRecording"
---| "None"
---| "SettingsSelected"
Enums["VoiceChat.VcPrivacyFlags"] = {
    ["AllowMicCapture"] = "AllowMicCapture",
    ["AllowRecording"] = "AllowRecording",
    ["None"] = "None",
    ["SettingsSelected"] = "SettingsSelected",
}
Enums.VoiceChat.VcPrivacyFlags = Enums["VoiceChat.VcPrivacyFlags"]
Enums.VcPrivacyFlags = Enums["VoiceChat.VcPrivacyFlags"]
VcPrivacyFlags = Enums["VoiceChat.VcPrivacyFlags"]

---@alias Enum_VoiceChat_002e_VoiceChatChannel
---| "Intercom"
---| "Mimicry"
---| "None"
---| "PreGameLobby"
---| "Proximity"
---| "Radio"
---| "RoundSummary"
---| "Scp1507"
---| "Scp1576"
---| "ScpChat"
---| "Spectator"
Enums["VoiceChat.VoiceChatChannel"] = {
    ["Intercom"] = "Intercom",
    ["Mimicry"] = "Mimicry",
    ["None"] = "None",
    ["PreGameLobby"] = "PreGameLobby",
    ["Proximity"] = "Proximity",
    ["Radio"] = "Radio",
    ["RoundSummary"] = "RoundSummary",
    ["Scp1507"] = "Scp1507",
    ["Scp1576"] = "Scp1576",
    ["ScpChat"] = "ScpChat",
    ["Spectator"] = "Spectator",
}
Enums.VoiceChat.VoiceChatChannel = Enums["VoiceChat.VoiceChatChannel"]
Enums.VoiceChatChannel = Enums["VoiceChat.VoiceChatChannel"]
VoiceChatChannel = Enums["VoiceChat.VoiceChatChannel"]

---@alias Enum_WaterPhysics_002e_BuoyancyMode
---| "Floater"
---| "LongTimeFloaters"
---| "NonBuoyant"
---| "ShortTimeFloaters"
---| "SuperHeavy"
---| "SuperLight"
Enums["WaterPhysics.BuoyancyMode"] = {
    ["Floater"] = "Floater",
    ["LongTimeFloaters"] = "LongTimeFloaters",
    ["NonBuoyant"] = "NonBuoyant",
    ["ShortTimeFloaters"] = "ShortTimeFloaters",
    ["SuperHeavy"] = "SuperHeavy",
    ["SuperLight"] = "SuperLight",
}
Enums.WaterPhysics = {}
Enums.WaterPhysics.BuoyancyMode = Enums["WaterPhysics.BuoyancyMode"]
Enums.BuoyancyMode = Enums["WaterPhysics.BuoyancyMode"]
BuoyancyMode = Enums["WaterPhysics.BuoyancyMode"]

---@class AdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_AdminToy = {}

---@class AdminToyAddedEventArgs
---@field AdminToy AdminToy|nil # Read-only.
local _type_AdminToyAddedEventArgs = {}

---@class AdminToyRemovedEventArgs
---@field AdminToy AdminToy|nil # Read-only.
local _type_AdminToyRemovedEventArgs = {}

--- A world ammunition stack with a replicated count.
---@class AmmoPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_AmmoPickup = {}

--- An SCP-939 cloud's replicated presentation state.
---@class AmnesticCloudHazard
---@field IsActive boolean # Read-only. The local hazard component's active state; this is not a server acceptance or damage guarantee.
---@field IsDestroyed boolean # Read-only.
---@field MaxDistance number # Read-only.
---@field MaxHeightDistance number # Read-only.
---@field Room Room|nil # Read-only.
local _type_AmnesticCloudHazard = {}

--- Fired when CASSIE schedules a line for playback (before audio starts). Pair with Anomaly.Client.Api.Events.Args.AnnouncerLineStartedEventArgs for the started-playing notification.
---@class AnnouncerLineScheduledEventArgs
---@field ApiName string|nil # Read-only. Convenience: API name (e.g. "ALERT") of the scheduled line.
---@field ScheduledStartDspTime number # Read-only. The DSP-time at which playback is scheduled to begin. Compare against UnityEngine.AudioSettings.dspTime to compute lead time.
---@field Word AnnouncerWord|nil # Read-only. Wrapped Anomaly.Client.Api.Features.AnnouncerWord for ergonomic access.
local _type_AnnouncerLineScheduledEventArgs = {}

--- Fired when a CASSIE line begins audible playback (engine's CassieLineSource.OnStartedPlaying static event). Useful for mods that want to flash subtitles in sync with audio.
---@class AnnouncerLineStartedEventArgs
---@field ApiName string|nil # Read-only. Convenience: API name (e.g. "ALERT") of the line.
---@field Word AnnouncerWord|nil # Read-only. Wrapped Anomaly.Client.Api.Features.AnnouncerWord for ergonomic access.
local _type_AnnouncerLineStartedEventArgs = {}

---@class AnnouncerWord
---@field ApiName string|nil # Read-only.
---@field Duration number # Read-only.
---@field Type Enum_Cassie_002e_CassieClipCategory # Read-only.
local _type_AnnouncerWord = {}

---@class AnomalyMessageReceivedEventArgs
local _type_AnomalyMessageReceivedEventArgs = {}

---@class AssetOverrideAppliedEventArgs
---@field ComponentPath string|nil # Read-only.
---@field MaterialIndex integer # Read-only.
---@field NetId integer # Read-only.
local _type_AssetOverrideAppliedEventArgs = {}

---@class AssetOverrideClearedEventArgs
---@field ComponentPath string|nil # Read-only.
---@field MaterialIndex integer # Read-only.
---@field NetId integer # Read-only.
local _type_AssetOverrideClearedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class AudioPlayingEventArgs
---@field Channel Enum_AudioPooling_002e_MixerChannel # Read-only. Which mixer channel the sound was going to play on (DefaultSfx, Voice, etc.).
---@field ClipName string|nil # Read-only. The local clip name, or an empty string when no clip is available.
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Volume number # Read-only. Volume scalar in [0, 1].
local _type_AudioPlayingEventArgs = {}

--- Body armor configuration and owning-client wear state.
---@class BodyArmorItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_BodyArmorItem = {}

--- A typed world armor pickup. Inventory armor configuration is exposed by Anomaly.Client.Api.Features.BodyArmorItem.
---@class BodyArmorPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_BodyArmorPickup = {}

--- Wrapper around Il2CppInteractables.Interobjects.BreakableDoor. Adds health / damage-type state on top of the base Anomaly.Client.Api.Features.Door surface for HUD overlays (door HP bars, "this door is about to break" warnings, faction-specific damage indicators).
---@class BreakableDoor
---@field ActiveLocks integer # Read-only.
---@field CanSeeThrough boolean # Read-only.
---@field Id integer # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsMoving boolean # Read-only.
---@field IsOpen boolean # Read-only.
---@field Name string|nil # Read-only.
local _type_BreakableDoor = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class BroadcastShowingEventArgs
---@field DurationSeconds integer # Read-only. How long the broadcast was intended to display, in seconds.
---@field Flags integer # Read-only. Raw Broadcast.BroadcastFlags byte (Truncated, AdminChat, etc.).
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Text string|nil # Read-only. The broadcast text the server sent.
local _type_BroadcastShowingEventArgs = {}

--- Read-only wrapper for client-observed Scp079CameraToy state.
---@class CameraAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_CameraAdminToy = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class CameraShakingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
local _type_CameraShakingEventArgs = {}

---@class CanvasReadyEventArgs
local _type_CanvasReadyEventArgs = {}

--- Read-only wrapper for client-observed CapybaraToy state.
---@class CapybaraAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_CapybaraAdminToy = {}

--- Wrapper around Il2CppInteractables.Interobjects.CheckpointDoor — multi-stage checkpoint gates (LCZ entrance, EZ/HCZ checkpoints) that control multiple sub-doors in sequence. Adds the sub-door list and current sequence state on top of the base Anomaly.Client.Api.Features.Door surface.
---@class CheckpointDoor
---@field ActiveLocks integer # Read-only.
---@field CanSeeThrough boolean # Read-only.
---@field Id integer # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsMoving boolean # Read-only.
---@field IsOpen boolean # Read-only.
---@field Name string|nil # Read-only.
local _type_CheckpointDoor = {}

---@class ClientConnectedEventArgs
---@field ServerIp string|nil # Read-only.
---@field ServerPort integer # Read-only.
local _type_ClientConnectedEventArgs = {}

---@class ClientDisconnectedEventArgs
---@field ServerIp string|nil # Read-only.
---@field ServerPort integer # Read-only.
local _type_ClientDisconnectedEventArgs = {}

---@class ClientReadyEventArgs
---@field SceneName string|nil # Read-only.
local _type_ClientReadyEventArgs = {}

--- A consumable with an owning-client use progress bar.
---@class ConsumableItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_ConsumableItem = {}

---@class CurrentItemChangedEventArgs
---@field CurrentSerial integer # Read-only. The serial held after the observed change.
---@field CurrentType Enum_ItemType # Read-only. The item type held after the observed change.
---@field Player Player|nil # Read-only.
---@field PreviousSerial integer # Read-only. The serial held before the observed change.
---@field PreviousType Enum_ItemType # Read-only. The item type held before the observed change.
local _type_CurrentItemChangedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class CurrentItemChangingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player Player|nil # Read-only.
---@field PreviousSerial integer # Read-only. The serial held before this local switch request.
---@field TargetSerial integer # Read-only. Serial of the item being switched to (0 for holster/no item).
local _type_CurrentItemChangingEventArgs = {}

--- A temporary hazard. Lifetime progression is server-owned, so no client countdown is promised.
---@class DecayableHazard
---@field IsActive boolean # Read-only. The local hazard component's active state; this is not a server acceptance or damage guarantee.
---@field IsDestroyed boolean # Read-only.
---@field MaxDistance number # Read-only.
---@field MaxHeightDistance number # Read-only.
---@field Room Room|nil # Read-only.
local _type_DecayableHazard = {}

---@class Door
---@field ActiveLocks integer # Read-only.
---@field CanSeeThrough boolean # Read-only.
---@field Id integer # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsMoving boolean # Read-only.
---@field IsOpen boolean # Read-only.
---@field Name string|nil # Read-only.
local _type_Door = {}

---@class DoorEventArgs
---@field Door Door|nil # Read-only.
local _type_DoorEventArgs = {}

---@class DoorLocksChangedEventArgs
---@field ActiveLocks integer # Read-only.
---@field Door Door|nil # Read-only.
---@field PreviousLocks integer # Read-only.
local _type_DoorLocksChangedEventArgs = {}

--- An observed target-state change, not a cancellable interaction or proof that the door finished moving.
---@class DoorStateChangedEventArgs
---@field Door Door|nil # Read-only.
---@field IsOpen boolean # Read-only.
---@field PreviousIsOpen boolean # Read-only.
local _type_DoorStateChangedEventArgs = {}

---@class EffectDisabledEventArgs
---@field Effect PlayerEffect|nil # Read-only.
local _type_EffectDisabledEventArgs = {}

---@class EffectEnabledEventArgs
---@field Effect PlayerEffect|nil # Read-only.
local _type_EffectEnabledEventArgs = {}

---@class EffectIntensityChangedEventArgs
---@field CurrentIntensity integer # Read-only.
---@field Effect PlayerEffect|nil # Read-only.
---@field PreviousIntensity integer # Read-only.
local _type_EffectIntensityChangedEventArgs = {}

--- Wrapper around Il2CppInteractables.Interobjects.ElevatorChamber. Exposes sequence state, destination level, and direction for floor-indicator HUDs and elevator-music mods. Read-only — call/recall is server-authoritative.
---@class Elevator
---@field CurrentSequence Enum_Interactables_002e_Interobjects_002e_ElevatorChamber_002e_ElevatorSequence # Read-only. The current sequence state (Ready / Arrived / DoorOpening / Moving / DoorClosing).
---@field DestinationLevel integer # Read-only. The level the chamber is currently moving to (or has stopped at).
---@field GoingUp boolean # Read-only. True if the chamber is currently traveling upward.
---@field IsDestroyed boolean # Read-only.
---@field IsReady boolean # Read-only. True if the chamber is in Il2CppInteractables.Interobjects.ElevatorChamber.ElevatorSequence.Ready phase.
---@field IsReadyForUserInput boolean # Read-only. True if the chamber is ready to accept user input (e.g. a button press).
---@field NextLevel integer # Read-only. The level above the current one.
---@field PreviousLevel integer # Read-only. The level below the current one.
---@field Room Room|nil # Read-only. The room containing the chamber, or null.
---@field SequenceElapsed number # Read-only. Seconds elapsed in the current sequence phase.
local _type_Elevator = {}

--- Wrapper around Il2CppInteractables.Interobjects.ElevatorDoor — the doors mounted on an elevator chamber's entry/exit. Adds the chamber link and elevator group on top of the base Anomaly.Client.Api.Features.Door surface.
---@class ElevatorDoor
---@field ActiveLocks integer # Read-only.
---@field CanSeeThrough boolean # Read-only.
---@field Id integer # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsMoving boolean # Read-only.
---@field IsOpen boolean # Read-only.
---@field Name string|nil # Read-only.
local _type_ElevatorDoor = {}

--- Wraps an Anomaly.Client.Api.Events.Args.ElevatorEventArgs.Elevator reference for chamber lifecycle / movement events.
---@class ElevatorEventArgs
---@field Elevator Elevator|nil # Read-only.
local _type_ElevatorEventArgs = {}

--- The server-disclosed enrollment identifier changed or was cleared.
---@class EnrollmentChangedEventArgs
---@field Current string|nil # Read-only.
---@field Previous string|nil # Read-only.
local _type_EnrollmentChangedEventArgs = {}

--- Read-only client wrapper for ExplosionGrenade.
---@class ExplosiveGrenadeProjectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_ExplosiveGrenadeProjectile = {}

--- Wrapper around Il2CppPlayerRoles.PlayableScps.Scp079.Cameras.Scp079Camera — the in-facility CCTV cameras that SCP-079 uses. Useful for SCP-079 spectator UIs, camera-feed mods, minimap markers showing camera coverage, etc.
---@class FacilityCamera
---@field HorizontalRotation number # Read-only.
---@field IsActive boolean # Read-only. True if SCP-079 is currently viewing through this camera.
---@field IsDestroyed boolean # Read-only.
---@field IsMain boolean # Read-only. True if this is the room's "main" camera (the one SCP-079 enters by default for the room).
---@field IsUsedByLocalPlayer boolean # Read-only. True if the local player is the SCP-079 currently using this camera.
---@field Label string|nil # Read-only. Display label for the camera (e.g. "Heavy Containment Zone Camera 7").
---@field RollRotation number # Read-only.
---@field VerticalRotation number # Read-only.
local _type_FacilityCamera = {}

--- Wraps a Anomaly.Client.Api.Features.FacilityCamera reference for CCTV lifecycle / state events.
---@class FacilityCameraEventArgs
---@field Camera FacilityCamera|nil # Read-only.
local _type_FacilityCameraEventArgs = {}

---@class FileTransferCompleteEventArgs
---@field FileCount integer # Read-only.
---@field Success boolean # Read-only.
local _type_FileTransferCompleteEventArgs = {}

---@class FileTransferProgressEventArgs
---@field CompletedFiles integer # Read-only.
---@field FileName string|nil # Read-only.
---@field Percent number # Read-only.
---@field ReceivedBytes integer # Read-only.
---@field TotalBytes integer # Read-only.
---@field TotalFiles integer # Read-only.
local _type_FileTransferProgressEventArgs = {}

--- Wrapper around Il2CppInventorySystem.Items.Firearms.Firearm. Exposes magazine state, fire-mode info, and module access for HUD overlays (custom ammo counters, hit-marker mods, attachment indicators) without requiring consumers to dig through the modular firearm subcomponent system.
---@class FirearmItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_FirearmItem = {}

--- Wrapper around a world-spawned Anomaly.Client.Api.Features.FirearmPickup.FirearmPickupBase. Adds a typed downcast and the firearm-pickup-specific Anomaly.Client.Api.Features.FirearmPickup.IsTemplateSet flag on top of the base Anomaly.Client.Api.Features.Pickup surface.
---@class FirearmPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_FirearmPickup = {}

---@class FirearmShotEventArgs
---@field Firearm Enum_ItemType # Read-only.
local _type_FirearmShotEventArgs = {}

--- A client-observed firearm snapshot. Null fields are unavailable, never an inferred zero or false.
---@class FirearmState
---@field IsAiming boolean|nil # Read-only.
---@field IsReloading boolean|nil # Read-only. Local-owner reload prediction. Unavailable on remote representations.
---@field IsUnloading boolean|nil # Read-only.
---@field MagazineAmmo integer|nil # Read-only.
---@field MagazineCapacity integer|nil # Read-only.
---@field MagazineInserted boolean|nil # Read-only.
local _type_FirearmState = {}

--- A change in the client's observed firearm state, including first observation of available fields.
---@class FirearmStateChangedEventArgs
---@field Current FirearmState|nil # Read-only.
---@field Firearm FirearmItem|nil # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous FirearmState|nil # Read-only. Null when this is the first observation of this item in the session.
local _type_FirearmStateChangedEventArgs = {}

--- Read-only client wrapper for FlashbangGrenade.
---@class FlashbangProjectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_FlashbangProjectile = {}

---@class GameContext
---@field IsRoundEnded boolean # Read-only.
---@field IsRoundStarted boolean # Read-only.
---@field LocalPlayer Player|nil # Read-only.
---@field PlayerCount integer # Read-only.
---@field RoundPhase Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase # Read-only.
---@field SpectatedPlayer Player|nil # Read-only.
local _type_GameContext = {}
---@overload fun(self: GameContext, playerId: integer): Item|nil
---@param playerId integer
---@return Item|nil
function _type_GameContext.GetCurrentItem(playerId) end
---@overload fun(self: GameContext, playerId: integer): FirearmState|nil
---@param playerId integer
---@return FirearmState|nil
function _type_GameContext.GetFirearmState(playerId) end
---@overload fun(self: GameContext, playerId: integer): number|nil
---@param playerId integer
---@return number|nil
function _type_GameContext.GetHealth(playerId) end
---@overload fun(self: GameContext, playerId: integer): number|nil
---@param playerId integer
---@return number|nil
function _type_GameContext.GetHumeShield(playerId) end
---@overload fun(self: GameContext, playerId: integer): Player|nil
---@param playerId integer
---@return Player|nil
function _type_GameContext.GetPlayer(playerId) end
---@overload fun(self: GameContext, index: integer): Player|nil
---@param index integer
---@return Player|nil
function _type_GameContext.GetPlayerAt(index) end
---@overload fun(self: GameContext, playerId: integer): Position|nil
---@param playerId integer
---@return Position|nil
function _type_GameContext.GetPosition(playerId) end
---@overload fun(self: GameContext, playerId: integer): Room|nil
---@param playerId integer
---@return Room|nil
function _type_GameContext.GetRoom(playerId) end
---@overload fun(self: GameContext, playerId: integer): number|nil
---@param playerId integer
---@return number|nil
function _type_GameContext.GetStamina(playerId) end

--- Wrapper around Il2CppMapGeneration.Distributors.Scp079Generator. Exposes activation / engagement state, cooldown timing, and door state for HUD overlays (charge percentages, "generators remaining" counters, faction-tinted glow indicators).
---@class Generator
---@field IsActivating boolean # Read-only. True while the generator is in its activation countdown phase (lever pulled, not yet engaged).
---@field IsActivationReady boolean # Read-only. True if the generator's activation cooldown is satisfied (matches game's ActivationReady).
---@field IsDestroyed boolean # Read-only.
---@field IsEngaged boolean # Read-only. True once the generator has finished its activation sequence.
---@field IsOpen boolean # Read-only. True if the door panel is open (lever / interior accessible).
---@field IsUnlocked boolean # Read-only. True if the unlock cooldown has elapsed and the generator can be engaged.
---@field Room Room|nil # Read-only. The Anomaly.Client.Api.Features.Generator.Room containing this generator, or null if the generator isn't placed in any tracked room.
---@field TotalActivationTime number # Read-only. Total seconds the activation sequence takes from lever-pull to engaged.
---@field TotalDeactivationTime number # Read-only. Total seconds the deactivation sequence takes.
local _type_Generator = {}

--- Fires when an Il2CppMapGeneration.Distributors.Scp079Generator reaches the engaged state.
---@class GeneratorEngagedEventArgs
---@field Generator Generator|nil # Read-only. The generator that just engaged.
local _type_GeneratorEngagedEventArgs = {}

--- An observed generator flag transition after replication, without an inferred interacting player.
---@class GeneratorStateChangedEventArgs
---@field Flags Enum_MapGeneration_002e_Distributors_002e_Scp079Generator_002e_GeneratorFlags|string # Read-only.
---@field Generator Generator|nil # Read-only.
---@field PreviousFlags Enum_MapGeneration_002e_Distributors_002e_Scp079Generator_002e_GeneratorFlags|string # Read-only.
local _type_GeneratorStateChangedEventArgs = {}

--- A hazard known to the local scene. This does not report server damage or the server's affected-player list.
---@class Hazard
---@field IsActive boolean # Read-only. The local hazard component's active state; this is not a server acceptance or damage guarantee.
---@field IsDestroyed boolean # Read-only.
---@field MaxDistance number # Read-only.
---@field MaxHeightDistance number # Read-only.
---@field Room Room|nil # Read-only.
local _type_Hazard = {}

---@class HazardEventArgs
---@field Hazard Hazard|nil # Read-only.
local _type_HazardEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class HintDisplayingEventArgs
---@field DurationSeconds number # Read-only. Display duration in seconds (0 if not provided by the hint).
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Text string|nil # Read-only. The raw hint text. May contain rich-text tags and translation tokens.
local _type_HintDisplayingEventArgs = {}

--- Read-only wrapper for client-observed InvisibleInteractableToy state.
---@class InteractableAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_InteractableAdminToy = {}

---@class InventoryChangedEventArgs
---@field Player Player|nil # Read-only.
local _type_InventoryChangedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class InventoryUiClosingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
local _type_InventoryUiClosingEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class InventoryUiOpeningEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
local _type_InventoryUiOpeningEventArgs = {}

---@class Item
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Item = {}

--- Wrapper around Il2CppInventorySystem.Items.Keycards.KeycardItem. Exposes the keycard's resolved permissions for door-permission HUDs (which doors can I open? access tier indicators, faction-themed keycard colors).
---@class KeycardItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_KeycardItem = {}

--- Wrapper around a world-spawned Anomaly.Client.Api.Features.KeycardPickup.KeycardPickupBase. Adds a typed downcast plus the keycard-pickup-specific Anomaly.Client.Api.Features.KeycardPickup.OpensDoorsOnCollision flag.
---@class KeycardPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_KeycardPickup = {}

---@class LightAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_LightAdminToy = {}

--- A toggleable held light, including flashlights and lanterns.
---@class LightItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_LightItem = {}

---@class LocalPlayer
---@field DisplayName string|nil # Read-only. The game's display name, including a server-provided custom name when present.
---@field IsAlive boolean # Read-only.
---@field IsCrouching boolean # Read-only. True if the player is in the crouching or sneaking state.
---@field IsDestroyed boolean # Read-only. Whether the underlying player object has been destroyed.
---@field IsGrounded boolean # Read-only. True if the player's FPC module reports being grounded. Returns false when the module is unavailable (non-FPC roles).
---@field IsHuman boolean # Read-only.
---@field IsLocal boolean # Read-only.
---@field IsScp boolean # Read-only.
---@field IsSprinting boolean # Read-only. True if the player is in the sprinting state.
---@field LifeId integer # Read-only. The current role's life identifier, or -1 when no role is available.
---@field Nickname string|nil # Read-only. The nickname known to this client, or an empty string when unavailable.
---@field PlayerId integer # Read-only. The replicated player ID, or -1 after the player object is destroyed.
---@field RoleType Enum_PlayerRoles_002e_RoleTypeId # Read-only. The current role ID, or Il2CppPlayerRoles.RoleTypeId.None when unavailable.
---@field Team Enum_PlayerRoles_002e_Team # Read-only. The current team, or Il2CppPlayerRoles.Team.Dead when no role is available.
---@field Zone Enum_MapGeneration_002e_FacilityZone # Read-only. The facility zone the player is currently in. Returns Il2CppMapGeneration.FacilityZone.None if the player isn't in any tracked room.
local _type_LocalPlayer = {}

--- Wrapper around Il2CppMapGeneration.Distributors.Locker. Aggregates the locker's Il2CppMapGeneration.Distributors.LockerChamber array for chamber-aware HUDs (loot indicators, "what's inside" overlays, faction-themed locker tints).
---@class Locker
---@field IsDestroyed boolean # Read-only.
---@field OpenedChambersMask integer # Read-only. Bitmask of currently-opened chambers (bit N set ⇒ chamber N is open).
---@field Room Room|nil # Read-only. The room containing the locker, or null if not in any tracked room.
local _type_Locker = {}

--- An updated replicated chamber mask. Local door animations may finish later.
---@class LockerStateChangedEventArgs
---@field ChangedChambers integer # Read-only.
---@field Locker Locker|nil # Read-only.
---@field OpenedChambers integer # Read-only.
---@field PreviousOpenedChambers integer # Read-only.
local _type_LockerStateChangedEventArgs = {}

--- The Micro H.I.D.'s client-observed energy and firing cycle.
---@class MicroHIDItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_MicroHIDItem = {}

---@class MotionDiscontinuity
---@field Grounded boolean # Read-only.
---@field Sequence integer # Read-only.
local _type_MotionDiscontinuity = {}

---@class Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Pickup = {}

---@class PickupAddedEventArgs
---@field Pickup Pickup|nil # Read-only.
local _type_PickupAddedEventArgs = {}

---@class PickupRemovedEventArgs
---@field Pickup Pickup|nil # Read-only.
local _type_PickupRemovedEventArgs = {}

---@class Player
---@field DisplayName string|nil # Read-only. The game's display name, including a server-provided custom name when present.
---@field IsAlive boolean # Read-only.
---@field IsCrouching boolean # Read-only. True if the player is in the crouching or sneaking state.
---@field IsDestroyed boolean # Read-only. Whether the underlying player object has been destroyed.
---@field IsGrounded boolean # Read-only. True if the player's FPC module reports being grounded. Returns false when the module is unavailable (non-FPC roles).
---@field IsHuman boolean # Read-only.
---@field IsLocal boolean # Read-only.
---@field IsScp boolean # Read-only.
---@field IsSprinting boolean # Read-only. True if the player is in the sprinting state.
---@field LifeId integer # Read-only. The current role's life identifier, or -1 when no role is available.
---@field Nickname string|nil # Read-only. The nickname known to this client, or an empty string when unavailable.
---@field PlayerId integer # Read-only. The replicated player ID, or -1 after the player object is destroyed.
---@field RoleType Enum_PlayerRoles_002e_RoleTypeId # Read-only. The current role ID, or Il2CppPlayerRoles.RoleTypeId.None when unavailable.
---@field Team Enum_PlayerRoles_002e_Team # Read-only. The current team, or Il2CppPlayerRoles.Team.Dead when no role is available.
---@field Zone Enum_MapGeneration_002e_FacilityZone # Read-only. The facility zone the player is currently in. Returns Il2CppMapGeneration.FacilityZone.None if the player isn't in any tracked room.
local _type_Player = {}

---@class PlayerAddedEventArgs
---@field Player Player|nil # Read-only.
local _type_PlayerAddedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerDroppingItemEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Item Item|nil # Read-only. The item when it can be resolved from the client cache.
---@field ItemSerial integer # Read-only. Serial of the item the player is attempting to drop.
---@field Player Player|nil # Read-only.
---@field TryThrow boolean # Read-only. True if the player is performing a drop-and-throw (G + movement), false for a plain drop.
local _type_PlayerDroppingItemEventArgs = {}

---@class PlayerEffect
---@field CanEnable boolean # Read-only.
---@field Classification string|nil # Read-only.
---@field Duration number # Read-only.
---@field Intensity integer # Read-only.
---@field IsEnabled boolean # Read-only.
---@field IsLocalPlayerEffect boolean # Read-only.
---@field IsSpectatedEffect boolean # Read-only.
---@field MaxIntensity integer # Read-only.
---@field Name string|nil # Read-only.
---@field TimeLeft number # Read-only.
local _type_PlayerEffect = {}

---@class PlayerEventArgs
---@field Player Player|nil # Read-only.
local _type_PlayerEventArgs = {}

---@class PlayerHealedEventArgs
---@field AmountHealed number # Read-only.
---@field CurrentHealth number # Read-only.
---@field Player Player|nil # Read-only.
---@field PreviousHealth number # Read-only.
local _type_PlayerHealedEventArgs = {}

--- Fires when a tracked player's HumeShieldStat value crosses a polling tick boundary. Delta is signed — negative for shield damage, positive for regeneration. The convenience Anomaly.Client.Api.Events.Args.PlayerHumeShieldChangedEventArgs.Broke property is true on the tick where the shield drops to zero from a non-zero value (mirrors LabAPI's server-side HumeShieldBroken event).
---@class PlayerHumeShieldChangedEventArgs
---@field Broke boolean # Read-only. True if the shield broke (dropped to zero) on this tick.
---@field Current number # Read-only.
---@field Delta number # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous number # Read-only.
local _type_PlayerHumeShieldChangedEventArgs = {}

--- A resolved local interaction after the local callback, before any server command. This is a notification, not proof of server acceptance or a server-side veto point.
---@class PlayerInteractedEventArgs
---@field ColliderId integer # Read-only.
---@field Door Door|nil # Read-only.
---@field Generator Generator|nil # Read-only.
---@field Locker Locker|nil # Read-only.
---@field Player Player|nil # Read-only.
---@field Workstation Workstation|nil # Read-only.
local _type_PlayerInteractedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerInteractingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player Player|nil # Read-only.
local _type_PlayerInteractingEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerJumpingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field JumpSpeed number # Read-only. The jump speed the engine was about to apply (pre-multiplier).
---@field Player Player|nil # Read-only.
local _type_PlayerJumpingEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerNoclipTogglingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player Player|nil # Read-only.
local _type_PlayerNoclipTogglingEventArgs = {}

---@class PlayerRemovedEventArgs
---@field Player Player|nil # Read-only.
local _type_PlayerRemovedEventArgs = {}

---@class PlayerRoleChangedEventArgs
---@field NewRole Role|nil # Read-only.
---@field Player Player|nil # Read-only.
---@field PreviousRole Role|nil # Read-only.
local _type_PlayerRoleChangedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerShootingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player Player|nil # Read-only.
local _type_PlayerShootingEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerSprintingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player Player|nil # Read-only.
local _type_PlayerSprintingEventArgs = {}

--- Fires when a tracked player's StaminaStat value crosses a polling tick boundary. Delta is signed — negative for stamina drain, positive for regeneration.
---@class PlayerStaminaChangedEventArgs
---@field Current number # Read-only.
---@field Delta number # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous number # Read-only.
local _type_PlayerStaminaChangedEventArgs = {}

---@class PlayerStat
---@field IsDestroyed boolean # Read-only.
---@field IsObservable boolean # Read-only. Whether this stat's local synchronization policy permits observation by the current local player. This is not a receipt or freshness guarantee. Server plugins can customize synchronization; raw values remain the last local values.
---@field Name string|nil # Read-only.
local _type_PlayerStat = {}

--- A change between observable local samples; not authoritative damage attribution.
---@class PlayerStatChangedEventArgs
---@field Current number # Read-only.
---@field Delta number # Read-only.
---@field Maximum number # Read-only.
---@field MaximumChanged boolean # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous number # Read-only.
---@field PreviousMaximum number # Read-only.
---@field Stat PlayerStat|nil # Read-only.
---@field ValueChanged boolean # Read-only.
local _type_PlayerStatChangedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class PlayerThrowingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Item ThrowableItem|nil # Read-only.
---@field Player Player|nil # Read-only.
local _type_PlayerThrowingEventArgs = {}

---@class Position
---@field X number # Read-only.
---@field Y number # Read-only.
---@field Z number # Read-only.
local _type_Position = {}

---@class PrimitiveAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_PrimitiveAdminToy = {}

---@class Projectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Projectile = {}

---@class ProjectileRemovedEventArgs
---@field Projectile Projectile|nil # Read-only.
local _type_ProjectileRemovedEventArgs = {}

---@class ProjectileSpawnedEventArgs
---@field Projectile Projectile|nil # Read-only.
local _type_ProjectileSpawnedEventArgs = {}

--- A radio with owning-client battery and range information.
---@class RadioItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_RadioItem = {}

--- A dropped radio's replicated enabled and range state. Pickup battery is server-only and is not exposed here.
---@class RadioPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_RadioPickup = {}

--- Wrapper around Il2CppPlayerRoles.Ragdolls.BasicRagdoll. Exposes the death snapshot (role, owner, nickname, creation time) for death-cam mods, ragdoll-cleanup mods, and "who killed whom" replays.
---@class Ragdoll
---@field CreationTime number # Read-only. Engine creation timestamp (DSP-time-style double).
---@field IsDestroyed boolean # Read-only.
---@field IsFrozen boolean # Read-only. True if the ragdoll has been frozen (no further physics).
---@field Nickname string|nil # Read-only. The nickname of the dead player at time of death.
---@field RoleType Enum_PlayerRoles_002e_RoleTypeId # Read-only. The role the dead player was when the ragdoll was created.
local _type_Ragdoll = {}

--- Wraps a Anomaly.Client.Api.Events.Args.RagdollEventArgs.Ragdoll reference for spawn / removed events.
---@class RagdollEventArgs
---@field Ragdoll Ragdoll|nil # Read-only.
local _type_RagdollEventArgs = {}

---@class Role
---@field IsAlive boolean # Read-only.
---@field IsDestroyed boolean # Read-only. Whether the native role was destroyed, returned to its pool, or reused for a different life. Role wrappers should not be retained across lives.
---@field IsHuman boolean # Read-only.
---@field IsScp boolean # Read-only.
---@field LifeIdentifier integer # Read-only.
---@field Name string|nil # Read-only.
---@field Team Enum_PlayerRoles_002e_Team # Read-only.
---@field Type Enum_PlayerRoles_002e_RoleTypeId # Read-only.
local _type_Role = {}

---@class Room
---@field IsDestroyed boolean # Read-only.
---@field Name string|nil # Read-only.
---@field RoomName Enum_MapGeneration_002e_RoomName # Read-only.
---@field Shape Enum_MapGeneration_002e_RoomShape # Read-only.
---@field Zone Enum_MapGeneration_002e_FacilityZone # Read-only.
local _type_Room = {}

--- Fired when the server-reported Anomaly.Shared.Networking.Messages.RoundPhase changes. Carries both the previous and new phases so handlers can react to specific transitions (e.g. only reset state when transitioning to Anomaly.Shared.Networking.Messages.RoundPhase.Restarted).
---@class RoundPhaseChangedEventArgs
---@field NewPhase Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase # Read-only.
---@field PreviousPhase Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase # Read-only.
local _type_RoundPhaseChangedEventArgs = {}

---@class SceneLoadedEventArgs
---@field SceneName string|nil # Read-only. The loaded scene's name, without exposing its native handle to scripts.
local _type_SceneLoadedEventArgs = {}

--- Read-only client wrapper for Scp018Projectile.
---@class Scp018Projectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp018Projectile = {}

--- A newly received SCP-079 auxiliary power observation.
---@class Scp079AuxiliaryPowerChangedEventArgs
---@field Current number # Read-only.
---@field IsInitialObservation boolean # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous number|nil # Read-only. The previous received value, or null before the first update in this viewing context.
local _type_Scp079AuxiliaryPowerChangedEventArgs = {}

--- A newly received SCP-079 total-experience observation.
---@class Scp079ExperienceChangedEventArgs
---@field Current integer # Read-only.
---@field IsInitialObservation boolean # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous integer|nil # Read-only. The previous received value, or null before the first update for this role life.
local _type_Scp079ExperienceChangedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class Scp079SwitchingCameraEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player Player|nil # Read-only.
---@field TargetCamera FacilityCamera|nil # Read-only. The camera the local SCP-079 is switching to.
local _type_Scp079SwitchingCameraEventArgs = {}

--- A newly received SCP-079 access-tier observation.
---@class Scp079TierChangedEventArgs
---@field Current integer # Read-only. The newly observed one-based tier.
---@field IsInitialObservation boolean # Read-only.
---@field Player Player|nil # Read-only.
---@field Previous integer|nil # Read-only. The previous one-based tier, or null before the first update for this role life.
local _type_Scp079TierChangedEventArgs = {}

--- SCP-1344's observed state and local use controls.
---@class Scp1344Item
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp1344Item = {}

--- SCP-1576 with the common usable-item facade and transmission configuration.
---@class Scp1576Item
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp1576Item = {}

--- SCP-1576's world pickup and replicated horn position.
---@class Scp1576Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp1576Pickup = {}

--- Read-only client wrapper for Scp2176Projectile.
---@class Scp2176Projectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp2176Projectile = {}

--- A deployed SCP-244 pickup and its replicated fog state.
---@class Scp244Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp244Pickup = {}

--- Wrapper around Il2CppInventorySystem.Items.Usables.Scp330.Scp330Bag — the SCP-330 candy bag item. Exposes the carried candy list and selected-candy index for HUD overlays (candy picker, current-candy indicator, "what did I get" toasts).
---@class Scp330Item
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp330Item = {}

--- Wrapper around a world-spawned Anomaly.Client.Api.Features.Scp330Pickup.Scp330PickupBase — the SCP-330 candy bag dropped on the ground. Exposes the bag's contents and the currently exposed (visually displayed) candy.
---@class Scp330Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_Scp330Pickup = {}

---@class ScriptOutput
local _type_ScriptOutput = {}
---@overload fun(self: ScriptOutput)
function _type_ScriptOutput.ClearText() end
---@overload fun(self: ScriptOutput, text: string|nil): boolean
---@param text string|nil
---@return boolean
function _type_ScriptOutput.Log(text) end
---@overload fun(self: ScriptOutput, text: string|nil, durationSeconds: number): boolean
---@param text string|nil
---@param durationSeconds number
---@return boolean
function _type_ScriptOutput.ShowText(text, durationSeconds) end

---@class ShootingTargetAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_ShootingTargetAdminToy = {}

--- A static sinkhole visible in the local scene.
---@class SinkholeHazard
---@field IsActive boolean # Read-only. The local hazard component's active state; this is not a server acceptance or damage guarantee.
---@field IsDestroyed boolean # Read-only.
---@field MaxDistance number # Read-only.
---@field MaxHeightDistance number # Read-only.
---@field Room Room|nil # Read-only.
local _type_SinkholeHazard = {}

---@class SpeakerAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_SpeakerAdminToy = {}

--- An observed change to the local spectator's player target.
---@class SpectatedPlayerChangedEventArgs
---@field CurrentPlayer Player|nil # Read-only. The new target, or null when the player target was cleared.
---@field Player Player|nil # Read-only.
---@field PreviousPlayer Player|nil # Read-only. The previous target, or null when no player was being spectated.
local _type_SpectatedPlayerChangedEventArgs = {}

--- An SCP-173 tantrum visible in the local scene.
---@class TantrumHazard
---@field IsActive boolean # Read-only. The local hazard component's active state; this is not a server acceptance or damage guarantee.
---@field IsDestroyed boolean # Read-only.
---@field MaxDistance number # Read-only.
---@field MaxHeightDistance number # Read-only.
---@field Room Room|nil # Read-only.
local _type_TantrumHazard = {}

--- Wrapper around Il2Cpp.TeslaGate. Exposes the in-progress flag and inactivity timer for HUD overlays (pre-fire warnings, tesla counters, faction-tinted gate visualization).
---@class Tesla
---@field InProgress boolean # Read-only. True while the tesla is in its shock animation.
---@field InactiveTime number # Read-only. Seconds the gate has been idle since the last burst.
---@field IsDestroyed boolean # Read-only.
---@field Room Room|nil # Read-only. The Anomaly.Client.Api.Features.Tesla.Room the tesla is mounted in, or null if not placed in any tracked room.
local _type_Tesla = {}

--- Wraps a Anomaly.Client.Api.Events.Args.TeslaEventArgs.Tesla reference for tesla lifecycle / burst events.
---@class TeslaEventArgs
---@field Tesla Tesla|nil # Read-only.
local _type_TeslaEventArgs = {}

--- Read-only wrapper for client-observed TextToy state.
---@class TextAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_TextAdminToy = {}

--- Wrapper around Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem. Exposes the weak/full throw settings (force, fuse) for HUD overlays (throw-strength indicator, fuse-time tooltips, custom telemetry).
---@class ThrowableItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_ThrowableItem = {}

--- Wrapper around a world-spawned Anomaly.Client.Api.Features.TimedGrenadePickup.TimedGrenadePickupBase — a dropped (un-thrown) grenade that's still in its idle state. Once thrown, grenades become Anomaly.Client.Api.Features.Projectiles instead.
---@class TimedGrenadePickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_TimedGrenadePickup = {}

--- Read-only client wrapper for TimeGrenade.
---@class TimedGrenadeProjectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_TimedGrenadeProjectile = {}

--- A usable item, including consumables and wearable SCP items.
---@class UsableItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category Enum_ItemCategory # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type Enum_ItemType # Read-only.
---@field Weight number # Read-only.
local _type_UsableItem = {}

--- A cooldown notification received for an item in the local inventory.
---@class UsableItemCooldownEventArgs
---@field Item Item|nil # Read-only.
---@field ItemSerial integer # Read-only.
---@field Player Player|nil # Read-only.
---@field RemainingTime number # Read-only. Seconds supplied in this notification, not a continuously updated countdown.
local _type_UsableItemCooldownEventArgs = {}

--- An observed server use-start/cancel notification. This is not a cancellable input request or a use-completed event.
---@class UsableItemStatusEventArgs
---@field Item Item|nil # Read-only. The item when it is resolvable locally; may be null after a switch/removal or for hidden inventory.
---@field ItemSerial integer # Read-only.
---@field Player Player|nil # Read-only.
local _type_UsableItemStatusEventArgs = {}

---@class VoiceMessageEventArgs
---@field Packet VoiceMessagePacket|nil # Read-only.
local _type_VoiceMessageEventArgs = {}

---@class VoiceMessagePacket
---@field Channel Enum_VoiceChat_002e_VoiceChatChannel # Read-only.
---@field DataLength integer # Read-only.
---@field IsIncoming boolean # Read-only.
---@field IsOutgoing boolean # Read-only.
---@field Speaker Player|nil # Read-only.
---@field SpeakerNull boolean # Read-only.
local _type_VoiceMessagePacket = {}

---@class VoiceMessageReceivedEventArgs
---@field Packet VoiceMessagePacket|nil # Read-only.
local _type_VoiceMessageReceivedEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class VoiceMessageReceivingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Packet VoiceMessagePacket|nil # Read-only.
local _type_VoiceMessageReceivingEventArgs = {}

--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
---@class VoiceMessageSendingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Packet VoiceMessagePacket|nil # Read-only.
local _type_VoiceMessageSendingEventArgs = {}

---@class VoiceMessageSentEventArgs
---@field Packet VoiceMessagePacket|nil # Read-only.
local _type_VoiceMessageSentEventArgs = {}

--- A client-observed alpha warhead detonation.
---@class WarheadDetonatedEventArgs
local _type_WarheadDetonatedEventArgs = {}

--- Fires when Il2Cpp.AlphaWarheadController's InProgress state transitions. Anomaly.Client.Api.Events.Args.WarheadProgressChangedEventArgs.InProgress reflects the new state.
---@class WarheadProgressChangedEventArgs
---@field InProgress boolean # Read-only. True when the countdown is now running, false when it stopped (cancelled or detonated).
local _type_WarheadProgressChangedEventArgs = {}

--- Read-only wrapper for client-observed WaypointToy state.
---@class WaypointAdminToy
---@field CommandName string|nil # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean # Read-only.
---@field MovementSmoothing integer # Read-only.
local _type_WaypointAdminToy = {}

--- A client-observed attachment workstation. Interaction remains server-authoritative.
---@class Workstation
---@field IsDestroyed boolean # Read-only.
---@field Room Room|nil # Read-only.
---@field Status Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_WorkstationController_002e_WorkstationStatus # Read-only. The replicated workstation status from the game.
local _type_Workstation = {}

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_AdminToy
---@field CommandName string|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsStatic boolean|nil # Read-only. Nil when captured after destruction.
---@field MovementSmoothing integer|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AdminToyAddedEventArgs
---@field AdminToy EventSnapshot_AdminToy|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AdminToyRemovedEventArgs
---@field AdminToy EventSnapshot_AdminToy|nil # Read-only.
local _snapshot = {}
end

do
--- Fired when CASSIE schedules a line for playback (before audio starts). Pair with Anomaly.Client.Api.Events.Args.AnnouncerLineStartedEventArgs for the started-playing notification.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AnnouncerLineScheduledEventArgs
---@field ApiName string|nil # Read-only. Convenience: API name (e.g. "ALERT") of the scheduled line.
---@field ScheduledStartDspTime number # Read-only. The DSP-time at which playback is scheduled to begin. Compare against UnityEngine.AudioSettings.dspTime to compute lead time.
---@field Word EventSnapshot_AnnouncerWord|nil # Read-only. Wrapped Anomaly.Client.Api.Features.AnnouncerWord for ergonomic access.
local _snapshot = {}
end

do
--- Fired when a CASSIE line begins audible playback (engine's CassieLineSource.OnStartedPlaying static event). Useful for mods that want to flash subtitles in sync with audio.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AnnouncerLineStartedEventArgs
---@field ApiName string|nil # Read-only. Convenience: API name (e.g. "ALERT") of the line.
---@field Word EventSnapshot_AnnouncerWord|nil # Read-only. Wrapped Anomaly.Client.Api.Features.AnnouncerWord for ergonomic access.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AnnouncerWord
---@field ApiName string|nil # Read-only.
---@field Duration number # Read-only.
---@field Type Enum_Cassie_002e_CassieClipCategory # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AnomalyMessageReceivedEventArgs
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AssetOverrideAppliedEventArgs
---@field ComponentPath string|nil # Read-only.
---@field MaterialIndex integer # Read-only.
---@field NetId integer # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AssetOverrideClearedEventArgs
---@field ComponentPath string|nil # Read-only.
---@field MaterialIndex integer # Read-only.
---@field NetId integer # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_AudioPlayingEventArgs
---@field Channel Enum_AudioPooling_002e_MixerChannel # Read-only. Which mixer channel the sound was going to play on (DefaultSfx, Voice, etc.).
---@field ClipName string|nil # Read-only. The local clip name, or an empty string when no clip is available.
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Volume number # Read-only. Volume scalar in [0, 1].
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_BroadcastShowingEventArgs
---@field DurationSeconds integer # Read-only. How long the broadcast was intended to display, in seconds.
---@field Flags integer # Read-only. Raw Broadcast.BroadcastFlags byte (Truncated, AdminChat, etc.).
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Text string|nil # Read-only. The broadcast text the server sent.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_CameraShakingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_CanvasReadyEventArgs
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_ClientConnectedEventArgs
---@field ServerIp string|nil # Read-only.
---@field ServerPort integer # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_ClientDisconnectedEventArgs
---@field ServerIp string|nil # Read-only.
---@field ServerPort integer # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_ClientReadyEventArgs
---@field SceneName string|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_CurrentItemChangedEventArgs
---@field CurrentSerial integer # Read-only. The serial held after the observed change.
---@field CurrentType Enum_ItemType # Read-only. The item type held after the observed change.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field PreviousSerial integer # Read-only. The serial held before the observed change.
---@field PreviousType Enum_ItemType # Read-only. The item type held before the observed change.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_CurrentItemChangingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field PreviousSerial integer # Read-only. The serial held before this local switch request.
---@field TargetSerial integer # Read-only. Serial of the item being switched to (0 for holster/no item).
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Door
---@field ActiveLocks integer|nil # Read-only. Nil when captured after destruction.
---@field CanSeeThrough boolean|nil # Read-only. Nil when captured after destruction.
---@field Id integer|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsMoving boolean|nil # Read-only. Nil when captured after destruction.
---@field IsOpen boolean|nil # Read-only. Nil when captured after destruction.
---@field Name string|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_DoorEventArgs
---@field Door EventSnapshot_Door|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_DoorLocksChangedEventArgs
---@field ActiveLocks integer # Read-only.
---@field Door EventSnapshot_Door|nil # Read-only.
---@field PreviousLocks integer # Read-only.
local _snapshot = {}
end

do
--- An observed target-state change, not a cancellable interaction or proof that the door finished moving.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_DoorStateChangedEventArgs
---@field Door EventSnapshot_Door|nil # Read-only.
---@field IsOpen boolean # Read-only.
---@field PreviousIsOpen boolean # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_EffectDisabledEventArgs
---@field Effect EventSnapshot_PlayerEffect|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_EffectEnabledEventArgs
---@field Effect EventSnapshot_PlayerEffect|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_EffectIntensityChangedEventArgs
---@field CurrentIntensity integer # Read-only.
---@field Effect EventSnapshot_PlayerEffect|nil # Read-only.
---@field PreviousIntensity integer # Read-only.
local _snapshot = {}
end

do
--- Wrapper around Il2CppInteractables.Interobjects.ElevatorChamber. Exposes sequence state, destination level, and direction for floor-indicator HUDs and elevator-music mods. Read-only — call/recall is server-authoritative.
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Elevator
---@field CurrentSequence Enum_Interactables_002e_Interobjects_002e_ElevatorChamber_002e_ElevatorSequence|nil # Read-only. The current sequence state (Ready / Arrived / DoorOpening / Moving / DoorClosing). Nil when captured after destruction.
---@field DestinationLevel integer|nil # Read-only. The level the chamber is currently moving to (or has stopped at). Nil when captured after destruction.
---@field GoingUp boolean|nil # Read-only. True if the chamber is currently traveling upward. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsReady boolean|nil # Read-only. True if the chamber is in Il2CppInteractables.Interobjects.ElevatorChamber.ElevatorSequence.Ready phase. Nil when captured after destruction.
---@field IsReadyForUserInput boolean|nil # Read-only. True if the chamber is ready to accept user input (e.g. a button press). Nil when captured after destruction.
---@field NextLevel integer|nil # Read-only. The level above the current one. Nil when captured after destruction.
---@field PreviousLevel integer|nil # Read-only. The level below the current one. Nil when captured after destruction.
---@field Room EventSnapshot_Room|nil # Read-only. The room containing the chamber, or null. Nil when captured after destruction.
---@field SequenceElapsed number|nil # Read-only. Seconds elapsed in the current sequence phase. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Wraps an Anomaly.Client.Api.Events.Args.ElevatorEventArgs.Elevator reference for chamber lifecycle / movement events.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_ElevatorEventArgs
---@field Elevator EventSnapshot_Elevator|nil # Read-only.
local _snapshot = {}
end

do
--- The server-disclosed enrollment identifier changed or was cleared.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_EnrollmentChangedEventArgs
---@field Current string|nil # Read-only.
---@field Previous string|nil # Read-only.
local _snapshot = {}
end

do
--- Wrapper around Il2CppPlayerRoles.PlayableScps.Scp079.Cameras.Scp079Camera — the in-facility CCTV cameras that SCP-079 uses. Useful for SCP-079 spectator UIs, camera-feed mods, minimap markers showing camera coverage, etc.
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_FacilityCamera
---@field HorizontalRotation number|nil # Read-only. Nil when captured after destruction.
---@field IsActive boolean|nil # Read-only. True if SCP-079 is currently viewing through this camera. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsMain boolean|nil # Read-only. True if this is the room's "main" camera (the one SCP-079 enters by default for the room). Nil when captured after destruction.
---@field IsUsedByLocalPlayer boolean|nil # Read-only. True if the local player is the SCP-079 currently using this camera. Nil when captured after destruction.
---@field Label string|nil # Read-only. Display label for the camera (e.g. "Heavy Containment Zone Camera 7"). Nil when captured after destruction.
---@field RollRotation number|nil # Read-only. Nil when captured after destruction.
---@field VerticalRotation number|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Wraps a Anomaly.Client.Api.Features.FacilityCamera reference for CCTV lifecycle / state events.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_FacilityCameraEventArgs
---@field Camera EventSnapshot_FacilityCamera|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_FileTransferCompleteEventArgs
---@field FileCount integer # Read-only.
---@field Success boolean # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_FileTransferProgressEventArgs
---@field CompletedFiles integer # Read-only.
---@field FileName string|nil # Read-only.
---@field Percent number # Read-only.
---@field ReceivedBytes integer # Read-only.
---@field TotalBytes integer # Read-only.
---@field TotalFiles integer # Read-only.
local _snapshot = {}
end

do
--- Wrapper around Il2CppInventorySystem.Items.Firearms.Firearm. Exposes magazine state, fire-mode info, and module access for HUD overlays (custom ammo counters, hit-marker mods, attachment indicators) without requiring consumers to dig through the modular firearm subcomponent system.
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_FirearmItem
---@field CanDrop boolean|nil # Read-only. Nil when captured after destruction.
---@field CanEquip boolean|nil # Read-only. Nil when captured after destruction.
---@field CanHolster boolean|nil # Read-only. Nil when captured after destruction.
---@field Category Enum_ItemCategory|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean|nil # Read-only. Nil when captured after destruction.
---@field IsLocalPlayerItem boolean|nil # Read-only. Nil when captured after destruction.
---@field Serial integer|nil # Read-only. Nil when captured after destruction.
---@field Type Enum_ItemType|nil # Read-only. Nil when captured after destruction.
---@field Weight number|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_FirearmShotEventArgs
---@field Firearm Enum_ItemType # Read-only.
local _snapshot = {}
end

do
--- A client-observed firearm snapshot. Null fields are unavailable, never an inferred zero or false.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_FirearmState
---@field IsAiming boolean|nil # Read-only.
---@field IsReloading boolean|nil # Read-only. Local-owner reload prediction. Unavailable on remote representations.
---@field IsUnloading boolean|nil # Read-only.
---@field MagazineAmmo integer|nil # Read-only.
---@field MagazineCapacity integer|nil # Read-only.
---@field MagazineInserted boolean|nil # Read-only.
local _snapshot = {}
end

do
--- A change in the client's observed firearm state, including first observation of available fields.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_FirearmStateChangedEventArgs
---@field Current EventSnapshot_FirearmState|nil # Read-only.
---@field Firearm EventSnapshot_FirearmItem|nil # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous EventSnapshot_FirearmState|nil # Read-only. Null when this is the first observation of this item in the session.
local _snapshot = {}
end

do
--- Wrapper around Il2CppMapGeneration.Distributors.Scp079Generator. Exposes activation / engagement state, cooldown timing, and door state for HUD overlays (charge percentages, "generators remaining" counters, faction-tinted glow indicators).
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Generator
---@field IsActivating boolean|nil # Read-only. True while the generator is in its activation countdown phase (lever pulled, not yet engaged). Nil when captured after destruction.
---@field IsActivationReady boolean|nil # Read-only. True if the generator's activation cooldown is satisfied (matches game's ActivationReady). Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsEngaged boolean|nil # Read-only. True once the generator has finished its activation sequence. Nil when captured after destruction.
---@field IsOpen boolean|nil # Read-only. True if the door panel is open (lever / interior accessible). Nil when captured after destruction.
---@field IsUnlocked boolean|nil # Read-only. True if the unlock cooldown has elapsed and the generator can be engaged. Nil when captured after destruction.
---@field Room EventSnapshot_Room|nil # Read-only. The Anomaly.Client.Api.Features.Generator.Room containing this generator, or null if the generator isn't placed in any tracked room. Nil when captured after destruction.
---@field TotalActivationTime number|nil # Read-only. Total seconds the activation sequence takes from lever-pull to engaged. Nil when captured after destruction.
---@field TotalDeactivationTime number|nil # Read-only. Total seconds the deactivation sequence takes. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Fires when an Il2CppMapGeneration.Distributors.Scp079Generator reaches the engaged state.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_GeneratorEngagedEventArgs
---@field Generator EventSnapshot_Generator|nil # Read-only. The generator that just engaged.
local _snapshot = {}
end

do
--- An observed generator flag transition after replication, without an inferred interacting player.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_GeneratorStateChangedEventArgs
---@field Flags Enum_MapGeneration_002e_Distributors_002e_Scp079Generator_002e_GeneratorFlags|string # Read-only.
---@field Generator EventSnapshot_Generator|nil # Read-only.
---@field PreviousFlags Enum_MapGeneration_002e_Distributors_002e_Scp079Generator_002e_GeneratorFlags|string # Read-only.
local _snapshot = {}
end

do
--- A hazard known to the local scene. This does not report server damage or the server's affected-player list.
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Hazard
---@field IsActive boolean|nil # Read-only. The local hazard component's active state; this is not a server acceptance or damage guarantee. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field MaxDistance number|nil # Read-only. Nil when captured after destruction.
---@field MaxHeightDistance number|nil # Read-only. Nil when captured after destruction.
---@field Room EventSnapshot_Room|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_HazardEventArgs
---@field Hazard EventSnapshot_Hazard|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_HintDisplayingEventArgs
---@field DurationSeconds number # Read-only. Display duration in seconds (0 if not provided by the hint).
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Text string|nil # Read-only. The raw hint text. May contain rich-text tags and translation tokens.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_InventoryChangedEventArgs
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_InventoryUiClosingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_InventoryUiOpeningEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Item
---@field CanDrop boolean|nil # Read-only. Nil when captured after destruction.
---@field CanEquip boolean|nil # Read-only. Nil when captured after destruction.
---@field CanHolster boolean|nil # Read-only. Nil when captured after destruction.
---@field Category Enum_ItemCategory|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean|nil # Read-only. Nil when captured after destruction.
---@field IsLocalPlayerItem boolean|nil # Read-only. Nil when captured after destruction.
---@field Serial integer|nil # Read-only. Nil when captured after destruction.
---@field Type Enum_ItemType|nil # Read-only. Nil when captured after destruction.
---@field Weight number|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Wrapper around Il2CppMapGeneration.Distributors.Locker. Aggregates the locker's Il2CppMapGeneration.Distributors.LockerChamber array for chamber-aware HUDs (loot indicators, "what's inside" overlays, faction-themed locker tints).
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Locker
---@field IsDestroyed boolean # Read-only.
---@field OpenedChambersMask integer|nil # Read-only. Bitmask of currently-opened chambers (bit N set ⇒ chamber N is open). Nil when captured after destruction.
---@field Room EventSnapshot_Room|nil # Read-only. The room containing the locker, or null if not in any tracked room. Nil when captured after destruction.
local _snapshot = {}
end

do
--- An updated replicated chamber mask. Local door animations may finish later.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_LockerStateChangedEventArgs
---@field ChangedChambers integer # Read-only.
---@field Locker EventSnapshot_Locker|nil # Read-only.
---@field OpenedChambers integer # Read-only.
---@field PreviousOpenedChambers integer # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_MotionDiscontinuity
---@field Grounded boolean # Read-only.
---@field Sequence integer # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Pickup
---@field CanSearch boolean|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean|nil # Read-only. Nil when captured after destruction.
---@field IsLocked boolean|nil # Read-only. Nil when captured after destruction.
---@field Serial integer|nil # Read-only. Nil when captured after destruction.
---@field Type Enum_ItemType|nil # Read-only. Nil when captured after destruction.
---@field Weight number|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PickupAddedEventArgs
---@field Pickup EventSnapshot_Pickup|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PickupRemovedEventArgs
---@field Pickup EventSnapshot_Pickup|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Player
---@field DisplayName string|nil # Read-only. The game's display name, including a server-provided custom name when present. Nil when captured after destruction.
---@field IsAlive boolean|nil # Read-only. Nil when captured after destruction.
---@field IsCrouching boolean|nil # Read-only. True if the player is in the crouching or sneaking state. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only. Whether the underlying player object has been destroyed.
---@field IsGrounded boolean|nil # Read-only. True if the player's FPC module reports being grounded. Returns false when the module is unavailable (non-FPC roles). Nil when captured after destruction.
---@field IsHuman boolean|nil # Read-only. Nil when captured after destruction.
---@field IsLocal boolean|nil # Read-only. Nil when captured after destruction.
---@field IsScp boolean|nil # Read-only. Nil when captured after destruction.
---@field IsSprinting boolean|nil # Read-only. True if the player is in the sprinting state. Nil when captured after destruction.
---@field LifeId integer|nil # Read-only. The current role's life identifier, or -1 when no role is available. Nil when captured after destruction.
---@field Nickname string|nil # Read-only. The nickname known to this client, or an empty string when unavailable. Nil when captured after destruction.
---@field PlayerId integer|nil # Read-only. The replicated player ID, or -1 after the player object is destroyed. Nil when captured after destruction.
---@field RoleType Enum_PlayerRoles_002e_RoleTypeId|nil # Read-only. The current role ID, or Il2CppPlayerRoles.RoleTypeId.None when unavailable. Nil when captured after destruction.
---@field Team Enum_PlayerRoles_002e_Team|nil # Read-only. The current team, or Il2CppPlayerRoles.Team.Dead when no role is available. Nil when captured after destruction.
---@field Zone Enum_MapGeneration_002e_FacilityZone|nil # Read-only. The facility zone the player is currently in. Returns Il2CppMapGeneration.FacilityZone.None if the player isn't in any tracked room. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerAddedEventArgs
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerDroppingItemEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Item EventSnapshot_Item|nil # Read-only. The item when it can be resolved from the client cache.
---@field ItemSerial integer # Read-only. Serial of the item the player is attempting to drop.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field TryThrow boolean # Read-only. True if the player is performing a drop-and-throw (G + movement), false for a plain drop.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerEffect
---@field CanEnable boolean # Read-only.
---@field Classification string|nil # Read-only.
---@field Duration number # Read-only.
---@field Intensity integer # Read-only.
---@field IsEnabled boolean # Read-only.
---@field IsLocalPlayerEffect boolean # Read-only.
---@field IsSpectatedEffect boolean # Read-only.
---@field MaxIntensity integer # Read-only.
---@field Name string|nil # Read-only.
---@field TimeLeft number # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerHealedEventArgs
---@field AmountHealed number # Read-only.
---@field CurrentHealth number # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field PreviousHealth number # Read-only.
local _snapshot = {}
end

do
--- Fires when a tracked player's HumeShieldStat value crosses a polling tick boundary. Delta is signed — negative for shield damage, positive for regeneration. The convenience Anomaly.Client.Api.Events.Args.PlayerHumeShieldChangedEventArgs.Broke property is true on the tick where the shield drops to zero from a non-zero value (mirrors LabAPI's server-side HumeShieldBroken event).
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerHumeShieldChangedEventArgs
---@field Broke boolean # Read-only. True if the shield broke (dropped to zero) on this tick.
---@field Current number # Read-only.
---@field Delta number # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous number # Read-only.
local _snapshot = {}
end

do
--- A resolved local interaction after the local callback, before any server command. This is a notification, not proof of server acceptance or a server-side veto point.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerInteractedEventArgs
---@field ColliderId integer # Read-only.
---@field Door EventSnapshot_Door|nil # Read-only.
---@field Generator EventSnapshot_Generator|nil # Read-only.
---@field Locker EventSnapshot_Locker|nil # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Workstation EventSnapshot_Workstation|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerInteractingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerJumpingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field JumpSpeed number # Read-only. The jump speed the engine was about to apply (pre-multiplier).
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerNoclipTogglingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerRemovedEventArgs
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerRoleChangedEventArgs
---@field NewRole EventSnapshot_Role|nil # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field PreviousRole EventSnapshot_Role|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerShootingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerSprintingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Fires when a tracked player's StaminaStat value crosses a polling tick boundary. Delta is signed — negative for stamina drain, positive for regeneration.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerStaminaChangedEventArgs
---@field Current number # Read-only.
---@field Delta number # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous number # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_PlayerStat
---@field IsDestroyed boolean # Read-only.
---@field IsObservable boolean|nil # Read-only. Whether this stat's local synchronization policy permits observation by the current local player. This is not a receipt or freshness guarantee. Server plugins can customize synchronization; raw values remain the last local values. Nil when captured after destruction.
---@field Name string|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- A change between observable local samples; not authoritative damage attribution.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerStatChangedEventArgs
---@field Current number # Read-only.
---@field Delta number # Read-only.
---@field Maximum number # Read-only.
---@field MaximumChanged boolean # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous number # Read-only.
---@field PreviousMaximum number # Read-only.
---@field Stat EventSnapshot_PlayerStat|nil # Read-only.
---@field ValueChanged boolean # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_PlayerThrowingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Item EventSnapshot_ThrowableItem|nil # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Projectile
---@field CanSearch boolean|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean|nil # Read-only. Nil when captured after destruction.
---@field IsLocked boolean|nil # Read-only. Nil when captured after destruction.
---@field Serial integer|nil # Read-only. Nil when captured after destruction.
---@field Type Enum_ItemType|nil # Read-only. Nil when captured after destruction.
---@field Weight number|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_ProjectileRemovedEventArgs
---@field Projectile EventSnapshot_Projectile|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_ProjectileSpawnedEventArgs
---@field Projectile EventSnapshot_Projectile|nil # Read-only.
local _snapshot = {}
end

do
--- Wrapper around Il2CppPlayerRoles.Ragdolls.BasicRagdoll. Exposes the death snapshot (role, owner, nickname, creation time) for death-cam mods, ragdoll-cleanup mods, and "who killed whom" replays.
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Ragdoll
---@field CreationTime number|nil # Read-only. Engine creation timestamp (DSP-time-style double). Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsFrozen boolean|nil # Read-only. True if the ragdoll has been frozen (no further physics). Nil when captured after destruction.
---@field Nickname string|nil # Read-only. The nickname of the dead player at time of death. Nil when captured after destruction.
---@field RoleType Enum_PlayerRoles_002e_RoleTypeId|nil # Read-only. The role the dead player was when the ragdoll was created. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Wraps a Anomaly.Client.Api.Events.Args.RagdollEventArgs.Ragdoll reference for spawn / removed events.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_RagdollEventArgs
---@field Ragdoll EventSnapshot_Ragdoll|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Role
---@field IsAlive boolean|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only. Whether the native role was destroyed, returned to its pool, or reused for a different life. Role wrappers should not be retained across lives.
---@field IsHuman boolean|nil # Read-only. Nil when captured after destruction.
---@field IsScp boolean|nil # Read-only. Nil when captured after destruction.
---@field LifeIdentifier integer|nil # Read-only. Nil when captured after destruction.
---@field Name string|nil # Read-only. Nil when captured after destruction.
---@field Team Enum_PlayerRoles_002e_Team|nil # Read-only. Nil when captured after destruction.
---@field Type Enum_PlayerRoles_002e_RoleTypeId|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Room
---@field IsDestroyed boolean # Read-only.
---@field Name string|nil # Read-only. Nil when captured after destruction.
---@field RoomName Enum_MapGeneration_002e_RoomName|nil # Read-only. Nil when captured after destruction.
---@field Shape Enum_MapGeneration_002e_RoomShape|nil # Read-only. Nil when captured after destruction.
---@field Zone Enum_MapGeneration_002e_FacilityZone|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Fired when the server-reported Anomaly.Shared.Networking.Messages.RoundPhase changes. Carries both the previous and new phases so handlers can react to specific transitions (e.g. only reset state when transitioning to Anomaly.Shared.Networking.Messages.RoundPhase.Restarted).
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_RoundPhaseChangedEventArgs
---@field NewPhase Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase # Read-only.
---@field PreviousPhase Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_SceneLoadedEventArgs
---@field SceneName string|nil # Read-only. The loaded scene's name, without exposing its native handle to scripts.
local _snapshot = {}
end

do
--- A newly received SCP-079 auxiliary power observation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_Scp079AuxiliaryPowerChangedEventArgs
---@field Current number # Read-only.
---@field IsInitialObservation boolean # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous number|nil # Read-only. The previous received value, or null before the first update in this viewing context.
local _snapshot = {}
end

do
--- A newly received SCP-079 total-experience observation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_Scp079ExperienceChangedEventArgs
---@field Current integer # Read-only.
---@field IsInitialObservation boolean # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous integer|nil # Read-only. The previous received value, or null before the first update for this role life.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_Scp079SwitchingCameraEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field TargetCamera EventSnapshot_FacilityCamera|nil # Read-only. The camera the local SCP-079 is switching to.
local _snapshot = {}
end

do
--- A newly received SCP-079 access-tier observation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_Scp079TierChangedEventArgs
---@field Current integer # Read-only. The newly observed one-based tier.
---@field IsInitialObservation boolean # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field Previous integer|nil # Read-only. The previous one-based tier, or null before the first update for this role life.
local _snapshot = {}
end

do
--- An observed change to the local spectator's player target.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_SpectatedPlayerChangedEventArgs
---@field CurrentPlayer EventSnapshot_Player|nil # Read-only. The new target, or null when the player target was cleared.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field PreviousPlayer EventSnapshot_Player|nil # Read-only. The previous target, or null when no player was being spectated.
local _snapshot = {}
end

do
--- Wrapper around Il2Cpp.TeslaGate. Exposes the in-progress flag and inactivity timer for HUD overlays (pre-fire warnings, tesla counters, faction-tinted gate visualization).
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Tesla
---@field InProgress boolean|nil # Read-only. True while the tesla is in its shock animation. Nil when captured after destruction.
---@field InactiveTime number|nil # Read-only. Seconds the gate has been idle since the last burst. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field Room EventSnapshot_Room|nil # Read-only. The Anomaly.Client.Api.Features.Tesla.Room the tesla is mounted in, or null if not placed in any tracked room. Nil when captured after destruction.
local _snapshot = {}
end

do
--- Wraps a Anomaly.Client.Api.Events.Args.TeslaEventArgs.Tesla reference for tesla lifecycle / burst events.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_TeslaEventArgs
---@field Tesla EventSnapshot_Tesla|nil # Read-only.
local _snapshot = {}
end

do
--- Wrapper around Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableItem. Exposes the weak/full throw settings (force, fuse) for HUD overlays (throw-strength indicator, fuse-time tooltips, custom telemetry).
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_ThrowableItem
---@field CanDrop boolean|nil # Read-only. Nil when captured after destruction.
---@field CanEquip boolean|nil # Read-only. Nil when captured after destruction.
---@field CanHolster boolean|nil # Read-only. Nil when captured after destruction.
---@field Category Enum_ItemCategory|nil # Read-only. Nil when captured after destruction.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean|nil # Read-only. Nil when captured after destruction.
---@field IsLocalPlayerItem boolean|nil # Read-only. Nil when captured after destruction.
---@field Serial integer|nil # Read-only. Nil when captured after destruction.
---@field Type Enum_ItemType|nil # Read-only. Nil when captured after destruction.
---@field Weight number|nil # Read-only. Nil when captured after destruction.
local _snapshot = {}
end

do
--- A cooldown notification received for an item in the local inventory.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_UsableItemCooldownEventArgs
---@field Item EventSnapshot_Item|nil # Read-only.
---@field ItemSerial integer # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
---@field RemainingTime number # Read-only. Seconds supplied in this notification, not a continuously updated countdown.
local _snapshot = {}
end

do
--- An observed server use-start/cancel notification. This is not a cancellable input request or a use-completed event.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_UsableItemStatusEventArgs
---@field Item EventSnapshot_Item|nil # Read-only. The item when it is resolvable locally; may be null after a switch/removal or for hidden inventory.
---@field ItemSerial integer # Read-only.
---@field Player EventSnapshot_Player|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_VoiceMessagePacket
---@field Channel Enum_VoiceChat_002e_VoiceChatChannel # Read-only.
---@field DataLength integer # Read-only.
---@field IsIncoming boolean # Read-only.
---@field IsOutgoing boolean # Read-only.
---@field Speaker EventSnapshot_Player|nil # Read-only.
---@field SpeakerNull boolean # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_VoiceMessageReceivedEventArgs
---@field Packet EventSnapshot_VoiceMessagePacket|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_VoiceMessageReceivingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Packet EventSnapshot_VoiceMessagePacket|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation. IsAllowed reports permission at capture time; Lua cannot cancel or modify the originating operation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_VoiceMessageSendingEventArgs
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Packet EventSnapshot_VoiceMessagePacket|nil # Read-only.
local _snapshot = {}
end

do
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_VoiceMessageSentEventArgs
---@field Packet EventSnapshot_VoiceMessagePacket|nil # Read-only.
local _snapshot = {}
end

do
--- A client-observed alpha warhead detonation.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_WarheadDetonatedEventArgs
local _snapshot = {}
end

do
--- Fires when Il2Cpp.AlphaWarheadController's InProgress state transitions. Anomaly.Client.Api.Events.Args.WarheadProgressChangedEventArgs.InProgress reflects the new state.
--- Frozen queued event observation; not a live host object.
---@class EventSnapshot_WarheadProgressChangedEventArgs
---@field InProgress boolean # Read-only. True when the countdown is now running, false when it stopped (cancelled or detonated).
local _snapshot = {}
end

do
--- A client-observed attachment workstation. Interaction remains server-authoritative.
--- Frozen queued event observation; not a live host object.
--- When IsDestroyed is true, every other field is nil.
---@class EventSnapshot_Workstation
---@field IsDestroyed boolean # Read-only.
---@field Room EventSnapshot_Room|nil # Read-only. Nil when captured after destruction.
---@field Status Enum_InventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_WorkstationController_002e_WorkstationStatus|nil # Read-only. The replicated workstation status from the game. Nil when captured after destruction.
local _snapshot = {}
end

---@type GameContext
Game = nil -- Host-installed read-only local queries; no constructor is exposed.

---@type ScriptOutput
Output = nil -- Host-owned bounded logging and plain text; automatically revoked with this session.

-- Host-installed queued subscription registry. Frozen observations cannot cancel or modify the original operation.
Events = {}
Events.AdminToy = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.AdminToy.Added = {}
---@param callback fun(ev: EventSnapshot_AdminToyAddedEventArgs|nil)
---@return string subscriptionToken
function Events.AdminToy.Added.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AdminToyAddedEventArgs|nil)|string
---@return boolean removed
function Events.AdminToy.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.AdminToy.Removed = {}
---@param callback fun(ev: EventSnapshot_AdminToyRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.AdminToy.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AdminToyRemovedEventArgs|nil)|string
---@return boolean removed
function Events.AdminToy.Removed.Remove(callbackOrToken) end
Events.Announcer = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Announcer.LineScheduled = {}
---@param callback fun(ev: EventSnapshot_AnnouncerLineScheduledEventArgs|nil)
---@return string subscriptionToken
function Events.Announcer.LineScheduled.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AnnouncerLineScheduledEventArgs|nil)|string
---@return boolean removed
function Events.Announcer.LineScheduled.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Announcer.LineStarted = {}
---@param callback fun(ev: EventSnapshot_AnnouncerLineStartedEventArgs|nil)
---@return string subscriptionToken
function Events.Announcer.LineStarted.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AnnouncerLineStartedEventArgs|nil)|string
---@return boolean removed
function Events.Announcer.LineStarted.Remove(callbackOrToken) end
Events.Asset = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Asset.OverrideApplied = {}
---@param callback fun(ev: EventSnapshot_AssetOverrideAppliedEventArgs|nil)
---@return string subscriptionToken
function Events.Asset.OverrideApplied.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AssetOverrideAppliedEventArgs|nil)|string
---@return boolean removed
function Events.Asset.OverrideApplied.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Asset.OverrideCleared = {}
---@param callback fun(ev: EventSnapshot_AssetOverrideClearedEventArgs|nil)
---@return string subscriptionToken
function Events.Asset.OverrideCleared.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AssetOverrideClearedEventArgs|nil)|string
---@return boolean removed
function Events.Asset.OverrideCleared.Remove(callbackOrToken) end
Events.Audio = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Audio.Playing = {}
---@param callback fun(ev: EventSnapshot_AudioPlayingEventArgs|nil)
---@return string subscriptionToken
function Events.Audio.Playing.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AudioPlayingEventArgs|nil)|string
---@return boolean removed
function Events.Audio.Playing.Remove(callbackOrToken) end
Events.Camera = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Camera.Shaking = {}
---@param callback fun(ev: EventSnapshot_CameraShakingEventArgs|nil)
---@return string subscriptionToken
function Events.Camera.Shaking.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_CameraShakingEventArgs|nil)|string
---@return boolean removed
function Events.Camera.Shaking.Remove(callbackOrToken) end
Events.Client = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.CanvasReady = {}
---@param callback fun(ev: EventSnapshot_CanvasReadyEventArgs|nil)
---@return string subscriptionToken
function Events.Client.CanvasReady.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_CanvasReadyEventArgs|nil)|string
---@return boolean removed
function Events.Client.CanvasReady.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.Connected = {}
---@param callback fun(ev: EventSnapshot_ClientConnectedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Connected.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ClientConnectedEventArgs|nil)|string
---@return boolean removed
function Events.Client.Connected.Remove(callbackOrToken) end
--- Reports a transport disconnect, including unsuccessful handshake and retry attempts that never reached Anomaly.Client.Api.Events.Handlers.ClientEvents.Ready. Cleanup handlers must be idempotent; do not count this as one event per completed gameplay session.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.Disconnected = {}
---@param callback fun(ev: EventSnapshot_ClientDisconnectedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Disconnected.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ClientDisconnectedEventArgs|nil)|string
---@return boolean removed
function Events.Client.Disconnected.Remove(callbackOrToken) end
--- Enrollment was disclosed or cleared. This does not replace transport or scene readiness.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.EnrollmentChanged = {}
---@param callback fun(ev: EventSnapshot_EnrollmentChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.EnrollmentChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_EnrollmentChangedEventArgs|nil)|string
---@return boolean removed
function Events.Client.EnrollmentChanged.Remove(callbackOrToken) end
--- Reports the client's scene-ready lifecycle callback. This can precede the local player hub, populated world collections, and server enrollment disclosure.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.Ready = {}
---@param callback fun(ev: EventSnapshot_ClientReadyEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Ready.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ClientReadyEventArgs|nil)|string
---@return boolean removed
function Events.Client.Ready.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.SceneLoaded = {}
---@param callback fun(ev: EventSnapshot_SceneLoadedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.SceneLoaded.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_SceneLoadedEventArgs|nil)|string
---@return boolean removed
function Events.Client.SceneLoaded.Remove(callbackOrToken) end
Events.Door = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.Added = {}
---@param callback fun(ev: EventSnapshot_DoorEventArgs|nil)
---@return string subscriptionToken
function Events.Door.Added.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_DoorEventArgs|nil)|string
---@return boolean removed
function Events.Door.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.LocksChanged = {}
---@param callback fun(ev: EventSnapshot_DoorLocksChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Door.LocksChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_DoorLocksChangedEventArgs|nil)|string
---@return boolean removed
function Events.Door.LocksChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.Removed = {}
---@param callback fun(ev: EventSnapshot_DoorEventArgs|nil)
---@return string subscriptionToken
function Events.Door.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_DoorEventArgs|nil)|string
---@return boolean removed
function Events.Door.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.StateChanged = {}
---@param callback fun(ev: EventSnapshot_DoorStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Door.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_DoorStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Door.StateChanged.Remove(callbackOrToken) end
Events.Effect = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Effect.Disabled = {}
---@param callback fun(ev: EventSnapshot_EffectDisabledEventArgs|nil)
---@return string subscriptionToken
function Events.Effect.Disabled.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_EffectDisabledEventArgs|nil)|string
---@return boolean removed
function Events.Effect.Disabled.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Effect.Enabled = {}
---@param callback fun(ev: EventSnapshot_EffectEnabledEventArgs|nil)
---@return string subscriptionToken
function Events.Effect.Enabled.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_EffectEnabledEventArgs|nil)|string
---@return boolean removed
function Events.Effect.Enabled.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Effect.IntensityChanged = {}
---@param callback fun(ev: EventSnapshot_EffectIntensityChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Effect.IntensityChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_EffectIntensityChangedEventArgs|nil)|string
---@return boolean removed
function Events.Effect.IntensityChanged.Remove(callbackOrToken) end
Events.Elevator = {}
--- Fires when an elevator chamber starts a movement sequence (door close → travel → door open at destination).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Elevator.Moved = {}
---@param callback fun(ev: EventSnapshot_ElevatorEventArgs|nil)
---@return string subscriptionToken
function Events.Elevator.Moved.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ElevatorEventArgs|nil)|string
---@return boolean removed
function Events.Elevator.Moved.Remove(callbackOrToken) end
--- Fires when an elevator chamber is removed from the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Elevator.Removed = {}
---@param callback fun(ev: EventSnapshot_ElevatorEventArgs|nil)
---@return string subscriptionToken
function Events.Elevator.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ElevatorEventArgs|nil)|string
---@return boolean removed
function Events.Elevator.Removed.Remove(callbackOrToken) end
--- Fires when a new elevator chamber spawns into the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Elevator.Spawned = {}
---@param callback fun(ev: EventSnapshot_ElevatorEventArgs|nil)
---@return string subscriptionToken
function Events.Elevator.Spawned.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ElevatorEventArgs|nil)|string
---@return boolean removed
function Events.Elevator.Spawned.Remove(callbackOrToken) end
Events.FacilityCamera = {}
--- Fires when a CCTV camera is created (typically at scene load).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.Created = {}
---@param callback fun(ev: EventSnapshot_FacilityCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.Created.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FacilityCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.Created.Remove(callbackOrToken) end
--- Fires when a CCTV camera is destroyed.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.Removed = {}
---@param callback fun(ev: EventSnapshot_FacilityCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FacilityCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.Scp079Switching = {}
---@param callback fun(ev: EventSnapshot_Scp079SwitchingCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.Scp079Switching.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_Scp079SwitchingCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.Scp079Switching.Remove(callbackOrToken) end
--- Fires when any CCTV camera's active/inactive state changes (e.g. SCP-079 switches the active camera). Use Anomaly.Client.Api.Features.FacilityCamera.IsActive to observe the new state.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.StateChanged = {}
---@param callback fun(ev: EventSnapshot_FacilityCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FacilityCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.StateChanged.Remove(callbackOrToken) end
Events.FileTransfer = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FileTransfer.Complete = {}
---@param callback fun(ev: EventSnapshot_FileTransferCompleteEventArgs|nil)
---@return string subscriptionToken
function Events.FileTransfer.Complete.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FileTransferCompleteEventArgs|nil)|string
---@return boolean removed
function Events.FileTransfer.Complete.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FileTransfer.Progress = {}
---@param callback fun(ev: EventSnapshot_FileTransferProgressEventArgs|nil)
---@return string subscriptionToken
function Events.FileTransfer.Progress.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FileTransferProgressEventArgs|nil)|string
---@return boolean removed
function Events.FileTransfer.Progress.Remove(callbackOrToken) end
Events.Firearm = {}
--- Local equipped ADS state changed in an observed snapshot. Remote ADS-only updates are not tracked.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.AimingChanged = {}
---@param callback fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.AimingChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.AimingChanged.Remove(callbackOrToken) end
--- Magazine ammo, capacity or insertion state changed in an observed snapshot.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.AmmoChanged = {}
---@param callback fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.AmmoChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.AmmoChanged.Remove(callbackOrToken) end
--- Local equipped reload/unload prediction changed in an observed snapshot.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.ReloadStateChanged = {}
---@param callback fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.ReloadStateChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.ReloadStateChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.Shooting = {}
---@param callback fun(ev: EventSnapshot_PlayerShootingEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.Shooting.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerShootingEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.Shooting.Remove(callbackOrToken) end
--- Fires after a shot resolves. Observer-only.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.Shot = {}
---@param callback fun(ev: EventSnapshot_FirearmShotEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.Shot.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FirearmShotEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.Shot.Remove(callbackOrToken) end
--- Observer snapshot after magazine sync or a local equipped-firearm update. Includes first observation; intermediate changes between local updates can be coalesced. This is not proof that the server accepted a reload or input action.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.StateChanged = {}
---@param callback fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.StateChanged.Remove(callbackOrToken) end
Events.Generator = {}
--- Fires once per generator the moment it transitions to Engaged.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Generator.Engaged = {}
---@param callback fun(ev: EventSnapshot_GeneratorEngagedEventArgs|nil)
---@return string subscriptionToken
function Events.Generator.Engaged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_GeneratorEngagedEventArgs|nil)|string
---@return boolean removed
function Events.Generator.Engaged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Generator.StateChanged = {}
---@param callback fun(ev: EventSnapshot_GeneratorStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Generator.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_GeneratorStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Generator.StateChanged.Remove(callbackOrToken) end
Events.Hazard = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hazard.Added = {}
---@param callback fun(ev: EventSnapshot_HazardEventArgs|nil)
---@return string subscriptionToken
function Events.Hazard.Added.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_HazardEventArgs|nil)|string
---@return boolean removed
function Events.Hazard.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hazard.Removed = {}
---@param callback fun(ev: EventSnapshot_HazardEventArgs|nil)
---@return string subscriptionToken
function Events.Hazard.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_HazardEventArgs|nil)|string
---@return boolean removed
function Events.Hazard.Removed.Remove(callbackOrToken) end
Events.Hud = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hud.BroadcastShowing = {}
---@param callback fun(ev: EventSnapshot_BroadcastShowingEventArgs|nil)
---@return string subscriptionToken
function Events.Hud.BroadcastShowing.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_BroadcastShowingEventArgs|nil)|string
---@return boolean removed
function Events.Hud.BroadcastShowing.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hud.HintDisplaying = {}
---@param callback fun(ev: EventSnapshot_HintDisplayingEventArgs|nil)
---@return string subscriptionToken
function Events.Hud.HintDisplaying.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_HintDisplayingEventArgs|nil)|string
---@return boolean removed
function Events.Hud.HintDisplaying.Remove(callbackOrToken) end
Events.InputRegistry = {}
--- Fires when a binding transitions from not-pressed to pressed. Dispatched by the internal Anomaly.Client.Api.Internal.Input.InputPoller.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.InputRegistry.Pressed = {}
---@param callback fun(obj: string|nil)
---@return string subscriptionToken
function Events.InputRegistry.Pressed.Add(callback) end
---@param callbackOrToken fun(obj: string|nil)|string
---@return boolean removed
function Events.InputRegistry.Pressed.Remove(callbackOrToken) end
--- Fires when a binding transitions from pressed to not-pressed.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.InputRegistry.Released = {}
---@param callback fun(obj: string|nil)
---@return string subscriptionToken
function Events.InputRegistry.Released.Add(callback) end
---@param callbackOrToken fun(obj: string|nil)|string
---@return boolean removed
function Events.InputRegistry.Released.Remove(callbackOrToken) end
Events.Inventory = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.CurrentItemChanged = {}
---@param callback fun(ev: EventSnapshot_CurrentItemChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.CurrentItemChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_CurrentItemChangedEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.CurrentItemChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.CurrentItemChanging = {}
---@param callback fun(ev: EventSnapshot_CurrentItemChangingEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.CurrentItemChanging.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_CurrentItemChangingEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.CurrentItemChanging.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.DroppingItem = {}
---@param callback fun(ev: EventSnapshot_PlayerDroppingItemEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.DroppingItem.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerDroppingItemEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.DroppingItem.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.ItemsChanged = {}
---@param callback fun(ev: EventSnapshot_InventoryChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.ItemsChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_InventoryChangedEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.ItemsChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.UiClosing = {}
---@param callback fun(ev: EventSnapshot_InventoryUiClosingEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.UiClosing.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_InventoryUiClosingEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.UiClosing.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.UiOpening = {}
---@param callback fun(ev: EventSnapshot_InventoryUiOpeningEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.UiOpening.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_InventoryUiOpeningEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.UiOpening.Remove(callbackOrToken) end
Events.LocalPlayerMotion = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.LocalPlayerMotion.Applied = {}
---@param callback fun(obj: EventSnapshot_MotionDiscontinuity|nil)
---@return string subscriptionToken
function Events.LocalPlayerMotion.Applied.Add(callback) end
---@param callbackOrToken fun(obj: EventSnapshot_MotionDiscontinuity|nil)|string
---@return boolean removed
function Events.LocalPlayerMotion.Applied.Remove(callbackOrToken) end
Events.Locker = {}
--- Fires for changed replicated chamber masks after the initial spawn snapshot.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Locker.StateChanged = {}
---@param callback fun(ev: EventSnapshot_LockerStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Locker.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_LockerStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Locker.StateChanged.Remove(callbackOrToken) end
Events.NetworkMessage = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.NetworkMessage.AnomalyMessageReceived = {}
---@param callback fun(ev: EventSnapshot_AnomalyMessageReceivedEventArgs|nil)
---@return string subscriptionToken
function Events.NetworkMessage.AnomalyMessageReceived.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_AnomalyMessageReceivedEventArgs|nil)|string
---@return boolean removed
function Events.NetworkMessage.AnomalyMessageReceived.Remove(callbackOrToken) end
Events.Pickup = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Pickup.Added = {}
---@param callback fun(ev: EventSnapshot_PickupAddedEventArgs|nil)
---@return string subscriptionToken
function Events.Pickup.Added.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PickupAddedEventArgs|nil)|string
---@return boolean removed
function Events.Pickup.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Pickup.Removed = {}
---@param callback fun(ev: EventSnapshot_PickupRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.Pickup.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PickupRemovedEventArgs|nil)|string
---@return boolean removed
function Events.Pickup.Removed.Remove(callbackOrToken) end
Events.Player = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Added = {}
---@param callback fun(ev: EventSnapshot_PlayerAddedEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Added.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerAddedEventArgs|nil)|string
---@return boolean removed
function Events.Player.Added.Remove(callbackOrToken) end
--- A resolved local interaction; carries typed targets but does not imply server acceptance.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Interacted = {}
---@param callback fun(ev: EventSnapshot_PlayerInteractedEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Interacted.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerInteractedEventArgs|nil)|string
---@return boolean removed
function Events.Player.Interacted.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Interacting = {}
---@param callback fun(ev: EventSnapshot_PlayerInteractingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Interacting.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerInteractingEventArgs|nil)|string
---@return boolean removed
function Events.Player.Interacting.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Jumping = {}
---@param callback fun(ev: EventSnapshot_PlayerJumpingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Jumping.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerJumpingEventArgs|nil)|string
---@return boolean removed
function Events.Player.Jumping.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.NoclipToggling = {}
---@param callback fun(ev: EventSnapshot_PlayerNoclipTogglingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.NoclipToggling.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerNoclipTogglingEventArgs|nil)|string
---@return boolean removed
function Events.Player.NoclipToggling.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Removed = {}
---@param callback fun(ev: EventSnapshot_PlayerRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerRemovedEventArgs|nil)|string
---@return boolean removed
function Events.Player.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Sprinting = {}
---@param callback fun(ev: EventSnapshot_PlayerSprintingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Sprinting.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerSprintingEventArgs|nil)|string
---@return boolean removed
function Events.Player.Sprinting.Remove(callbackOrToken) end
Events.Projectile = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Projectile.Removed = {}
---@param callback fun(ev: EventSnapshot_ProjectileRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.Projectile.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ProjectileRemovedEventArgs|nil)|string
---@return boolean removed
function Events.Projectile.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Projectile.Spawned = {}
---@param callback fun(ev: EventSnapshot_ProjectileSpawnedEventArgs|nil)
---@return string subscriptionToken
function Events.Projectile.Spawned.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_ProjectileSpawnedEventArgs|nil)|string
---@return boolean removed
function Events.Projectile.Spawned.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Projectile.Throwing = {}
---@param callback fun(ev: EventSnapshot_PlayerThrowingEventArgs|nil)
---@return string subscriptionToken
function Events.Projectile.Throwing.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerThrowingEventArgs|nil)|string
---@return boolean removed
function Events.Projectile.Throwing.Remove(callbackOrToken) end
Events.Ragdoll = {}
--- Fires when a ragdoll is removed from the scene (cleanup, round restart).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Ragdoll.Removed = {}
---@param callback fun(ev: EventSnapshot_RagdollEventArgs|nil)
---@return string subscriptionToken
function Events.Ragdoll.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RagdollEventArgs|nil)|string
---@return boolean removed
function Events.Ragdoll.Removed.Remove(callbackOrToken) end
--- Fires when a new ragdoll spawns into the scene (someone died).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Ragdoll.Spawned = {}
---@param callback fun(ev: EventSnapshot_RagdollEventArgs|nil)
---@return string subscriptionToken
function Events.Ragdoll.Spawned.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RagdollEventArgs|nil)|string
---@return boolean removed
function Events.Ragdoll.Spawned.Remove(callbackOrToken) end
Events.Role = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Role.Changed = {}
---@param callback fun(ev: EventSnapshot_PlayerRoleChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Role.Changed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerRoleChangedEventArgs|nil)|string
---@return boolean removed
function Events.Role.Changed.Remove(callbackOrToken) end
Events.Round = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Ended = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Ended.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Ended.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Ending = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Ending.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Ending.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.PhaseChanged = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.PhaseChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.PhaseChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Restarted = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Restarted.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Restarted.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Started = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Started.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Started.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Starting = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Starting.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Starting.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.WaitingForPlayers = {}
---@param callback fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.WaitingForPlayers.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.WaitingForPlayers.Remove(callbackOrToken) end
Events.Scp079 = {}
--- Fires for the first known or changed power value while the SCP-079 is locally controlled or spectated. This is not a per-frame prediction event.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Scp079.AuxiliaryPowerChanged = {}
---@param callback fun(ev: EventSnapshot_Scp079AuxiliaryPowerChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Scp079.AuxiliaryPowerChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_Scp079AuxiliaryPowerChangedEventArgs|nil)|string
---@return boolean removed
function Events.Scp079.AuxiliaryPowerChanged.Remove(callbackOrToken) end
--- Fires for the first known or changed total experience for a role life.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Scp079.ExperienceChanged = {}
---@param callback fun(ev: EventSnapshot_Scp079ExperienceChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Scp079.ExperienceChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_Scp079ExperienceChangedEventArgs|nil)|string
---@return boolean removed
function Events.Scp079.ExperienceChanged.Remove(callbackOrToken) end
--- Fires for the first known or changed one-based tier for a role life.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Scp079.TierChanged = {}
---@param callback fun(ev: EventSnapshot_Scp079TierChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Scp079.TierChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_Scp079TierChangedEventArgs|nil)|string
---@return boolean removed
function Events.Scp079.TierChanged.Remove(callbackOrToken) end
Events.Spectator = {}
--- Fires after the stock tracker changes its player target, including when the target is cleared. This does not observe remote spectators.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Spectator.TargetChanged = {}
---@param callback fun(ev: EventSnapshot_SpectatedPlayerChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Spectator.TargetChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_SpectatedPlayerChangedEventArgs|nil)|string
---@return boolean removed
function Events.Spectator.TargetChanged.Remove(callbackOrToken) end
Events.Stats = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.ArtificialHealthChanged = {}
---@param callback fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.ArtificialHealthChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.ArtificialHealthChanged.Remove(callbackOrToken) end
--- Observed value or maximum changes. First samples and visibility changes rebase silently.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.Changed = {}
---@param callback fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.Changed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.Changed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Stats.Healed = {}
---@param callback fun(ev: EventSnapshot_PlayerHealedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.Healed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerHealedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.Healed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.HealthChanged = {}
---@param callback fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.HealthChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.HealthChanged.Remove(callbackOrToken) end
--- Fires when a player's Hume shield value changes. Use the convenience Anomaly.Client.Api.Events.Args.PlayerHumeShieldChangedEventArgs.Broke flag to detect the shield-broken transition specifically.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.HumeShieldChanged = {}
---@param callback fun(ev: EventSnapshot_PlayerHumeShieldChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.HumeShieldChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerHumeShieldChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.HumeShieldChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.MaximumChanged = {}
---@param callback fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.MaximumChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.MaximumChanged.Remove(callbackOrToken) end
--- Fires when a player's stamina value changes. Driven by the polling tracker in the loader, so deltas reflect the per-tick change rather than the underlying continuous stamina drain.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.StaminaChanged = {}
---@param callback fun(ev: EventSnapshot_PlayerStaminaChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.StaminaChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_PlayerStaminaChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.StaminaChanged.Remove(callbackOrToken) end
Events.Tesla = {}
--- Fires when a tesla gate spawns into the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Tesla.Added = {}
---@param callback fun(ev: EventSnapshot_TeslaEventArgs|nil)
---@return string subscriptionToken
function Events.Tesla.Added.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_TeslaEventArgs|nil)|string
---@return boolean removed
function Events.Tesla.Added.Remove(callbackOrToken) end
--- Fires when a tesla gate completes its shock burst.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Tesla.Bursted = {}
---@param callback fun(ev: EventSnapshot_TeslaEventArgs|nil)
---@return string subscriptionToken
function Events.Tesla.Bursted.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_TeslaEventArgs|nil)|string
---@return boolean removed
function Events.Tesla.Bursted.Remove(callbackOrToken) end
--- Fires when a tesla gate is removed from the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Tesla.Removed = {}
---@param callback fun(ev: EventSnapshot_TeslaEventArgs|nil)
---@return string subscriptionToken
function Events.Tesla.Removed.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_TeslaEventArgs|nil)|string
---@return boolean removed
function Events.Tesla.Removed.Remove(callbackOrToken) end
Events.UsableItem = {}
--- An explicit cancel notification, not normal completion or removal.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.UsableItem.Cancelled = {}
---@param callback fun(ev: EventSnapshot_UsableItemStatusEventArgs|nil)
---@return string subscriptionToken
function Events.UsableItem.Cancelled.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_UsableItemStatusEventArgs|nil)|string
---@return boolean removed
function Events.UsableItem.Cancelled.Remove(callbackOrToken) end
--- A received cooldown applied to a usable item in the local inventory.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.UsableItem.CooldownChanged = {}
---@param callback fun(ev: EventSnapshot_UsableItemCooldownEventArgs|nil)
---@return string subscriptionToken
function Events.UsableItem.CooldownChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_UsableItemCooldownEventArgs|nil)|string
---@return boolean removed
function Events.UsableItem.CooldownChanged.Remove(callbackOrToken) end
--- A start notification received for a serial; the item/owner can be unavailable locally.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.UsableItem.Started = {}
---@param callback fun(ev: EventSnapshot_UsableItemStatusEventArgs|nil)
---@return string subscriptionToken
function Events.UsableItem.Started.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_UsableItemStatusEventArgs|nil)|string
---@return boolean removed
function Events.UsableItem.Started.Remove(callbackOrToken) end
Events.Voice = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Received = {}
---@param callback fun(ev: EventSnapshot_VoiceMessageReceivedEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Received.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_VoiceMessageReceivedEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Received.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Receiving = {}
---@param callback fun(ev: EventSnapshot_VoiceMessageReceivingEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Receiving.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_VoiceMessageReceivingEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Receiving.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Sending = {}
---@param callback fun(ev: EventSnapshot_VoiceMessageSendingEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Sending.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_VoiceMessageSendingEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Sending.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Sent = {}
---@param callback fun(ev: EventSnapshot_VoiceMessageSentEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Sent.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_VoiceMessageSentEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Sent.Remove(callbackOrToken) end
Events.Warhead = {}
--- Fires once when the warhead actually detonates.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Warhead.Detonated = {}
---@param callback fun(ev: EventSnapshot_WarheadDetonatedEventArgs|nil)
---@return string subscriptionToken
function Events.Warhead.Detonated.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_WarheadDetonatedEventArgs|nil)|string
---@return boolean removed
function Events.Warhead.Detonated.Remove(callbackOrToken) end
--- Fires when the countdown starts or stops (cancellation / detonation).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Warhead.ProgressChanged = {}
---@param callback fun(ev: EventSnapshot_WarheadProgressChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Warhead.ProgressChanged.Add(callback) end
---@param callbackOrToken fun(ev: EventSnapshot_WarheadProgressChangedEventArgs|nil)|string
---@return boolean removed
function Events.Warhead.ProgressChanged.Remove(callbackOrToken) end

-- Installed for approved pack scripts. Message names and flat primitive record fields come from that pack index.
Messages = {}
---@param Name string # Approved server-to-client message name.
---@param Callback fun(Message: table<string, boolean|number|string>)
---@return string SubscriptionToken
function Messages.On(Name, Callback) end
---@param SubscriptionToken string
---@return boolean Removed
function Messages.Off(SubscriptionToken) end
---@param Name string # Approved client-to-server message name.
---@param Payload table<string, boolean|number|string> # Exact required fields; no nested tables or metatable.
---@return boolean Accepted
---@return string|nil ErrorCode # NotReady, UnknownMessage, WrongDirection, or InvalidPayload.
function Messages.Send(Name, Payload) end
