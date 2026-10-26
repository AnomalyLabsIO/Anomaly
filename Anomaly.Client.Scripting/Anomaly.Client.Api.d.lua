---@meta
-- Generated from the approved script catalog. Definitions only; no host objects are constructed.

-- Host enum registries are read-only. Values are canonical names; enum inputs also accept case-insensitive names.
-- Flags inputs accept comma-separated declared names and the unnamed zero string '0'; other numeric inputs are rejected.
Enums = {}

---@enum Enum_Anomaly_002e_Shared_002e_Networking_002e_Messages_002e_RoundPhase
Enums["Anomaly.Shared.Networking.Messages.RoundPhase"] = {
    ["Ended"] = "Ended",
    ["Ending"] = "Ending",
    ["InProgress"] = "InProgress",
    ["Restarted"] = "Restarted",
    ["Starting"] = "Starting",
    ["WaitingForPlayers"] = "WaitingForPlayers",
}
Enums.RoundPhase = Enums["Anomaly.Shared.Networking.Messages.RoundPhase"]
RoundPhase = Enums["Anomaly.Shared.Networking.Messages.RoundPhase"]

---@enum Enum_Il2Cpp_002e_ActionCategory
Enums["Il2Cpp.ActionCategory"] = {
    ["Communication"] = "Communication",
    ["Gameplay"] = "Gameplay",
    ["Movement"] = "Movement",
    ["Scp079"] = "Scp079",
    ["System"] = "System",
    ["Unbindable"] = "Unbindable",
    ["Weapons"] = "Weapons",
}
Enums.ActionCategory = Enums["Il2Cpp.ActionCategory"]
ActionCategory = Enums["Il2Cpp.ActionCategory"]

---@enum Enum_Il2Cpp_002e_ActionName
Enums["Il2Cpp.ActionName"] = {
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
Enums.ActionName = Enums["Il2Cpp.ActionName"]
ActionName = Enums["Il2Cpp.ActionName"]

---@enum Enum_Il2Cpp_002e_AlphaWarheadNukesitePanel_002b_DiodeType
Enums["Il2Cpp.AlphaWarheadNukesitePanel+DiodeType"] = {
    ["BlastDoor"] = "BlastDoor",
    ["InProgress"] = "InProgress",
    ["OutsideDoor"] = "OutsideDoor",
}
Enums.DiodeType = Enums["Il2Cpp.AlphaWarheadNukesitePanel+DiodeType"]

---@enum Enum_Il2Cpp_002e_AlphaWarheadNukesitePanel_002b_PanelColliderId
Enums["Il2Cpp.AlphaWarheadNukesitePanel+PanelColliderId"] = {
    ["Cancel"] = "Cancel",
    ["Lever"] = "Lever",
}
Enums.PanelColliderId = Enums["Il2Cpp.AlphaWarheadNukesitePanel+PanelColliderId"]

---@enum Enum_Il2Cpp_002e_AlphaWarheadOutsitePanel_002b_UpdateType
Enums["Il2Cpp.AlphaWarheadOutsitePanel+UpdateType"] = {
    ["Disabled"] = "Disabled",
    ["Empty"] = "Empty",
    ["Ready"] = "Ready",
    ["Skip"] = "Skip",
    ["Time"] = "Time",
    ["Wait"] = "Wait",
    ["Zero"] = "Zero",
}

---@enum Enum_Il2Cpp_002e_AuthStatusType
Enums["Il2Cpp.AuthStatusType"] = {
    ["Connecting"] = "Connecting",
    ["Failure"] = "Failure",
    ["PlatformAuthFailure"] = "PlatformAuthFailure",
    ["Success"] = "Success",
}
Enums.AuthStatusType = Enums["Il2Cpp.AuthStatusType"]
AuthStatusType = Enums["Il2Cpp.AuthStatusType"]

---@enum Enum_Il2Cpp_002e_BanHandler_002b_BanType
Enums["Il2Cpp.BanHandler+BanType"] = {
    ["IP"] = "IP",
    ["NULL"] = "NULL",
    ["UserId"] = "UserId",
}
Enums.BanType = Enums["Il2Cpp.BanHandler+BanType"]

---@enum Enum_Il2Cpp_002e_Broadcast_002b_BroadcastFlags
Enums["Il2Cpp.Broadcast+BroadcastFlags"] = {
    ["AdminChat"] = "AdminChat",
    ["Normal"] = "Normal",
    ["Truncated"] = "Truncated",
}
Enums.BroadcastFlags = Enums["Il2Cpp.Broadcast+BroadcastFlags"]

---@enum Enum_Il2Cpp_002e_CentralAuthPreauthFlags
Enums["Il2Cpp.CentralAuthPreauthFlags"] = {
    ["AuthRejected"] = "AuthRejected",
    ["GloballyBanned"] = "GloballyBanned",
    ["IgnoreBans"] = "IgnoreBans",
    ["IgnoreGeoblock"] = "IgnoreGeoblock",
    ["IgnoreWhitelist"] = "IgnoreWhitelist",
    ["None"] = "None",
    ["NorthwoodStaff"] = "NorthwoodStaff",
    ["ReservedSlot"] = "ReservedSlot",
}
Enums.CentralAuthPreauthFlags = Enums["Il2Cpp.CentralAuthPreauthFlags"]
CentralAuthPreauthFlags = Enums["Il2Cpp.CentralAuthPreauthFlags"]

---@enum Enum_Il2Cpp_002e_ChallengeState
Enums["Il2Cpp.ChallengeState"] = {
    ["Done"] = "Done",
    ["DoneRejoin"] = "DoneRejoin",
    ["None"] = "None",
    ["Processing"] = "Processing",
}
Enums.ChallengeState = Enums["Il2Cpp.ChallengeState"]
ChallengeState = Enums["Il2Cpp.ChallengeState"]

---@enum Enum_Il2Cpp_002e_ChallengeType
Enums["Il2Cpp.ChallengeType"] = {
    ["MD5"] = "MD5",
    ["Reply"] = "Reply",
    ["SHA1"] = "SHA1",
}
Enums.ChallengeType = Enums["Il2Cpp.ChallengeType"]
ChallengeType = Enums["Il2Cpp.ChallengeType"]

---@enum Enum_Il2Cpp_002e_CoffeeTranslation
Enums["Il2Cpp.CoffeeTranslation"] = {
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
Enums.CoffeeTranslation = Enums["Il2Cpp.CoffeeTranslation"]
CoffeeTranslation = Enums["Il2Cpp.CoffeeTranslation"]

---@enum Enum_Il2Cpp_002e_CustomLiteNetLib4MirrorTransport_002b_ClientType
Enums["Il2Cpp.CustomLiteNetLib4MirrorTransport+ClientType"] = {
    ["GameClient"] = "GameClient",
    ["VerificationService"] = "VerificationService",
}
Enums.ClientType = Enums["Il2Cpp.CustomLiteNetLib4MirrorTransport+ClientType"]

---@enum Enum_Il2Cpp_002e_DebugLevel
Enums["Il2Cpp.DebugLevel"] = {
    ["Elevated"] = "Elevated",
    ["Full"] = "Full",
    ["Muted"] = "Muted",
    ["Narrow"] = "Narrow",
    ["Normal"] = "Normal",
}
Enums.DebugLevel = Enums["Il2Cpp.DebugLevel"]
DebugLevel = Enums["Il2Cpp.DebugLevel"]

---@enum Enum_Il2Cpp_002e_EncryptedChannelManager_002b_EncryptedChannel
Enums["Il2Cpp.EncryptedChannelManager+EncryptedChannel"] = {
    ["AdminChat"] = "AdminChat",
    ["GameConsole"] = "GameConsole",
    ["RemoteAdmin"] = "RemoteAdmin",
}
Enums.EncryptedChannel = Enums["Il2Cpp.EncryptedChannelManager+EncryptedChannel"]

---@enum Enum_Il2Cpp_002e_EncryptedChannelManager_002b_SecurityLevel
Enums["Il2Cpp.EncryptedChannelManager+SecurityLevel"] = {
    ["EncryptedAndAuthenticated"] = "EncryptedAndAuthenticated",
    ["Unsecured"] = "Unsecured",
}
Enums.SecurityLevel = Enums["Il2Cpp.EncryptedChannelManager+SecurityLevel"]

---@enum Enum_Il2Cpp_002e_Escape_002b_EscapeScenarioType
Enums["Il2Cpp.Escape+EscapeScenarioType"] = {
    ["ClassD"] = "ClassD",
    ["CuffedClassD"] = "CuffedClassD",
    ["CuffedScientist"] = "CuffedScientist",
    ["Custom"] = "Custom",
    ["None"] = "None",
    ["Scientist"] = "Scientist",
}
Enums.EscapeScenarioType = Enums["Il2Cpp.Escape+EscapeScenarioType"]

---@enum Enum_Il2Cpp_002e_ExplosionType
Enums["Il2Cpp.ExplosionType"] = {
    ["Cola"] = "Cola",
    ["Custom"] = "Custom",
    ["Disruptor"] = "Disruptor",
    ["Grenade"] = "Grenade",
    ["Jailbird"] = "Jailbird",
    ["PinkCandy"] = "PinkCandy",
    ["SCP018"] = "SCP018",
}
Enums.ExplosionType = Enums["Il2Cpp.ExplosionType"]
ExplosionType = Enums["Il2Cpp.ExplosionType"]

---@enum Enum_Il2Cpp_002e_FalloffType
Enums["Il2Cpp.FalloffType"] = {
    ["Exponential"] = "Exponential",
    ["Footstep"] = "Footstep",
    ["Linear"] = "Linear",
}
Enums.FalloffType = Enums["Il2Cpp.FalloffType"]
FalloffType = Enums["Il2Cpp.FalloffType"]

---@enum Enum_Il2Cpp_002e_FavoriteAndHistory_002b_StorageLocation
Enums["Il2Cpp.FavoriteAndHistory+StorageLocation"] = {
    ["Favorites"] = "Favorites",
    ["History"] = "History",
    ["IPHistory"] = "IPHistory",
}
Enums.StorageLocation = Enums["Il2Cpp.FavoriteAndHistory+StorageLocation"]

---@enum Enum_Il2Cpp_002e_FriendlyFireAction
Enums["Il2Cpp.FriendlyFireAction"] = {
    ["Ban"] = "Ban",
    ["Kick"] = "Kick",
    ["Kill"] = "Kill",
    ["Noop"] = "Noop",
}
Enums.FriendlyFireAction = Enums["Il2Cpp.FriendlyFireAction"]
FriendlyFireAction = Enums["Il2Cpp.FriendlyFireAction"]

---@enum Enum_Il2Cpp_002e_GeoblockingMode
Enums["Il2Cpp.GeoblockingMode"] = {
    ["Blacklist"] = "Blacklist",
    ["None"] = "None",
    ["Whitelist"] = "Whitelist",
}
Enums.GeoblockingMode = Enums["Il2Cpp.GeoblockingMode"]
GeoblockingMode = Enums["Il2Cpp.GeoblockingMode"]

---@enum Enum_Il2Cpp_002e_GraphicsApiDropdown_002b_GraphicsApi
Enums["Il2Cpp.GraphicsApiDropdown+GraphicsApi"] = {
    ["Auto"] = "Auto",
    ["DirectX11"] = "DirectX11",
    ["DirectX12"] = "DirectX12",
    ["Vulkan"] = "Vulkan",
}
Enums.GraphicsApi = Enums["Il2Cpp.GraphicsApiDropdown+GraphicsApi"]

---@enum Enum_Il2Cpp_002e_HitboxType
Enums["Il2Cpp.HitboxType"] = {
    ["Body"] = "Body",
    ["Headshot"] = "Headshot",
    ["Limb"] = "Limb",
}
Enums.HitboxType = Enums["Il2Cpp.HitboxType"]
HitboxType = Enums["Il2Cpp.HitboxType"]

---@enum Enum_Il2Cpp_002e_HitmarkerType
Enums["Il2Cpp.HitmarkerType"] = {
    ["Blocked"] = "Blocked",
    ["None"] = "None",
    ["Regular"] = "Regular",
}
Enums.HitmarkerType = Enums["Il2Cpp.HitmarkerType"]
HitmarkerType = Enums["Il2Cpp.HitmarkerType"]

---@enum Enum_Il2Cpp_002e_ItemCategory
Enums["Il2Cpp.ItemCategory"] = {
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
Enums.ItemCategory = Enums["Il2Cpp.ItemCategory"]
ItemCategory = Enums["Il2Cpp.ItemCategory"]

---@enum Enum_Il2Cpp_002e_ItemTierFlags
Enums["Il2Cpp.ItemTierFlags"] = {
    ["Common"] = "Common",
    ["ExtraRare"] = "ExtraRare",
    ["MilitaryGrade"] = "MilitaryGrade",
    ["Rare"] = "Rare",
}
Enums.ItemTierFlags = Enums["Il2Cpp.ItemTierFlags"]
ItemTierFlags = Enums["Il2Cpp.ItemTierFlags"]

---@enum Enum_Il2Cpp_002e_ItemType
Enums["Il2Cpp.ItemType"] = {
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
Enums.ItemType = Enums["Il2Cpp.ItemType"]
ItemType = Enums["Il2Cpp.ItemType"]

---@enum Enum_Il2Cpp_002e_LegacyInterfaces
Enums["Il2Cpp.LegacyInterfaces"] = {
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
Enums.LegacyInterfaces = Enums["Il2Cpp.LegacyInterfaces"]
LegacyInterfaces = Enums["Il2Cpp.LegacyInterfaces"]

---@enum Enum_Il2Cpp_002e_MainMenuSoundtrackController_002b_MenuSoundtrackState
Enums["Il2Cpp.MainMenuSoundtrackController+MenuSoundtrackState"] = {
    ["MenuIntensive"] = "MenuIntensive",
    ["MenuJustLoaded"] = "MenuJustLoaded",
    ["MenuLoop"] = "MenuLoop",
    ["Muted"] = "Muted",
    ["PregameLobby"] = "PregameLobby",
    ["Retro"] = "Retro",
}
Enums.MenuSoundtrackState = Enums["Il2Cpp.MainMenuSoundtrackController+MenuSoundtrackState"]

---@enum Enum_Il2Cpp_002e_MainMenuSoundtrackController_002b_MenuThemeSetting
Enums["Il2Cpp.MainMenuSoundtrackController+MenuThemeSetting"] = {
    ["EventTheme"] = "EventTheme",
    ["RegularTheme"] = "RegularTheme",
}
Enums.MenuThemeSetting = Enums["Il2Cpp.MainMenuSoundtrackController+MenuThemeSetting"]

---@enum Enum_Il2Cpp_002e_MainThreadDispatcher_002b_DispatchTime
Enums["Il2Cpp.MainThreadDispatcher+DispatchTime"] = {
    ["FixedUpdate"] = "FixedUpdate",
    ["LateUpdate"] = "LateUpdate",
    ["Update"] = "Update",
}
Enums.DispatchTime = Enums["Il2Cpp.MainThreadDispatcher+DispatchTime"]

---@enum Enum_Il2Cpp_002e_MessageImportance
Enums["Il2Cpp.MessageImportance"] = {
    ["LeastImportant"] = "LeastImportant",
    ["LessImportant"] = "LessImportant",
    ["MostImportant"] = "MostImportant",
    ["Normal"] = "Normal",
}
Enums.MessageImportance = Enums["Il2Cpp.MessageImportance"]
MessageImportance = Enums["Il2Cpp.MessageImportance"]

---@enum Enum_Il2Cpp_002e_Misc_002b_CommandOperationMode
Enums["Il2Cpp.Misc+CommandOperationMode"] = {
    ["Disable"] = "Disable",
    ["Enable"] = "Enable",
    ["Toggle"] = "Toggle",
}
Enums.CommandOperationMode = Enums["Il2Cpp.Misc+CommandOperationMode"]

---@enum Enum_Il2Cpp_002e_Misc_002b_IPAddressType
Enums["Il2Cpp.Misc+IPAddressType"] = {
    ["Hostname"] = "Hostname",
    ["IPV4"] = "IPV4",
    ["IPV6"] = "IPV6",
    ["Localhost"] = "Localhost",
    ["Unknown"] = "Unknown",
}
Enums.IPAddressType = Enums["Il2Cpp.Misc+IPAddressType"]

---@enum Enum_Il2Cpp_002e_Misc_002b_PlayerInfoColorTypes
Enums["Il2Cpp.Misc+PlayerInfoColorTypes"] = {
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
Enums.PlayerInfoColorTypes = Enums["Il2Cpp.Misc+PlayerInfoColorTypes"]

---@enum Enum_Il2Cpp_002e_NonFacilityCompatibility_002b_SceneDescription_002b_VoiceChatSupportMode
Enums["Il2Cpp.NonFacilityCompatibility+SceneDescription+VoiceChatSupportMode"] = {
    ["FullySupported"] = "FullySupported",
    ["Unsupported"] = "Unsupported",
    ["WithoutIntercom"] = "WithoutIntercom",
}
Enums.VoiceChatSupportMode = Enums["Il2Cpp.NonFacilityCompatibility+SceneDescription+VoiceChatSupportMode"]

---@enum Enum_Il2Cpp_002e_PhotosensitivityWarning_002b_Photosensitivity
Enums["Il2Cpp.PhotosensitivityWarning+Photosensitivity"] = {
    ["FirstViewing"] = "FirstViewing",
}
Enums.Photosensitivity = Enums["Il2Cpp.PhotosensitivityWarning+Photosensitivity"]

---@enum Enum_Il2Cpp_002e_PlayerInfoArea
Enums["Il2Cpp.PlayerInfoArea"] = {
    ["Badge"] = "Badge",
    ["CustomInfo"] = "CustomInfo",
    ["Nickname"] = "Nickname",
    ["PowerStatus"] = "PowerStatus",
    ["Role"] = "Role",
    ["UnitName"] = "UnitName",
}
Enums.PlayerInfoArea = Enums["Il2Cpp.PlayerInfoArea"]
PlayerInfoArea = Enums["Il2Cpp.PlayerInfoArea"]

---@enum Enum_Il2Cpp_002e_PlayerPermissionType
Enums["Il2Cpp.PlayerPermissionType"] = {
    ["Misc"] = "Misc",
    ["RaAccess"] = "RaAccess",
    ["ViewAccess"] = "ViewAccess",
}
Enums.PlayerPermissionType = Enums["Il2Cpp.PlayerPermissionType"]
PlayerPermissionType = Enums["Il2Cpp.PlayerPermissionType"]

---@enum Enum_Il2Cpp_002e_PlayerPermissions
Enums["Il2Cpp.PlayerPermissions"] = {
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
Enums.PlayerPermissions = Enums["Il2Cpp.PlayerPermissions"]
PlayerPermissions = Enums["Il2Cpp.PlayerPermissions"]

---@enum Enum_Il2Cpp_002e_PlayerPrefsSl_002b_DataType
Enums["Il2Cpp.PlayerPrefsSl+DataType"] = {
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
Enums.DataType = Enums["Il2Cpp.PlayerPrefsSl+DataType"]

---@enum Enum_Il2Cpp_002e_PocketDimensionTeleport_002b_PDTeleportType
Enums["Il2Cpp.PocketDimensionTeleport+PDTeleportType"] = {
    ["Exit"] = "Exit",
    ["Killer"] = "Killer",
}
Enums.PDTeleportType = Enums["Il2Cpp.PocketDimensionTeleport+PDTeleportType"]

---@enum Enum_Il2Cpp_002e_RejectionReason
Enums["Il2Cpp.RejectionReason"] = {
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

---@enum Enum_Il2Cpp_002e_RemoteAdminResponse_002b_RemoteAdminResponseFlags
Enums["Il2Cpp.RemoteAdminResponse+RemoteAdminResponseFlags"] = {
    ["LogInConsole"] = "LogInConsole",
    ["Successful"] = "Successful",
}
Enums.RemoteAdminResponseFlags = Enums["Il2Cpp.RemoteAdminResponse+RemoteAdminResponseFlags"]

---@enum Enum_Il2Cpp_002e_ReportServer_002b_CentralResponse
Enums["Il2Cpp.ReportServer+CentralResponse"] = {
    ["Error"] = "Error",
    ["None"] = "None",
    ["Success"] = "Success",
}
Enums.CentralResponse = Enums["Il2Cpp.ReportServer+CentralResponse"]

---@enum Enum_Il2Cpp_002e_ReproProjectAssetType
Enums["Il2Cpp.ReproProjectAssetType"] = {
    ["Asset"] = "Asset",
    ["Prefab"] = "Prefab",
    ["Scene"] = "Scene",
    ["Wildcard"] = "Wildcard",
}
Enums.ReproProjectAssetType = Enums["Il2Cpp.ReproProjectAssetType"]
ReproProjectAssetType = Enums["Il2Cpp.ReproProjectAssetType"]

---@enum Enum_Il2Cpp_002e_RoundSummary_002b_LeadingTeam
Enums["Il2Cpp.RoundSummary+LeadingTeam"] = {
    ["Anomalies"] = "Anomalies",
    ["ChaosInsurgency"] = "ChaosInsurgency",
    ["Draw"] = "Draw",
    ["FacilityForces"] = "FacilityForces",
    ["Flamingos"] = "Flamingos",
}
Enums.LeadingTeam = Enums["Il2Cpp.RoundSummary+LeadingTeam"]

---@enum Enum_Il2Cpp_002e_Scp956Target_002b_EffectReason
Enums["Il2Cpp.Scp956Target+EffectReason"] = {
    ["Child"] = "Child",
    ["HasCandy"] = "HasCandy",
    ["None"] = "None",
}
Enums.EffectReason = Enums["Il2Cpp.Scp956Target+EffectReason"]

---@enum Enum_Il2Cpp_002e_ServerConfigSynchronizer_002b_MainBoolsSettings
Enums["Il2Cpp.ServerConfigSynchronizer+MainBoolsSettings"] = {
    ["DisabledAchievements"] = "DisabledAchievements",
    ["FriendlyFire"] = "FriendlyFire",
}
Enums.MainBoolsSettings = Enums["Il2Cpp.ServerConfigSynchronizer+MainBoolsSettings"]

---@enum Enum_Il2Cpp_002e_ServerLogs_002b_LoggingState
Enums["Il2Cpp.ServerLogs+LoggingState"] = {
    ["Off"] = "Off",
    ["Restart"] = "Restart",
    ["Standby"] = "Standby",
    ["Terminate"] = "Terminate",
    ["Write"] = "Write",
}
Enums.LoggingState = Enums["Il2Cpp.ServerLogs+LoggingState"]

---@enum Enum_Il2Cpp_002e_ServerLogs_002b_Modules
Enums["Il2Cpp.ServerLogs+Modules"] = {
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
Enums.Modules = Enums["Il2Cpp.ServerLogs+Modules"]

---@enum Enum_Il2Cpp_002e_ServerLogs_002b_ServerLogType
Enums["Il2Cpp.ServerLogs+ServerLogType"] = {
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
Enums.ServerLogType = Enums["Il2Cpp.ServerLogs+ServerLogType"]

---@enum Enum_Il2Cpp_002e_ServerRoles_002b_BadgePreferences
Enums["Il2Cpp.ServerRoles+BadgePreferences"] = {
    ["NoPreference"] = "NoPreference",
    ["PreferGlobal"] = "PreferGlobal",
    ["PreferLocal"] = "PreferLocal",
}
Enums.BadgePreferences = Enums["Il2Cpp.ServerRoles+BadgePreferences"]

---@enum Enum_Il2Cpp_002e_ServerRoles_002b_BadgeVisibilityPreferences
Enums["Il2Cpp.ServerRoles+BadgeVisibilityPreferences"] = {
    ["Hidden"] = "Hidden",
    ["NoPreference"] = "NoPreference",
    ["Visible"] = "Visible",
}
Enums.BadgeVisibilityPreferences = Enums["Il2Cpp.ServerRoles+BadgeVisibilityPreferences"]

---@enum Enum_Il2Cpp_002e_ServerShutdown_002b_ServerShutdownState
Enums["Il2Cpp.ServerShutdown+ServerShutdownState"] = {
    ["BroadcastingShutdown"] = "BroadcastingShutdown",
    ["Complete"] = "Complete",
    ["NotInitiated"] = "NotInitiated",
    ["ShuttingDown"] = "ShuttingDown",
}
Enums.ServerShutdownState = Enums["Il2Cpp.ServerShutdown+ServerShutdownState"]

---@enum Enum_Il2Cpp_002e_ServerStatic_002b_NextRoundAction
Enums["Il2Cpp.ServerStatic+NextRoundAction"] = {
    ["DoNothing"] = "DoNothing",
    ["Restart"] = "Restart",
    ["Shutdown"] = "Shutdown",
}
Enums.NextRoundAction = Enums["Il2Cpp.ServerStatic+NextRoundAction"]

---@enum Enum_Il2Cpp_002e_ServerTab
Enums["Il2Cpp.ServerTab"] = {
    ["Favorites"] = "Favorites",
    ["Friends"] = "Friends",
    ["History"] = "History",
    ["Internet"] = "Internet",
    ["Official"] = "Official",
}
Enums.ServerTab = Enums["Il2Cpp.ServerTab"]
ServerTab = Enums["Il2Cpp.ServerTab"]

---@enum Enum_Il2Cpp_002e_StatusBar_002b_AutoHideType
Enums["Il2Cpp.StatusBar+AutoHideType"] = {
    ["AlwaysVisible"] = "AlwaysVisible",
    ["WhenEmpty"] = "WhenEmpty",
    ["WhenFull"] = "WhenFull",
}
Enums.AutoHideType = Enums["Il2Cpp.StatusBar+AutoHideType"]

---@enum Enum_Il2Cpp_002e_WarheadScenarioType
Enums["Il2Cpp.WarheadScenarioType"] = {
    ["DeadmanSwitch"] = "DeadmanSwitch",
    ["Resume"] = "Resume",
    ["Start"] = "Start",
}
Enums.WarheadScenarioType = Enums["Il2Cpp.WarheadScenarioType"]
WarheadScenarioType = Enums["Il2Cpp.WarheadScenarioType"]

---@enum Enum_Il2CppAchievements_002e_AchievementName
Enums["Il2CppAchievements.AchievementName"] = {
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
Enums.AchievementName = Enums["Il2CppAchievements.AchievementName"]
AchievementName = Enums["Il2CppAchievements.AchievementName"]

---@enum Enum_Il2CppAdminToys_002e_InvisibleInteractableToy_002b_ColliderShape
Enums["Il2CppAdminToys.InvisibleInteractableToy+ColliderShape"] = {
    ["Box"] = "Box",
    ["Capsule"] = "Capsule",
    ["Sphere"] = "Sphere",
}
Enums.ColliderShape = Enums["Il2CppAdminToys.InvisibleInteractableToy+ColliderShape"]

---@enum Enum_Il2CppAdminToys_002e_PrimitiveFlags
Enums["Il2CppAdminToys.PrimitiveFlags"] = {
    ["Collidable"] = "Collidable",
    ["None"] = "None",
    ["Visible"] = "Visible",
}
Enums.PrimitiveFlags = Enums["Il2CppAdminToys.PrimitiveFlags"]
PrimitiveFlags = Enums["Il2CppAdminToys.PrimitiveFlags"]

---@enum Enum_Il2CppAdminToys_002e_ShootingTarget_002b_TargetButton
Enums["Il2CppAdminToys.ShootingTarget+TargetButton"] = {
    ["DecreaseHP"] = "DecreaseHP",
    ["DecreaseResetTime"] = "DecreaseResetTime",
    ["GlobalResults"] = "GlobalResults",
    ["IncreaseHP"] = "IncreaseHP",
    ["IncreaseResetTime"] = "IncreaseResetTime",
    ["ManualReset"] = "ManualReset",
    ["Remove"] = "Remove",
}
Enums.TargetButton = Enums["Il2CppAdminToys.ShootingTarget+TargetButton"]

---@enum Enum_Il2CppAudioPooling_002e_MixerChannel
Enums["Il2CppAudioPooling.MixerChannel"] = {
    ["DefaultSfx"] = "DefaultSfx",
    ["Interface"] = "Interface",
    ["NoDucking"] = "NoDucking",
    ["Scp127VoiceViewmodel"] = "Scp127VoiceViewmodel",
    ["VoiceChat"] = "VoiceChat",
    ["Weapons"] = "Weapons",
}
Enums.MixerChannel = Enums["Il2CppAudioPooling.MixerChannel"]
MixerChannel = Enums["Il2CppAudioPooling.MixerChannel"]

---@enum Enum_Il2CppCassie_002e_CassieClipCategory
Enums["Il2CppCassie.CassieClipCategory"] = {
    ["Glitch"] = "Glitch",
    ["Other"] = "Other",
    ["Word"] = "Word",
}
Enums.CassieClipCategory = Enums["Il2CppCassie.CassieClipCategory"]
CassieClipCategory = Enums["Il2CppCassie.CassieClipCategory"]

---@enum Enum_Il2CppCassie_002e_CassieTtsPayload_002b_SubtitleMode
Enums["Il2CppCassie.CassieTtsPayload+SubtitleMode"] = {
    ["Automatic"] = "Automatic",
    ["Custom"] = "Custom",
    ["FromTranslation"] = "FromTranslation",
    ["None"] = "None",
}
Enums.SubtitleMode = Enums["Il2CppCassie.CassieTtsPayload+SubtitleMode"]

---@enum Enum_Il2CppCassie_002e_Interpreters_002e_CassieInterpreter_002b_NextAction
Enums["Il2CppCassie.Interpreters.CassieInterpreter+NextAction"] = {
    ["Continue"] = "Continue",
    ["Halt"] = "Halt",
}
Enums.NextAction = Enums["Il2CppCassie.Interpreters.CassieInterpreter+NextAction"]

---@enum Enum_Il2CppCentralAuth_002e_AuthenticationResponseFunctions_002b_AuthenticationResponseFlags
Enums["Il2CppCentralAuth.AuthenticationResponseFunctions+AuthenticationResponseFlags"] = {
    ["AuthToken"] = "AuthToken",
    ["BadgeToken"] = "BadgeToken",
    ["DoNotTrack"] = "DoNotTrack",
    ["HideBadge"] = "HideBadge",
}
Enums.AuthenticationResponseFlags = Enums["Il2CppCentralAuth.AuthenticationResponseFunctions+AuthenticationResponseFlags"]

---@enum Enum_Il2CppCentralAuth_002e_ClientInstanceMode
Enums["Il2CppCentralAuth.ClientInstanceMode"] = {
    ["DedicatedServer"] = "DedicatedServer",
    ["Dummy"] = "Dummy",
    ["Host"] = "Host",
    ["ReadyClient"] = "ReadyClient",
    ["Unverified"] = "Unverified",
}
Enums.ClientInstanceMode = Enums["Il2CppCentralAuth.ClientInstanceMode"]
ClientInstanceMode = Enums["Il2CppCentralAuth.ClientInstanceMode"]

---@enum Enum_Il2CppChristmas_002e_Scp2536_002e_UrgencyLevel
Enums["Il2CppChristmas.Scp2536.UrgencyLevel"] = {
    ["Exclusive"] = "Exclusive",
    ["Five"] = "Five",
    ["Four"] = "Four",
    ["One"] = "One",
    ["Six"] = "Six",
    ["Three"] = "Three",
    ["Two"] = "Two",
    ["Zero"] = "Zero",
}
Enums.UrgencyLevel = Enums["Il2CppChristmas.Scp2536.UrgencyLevel"]
UrgencyLevel = Enums["Il2CppChristmas.Scp2536.UrgencyLevel"]

---@enum Enum_Il2CppCursorManagement_002e_CursorOverrideMode
Enums["Il2CppCursorManagement.CursorOverrideMode"] = {
    ["Centered"] = "Centered",
    ["Confined"] = "Confined",
    ["Free"] = "Free",
    ["NoOverride"] = "NoOverride",
}
Enums.CursorOverrideMode = Enums["Il2CppCursorManagement.CursorOverrideMode"]
CursorOverrideMode = Enums["Il2CppCursorManagement.CursorOverrideMode"]

---@enum Enum_Il2CppCustomPlayerEffects_002e_StatusEffectBase_002b_EffectClassification
Enums["Il2CppCustomPlayerEffects.StatusEffectBase+EffectClassification"] = {
    ["Mixed"] = "Mixed",
    ["Negative"] = "Negative",
    ["Positive"] = "Positive",
    ["Technical"] = "Technical",
}
Enums.EffectClassification = Enums["Il2CppCustomPlayerEffects.StatusEffectBase+EffectClassification"]

---@enum Enum_Il2CppCustomRendering_002e_ApplyCameraGraphicSettings_002b_AntiAliasingType
Enums["Il2CppCustomRendering.ApplyCameraGraphicSettings+AntiAliasingType"] = {
    ["Disabled"] = "Disabled",
    ["FXAA"] = "FXAA",
    ["SMAA"] = "SMAA",
}
Enums.AntiAliasingType = Enums["Il2CppCustomRendering.ApplyCameraGraphicSettings+AntiAliasingType"]

---@enum Enum_Il2CppCustomRendering_002e_FogType
Enums["Il2CppCustomRendering.FogType"] = {
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
Enums.FogType = Enums["Il2CppCustomRendering.FogType"]
FogType = Enums["Il2CppCustomRendering.FogType"]

---@enum Enum_Il2CppDecals_002e_DecalPoolType
Enums["Il2CppDecals.DecalPoolType"] = {
    ["Blood"] = "Blood",
    ["Buckshot"] = "Buckshot",
    ["Bullet"] = "Bullet",
    ["GlassCrack"] = "GlassCrack",
    ["None"] = "None",
}
Enums.DecalPoolType = Enums["Il2CppDecals.DecalPoolType"]
DecalPoolType = Enums["Il2CppDecals.DecalPoolType"]

---@enum Enum_Il2CppDiscord_002e_DiscordGameStatus
Enums["Il2CppDiscord.DiscordGameStatus"] = {
    ["Lobby"] = "Lobby",
    ["MainMenu"] = "MainMenu",
}
Enums.DiscordGameStatus = Enums["Il2CppDiscord.DiscordGameStatus"]
DiscordGameStatus = Enums["Il2CppDiscord.DiscordGameStatus"]

---@enum Enum_Il2CppDiscord_002e_Reply
Enums["Il2CppDiscord.Reply"] = {
    ["Ignore"] = "Ignore",
    ["No"] = "No",
    ["Yes"] = "Yes",
}
Enums.Reply = Enums["Il2CppDiscord.Reply"]
Reply = Enums["Il2CppDiscord.Reply"]

---@enum Enum_Il2CppGameCore_002e_ConfigShareTypes
Enums["Il2CppGameCore.ConfigShareTypes"] = {
    ["Bans"] = "Bans",
    ["GameplayDatabase"] = "GameplayDatabase",
    ["Groups"] = "Groups",
    ["GroupsMembers"] = "GroupsMembers",
    ["Mutes"] = "Mutes",
    ["ReservedSlots"] = "ReservedSlots",
    ["Whitelist"] = "Whitelist",
}
Enums.ConfigShareTypes = Enums["Il2CppGameCore.ConfigShareTypes"]
ConfigShareTypes = Enums["Il2CppGameCore.ConfigShareTypes"]

---@enum Enum_Il2CppGameCore_002e_ConfigSharing_002b_ConfigShare
Enums["Il2CppGameCore.ConfigSharing+ConfigShare"] = {
    ["Bans"] = "Bans",
    ["GameplayDatabase"] = "GameplayDatabase",
    ["Groups"] = "Groups",
    ["GroupsMembers"] = "GroupsMembers",
    ["Mutes"] = "Mutes",
    ["ReservedSlots"] = "ReservedSlots",
    ["Whitelist"] = "Whitelist",
}
Enums.ConfigShare = Enums["Il2CppGameCore.ConfigSharing+ConfigShare"]

---@enum Enum_Il2CppGameCore_002e_ConsoleLogType
Enums["Il2CppGameCore.ConsoleLogType"] = {
    ["DoNotLog"] = "DoNotLog",
    ["Error"] = "Error",
    ["Log"] = "Log",
    ["Warning"] = "Warning",
}
Enums.ConsoleLogType = Enums["Il2CppGameCore.ConsoleLogType"]
ConsoleLogType = Enums["Il2CppGameCore.ConsoleLogType"]

---@enum Enum_Il2CppGameCore_002e_DistributionPlatform
Enums["Il2CppGameCore.DistributionPlatform"] = {
    ["Dedicated"] = "Dedicated",
    ["Discord"] = "Discord",
    ["Steam"] = "Steam",
}
Enums.DistributionPlatform = Enums["Il2CppGameCore.DistributionPlatform"]
DistributionPlatform = Enums["Il2CppGameCore.DistributionPlatform"]

---@enum Enum_Il2CppGameCore_002e_ResizableWindow_002b_ResizeDirection
Enums["Il2CppGameCore.ResizableWindow+ResizeDirection"] = {
    ["Bottom"] = "Bottom",
    ["Left"] = "Left",
    ["None"] = "None",
    ["Right"] = "Right",
    ["Top"] = "Top",
}
Enums.ResizeDirection = Enums["Il2CppGameCore.ResizableWindow+ResizeDirection"]

---@enum Enum_Il2CppGameCore_002e_SystemCursorID
Enums["Il2CppGameCore.SystemCursorID"] = {
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
Enums.SystemCursorID = Enums["Il2CppGameCore.SystemCursorID"]
SystemCursorID = Enums["Il2CppGameCore.SystemCursorID"]

---@enum Enum_Il2CppGameCore_002e_Version_002b_VersionType
Enums["Il2CppGameCore.Version+VersionType"] = {
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
Enums.VersionType = Enums["Il2CppGameCore.Version+VersionType"]

---@enum Enum_Il2CppHints_002e_HintMessageParameterFunctions_002b_HintMessageTypes
Enums["Il2CppHints.HintMessageParameterFunctions+HintMessageTypes"] = {
    ["TextHint"] = "TextHint",
    ["TranslationHint"] = "TranslationHint",
    ["Unknown"] = "Unknown",
}
Enums.HintMessageTypes = Enums["Il2CppHints.HintMessageParameterFunctions+HintMessageTypes"]

---@enum Enum_Il2CppHints_002e_HintTranslations
Enums["Il2CppHints.HintTranslations"] = {
    ["MaxAmmoAlreadyReached"] = "MaxAmmoAlreadyReached",
    ["MaxAmmoReached"] = "MaxAmmoReached",
    ["MaxItemCategoryAlreadyReached"] = "MaxItemCategoryAlreadyReached",
    ["MaxItemCategoryReached"] = "MaxItemCategoryReached",
    ["MaxItemsAlreadyReached"] = "MaxItemsAlreadyReached",
    ["WarheadDetonateCoverDenied"] = "WarheadDetonateCoverDenied",
}
Enums.HintTranslations = Enums["Il2CppHints.HintTranslations"]
HintTranslations = Enums["Il2CppHints.HintTranslations"]

---@enum Enum_Il2CppHints_002e_KeyCodeTranslations
Enums["Il2CppHints.KeyCodeTranslations"] = {
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
Enums.KeyCodeTranslations = Enums["Il2CppHints.KeyCodeTranslations"]
KeyCodeTranslations = Enums["Il2CppHints.KeyCodeTranslations"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_CheckpointDoor_002b_SequenceState
Enums["Il2CppInteractables.Interobjects.CheckpointDoor+SequenceState"] = {
    ["ClosingWarning"] = "ClosingWarning",
    ["Granted"] = "Granted",
    ["Idle"] = "Idle",
    ["OpenLoop"] = "OpenLoop",
}
Enums.SequenceState = Enums["Il2CppInteractables.Interobjects.CheckpointDoor+SequenceState"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorAction
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorAction"] = {
    ["AccessDenied"] = "AccessDenied",
    ["Closed"] = "Closed",
    ["Destroyed"] = "Destroyed",
    ["Locked"] = "Locked",
    ["Opened"] = "Opened",
    ["Unlocked"] = "Unlocked",
}
Enums.DoorAction = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorAction"]
DoorAction = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorAction"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorDamageType
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorDamageType"] = {
    ["Grenade"] = "Grenade",
    ["None"] = "None",
    ["ParticleDisruptor"] = "ParticleDisruptor",
    ["Scp096"] = "Scp096",
    ["ServerCommand"] = "ServerCommand",
    ["Weapon"] = "Weapon",
}
Enums.DoorDamageType = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorDamageType"]
DoorDamageType = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorDamageType"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorEventOpenerExtension_002b_OpenerEventType
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorEventOpenerExtension+OpenerEventType"] = {
    ["DeconEvac"] = "DeconEvac",
    ["DeconFinish"] = "DeconFinish",
    ["DeconReset"] = "DeconReset",
    ["WarheadCancel"] = "WarheadCancel",
    ["WarheadStart"] = "WarheadStart",
}
Enums.OpenerEventType = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorEventOpenerExtension+OpenerEventType"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorLockMode
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorLockMode"] = {
    ["CanClose"] = "CanClose",
    ["CanOpen"] = "CanOpen",
    ["FullLock"] = "FullLock",
    ["ScpOverride"] = "ScpOverride",
}
Enums.DoorLockMode = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorLockMode"]
DoorLockMode = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorLockMode"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorLockReason
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorLockReason"] = {
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
Enums.DoorLockReason = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorLockReason"]
DoorLockReason = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorLockReason"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorPermissionFlags
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorPermissionFlags"] = {
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
Enums.DoorPermissionFlags = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorPermissionFlags"]
DoorPermissionFlags = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorPermissionFlags"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_DoorUtils_002e_DoorVariant_002b_CollisionsDisablingReasons
Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorVariant+CollisionsDisablingReasons"] = {
    ["DoorClosing"] = "DoorClosing",
    ["Scp106"] = "Scp106",
}
Enums.CollisionsDisablingReasons = Enums["Il2CppInteractables.Interobjects.DoorUtils.DoorVariant+CollisionsDisablingReasons"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_ElevatorChamber_002b_ElevatorSequence
Enums["Il2CppInteractables.Interobjects.ElevatorChamber+ElevatorSequence"] = {
    ["Arriving"] = "Arriving",
    ["DoorClosing"] = "DoorClosing",
    ["DoorOpening"] = "DoorOpening",
    ["MovingAway"] = "MovingAway",
    ["Ready"] = "Ready",
    ["StartingSequence"] = "StartingSequence",
}
Enums.ElevatorSequence = Enums["Il2CppInteractables.Interobjects.ElevatorChamber+ElevatorSequence"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_ElevatorGroup
Enums["Il2CppInteractables.Interobjects.ElevatorGroup"] = {
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
Enums.ElevatorGroup = Enums["Il2CppInteractables.Interobjects.ElevatorGroup"]
ElevatorGroup = Enums["Il2CppInteractables.Interobjects.ElevatorGroup"]

---@enum Enum_Il2CppInteractables_002e_Interobjects_002e_PopupInterobject_002b_PopupState
Enums["Il2CppInteractables.Interobjects.PopupInterobject+PopupState"] = {
    ["Disabling"] = "Disabling",
    ["Enabling"] = "Enabling",
    ["Off"] = "Off",
    ["On"] = "On",
}
Enums.PopupState = Enums["Il2CppInteractables.Interobjects.PopupInterobject+PopupState"]

---@enum Enum_Il2CppInventorySystem_002e_Drawers_002e_AlertContent_002b_ColorMode
Enums["Il2CppInventorySystem.Drawers.AlertContent+ColorMode"] = {
    ["Accented"] = "Accented",
    ["Role"] = "Role",
    ["White"] = "White",
}
Enums.ColorMode = Enums["Il2CppInventorySystem.Drawers.AlertContent+ColorMode"]

---@enum Enum_Il2CppInventorySystem_002e_GUI_002e_InventoryGuiAction
Enums["Il2CppInventorySystem.GUI.InventoryGuiAction"] = {
    ["Drop"] = "Drop",
    ["None"] = "None",
    ["Select"] = "Select",
}
Enums.InventoryGuiAction = Enums["Il2CppInventorySystem.GUI.InventoryGuiAction"]
InventoryGuiAction = Enums["Il2CppInventorySystem.GUI.InventoryGuiAction"]

---@enum Enum_Il2CppInventorySystem_002e_GUI_002e_InventoryGuiTranslation
Enums["Il2CppInventorySystem.GUI.InventoryGuiTranslation"] = {
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
Enums.InventoryGuiTranslation = Enums["Il2CppInventorySystem.GUI.InventoryGuiTranslation"]
InventoryGuiTranslation = Enums["Il2CppInventorySystem.GUI.InventoryGuiTranslation"]

---@enum Enum_Il2CppInventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeyApperance
Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyApperance"] = {
    ["Hidden"] = "Hidden",
    ["NormalIcon"] = "NormalIcon",
    ["SmallIcon"] = "SmallIcon",
}
Enums.HotkeyApperance = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyApperance"]
HotkeyApperance = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyApperance"]

---@enum Enum_Il2CppInventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeyItemGroup
Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyItemGroup"] = {
    ["AnyFirearm"] = "AnyFirearm",
    ["AnyKeycard"] = "AnyKeycard",
    ["AnyScpItem"] = "AnyScpItem",
    ["AnyThrowable"] = "AnyThrowable",
}
Enums.HotkeyItemGroup = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyItemGroup"]
HotkeyItemGroup = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyItemGroup"]

---@enum Enum_Il2CppInventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeyOverrideOption
Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyOverrideOption"] = {
    ["BestMedical"] = "BestMedical",
    ["NoOverride"] = "NoOverride",
    ["PrioritizeNearbyDoors"] = "PrioritizeNearbyDoors",
    ["RememberPrevious"] = "RememberPrevious",
    ["SkipEmptyFirearms"] = "SkipEmptyFirearms",
}
Enums.HotkeyOverrideOption = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyOverrideOption"]
HotkeyOverrideOption = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeyOverrideOption"]

---@enum Enum_Il2CppInventorySystem_002e_Hotkeys_002e_Customization_002e_HotkeySortMode
Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeySortMode"] = {
    ["InventoryOrder"] = "InventoryOrder",
    ["ItemPoolOrder"] = "ItemPoolOrder",
}
Enums.HotkeySortMode = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeySortMode"]
HotkeySortMode = Enums["Il2CppInventorySystem.Hotkeys.Customization.HotkeySortMode"]

---@enum Enum_Il2CppInventorySystem_002e_Hotkeys_002e_Customization_002e_PoolElementData_002b_ElementType
Enums["Il2CppInventorySystem.Hotkeys.Customization.PoolElementData+ElementType"] = {
    ["Group"] = "Group",
    ["Order"] = "Order",
    ["SpecificItem"] = "SpecificItem",
}
Enums.ElementType = Enums["Il2CppInventorySystem.Hotkeys.Customization.PoolElementData+ElementType"]

---@enum Enum_Il2CppInventorySystem_002e_Hotkeys_002e_HotkeysTranslation
Enums["Il2CppInventorySystem.Hotkeys.HotkeysTranslation"] = {
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
Enums.HotkeysTranslation = Enums["Il2CppInventorySystem.Hotkeys.HotkeysTranslation"]
HotkeysTranslation = Enums["Il2CppInventorySystem.Hotkeys.HotkeysTranslation"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Autosync_002e_AutosyncInstantiationStatus
Enums["Il2CppInventorySystem.Items.Autosync.AutosyncInstantiationStatus"] = {
    ["InventoryInstance"] = "InventoryInstance",
    ["SimulatedInstance"] = "SimulatedInstance",
    ["Template"] = "Template",
    ["Unspecified"] = "Unspecified",
}
Enums.AutosyncInstantiationStatus = Enums["Il2CppInventorySystem.Items.Autosync.AutosyncInstantiationStatus"]
AutosyncInstantiationStatus = Enums["Il2CppInventorySystem.Items.Autosync.AutosyncInstantiationStatus"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Autosync_002e_AutosyncMessageType
Enums["Il2CppInventorySystem.Items.Autosync.AutosyncMessageType"] = {
    ["Cmd"] = "Cmd",
    ["RpcInstance"] = "RpcInstance",
    ["RpcTemplate"] = "RpcTemplate",
}
Enums.AutosyncMessageType = Enums["Il2CppInventorySystem.Items.Autosync.AutosyncMessageType"]
AutosyncMessageType = Enums["Il2CppInventorySystem.Items.Autosync.AutosyncMessageType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Autosync_002e_AutosyncRpc_002b_Mode
Enums["Il2CppInventorySystem.Items.Autosync.AutosyncRpc+Mode"] = {
    ["AllClients"] = "AllClients",
    ["Conditional"] = "Conditional",
    ["SpecificClient"] = "SpecificClient",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Autosync_002e_MeleeAutoSync_002b_HitResult
Enums["Il2CppInventorySystem.Items.Autosync.MeleeAutoSync+HitResult"] = {
    ["HitPlayer"] = "HitPlayer",
    ["HitWall"] = "HitWall",
    ["KilledPlayer"] = "KilledPlayer",
    ["Missed"] = "Missed",
}
Enums.HitResult = Enums["Il2CppInventorySystem.Items.Autosync.MeleeAutoSync+HitResult"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Autosync_002e_MessageInterceptionResult
Enums["Il2CppInventorySystem.Items.Autosync.MessageInterceptionResult"] = {
    ["Continue"] = "Continue",
    ["ResetAndContinue"] = "ResetAndContinue",
    ["Stop"] = "Stop",
}
Enums.MessageInterceptionResult = Enums["Il2CppInventorySystem.Items.Autosync.MessageInterceptionResult"]
MessageInterceptionResult = Enums["Il2CppInventorySystem.Items.Autosync.MessageInterceptionResult"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_BlockedInteraction
Enums["Il2CppInventorySystem.Items.BlockedInteraction"] = {
    ["All"] = "All",
    ["BeDisarmed"] = "BeDisarmed",
    ["GeneralInteractions"] = "GeneralInteractions",
    ["GrabItems"] = "GrabItems",
    ["ItemPrimaryAction"] = "ItemPrimaryAction",
    ["ItemUsage"] = "ItemUsage",
    ["OpenInventory"] = "OpenInventory",
    ["UndisarmPlayers"] = "UndisarmPlayers",
}
Enums.BlockedInteraction = Enums["Il2CppInventorySystem.Items.BlockedInteraction"]
BlockedInteraction = Enums["Il2CppInventorySystem.Items.BlockedInteraction"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentDescriptiveAdvantages
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"] = {
    ["AmmoCounter"] = "AmmoCounter",
    ["FlashSuppression"] = "FlashSuppression",
    ["Flashlight"] = "Flashlight",
    ["NightVision"] = "NightVision",
    ["None"] = "None",
    ["ShotgunShells"] = "ShotgunShells",
}
Enums.AttachmentDescriptiveAdvantages = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"]
AttachmentDescriptiveAdvantages = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveAdvantages"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentDescriptiveDownsides
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"] = {
    ["Laser"] = "Laser",
    ["NoHeadshots"] = "NoHeadshots",
    ["None"] = "None",
}
Enums.AttachmentDescriptiveDownsides = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"]
AttachmentDescriptiveDownsides = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentDescriptiveDownsides"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentEditorsTranslation
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"] = {
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
Enums.AttachmentEditorsTranslation = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"]
AttachmentEditorsTranslation = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentEditorsTranslation"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentName
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentName"] = {
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
Enums.AttachmentName = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentName"]
AttachmentName = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentName"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentParam
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentParam"] = {
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
Enums.AttachmentParam = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentParam"]
AttachmentParam = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentParam"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentParamState
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentParamState"] = {
    ["ActiveAndDisplayed"] = "ActiveAndDisplayed",
    ["Disabled"] = "Disabled",
    ["SilentlyActive"] = "SilentlyActive",
    ["UserInterface"] = "UserInterface",
}
Enums.AttachmentParamState = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentParamState"]
AttachmentParamState = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentParamState"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_AttachmentSlot
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentSlot"] = {
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
Enums.AttachmentSlot = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentSlot"]
AttachmentSlot = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.AttachmentSlot"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_FlashlightAttachment_002b_RpcType
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.FlashlightAttachment+RpcType"] = {
    ["Disabled"] = "Disabled",
    ["Enabled"] = "Enabled",
    ["FullResync"] = "FullResync",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_ParameterMixingMode
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.ParameterMixingMode"] = {
    ["Additive"] = "Additive",
    ["Override"] = "Override",
    ["Percent"] = "Percent",
}
Enums.ParameterMixingMode = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.ParameterMixingMode"]
ParameterMixingMode = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.ParameterMixingMode"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Attachments_002e_WorkstationController_002b_WorkstationStatus
Enums["Il2CppInventorySystem.Items.Firearms.Attachments.WorkstationController+WorkstationStatus"] = {
    ["Offline"] = "Offline",
    ["Online"] = "Online",
    ["PoweringDown"] = "PoweringDown",
    ["PoweringUp"] = "PoweringUp",
}
Enums.WorkstationStatus = Enums["Il2CppInventorySystem.Items.Firearms.Attachments.WorkstationController+WorkstationStatus"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_ConditionalEvaluator_002b_AttachmentStatus
Enums["Il2CppInventorySystem.Items.Firearms.Extensions.ConditionalEvaluator+AttachmentStatus"] = {
    ["Disabled"] = "Disabled",
    ["Enabled"] = "Enabled",
    ["EnabledAndNotReady"] = "EnabledAndNotReady",
    ["EnabledAndReady"] = "EnabledAndReady",
}
Enums.AttachmentStatus = Enums["Il2CppInventorySystem.Items.Firearms.Extensions.ConditionalEvaluator+AttachmentStatus"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_ConditionalEvaluator_002b_OtherCondition
Enums["Il2CppInventorySystem.Items.Firearms.Extensions.ConditionalEvaluator+OtherCondition"] = {
    ["Equipped"] = "Equipped",
    ["Firstperson"] = "Firstperson",
    ["Pickup"] = "Pickup",
    ["Thirdperson"] = "Thirdperson",
}
Enums.OtherCondition = Enums["Il2CppInventorySystem.Items.Firearms.Extensions.ConditionalEvaluator+OtherCondition"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_DisruptorDualCamExtension_002b_IconType
Enums["Il2CppInventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension+IconType"] = {
    ["BurstMode"] = "BurstMode",
    ["CrashWarn"] = "CrashWarn",
    ["CriticalWarn"] = "CriticalWarn",
    ["DesintegratorMode"] = "DesintegratorMode",
    ["None"] = "None",
}
Enums.IconType = Enums["Il2CppInventorySystem.Items.Firearms.Extensions.DisruptorDualCamExtension+IconType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_ViewmodelReloadOnlyLayerExtension_002b_Mode
Enums["Il2CppInventorySystem.Items.Firearms.Extensions.ViewmodelReloadOnlyLayerExtension+Mode"] = {
    ["DuringReloadsAndUnloads"] = "DuringReloadsAndUnloads",
    ["OutsideReloadsAndUnloads"] = "OutsideReloadsAndUnloads",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Extensions_002e_WorldmodelMagazineExtension_002b_RemovalMode
Enums["Il2CppInventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension+RemovalMode"] = {
    ["DeactivateObject"] = "DeactivateObject",
    ["SetScaleZero"] = "SetScaleZero",
}
Enums.RemovalMode = Enums["Il2CppInventorySystem.Items.Firearms.Extensions.WorldmodelMagazineExtension+RemovalMode"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_FirearmCategory
Enums["Il2CppInventorySystem.Items.Firearms.FirearmCategory"] = {
    ["LightMachineGun"] = "LightMachineGun",
    ["Pistol"] = "Pistol",
    ["Revolver"] = "Revolver",
    ["Rifle"] = "Rifle",
    ["Shotgun"] = "Shotgun",
    ["SubmachineGun"] = "SubmachineGun",
    ["Unclassified"] = "Unclassified",
}
Enums.FirearmCategory = Enums["Il2CppInventorySystem.Items.Firearms.FirearmCategory"]
FirearmCategory = Enums["Il2CppInventorySystem.Items.Firearms.FirearmCategory"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_FirearmWorldmodelType
Enums["Il2CppInventorySystem.Items.Firearms.FirearmWorldmodelType"] = {
    ["Pickup"] = "Pickup",
    ["Presentation"] = "Presentation",
    ["Thirdperson"] = "Thirdperson",
}
Enums.FirearmWorldmodelType = Enums["Il2CppInventorySystem.Items.Firearms.FirearmWorldmodelType"]
FirearmWorldmodelType = Enums["Il2CppInventorySystem.Items.Firearms.FirearmWorldmodelType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AnimationToggleableReloaderModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.AnimationToggleableReloaderModule+RpcType"] = {
    ["LoadableAmmoSync"] = "LoadableAmmoSync",
    ["StopAnimations"] = "StopAnimations",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AnimatorReloaderModuleBase_002b_ReloaderMessageHeader
Enums["Il2CppInventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase+ReloaderMessageHeader"] = {
    ["Custom"] = "Custom",
    ["Reload"] = "Reload",
    ["RequestRejected"] = "RequestRejected",
    ["Stop"] = "Stop",
    ["Unload"] = "Unload",
}
Enums.ReloaderMessageHeader = Enums["Il2CppInventorySystem.Items.Firearms.Modules.AnimatorReloaderModuleBase+ReloaderMessageHeader"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AutomaticActionModule_002b_MessageHeader
Enums["Il2CppInventorySystem.Items.Firearms.Modules.AutomaticActionModule+MessageHeader"] = {
    ["CmdShoot"] = "CmdShoot",
    ["RpcDryFire"] = "RpcDryFire",
    ["RpcFire"] = "RpcFire",
    ["RpcNewPlayerSync"] = "RpcNewPlayerSync",
    ["RpcPublicSync"] = "RpcPublicSync",
    ["RpcRejectionReason"] = "RpcRejectionReason",
    ["RpcResponse"] = "RpcResponse",
}
Enums.MessageHeader = Enums["Il2CppInventorySystem.Items.Firearms.Modules.AutomaticActionModule+MessageHeader"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AutomaticActionModule_002b_RejectionReason
Enums["Il2CppInventorySystem.Items.Firearms.Modules.AutomaticActionModule+RejectionReason"] = {
    ["BoltLocked"] = "BoltLocked",
    ["ModuleBusy"] = "ModuleBusy",
    ["NotCocked"] = "NotCocked",
    ["TimedOut"] = "TimedOut",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_AutomaticActionModule_002b_SyncDataFlags
Enums["Il2CppInventorySystem.Items.Firearms.Modules.AutomaticActionModule+SyncDataFlags"] = {
    ["AmmoChamberedBit0"] = "AmmoChamberedBit0",
    ["AmmoChamberedBit1"] = "AmmoChamberedBit1",
    ["AmmoChamberedBit2"] = "AmmoChamberedBit2",
    ["AmmoChamberedBit3"] = "AmmoChamberedBit3",
    ["AmmoChamberedFilter"] = "AmmoChamberedFilter",
    ["BoltLocked"] = "BoltLocked",
    ["Cocked"] = "Cocked",
    ["None"] = "None",
}
Enums.SyncDataFlags = Enums["Il2CppInventorySystem.Items.Firearms.Modules.AutomaticActionModule+SyncDataFlags"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_CylinderAmmoModule_002b_ChamberState
Enums["Il2CppInventorySystem.Items.Firearms.Modules.CylinderAmmoModule+ChamberState"] = {
    ["Discharged"] = "Discharged",
    ["Empty"] = "Empty",
    ["Live"] = "Live",
}
Enums.ChamberState = Enums["Il2CppInventorySystem.Items.Firearms.Modules.CylinderAmmoModule+ChamberState"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_DisruptorActionModule_002b_FiringState
Enums["Il2CppInventorySystem.Items.Firearms.Modules.DisruptorActionModule+FiringState"] = {
    ["FiringRapid"] = "FiringRapid",
    ["FiringSingle"] = "FiringSingle",
    ["None"] = "None",
}
Enums.FiringState = Enums["Il2CppInventorySystem.Items.Firearms.Modules.DisruptorActionModule+FiringState"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_DisruptorActionModule_002b_MessageType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.DisruptorActionModule+MessageType"] = {
    ["CmdConfirmDischarge"] = "CmdConfirmDischarge",
    ["CmdRequestStartFiring"] = "CmdRequestStartFiring",
    ["RpcOnShot"] = "RpcOnShot",
    ["RpcRequireReloadFalse"] = "RpcRequireReloadFalse",
    ["RpcRequireReloadFullResync"] = "RpcRequireReloadFullResync",
    ["RpcRequireReloadTrue"] = "RpcRequireReloadTrue",
    ["RpcStartFiring"] = "RpcStartFiring",
    ["RpcStopFiring"] = "RpcStopFiring",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_DoubleActionModule_002b_MessageType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.DoubleActionModule+MessageType"] = {
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

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_EventBasedEquipperModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.EventBasedEquipperModule+RpcType"] = {
    ["FirstTimeFalse"] = "FirstTimeFalse",
    ["FirstTimeTrue"] = "FirstTimeTrue",
    ["ResyncAllFirstTime"] = "ResyncAllFirstTime",
    ["SeedSync"] = "SeedSync",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_IReloadUnloadValidatorModule_002b_Authorization
Enums["Il2CppInventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule+Authorization"] = {
    ["Allowed"] = "Allowed",
    ["Idle"] = "Idle",
    ["Vetoed"] = "Vetoed",
}
Enums.Authorization = Enums["Il2CppInventorySystem.Items.Firearms.Modules.IReloadUnloadValidatorModule+Authorization"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_ImpactEffectsModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.ImpactEffectsModule+RpcType"] = {
    ["ImpactDecal"] = "ImpactDecal",
    ["PlayerHit"] = "PlayerHit",
    ["TracerDefault"] = "TracerDefault",
    ["TracerOverride"] = "TracerOverride",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Misc_002e_StatModifier_002b_ModifierMode
Enums["Il2CppInventorySystem.Items.Firearms.Modules.Misc.StatModifier+ModifierMode"] = {
    ["Add"] = "Add",
    ["Inactive"] = "Inactive",
    ["Multiply"] = "Multiply",
    ["Override"] = "Override",
}
Enums.ModifierMode = Enums["Il2CppInventorySystem.Items.Firearms.Modules.Misc.StatModifier+ModifierMode"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_PumpActionModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.PumpActionModule+RpcType"] = {
    ["ResyncAll"] = "ResyncAll",
    ["ResyncOne"] = "ResyncOne",
    ["SchedulePump"] = "SchedulePump",
    ["Shoot"] = "Shoot",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127Tier
Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"] = {
    ["Tier1"] = "Tier1",
    ["Tier2"] = "Tier2",
    ["Tier3"] = "Tier3",
}
Enums.Scp127Tier = Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"]
Scp127Tier = Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127Tier"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127VoiceLineManagerModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineManagerModule+RpcType"] = {
    ["OwnerRegistered"] = "OwnerRegistered",
    ["PlayLine"] = "PlayLine",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127VoiceLinesTranslation
Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"] = {
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
Enums.Scp127VoiceLinesTranslation = Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"]
Scp127VoiceLinesTranslation = Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLinesTranslation"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Modules_002e_Scp127_002e_Scp127VoiceTriggerBase_002b_VoiceLinePriority
Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase+VoiceLinePriority"] = {
    ["High"] = "High",
    ["Low"] = "Low",
    ["Normal"] = "Normal",
    ["VeryHigh"] = "VeryHigh",
}
Enums.VoiceLinePriority = Enums["Il2CppInventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase+VoiceLinePriority"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Firearms_002e_Thirdperson_002e_FirearmThirdpersonItem_002b_FirearmAnim
Enums["Il2CppInventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem+FirearmAnim"] = {
    ["Ads"] = "Ads",
    ["Hip"] = "Hip",
    ["Reload"] = "Reload",
}
Enums.FirearmAnim = Enums["Il2CppInventorySystem.Items.Firearms.Thirdperson.FirearmThirdpersonItem+FirearmAnim"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_ItemAddReason
Enums["Il2CppInventorySystem.Items.ItemAddReason"] = {
    ["AdminCommand"] = "AdminCommand",
    ["PickedUp"] = "PickedUp",
    ["Scp2536"] = "Scp2536",
    ["Scp914Upgrade"] = "Scp914Upgrade",
    ["StartingItem"] = "StartingItem",
    ["StatusEffect"] = "StatusEffect",
    ["Undefined"] = "Undefined",
}
Enums.ItemAddReason = Enums["Il2CppInventorySystem.Items.ItemAddReason"]
ItemAddReason = Enums["Il2CppInventorySystem.Items.ItemAddReason"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_ItemDescriptionType
Enums["Il2CppInventorySystem.Items.ItemDescriptionType"] = {
    ["Armor"] = "Armor",
    ["Custom"] = "Custom",
    ["Default"] = "Default",
    ["Firearm"] = "Firearm",
    ["Keycard"] = "Keycard",
    ["None"] = "None",
    ["Scp330Bag"] = "Scp330Bag",
}
Enums.ItemDescriptionType = Enums["Il2CppInventorySystem.Items.ItemDescriptionType"]
ItemDescriptionType = Enums["Il2CppInventorySystem.Items.ItemDescriptionType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Jailbird_002e_JailbirdMessageType
Enums["Il2CppInventorySystem.Items.Jailbird.JailbirdMessageType"] = {
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
Enums.JailbirdMessageType = Enums["Il2CppInventorySystem.Items.Jailbird.JailbirdMessageType"]
JailbirdMessageType = Enums["Il2CppInventorySystem.Items.Jailbird.JailbirdMessageType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Jailbird_002e_JailbirdWearState
Enums["Il2CppInventorySystem.Items.Jailbird.JailbirdWearState"] = {
    ["AlmostBroken"] = "AlmostBroken",
    ["Broken"] = "Broken",
    ["Healthy"] = "Healthy",
    ["HighWear"] = "HighWear",
    ["LowWear"] = "LowWear",
    ["MediumWear"] = "MediumWear",
}
Enums.JailbirdWearState = Enums["Il2CppInventorySystem.Items.Jailbird.JailbirdWearState"]
JailbirdWearState = Enums["Il2CppInventorySystem.Items.Jailbird.JailbirdWearState"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Keycards_002e_ChaosKeycardItem_002b_ChaosMsgType
Enums["Il2CppInventorySystem.Items.Keycards.ChaosKeycardItem+ChaosMsgType"] = {
    ["MovementSwitch"] = "MovementSwitch",
    ["NewConnectionFullSync"] = "NewConnectionFullSync",
    ["SnakeMsgSync"] = "SnakeMsgSync",
    ["UseDetails"] = "UseDetails",
}
Enums.ChaosMsgType = Enums["Il2CppInventorySystem.Items.Keycards.ChaosKeycardItem+ChaosMsgType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Keycards_002e_KeycardItem_002b_MsgType
Enums["Il2CppInventorySystem.Items.Keycards.KeycardItem+MsgType"] = {
    ["Custom"] = "Custom",
    ["Inspect"] = "Inspect",
    ["NewPlayerFullResync"] = "NewPlayerFullResync",
    ["OnKeycardUsed"] = "OnKeycardUsed",
}
Enums.MsgType = Enums["Il2CppInventorySystem.Items.Keycards.KeycardItem+MsgType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Keycards_002e_TranslatedLabelDetail_002b_KeycardLabelTranslation
Enums["Il2CppInventorySystem.Items.Keycards.TranslatedLabelDetail+KeycardLabelTranslation"] = {
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
Enums.KeycardLabelTranslation = Enums["Il2CppInventorySystem.Items.Keycards.TranslatedLabelDetail+KeycardLabelTranslation"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MarshmallowMan_002e_MarshmallowItem_002b_RpcType
Enums["Il2CppInventorySystem.Items.MarshmallowMan.MarshmallowItem+RpcType"] = {
    ["AttackStart"] = "AttackStart",
    ["Cackle"] = "Cackle",
    ["Evil"] = "Evil",
    ["Hit"] = "Hit",
    ["Holster"] = "Holster",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MicroHID_002e_MicroHIDMonitor_002b_LcdElementType
Enums["Il2CppInventorySystem.Items.MicroHID.MicroHIDMonitor+LcdElementType"] = {
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
Enums.LcdElementType = Enums["Il2CppInventorySystem.Items.MicroHID.MicroHIDMonitor+LcdElementType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_BrokenSyncModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.MicroHID.Modules.BrokenSyncModule+RpcType"] = {
    ["BreakSpecific"] = "BreakSpecific",
    ["ResyncAll"] = "ResyncAll",
    ["UnbreakSpecific"] = "UnbreakSpecific",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_DrawAndInspectorModule_002b_RpcType
Enums["Il2CppInventorySystem.Items.MicroHID.Modules.DrawAndInspectorModule+RpcType"] = {
    ["AddPickup"] = "AddPickup",
    ["InspectRequested"] = "InspectRequested",
    ["OnEquipped"] = "OnEquipped",
    ["OnHolstered"] = "OnHolstered",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_InputSyncModule_002b_SyncData
Enums["Il2CppInventorySystem.Items.MicroHID.Modules.InputSyncModule+SyncData"] = {
    ["None"] = "None",
    ["Primary"] = "Primary",
    ["Secondary"] = "Secondary",
}
Enums.SyncData = Enums["Il2CppInventorySystem.Items.MicroHID.Modules.InputSyncModule+SyncData"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_MicroHidFiringMode
Enums["Il2CppInventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"] = {
    ["BrokenFire"] = "BrokenFire",
    ["ChargeFire"] = "ChargeFire",
    ["PrimaryFire"] = "PrimaryFire",
}
Enums.MicroHidFiringMode = Enums["Il2CppInventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"]
MicroHidFiringMode = Enums["Il2CppInventorySystem.Items.MicroHID.Modules.MicroHidFiringMode"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_MicroHID_002e_Modules_002e_MicroHidPhase
Enums["Il2CppInventorySystem.Items.MicroHID.Modules.MicroHidPhase"] = {
    ["Firing"] = "Firing",
    ["Standby"] = "Standby",
    ["WindingDown"] = "WindingDown",
    ["WindingUp"] = "WindingUp",
    ["WoundUpSustain"] = "WoundUpSustain",
}
Enums.MicroHidPhase = Enums["Il2CppInventorySystem.Items.MicroHID.Modules.MicroHidPhase"]
MicroHidPhase = Enums["Il2CppInventorySystem.Items.MicroHID.Modules.MicroHidPhase"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Pickups_002e_PickupStandardPhysics_002b_FreezingMode
Enums["Il2CppInventorySystem.Items.Pickups.PickupStandardPhysics+FreezingMode"] = {
    ["Default"] = "Default",
    ["FreezeWhenSleeping"] = "FreezeWhenSleeping",
    ["NeverFreeze"] = "NeverFreeze",
}
Enums.FreezingMode = Enums["Il2CppInventorySystem.Items.Pickups.PickupStandardPhysics+FreezingMode"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Pickups_002e_PickupSyncInfo_002b_PickupFlags
Enums["Il2CppInventorySystem.Items.Pickups.PickupSyncInfo+PickupFlags"] = {
    ["InUse"] = "InUse",
    ["Locked"] = "Locked",
}
Enums.PickupFlags = Enums["Il2CppInventorySystem.Items.Pickups.PickupSyncInfo+PickupFlags"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Radio_002e_RadioMessages_002b_RadioCommand
Enums["Il2CppInventorySystem.Items.Radio.RadioMessages+RadioCommand"] = {
    ["ChangeRange"] = "ChangeRange",
    ["Disable"] = "Disable",
    ["Enable"] = "Enable",
}
Enums.RadioCommand = Enums["Il2CppInventorySystem.Items.Radio.RadioMessages+RadioCommand"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Radio_002e_RadioMessages_002b_RadioRangeLevel
Enums["Il2CppInventorySystem.Items.Radio.RadioMessages+RadioRangeLevel"] = {
    ["HighRange"] = "HighRange",
    ["LowRange"] = "LowRange",
    ["MediumRange"] = "MediumRange",
    ["RadioDisabled"] = "RadioDisabled",
    ["UltraRange"] = "UltraRange",
}
Enums.RadioRangeLevel = Enums["Il2CppInventorySystem.Items.Radio.RadioMessages+RadioRangeLevel"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Scp1509_002e_Scp1509MessageType
Enums["Il2CppInventorySystem.Items.Scp1509.Scp1509MessageType"] = {
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
Enums.Scp1509MessageType = Enums["Il2CppInventorySystem.Items.Scp1509.Scp1509MessageType"]
Scp1509MessageType = Enums["Il2CppInventorySystem.Items.Scp1509.Scp1509MessageType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Thirdperson_002e_AnimItemLayer3p
Enums["Il2CppInventorySystem.Items.Thirdperson.AnimItemLayer3p"] = {
    ["Additive"] = "Additive",
    ["Left"] = "Left",
    ["Middle"] = "Middle",
    ["PreMovement"] = "PreMovement",
    ["Right"] = "Right",
}
Enums.AnimItemLayer3p = Enums["Il2CppInventorySystem.Items.Thirdperson.AnimItemLayer3p"]
AnimItemLayer3p = Enums["Il2CppInventorySystem.Items.Thirdperson.AnimItemLayer3p"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Thirdperson_002e_AnimState3p
Enums["Il2CppInventorySystem.Items.Thirdperson.AnimState3p"] = {
    ["Additive0"] = "Additive0",
    ["Additive1"] = "Additive1",
    ["Additive2"] = "Additive2",
    ["Override0"] = "Override0",
    ["Override1"] = "Override1",
    ["Override2"] = "Override2",
    ["RunForward"] = "RunForward",
    ["SprintForward"] = "SprintForward",
}
Enums.AnimState3p = Enums["Il2CppInventorySystem.Items.Thirdperson.AnimState3p"]
AnimState3p = Enums["Il2CppInventorySystem.Items.Thirdperson.AnimState3p"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_ThrowableProjectiles_002e_FlybyDetectorProjectile_002b_FriendlyFireInteraction
Enums["Il2CppInventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile+FriendlyFireInteraction"] = {
    ["CollideWithEveryone"] = "CollideWithEveryone",
    ["CollideWithOnlyEnemies"] = "CollideWithOnlyEnemies",
    ["UseFriendlyFireConfig"] = "UseFriendlyFireConfig",
}
Enums.FriendlyFireInteraction = Enums["Il2CppInventorySystem.Items.ThrowableProjectiles.FlybyDetectorProjectile+FriendlyFireInteraction"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_ThrowableProjectiles_002e_ThrowableNetworkHandler_002b_RequestType
Enums["Il2CppInventorySystem.Items.ThrowableProjectiles.ThrowableNetworkHandler+RequestType"] = {
    ["BeginThrow"] = "BeginThrow",
    ["CancelThrow"] = "CancelThrow",
    ["ConfirmThrowFullForce"] = "ConfirmThrowFullForce",
    ["ConfirmThrowWeak"] = "ConfirmThrowWeak",
}

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Usables_002e_Scp1344_002e_Scp1344Status
Enums["Il2CppInventorySystem.Items.Usables.Scp1344.Scp1344Status"] = {
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
Enums.Scp1344Status = Enums["Il2CppInventorySystem.Items.Usables.Scp1344.Scp1344Status"]
Scp1344Status = Enums["Il2CppInventorySystem.Items.Usables.Scp1344.Scp1344Status"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Usables_002e_Scp244_002e_Scp244State
Enums["Il2CppInventorySystem.Items.Usables.Scp244.Scp244State"] = {
    ["Active"] = "Active",
    ["Destroyed"] = "Destroyed",
    ["Idle"] = "Idle",
    ["PickedUp"] = "PickedUp",
}
Enums.Scp244State = Enums["Il2CppInventorySystem.Items.Usables.Scp244.Scp244State"]
Scp244State = Enums["Il2CppInventorySystem.Items.Usables.Scp244.Scp244State"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Usables_002e_Scp330_002e_CandyKindID
Enums["Il2CppInventorySystem.Items.Usables.Scp330.CandyKindID"] = {
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
Enums.CandyKindID = Enums["Il2CppInventorySystem.Items.Usables.Scp330.CandyKindID"]
CandyKindID = Enums["Il2CppInventorySystem.Items.Usables.Scp330.CandyKindID"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Usables_002e_Scp330_002e_Scp330Translations_002b_Entry
Enums["Il2CppInventorySystem.Items.Usables.Scp330.Scp330Translations+Entry"] = {
    ["Candies"] = "Candies",
}
Enums.Entry = Enums["Il2CppInventorySystem.Items.Usables.Scp330.Scp330Translations+Entry"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_Usables_002e_StatusMessage_002b_StatusType
Enums["Il2CppInventorySystem.Items.Usables.StatusMessage+StatusType"] = {
    ["Cancel"] = "Cancel",
    ["Start"] = "Start",
}
Enums.StatusType = Enums["Il2CppInventorySystem.Items.Usables.StatusMessage+StatusType"]

---@enum Enum_Il2CppInventorySystem_002e_Items_002e_WearableSlot
Enums["Il2CppInventorySystem.Items.WearableSlot"] = {
    ["Body"] = "Body",
    ["Eyes"] = "Eyes",
    ["Hat"] = "Hat",
}
Enums.WearableSlot = Enums["Il2CppInventorySystem.Items.WearableSlot"]
WearableSlot = Enums["Il2CppInventorySystem.Items.WearableSlot"]

---@enum Enum_Il2CppInventorySystem_002e_Searching_002e_SearchSessionPipe_002b_Activity
Enums["Il2CppInventorySystem.Searching.SearchSessionPipe+Activity"] = {
    ["Idle"] = "Idle",
    ["Promised"] = "Promised",
    ["Requested"] = "Requested",
}
Enums.Activity = Enums["Il2CppInventorySystem.Searching.SearchSessionPipe+Activity"]

---@enum Enum_Il2CppLightContainmentZoneDecontamination_002e_DecontaminationController_002b_DecontaminationPhase_002b_PhaseFunction
Enums["Il2CppLightContainmentZoneDecontamination.DecontaminationController+DecontaminationPhase+PhaseFunction"] = {
    ["Final"] = "Final",
    ["GloballyAudible"] = "GloballyAudible",
    ["None"] = "None",
    ["OpenCheckpoints"] = "OpenCheckpoints",
}
Enums.PhaseFunction = Enums["Il2CppLightContainmentZoneDecontamination.DecontaminationController+DecontaminationPhase+PhaseFunction"]

---@enum Enum_Il2CppLightContainmentZoneDecontamination_002e_DecontaminationController_002b_DecontaminationStatus
Enums["Il2CppLightContainmentZoneDecontamination.DecontaminationController+DecontaminationStatus"] = {
    ["Disabled"] = "Disabled",
    ["Forced"] = "Forced",
    ["None"] = "None",
}
Enums.DecontaminationStatus = Enums["Il2CppLightContainmentZoneDecontamination.DecontaminationController+DecontaminationStatus"]

---@enum Enum_Il2CppLiteNetLib_002e_ConnectRequestResult
Enums["Il2CppLiteNetLib.ConnectRequestResult"] = {
    ["NewConnection"] = "NewConnection",
    ["None"] = "None",
    ["P2PLose"] = "P2PLose",
    ["Reconnection"] = "Reconnection",
}
Enums.ConnectRequestResult = Enums["Il2CppLiteNetLib.ConnectRequestResult"]
ConnectRequestResult = Enums["Il2CppLiteNetLib.ConnectRequestResult"]

---@enum Enum_Il2CppLiteNetLib_002e_ConnectionRequestResult
Enums["Il2CppLiteNetLib.ConnectionRequestResult"] = {
    ["Accept"] = "Accept",
    ["None"] = "None",
    ["Reject"] = "Reject",
    ["RejectForce"] = "RejectForce",
}
Enums.ConnectionRequestResult = Enums["Il2CppLiteNetLib.ConnectionRequestResult"]
ConnectionRequestResult = Enums["Il2CppLiteNetLib.ConnectionRequestResult"]

---@enum Enum_Il2CppLiteNetLib_002e_ConnectionState
Enums["Il2CppLiteNetLib.ConnectionState"] = {
    ["Any"] = "Any",
    ["Connected"] = "Connected",
    ["Disconnected"] = "Disconnected",
    ["EndPointChange"] = "EndPointChange",
    ["Outgoing"] = "Outgoing",
    ["ShutdownRequested"] = "ShutdownRequested",
}
Enums.ConnectionState = Enums["Il2CppLiteNetLib.ConnectionState"]
ConnectionState = Enums["Il2CppLiteNetLib.ConnectionState"]

---@enum Enum_Il2CppLiteNetLib_002e_DeliveryMethod
Enums["Il2CppLiteNetLib.DeliveryMethod"] = {
    ["ReliableOrdered"] = "ReliableOrdered",
    ["ReliableSequenced"] = "ReliableSequenced",
    ["ReliableUnordered"] = "ReliableUnordered",
    ["Sequenced"] = "Sequenced",
    ["Unreliable"] = "Unreliable",
}
Enums.DeliveryMethod = Enums["Il2CppLiteNetLib.DeliveryMethod"]
DeliveryMethod = Enums["Il2CppLiteNetLib.DeliveryMethod"]

---@enum Enum_Il2CppLiteNetLib_002e_DisconnectReason
Enums["Il2CppLiteNetLib.DisconnectReason"] = {
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
Enums.DisconnectReason = Enums["Il2CppLiteNetLib.DisconnectReason"]
DisconnectReason = Enums["Il2CppLiteNetLib.DisconnectReason"]

---@enum Enum_Il2CppLiteNetLib_002e_DisconnectResult
Enums["Il2CppLiteNetLib.DisconnectResult"] = {
    ["Disconnect"] = "Disconnect",
    ["None"] = "None",
    ["Reject"] = "Reject",
}
Enums.DisconnectResult = Enums["Il2CppLiteNetLib.DisconnectResult"]
DisconnectResult = Enums["Il2CppLiteNetLib.DisconnectResult"]

---@enum Enum_Il2CppLiteNetLib_002e_LocalAddrType
Enums["Il2CppLiteNetLib.LocalAddrType"] = {
    ["All"] = "All",
    ["IPv4"] = "IPv4",
    ["IPv6"] = "IPv6",
}
Enums.LocalAddrType = Enums["Il2CppLiteNetLib.LocalAddrType"]
LocalAddrType = Enums["Il2CppLiteNetLib.LocalAddrType"]

---@enum Enum_Il2CppLiteNetLib_002e_NatAddressType
Enums["Il2CppLiteNetLib.NatAddressType"] = {
    ["External"] = "External",
    ["Internal"] = "Internal",
}
Enums.NatAddressType = Enums["Il2CppLiteNetLib.NatAddressType"]
NatAddressType = Enums["Il2CppLiteNetLib.NatAddressType"]

---@enum Enum_Il2CppLiteNetLib_002e_NetEvent_002b_EType
Enums["Il2CppLiteNetLib.NetEvent+EType"] = {
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
Enums.EType = Enums["Il2CppLiteNetLib.NetEvent+EType"]

---@enum Enum_Il2CppLiteNetLib_002e_NetLogLevel
Enums["Il2CppLiteNetLib.NetLogLevel"] = {
    ["Error"] = "Error",
    ["Info"] = "Info",
    ["Trace"] = "Trace",
    ["Warning"] = "Warning",
}
Enums.NetLogLevel = Enums["Il2CppLiteNetLib.NetLogLevel"]
NetLogLevel = Enums["Il2CppLiteNetLib.NetLogLevel"]

---@enum Enum_Il2CppLiteNetLib_002e_PacketProperty
Enums["Il2CppLiteNetLib.PacketProperty"] = {
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
Enums.PacketProperty = Enums["Il2CppLiteNetLib.PacketProperty"]
PacketProperty = Enums["Il2CppLiteNetLib.PacketProperty"]

---@enum Enum_Il2CppLiteNetLib_002e_ShutdownResult
Enums["Il2CppLiteNetLib.ShutdownResult"] = {
    ["None"] = "None",
    ["Success"] = "Success",
    ["WasConnected"] = "WasConnected",
}
Enums.ShutdownResult = Enums["Il2CppLiteNetLib.ShutdownResult"]
ShutdownResult = Enums["Il2CppLiteNetLib.ShutdownResult"]

---@enum Enum_Il2CppLiteNetLib_002e_UnconnectedMessageType
Enums["Il2CppLiteNetLib.UnconnectedMessageType"] = {
    ["BasicMessage"] = "BasicMessage",
    ["Broadcast"] = "Broadcast",
}
Enums.UnconnectedMessageType = Enums["Il2CppLiteNetLib.UnconnectedMessageType"]
UnconnectedMessageType = Enums["Il2CppLiteNetLib.UnconnectedMessageType"]

---@enum Enum_Il2CppLiteNetLib_002e_Utils_002e_NetSerializer_002b_CallType
Enums["Il2CppLiteNetLib.Utils.NetSerializer+CallType"] = {
    ["Array"] = "Array",
    ["Basic"] = "Basic",
    ["List"] = "List",
}
Enums.CallType = Enums["Il2CppLiteNetLib.Utils.NetSerializer+CallType"]

---@enum Enum_Il2CppLiteNetLib_002e_Utils_002e_NtpLeapIndicator
Enums["Il2CppLiteNetLib.Utils.NtpLeapIndicator"] = {
    ["AlarmCondition"] = "AlarmCondition",
    ["LastMinuteHas59Seconds"] = "LastMinuteHas59Seconds",
    ["LastMinuteHas61Seconds"] = "LastMinuteHas61Seconds",
    ["NoWarning"] = "NoWarning",
}
Enums.NtpLeapIndicator = Enums["Il2CppLiteNetLib.Utils.NtpLeapIndicator"]
NtpLeapIndicator = Enums["Il2CppLiteNetLib.Utils.NtpLeapIndicator"]

---@enum Enum_Il2CppLiteNetLib_002e_Utils_002e_NtpMode
Enums["Il2CppLiteNetLib.Utils.NtpMode"] = {
    ["Client"] = "Client",
    ["Server"] = "Server",
}
Enums.NtpMode = Enums["Il2CppLiteNetLib.Utils.NtpMode"]
NtpMode = Enums["Il2CppLiteNetLib.Utils.NtpMode"]

---@enum Enum_Il2CppLiteNetLib4Mirror_002e_Open_002e_Nat_002e_MappingLifetime
Enums["Il2CppLiteNetLib4Mirror.Open.Nat.MappingLifetime"] = {
    ["ForcedSession"] = "ForcedSession",
    ["Manual"] = "Manual",
    ["Permanent"] = "Permanent",
    ["Session"] = "Session",
}
Enums.MappingLifetime = Enums["Il2CppLiteNetLib4Mirror.Open.Nat.MappingLifetime"]
MappingLifetime = Enums["Il2CppLiteNetLib4Mirror.Open.Nat.MappingLifetime"]

---@enum Enum_Il2CppLiteNetLib4Mirror_002e_Open_002e_Nat_002e_NetworkProtocolType
Enums["Il2CppLiteNetLib4Mirror.Open.Nat.NetworkProtocolType"] = {
    ["Tcp"] = "Tcp",
    ["Udp"] = "Udp",
}
Enums.NetworkProtocolType = Enums["Il2CppLiteNetLib4Mirror.Open.Nat.NetworkProtocolType"]
NetworkProtocolType = Enums["Il2CppLiteNetLib4Mirror.Open.Nat.NetworkProtocolType"]

---@enum Enum_Il2CppLiteNetLib4Mirror_002e_Open_002e_Nat_002e_PortMapper
Enums["Il2CppLiteNetLib4Mirror.Open.Nat.PortMapper"] = {
    ["Pmp"] = "Pmp",
    ["Upnp"] = "Upnp",
}
Enums.PortMapper = Enums["Il2CppLiteNetLib4Mirror.Open.Nat.PortMapper"]
PortMapper = Enums["Il2CppLiteNetLib4Mirror.Open.Nat.PortMapper"]

---@enum Enum_Il2CppMapGeneration_002e_Distributors_002e_Scp079Generator_002b_GeneratorColliderId
Enums["Il2CppMapGeneration.Distributors.Scp079Generator+GeneratorColliderId"] = {
    ["CancelButton"] = "CancelButton",
    ["Door"] = "Door",
    ["Switch"] = "Switch",
}
Enums.GeneratorColliderId = Enums["Il2CppMapGeneration.Distributors.Scp079Generator+GeneratorColliderId"]

---@enum Enum_Il2CppMapGeneration_002e_Distributors_002e_Scp079Generator_002b_GeneratorFlags
Enums["Il2CppMapGeneration.Distributors.Scp079Generator+GeneratorFlags"] = {
    ["Activating"] = "Activating",
    ["Engaged"] = "Engaged",
    ["None"] = "None",
    ["Open"] = "Open",
    ["Unlocked"] = "Unlocked",
}
Enums.GeneratorFlags = Enums["Il2CppMapGeneration.Distributors.Scp079Generator+GeneratorFlags"]

---@enum Enum_Il2CppMapGeneration_002e_Distributors_002e_StructureType
Enums["Il2CppMapGeneration.Distributors.StructureType"] = {
    ["ExperimentalWeaponLocker"] = "ExperimentalWeaponLocker",
    ["LargeGunLocker"] = "LargeGunLocker",
    ["Scp079Generator"] = "Scp079Generator",
    ["ScpPedestal"] = "ScpPedestal",
    ["SmallWallCabinet"] = "SmallWallCabinet",
    ["StandardLocker"] = "StandardLocker",
    ["Workstation"] = "Workstation",
}
Enums.StructureType = Enums["Il2CppMapGeneration.Distributors.StructureType"]
StructureType = Enums["Il2CppMapGeneration.Distributors.StructureType"]

---@enum Enum_Il2CppMapGeneration_002e_FacilityZone
Enums["Il2CppMapGeneration.FacilityZone"] = {
    ["Entrance"] = "Entrance",
    ["HeavyContainment"] = "HeavyContainment",
    ["LightContainment"] = "LightContainment",
    ["None"] = "None",
    ["Other"] = "Other",
    ["Surface"] = "Surface",
}
Enums.FacilityZone = Enums["Il2CppMapGeneration.FacilityZone"]
FacilityZone = Enums["Il2CppMapGeneration.FacilityZone"]

---@enum Enum_Il2CppMapGeneration_002e_Holidays_002e_HolidayType
Enums["Il2CppMapGeneration.Holidays.HolidayType"] = {
    ["AprilFools"] = "AprilFools",
    ["Christmas"] = "Christmas",
    ["Halloween"] = "Halloween",
    ["None"] = "None",
}
Enums.HolidayType = Enums["Il2CppMapGeneration.Holidays.HolidayType"]
HolidayType = Enums["Il2CppMapGeneration.Holidays.HolidayType"]

---@enum Enum_Il2CppMapGeneration_002e_MapGenerationPhase
Enums["Il2CppMapGeneration.MapGenerationPhase"] = {
    ["ComplexDecorationsAndClutter"] = "ComplexDecorationsAndClutter",
    ["CullingCaching"] = "CullingCaching",
    ["ParentRoomRegistration"] = "ParentRoomRegistration",
    ["RelativePositioningWaypoints"] = "RelativePositioningWaypoints",
    ["RoomCoordsRegistrations"] = "RoomCoordsRegistrations",
    ["SimpleDecorations"] = "SimpleDecorations",
    ["SpawnableStructures"] = "SpawnableStructures",
    ["StaticBatching"] = "StaticBatching",
}
Enums.MapGenerationPhase = Enums["Il2CppMapGeneration.MapGenerationPhase"]
MapGenerationPhase = Enums["Il2CppMapGeneration.MapGenerationPhase"]

---@enum Enum_Il2CppMapGeneration_002e_RoomConnectors_002e_SpawnableRoomConnectorType
Enums["Il2CppMapGeneration.RoomConnectors.SpawnableRoomConnectorType"] = {
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
Enums.SpawnableRoomConnectorType = Enums["Il2CppMapGeneration.RoomConnectors.SpawnableRoomConnectorType"]
SpawnableRoomConnectorType = Enums["Il2CppMapGeneration.RoomConnectors.SpawnableRoomConnectorType"]

---@enum Enum_Il2CppMapGeneration_002e_RoomName
Enums["Il2CppMapGeneration.RoomName"] = {
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
Enums.RoomName = Enums["Il2CppMapGeneration.RoomName"]
RoomName = Enums["Il2CppMapGeneration.RoomName"]

---@enum Enum_Il2CppMapGeneration_002e_RoomShape
Enums["Il2CppMapGeneration.RoomShape"] = {
    ["Curve"] = "Curve",
    ["Endroom"] = "Endroom",
    ["Straight"] = "Straight",
    ["TShape"] = "TShape",
    ["Undefined"] = "Undefined",
    ["XShape"] = "XShape",
}
Enums.RoomShape = Enums["Il2CppMapGeneration.RoomShape"]
RoomShape = Enums["Il2CppMapGeneration.RoomShape"]

---@enum Enum_Il2CppMapGeneration_002e_Rooms_002e_RoomLevelName
Enums["Il2CppMapGeneration.Rooms.RoomLevelName"] = {
    ["Hcz049Sublevel"] = "Hcz049Sublevel",
    ["Main"] = "Main",
    ["NukeSilo"] = "NukeSilo",
}
Enums.RoomLevelName = Enums["Il2CppMapGeneration.Rooms.RoomLevelName"]
RoomLevelName = Enums["Il2CppMapGeneration.Rooms.RoomLevelName"]

---@enum Enum_Il2CppMetrics_002e_DeathsCollector_002b_ExportPreset
Enums["Il2CppMetrics.DeathsCollector+ExportPreset"] = {
    ["DeathReasons"] = "DeathReasons",
}
Enums.ExportPreset = Enums["Il2CppMetrics.DeathsCollector+ExportPreset"]

---@enum Enum_Il2CppMetrics_002e_ScpPreferencesCollector_002b_OptOutExportBehavior
Enums["Il2CppMetrics.ScpPreferencesCollector+OptOutExportBehavior"] = {
    ["Exclude"] = "Exclude",
    ["Include"] = "Include",
    ["TreatAsZero"] = "TreatAsZero",
}
Enums.OptOutExportBehavior = Enums["Il2CppMetrics.ScpPreferencesCollector+OptOutExportBehavior"]

---@enum Enum_Il2CppMirror_002e_LiteNetLib4Mirror_002e_LiteNetLib4MirrorCore_002b_States
Enums["Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore+States"] = {
    ["ClientConnected"] = "ClientConnected",
    ["ClientConnecting"] = "ClientConnecting",
    ["Discovery"] = "Discovery",
    ["Idle"] = "Idle",
    ["NonInitialized"] = "NonInitialized",
    ["Server"] = "Server",
}
Enums.States = Enums["Il2CppMirror.LiteNetLib4Mirror.LiteNetLib4MirrorCore+States"]

---@enum Enum_Il2CppPlayerRoles_002e_Faction
Enums["Il2CppPlayerRoles.Faction"] = {
    ["Flamingos"] = "Flamingos",
    ["FoundationEnemy"] = "FoundationEnemy",
    ["FoundationStaff"] = "FoundationStaff",
    ["SCP"] = "SCP",
    ["Unclassified"] = "Unclassified",
}
Enums.Faction = Enums["Il2CppPlayerRoles.Faction"]
Faction = Enums["Il2CppPlayerRoles.Faction"]

---@enum Enum_Il2CppPlayerRoles_002e_Filmmaker_002e_FilmmakerBlendPreset
Enums["Il2CppPlayerRoles.Filmmaker.FilmmakerBlendPreset"] = {
    ["FetchNext"] = "FetchNext",
    ["Hold"] = "Hold",
    ["Linear"] = "Linear",
    ["Smooth"] = "Smooth",
}
Enums.FilmmakerBlendPreset = Enums["Il2CppPlayerRoles.Filmmaker.FilmmakerBlendPreset"]
FilmmakerBlendPreset = Enums["Il2CppPlayerRoles.Filmmaker.FilmmakerBlendPreset"]

---@enum Enum_Il2CppPlayerRoles_002e_Filmmaker_002e_FilmmakerUI_002b_FilmmakerKey
Enums["Il2CppPlayerRoles.Filmmaker.FilmmakerUI+FilmmakerKey"] = {
    ["JumpBackward"] = "JumpBackward",
    ["JumpForward"] = "JumpForward",
    ["JumpToBeginning"] = "JumpToBeginning",
    ["JumpToEnd"] = "JumpToEnd",
    ["RemoveKeyframe"] = "RemoveKeyframe",
    ["UpdatePlayback"] = "UpdatePlayback",
}
Enums.FilmmakerKey = Enums["Il2CppPlayerRoles.Filmmaker.FilmmakerUI+FilmmakerKey"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_FpcMotor_002b_FpcViewMode
Enums["Il2CppPlayerRoles.FirstPersonControl.FpcMotor+FpcViewMode"] = {
    ["LocalPlayer"] = "LocalPlayer",
    ["Server"] = "Server",
    ["Thirdperson"] = "Thirdperson",
}
Enums.FpcViewMode = Enums["Il2CppPlayerRoles.FirstPersonControl.FpcMotor+FpcViewMode"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_PlayerMovementState
Enums["Il2CppPlayerRoles.FirstPersonControl.PlayerMovementState"] = {
    ["Crouching"] = "Crouching",
    ["Sneaking"] = "Sneaking",
    ["Sprinting"] = "Sprinting",
    ["Walking"] = "Walking",
}
Enums.PlayerMovementState = Enums["Il2CppPlayerRoles.FirstPersonControl.PlayerMovementState"]
PlayerMovementState = Enums["Il2CppPlayerRoles.FirstPersonControl.PlayerMovementState"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_AnimatedCharacterModel_002b_FootstepLoudness
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel+FootstepLoudness"] = {
    ["Chaos"] = "Chaos",
    ["Civilian"] = "Civilian",
    ["FoundationForces"] = "FoundationForces",
    ["Scp"] = "Scp",
}
Enums.FootstepLoudness = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.AnimatedCharacterModel+FootstepLoudness"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_RefpointType
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.RefpointType"] = {
    ["Hitbox"] = "Hitbox",
    ["LineOfSightPoint"] = "LineOfSightPoint",
    ["None"] = "None",
}
Enums.RefpointType = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.RefpointType"]
RefpointType = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.RefpointType"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_CullingSubcontroller_002b_AnimationQuality
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller+AnimationQuality"] = {
    ["High"] = "High",
    ["Low"] = "Low",
    ["Medium"] = "Medium",
    ["Ultra"] = "Ultra",
    ["VeryLow"] = "VeryLow",
}
Enums.AnimationQuality = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.CullingSubcontroller+AnimationQuality"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_EmotionBlendshape
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"] = {
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
Enums.EmotionBlendshape = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"]
EmotionBlendshape = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionBlendshape"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_EmotionPresetType
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"] = {
    ["Angry"] = "Angry",
    ["AwkwardSmile"] = "AwkwardSmile",
    ["Chad"] = "Chad",
    ["Happy"] = "Happy",
    ["Neutral"] = "Neutral",
    ["Ogre"] = "Ogre",
    ["Scared"] = "Scared",
}
Enums.EmotionPresetType = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"]
EmotionPresetType = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.EmotionPresetType"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_GlowInTheDarkSubcontroller_002b_ForceCondition
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller+ForceCondition"] = {
    ["ForceWhenEnemy"] = "ForceWhenEnemy",
    ["ForceWhenFriendly"] = "ForceWhenFriendly",
    ["NeverForce"] = "NeverForce",
}
Enums.ForceCondition = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller+ForceCondition"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_GlowInTheDarkSubcontroller_002b_ForceResult
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller+ForceResult"] = {
    ["ForceDarkened"] = "ForceDarkened",
    ["ForceNormal"] = "ForceNormal",
}
Enums.ForceResult = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.GlowInTheDarkSubcontroller+ForceResult"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_IdlePoseRetainerSubcontroller_002b_TurnStatus
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller+TurnStatus"] = {
    ["Idle"] = "Idle",
    ["TurningLeft"] = "TurningLeft",
    ["TurningRight"] = "TurningRight",
}
Enums.TurnStatus = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.IdlePoseRetainerSubcontroller+TurnStatus"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_InventorySubcontroller_002b_TransitionStatus
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller+TransitionStatus"] = {
    ["Done"] = "Done",
    ["EquippingNew"] = "EquippingNew",
    ["RetractingPrevious"] = "RetractingPrevious",
}
Enums.TransitionStatus = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.InventorySubcontroller+TransitionStatus"]

---@enum Enum_Il2CppPlayerRoles_002e_FirstPersonControl_002e_Thirdperson_002e_Subcontrollers_002e_Wearables_002e_WearableElements
Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"] = {
    ["Armor"] = "Armor",
    ["None"] = "None",
    ["Scp1344Goggles"] = "Scp1344Goggles",
    ["Scp268Hat"] = "Scp268Hat",
}
Enums.WearableElements = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"]
WearableElements = Enums["Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers.Wearables.WearableElements"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp049_002e_Scp049ResurrectAbility_002b_ResurrectError
Enums["Il2CppPlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility+ResurrectError"] = {
    ["Expired"] = "Expired",
    ["MaxReached"] = "MaxReached",
    ["None"] = "None",
    ["Refused"] = "Refused",
    ["TargetInvalid"] = "TargetInvalid",
    ["TargetNull"] = "TargetNull",
}
Enums.ResurrectError = Enums["Il2CppPlayerRoles.PlayableScps.Scp049.Scp049ResurrectAbility+ResurrectError"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp049_002e_Zombies_002e_ZombieAudioPlayer_002b_Scp0492SoundId
Enums["Il2CppPlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer+Scp0492SoundId"] = {
    ["AngryGrowl"] = "AngryGrowl",
    ["Attack"] = "Attack",
    ["Growl"] = "Growl",
}
Enums.Scp0492SoundId = Enums["Il2CppPlayerRoles.PlayableScps.Scp049.Zombies.ZombieAudioPlayer+Scp0492SoundId"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp049_002e_Zombies_002e_ZombieConsumeAbility_002b_ConsumeError
Enums["Il2CppPlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility+ConsumeError"] = {
    ["AlreadyConsumed"] = "AlreadyConsumed",
    ["BeingConsumed"] = "BeingConsumed",
    ["CannotCancel"] = "CannotCancel",
    ["FullHealth"] = "FullHealth",
    ["None"] = "None",
    ["TargetNotValid"] = "TargetNotValid",
}
Enums.ConsumeError = Enums["Il2CppPlayerRoles.PlayableScps.Scp049.Zombies.ZombieConsumeAbility+ConsumeError"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Cameras_002e_Scp079CurrentCameraSync_002b_ClientSwitchState
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync+ClientSwitchState"] = {
    ["None"] = "None",
    ["SwitchingRoom"] = "SwitchingRoom",
    ["SwitchingZone"] = "SwitchingZone",
}
Enums.ClientSwitchState = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Cameras.Scp079CurrentCameraSync+ClientSwitchState"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_GUI_002e_NotificationSound
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"] = {
    ["None"] = "None",
    ["Standard"] = "Standard",
}
Enums.NotificationSound = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"]
NotificationSound = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.GUI.NotificationSound"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Map_002e_Scp079TeammateIndicators_002b_IndicatorType
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators+IndicatorType"] = {
    ["High"] = "High",
    ["Low"] = "Low",
    ["Medium"] = "Medium",
}
Enums.IndicatorType = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Map.Scp079TeammateIndicators+IndicatorType"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079BlackoutRoomAbility_002b_ValidationError
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079BlackoutRoomAbility+ValidationError"] = {
    ["AlreadyBlackedOut"] = "AlreadyBlackedOut",
    ["MaxCapacityReached"] = "MaxCapacityReached",
    ["NoController"] = "NoController",
    ["None"] = "None",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["RoomOnCooldown"] = "RoomOnCooldown",
}

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079BlackoutZoneAbility_002b_ValidationError
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079BlackoutZoneAbility+ValidationError"] = {
    ["Cooldown"] = "Cooldown",
    ["None"] = "None",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["Unavailable"] = "Unavailable",
}

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079HudTranslation
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"] = {
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
Enums.Scp079HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"]
Scp079HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079HudTranslation"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079KeyAbilityBase_002b_Category
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase+Category"] = {
    ["Movement"] = "Movement",
    ["OverconInteraction"] = "OverconInteraction",
    ["SpecialAbility"] = "SpecialAbility",
}
Enums.Category = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079KeyAbilityBase+Category"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079LockdownRoomAbility_002b_ValidationError
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079LockdownRoomAbility+ValidationError"] = {
    ["Cooldown"] = "Cooldown",
    ["NoDoors"] = "NoDoors",
    ["None"] = "None",
    ["NotEnoughAux"] = "NotEnoughAux",
    ["TierTooLow"] = "TierTooLow",
    ["Unknown"] = "Unknown",
}

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079ScannerSequence_002b_ScanSequenceStep
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence+ScanSequenceStep"] = {
    ["CountingDown"] = "CountingDown",
    ["Init"] = "Init",
    ["ScanningFailedCooldown"] = "ScanningFailedCooldown",
    ["ScanningFindNewTarget"] = "ScanningFindNewTarget",
    ["ScanningUpdateTarget"] = "ScanningUpdateTarget",
}
Enums.ScanSequenceStep = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence+ScanSequenceStep"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp079_002e_Scp079ScannerSequence_002b_TrackerMessage
Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence+TrackerMessage"] = {
    ["None"] = "None",
    ["ScanNoResults"] = "ScanNoResults",
    ["ScanSuccessful"] = "ScanSuccessful",
    ["ScanTimeSync"] = "ScanTimeSync",
    ["ScannerDisabled"] = "ScannerDisabled",
}
Enums.TrackerMessage = Enums["Il2CppPlayerRoles.PlayableScps.Scp079.Scp079ScannerSequence+TrackerMessage"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096AbilityState
Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096AbilityState"] = {
    ["Attacking"] = "Attacking",
    ["Charging"] = "Charging",
    ["None"] = "None",
    ["PryingGate"] = "PryingGate",
    ["TryingNotToCry"] = "TryingNotToCry",
}
Enums.Scp096AbilityState = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096AbilityState"]
Scp096AbilityState = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096AbilityState"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096HitResult
Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096HitResult"] = {
    ["Door"] = "Door",
    ["Human"] = "Human",
    ["Lethal"] = "Lethal",
    ["None"] = "None",
    ["Window"] = "Window",
}
Enums.Scp096HitResult = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096HitResult"]
Scp096HitResult = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096HitResult"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096HudTranslation
Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"] = {
    ["EnterRageKeyInfo"] = "EnterRageKeyInfo",
    ["ExitRageKeyInfo"] = "ExitRageKeyInfo",
    ["HelpMenuCharge"] = "HelpMenuCharge",
    ["HelpMenuDescription"] = "HelpMenuDescription",
    ["HelpMenuDocileOnly"] = "HelpMenuDocileOnly",
    ["HelpMenuMeeleAttack"] = "HelpMenuMeeleAttack",
    ["HelpMenuRageOnly"] = "HelpMenuRageOnly",
    ["HelpMenuTryNotToCry"] = "HelpMenuTryNotToCry",
}
Enums.Scp096HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"]
Scp096HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096HudTranslation"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp096_002e_Scp096RageState
Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096RageState"] = {
    ["Calming"] = "Calming",
    ["Distressed"] = "Distressed",
    ["Docile"] = "Docile",
    ["Enraged"] = "Enraged",
}
Enums.Scp096RageState = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096RageState"]
Scp096RageState = Enums["Il2CppPlayerRoles.PlayableScps.Scp096.Scp096RageState"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp106_002e_Scp106StalkAbility_002b_RpcType
Enums["Il2CppPlayerRoles.PlayableScps.Scp106.Scp106StalkAbility+RpcType"] = {
    ["NotEnoughVigor"] = "NotEnoughVigor",
    ["StalkActive"] = "StalkActive",
    ["StalkInactive"] = "StalkInactive",
}

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp1507_002e_Scp1507Spawner_002b_State
Enums["Il2CppPlayerRoles.PlayableScps.Scp1507.Scp1507Spawner+State"] = {
    ["Idle"] = "Idle",
    ["Spawned"] = "Spawned",
    ["Spawning"] = "Spawning",
    ["WaitForRespawnCycle"] = "WaitForRespawnCycle",
    ["WaitForSpectators"] = "WaitForSpectators",
}
Enums.State = Enums["Il2CppPlayerRoles.PlayableScps.Scp1507.Scp1507Spawner+State"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp173_002e_Scp173AudioPlayer_002b_Scp173SoundId
Enums["Il2CppPlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer+Scp173SoundId"] = {
    ["Hit"] = "Hit",
    ["Snap"] = "Snap",
    ["Teleport"] = "Teleport",
}
Enums.Scp173SoundId = Enums["Il2CppPlayerRoles.PlayableScps.Scp173.Scp173AudioPlayer+Scp173SoundId"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp173_002e_Scp173TeleportAbility_002b_CmdTeleportData
Enums["Il2CppPlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility+CmdTeleportData"] = {
    ["Aiming"] = "Aiming",
    ["WantsToTeleport"] = "WantsToTeleport",
}
Enums.CmdTeleportData = Enums["Il2CppPlayerRoles.PlayableScps.Scp173.Scp173TeleportAbility+CmdTeleportData"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114DamageHandler_002b_HandlerType
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler+HandlerType"] = {
    ["SkinSteal"] = "SkinSteal",
    ["Slap"] = "Slap",
    ["Strangulation"] = "Strangulation",
}
Enums.HandlerType = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114DamageHandler+HandlerType"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114HudTranslation
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"] = {
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
Enums.Scp3114HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"]
Scp3114HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114HudTranslation"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114Identity_002b_DisguiseStatus
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114Identity+DisguiseStatus"] = {
    ["Active"] = "Active",
    ["Equipping"] = "Equipping",
    ["None"] = "None",
}
Enums.DisguiseStatus = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114Identity+DisguiseStatus"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114MaterialType
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"] = {
    ["Disguise"] = "Disguise",
    ["Original"] = "Original",
    ["Reveal"] = "Reveal",
}
Enums.Scp3114MaterialType = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"]
Scp3114MaterialType = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114MaterialType"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114Strangle_002b_RpcType
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114Strangle+RpcType"] = {
    ["AttackCancelled"] = "AttackCancelled",
    ["AttackInterrupted"] = "AttackInterrupted",
    ["OutOfRange"] = "OutOfRange",
    ["StrangleTimer"] = "StrangleTimer",
    ["TargetKilled"] = "TargetKilled",
    ["TargetResync"] = "TargetResync",
}

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114StrangleAudio_002b_RpcType
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114StrangleAudio+RpcType"] = {
    ["ChokeSync"] = "ChokeSync",
    ["Kill"] = "Kill",
}

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp3114_002e_Scp3114VoiceLines_002b_VoiceLinesName
Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines+VoiceLinesName"] = {
    ["EquipStart"] = "EquipStart",
    ["KillSlap"] = "KillSlap",
    ["KillStrangle"] = "KillStrangle",
    ["RandomIdle"] = "RandomIdle",
    ["Reveal"] = "Reveal",
    ["Slap"] = "Slap",
    ["StartStrangle"] = "StartStrangle",
}
Enums.VoiceLinesName = Enums["Il2CppPlayerRoles.PlayableScps.Scp3114.Scp3114VoiceLines+VoiceLinesName"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp939_002e_Mimicry_002e_MimicPointController_002b_RpcStateMsg
Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController+RpcStateMsg"] = {
    ["DestroyedByDistance"] = "DestroyedByDistance",
    ["None"] = "None",
    ["PlacedByUser"] = "PlacedByUser",
    ["RemovedByUser"] = "RemovedByUser",
}
Enums.RpcStateMsg = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Mimicry.MimicPointController+RpcStateMsg"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939AmnesticCloudInstance_002b_CloudState
Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance+CloudState"] = {
    ["Created"] = "Created",
    ["Destroyed"] = "Destroyed",
    ["Spawning"] = "Spawning",
}
Enums.CloudState = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance+CloudState"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939ChaseThemeProvider_002b_TrackType
Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider+TrackType"] = {
    ["Detected"] = "Detected",
    ["MadeSound"] = "MadeSound",
    ["None"] = "None",
    ["Quiet"] = "Quiet",
}
Enums.TrackType = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939ChaseThemeProvider+TrackType"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939DamageType
Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939DamageType"] = {
    ["Claw"] = "Claw",
    ["LungeSecondary"] = "LungeSecondary",
    ["LungeTarget"] = "LungeTarget",
    ["None"] = "None",
}
Enums.Scp939DamageType = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939DamageType"]
Scp939DamageType = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939DamageType"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939HudTranslation
Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"] = {
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
Enums.Scp939HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"]
Scp939HudTranslation = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939HudTranslation"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Scp939_002e_Scp939LungeState
Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939LungeState"] = {
    ["LandHarsh"] = "LandHarsh",
    ["LandHit"] = "LandHit",
    ["LandRegular"] = "LandRegular",
    ["None"] = "None",
    ["Triggered"] = "Triggered",
}
Enums.Scp939LungeState = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939LungeState"]
Scp939LungeState = Enums["Il2CppPlayerRoles.PlayableScps.Scp939.Scp939LungeState"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_Subroutines_002e_AttackResult
Enums["Il2CppPlayerRoles.PlayableScps.Subroutines.AttackResult"] = {
    ["AttackedObject"] = "AttackedObject",
    ["AttackedPlayer"] = "AttackedPlayer",
    ["KilledPlayer"] = "KilledPlayer",
    ["None"] = "None",
}
Enums.AttackResult = Enums["Il2CppPlayerRoles.PlayableScps.Subroutines.AttackResult"]
AttackResult = Enums["Il2CppPlayerRoles.PlayableScps.Subroutines.AttackResult"]

---@enum Enum_Il2CppPlayerRoles_002e_PlayableScps_002e_VisionInformation_002b_FailReason
Enums["Il2CppPlayerRoles.PlayableScps.VisionInformation+FailReason"] = {
    ["InDarkRoom"] = "InDarkRoom",
    ["IsLooking"] = "IsLooking",
    ["NotInDistance"] = "NotInDistance",
    ["NotInLineOfSight"] = "NotInLineOfSight",
    ["NotInView"] = "NotInView",
    ["NotOnSameFloor"] = "NotOnSameFloor",
    ["UnkownReason"] = "UnkownReason",
}
Enums.FailReason = Enums["Il2CppPlayerRoles.PlayableScps.VisionInformation+FailReason"]

---@enum Enum_Il2CppPlayerRoles_002e_RoleChangeReason
Enums["Il2CppPlayerRoles.RoleChangeReason"] = {
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
Enums.RoleChangeReason = Enums["Il2CppPlayerRoles.RoleChangeReason"]
RoleChangeReason = Enums["Il2CppPlayerRoles.RoleChangeReason"]

---@enum Enum_Il2CppPlayerRoles_002e_RoleSpawnFlags
Enums["Il2CppPlayerRoles.RoleSpawnFlags"] = {
    ["All"] = "All",
    ["AssignInventory"] = "AssignInventory",
    ["None"] = "None",
    ["UseSpawnpoint"] = "UseSpawnpoint",
}
Enums.RoleSpawnFlags = Enums["Il2CppPlayerRoles.RoleSpawnFlags"]
RoleSpawnFlags = Enums["Il2CppPlayerRoles.RoleSpawnFlags"]

---@enum Enum_Il2CppPlayerRoles_002e_RoleTypeId
Enums["Il2CppPlayerRoles.RoleTypeId"] = {
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
Enums.RoleTypeId = Enums["Il2CppPlayerRoles.RoleTypeId"]
RoleTypeId = Enums["Il2CppPlayerRoles.RoleTypeId"]

---@enum Enum_Il2CppPlayerRoles_002e_Spectating_002e_OverwatchSettings
Enums["Il2CppPlayerRoles.Spectating.OverwatchSettings"] = {
    ["DebugLines"] = "DebugLines",
    ["HitRegisterToggle"] = "HitRegisterToggle",
    ["PlayerInfoToggle"] = "PlayerInfoToggle",
}
Enums.OverwatchSettings = Enums["Il2CppPlayerRoles.Spectating.OverwatchSettings"]
OverwatchSettings = Enums["Il2CppPlayerRoles.Spectating.OverwatchSettings"]

---@enum Enum_Il2CppPlayerRoles_002e_Spectating_002e_SpectatableListElementType
Enums["Il2CppPlayerRoles.Spectating.SpectatableListElementType"] = {
    ["Scp079"] = "Scp079",
    ["Scp3114"] = "Scp3114",
    ["Standard"] = "Standard",
}
Enums.SpectatableListElementType = Enums["Il2CppPlayerRoles.Spectating.SpectatableListElementType"]
SpectatableListElementType = Enums["Il2CppPlayerRoles.Spectating.SpectatableListElementType"]

---@enum Enum_Il2CppPlayerRoles_002e_Spectating_002e_SpectatorSpawnReason
Enums["Il2CppPlayerRoles.Spectating.SpectatorSpawnReason"] = {
    ["KilledByPlayer"] = "KilledByPlayer",
    ["None"] = "None",
    ["Other"] = "Other",
}
Enums.SpectatorSpawnReason = Enums["Il2CppPlayerRoles.Spectating.SpectatorSpawnReason"]
SpectatorSpawnReason = Enums["Il2CppPlayerRoles.Spectating.SpectatorSpawnReason"]

---@enum Enum_Il2CppPlayerRoles_002e_Team
Enums["Il2CppPlayerRoles.Team"] = {
    ["ChaosInsurgency"] = "ChaosInsurgency",
    ["ClassD"] = "ClassD",
    ["Dead"] = "Dead",
    ["Flamingos"] = "Flamingos",
    ["FoundationForces"] = "FoundationForces",
    ["OtherAlive"] = "OtherAlive",
    ["SCPs"] = "SCPs",
    ["Scientists"] = "Scientists",
}
Enums.Team = Enums["Il2CppPlayerRoles.Team"]
Team = Enums["Il2CppPlayerRoles.Team"]

---@enum Enum_Il2CppPlayerRoles_002e_Visibility_002e_InvisibilityFlags
Enums["Il2CppPlayerRoles.Visibility.InvisibilityFlags"] = {
    ["None"] = "None",
    ["OutOfRange"] = "OutOfRange",
    ["Scp106Sinkhole"] = "Scp106Sinkhole",
    ["Scp268"] = "Scp268",
}
Enums.InvisibilityFlags = Enums["Il2CppPlayerRoles.Visibility.InvisibilityFlags"]
InvisibilityFlags = Enums["Il2CppPlayerRoles.Visibility.InvisibilityFlags"]

---@enum Enum_Il2CppPlayerRoles_002e_Voice_002e_GroupMuteFlags
Enums["Il2CppPlayerRoles.Voice.GroupMuteFlags"] = {
    ["Alive"] = "Alive",
    ["Lobby"] = "Lobby",
    ["None"] = "None",
    ["Spectators"] = "Spectators",
    ["Summary"] = "Summary",
}
Enums.GroupMuteFlags = Enums["Il2CppPlayerRoles.Voice.GroupMuteFlags"]
GroupMuteFlags = Enums["Il2CppPlayerRoles.Voice.GroupMuteFlags"]

---@enum Enum_Il2CppPlayerRoles_002e_Voice_002e_IntercomDisplay_002b_IcomText
Enums["Il2CppPlayerRoles.Voice.IntercomDisplay+IcomText"] = {
    ["AdminUsing"] = "AdminUsing",
    ["Muted"] = "Muted",
    ["Ready"] = "Ready",
    ["Restarting"] = "Restarting",
    ["Transmitting"] = "Transmitting",
    ["TrasmittingBypass"] = "TrasmittingBypass",
    ["Unknown"] = "Unknown",
    ["Wait"] = "Wait",
}
Enums.IcomText = Enums["Il2CppPlayerRoles.Voice.IntercomDisplay+IcomText"]

---@enum Enum_Il2CppPlayerRoles_002e_Voice_002e_IntercomState
Enums["Il2CppPlayerRoles.Voice.IntercomState"] = {
    ["Cooldown"] = "Cooldown",
    ["InUse"] = "InUse",
    ["NotFound"] = "NotFound",
    ["Ready"] = "Ready",
    ["Starting"] = "Starting",
}
Enums.IntercomState = Enums["Il2CppPlayerRoles.Voice.IntercomState"]
IntercomState = Enums["Il2CppPlayerRoles.Voice.IntercomState"]

---@enum Enum_Il2CppPlayerStatsSystem_002e_AdminFlags
Enums["Il2CppPlayerStatsSystem.AdminFlags"] = {
    ["BypassMode"] = "BypassMode",
    ["GodMode"] = "GodMode",
    ["Noclip"] = "Noclip",
    ["None"] = "None",
}
Enums.AdminFlags = Enums["Il2CppPlayerStatsSystem.AdminFlags"]
AdminFlags = Enums["Il2CppPlayerStatsSystem.AdminFlags"]

---@enum Enum_Il2CppPlayerStatsSystem_002e_DamageHandlerBase_002b_HandlerOutput
Enums["Il2CppPlayerStatsSystem.DamageHandlerBase+HandlerOutput"] = {
    ["Damaged"] = "Damaged",
    ["Death"] = "Death",
    ["Nothing"] = "Nothing",
}
Enums.HandlerOutput = Enums["Il2CppPlayerStatsSystem.DamageHandlerBase+HandlerOutput"]

---@enum Enum_Il2CppPlayerStatsSystem_002e_Scp049DamageHandler_002b_AttackType
Enums["Il2CppPlayerStatsSystem.Scp049DamageHandler+AttackType"] = {
    ["CardiacArrest"] = "CardiacArrest",
    ["Instakill"] = "Instakill",
    ["Scp0492"] = "Scp0492",
}

---@enum Enum_Il2CppPlayerStatsSystem_002e_Scp096DamageHandler_002b_AttackType
Enums["Il2CppPlayerStatsSystem.Scp096DamageHandler+AttackType"] = {
    ["Charge"] = "Charge",
    ["GateKill"] = "GateKill",
    ["SlapLeft"] = "SlapLeft",
    ["SlapRight"] = "SlapRight",
}

---@enum Enum_Il2CppPlayerStatsSystem_002e_StatSlider_002b_DisplayExactMode
Enums["Il2CppPlayerStatsSystem.StatSlider+DisplayExactMode"] = {
    ["AlwaysExact"] = "AlwaysExact",
    ["AlwaysPercent"] = "AlwaysPercent",
    ["PreferenceBased"] = "PreferenceBased",
    ["ValueHidden"] = "ValueHidden",
}
Enums.DisplayExactMode = Enums["Il2CppPlayerStatsSystem.StatSlider+DisplayExactMode"]

---@enum Enum_Il2CppPlayerStatsSystem_002e_SyncedStatBase_002b_SyncMode
Enums["Il2CppPlayerStatsSystem.SyncedStatBase+SyncMode"] = {
    ["Private"] = "Private",
    ["PrivateAndSpectators"] = "PrivateAndSpectators",
    ["Public"] = "Public",
}
Enums.SyncMode = Enums["Il2CppPlayerStatsSystem.SyncedStatBase+SyncMode"]

---@enum Enum_Il2CppPlayerStatsSystem_002e_SyncedStatMessages_002b_StatMessageType
Enums["Il2CppPlayerStatsSystem.SyncedStatMessages+StatMessageType"] = {
    ["CurrentValue"] = "CurrentValue",
    ["MaxValue"] = "MaxValue",
}
Enums.StatMessageType = Enums["Il2CppPlayerStatsSystem.SyncedStatMessages+StatMessageType"]

---@enum Enum_Il2CppProgressiveCulling_002e_RootCullablePriority
Enums["Il2CppProgressiveCulling.RootCullablePriority"] = {
    ["Dynamic"] = "Dynamic",
    ["PreCull"] = "PreCull",
    ["Rooms"] = "Rooms",
}
Enums.RootCullablePriority = Enums["Il2CppProgressiveCulling.RootCullablePriority"]
RootCullablePriority = Enums["Il2CppProgressiveCulling.RootCullablePriority"]

---@enum Enum_Il2CppQuery_002e_QueryHandshake_002b_ClientFlags
Enums["Il2CppQuery.QueryHandshake+ClientFlags"] = {
    ["None"] = "None",
    ["RemoteAdminMetadata"] = "RemoteAdminMetadata",
    ["RestrictPermissions"] = "RestrictPermissions",
    ["SpecifyLogUsername"] = "SpecifyLogUsername",
    ["SubscribeServerConsole"] = "SubscribeServerConsole",
    ["SubscribeServerLogs"] = "SubscribeServerLogs",
    ["SuppressRaResponses"] = "SuppressRaResponses",
}
Enums.ClientFlags = Enums["Il2CppQuery.QueryHandshake+ClientFlags"]

---@enum Enum_Il2CppQuery_002e_QueryMessage_002b_ClientReceivedContentType
Enums["Il2CppQuery.QueryMessage+ClientReceivedContentType"] = {
    ["CommandException"] = "CommandException",
    ["ConsoleString"] = "ConsoleString",
    ["QueryMessage"] = "QueryMessage",
    ["RemoteAdminPlaintextResponse"] = "RemoteAdminPlaintextResponse",
    ["RemoteAdminSerializedResponse"] = "RemoteAdminSerializedResponse",
    ["RemoteAdminUnsuccessfulPlaintextResponse"] = "RemoteAdminUnsuccessfulPlaintextResponse",
}
Enums.ClientReceivedContentType = Enums["Il2CppQuery.QueryMessage+ClientReceivedContentType"]

---@enum Enum_Il2CppQuery_002e_QueryMessage_002b_ServerReceivedContentType
Enums["Il2CppQuery.QueryMessage+ServerReceivedContentType"] = {
    ["Command"] = "Command",
}
Enums.ServerReceivedContentType = Enums["Il2CppQuery.QueryMessage+ServerReceivedContentType"]

---@enum Enum_Il2CppRemoteAdmin_002e_ColorSlider_002b_ModifiedColorValue
Enums["Il2CppRemoteAdmin.ColorSlider+ModifiedColorValue"] = {
    ["A"] = "A",
    ["B"] = "B",
    ["G"] = "G",
    ["R"] = "R",
}
Enums.ModifiedColorValue = Enums["Il2CppRemoteAdmin.ColorSlider+ModifiedColorValue"]

---@enum Enum_Il2CppRemoteAdmin_002e_Communication_002e_RaClipboard_002b_RaClipBoardType
Enums["Il2CppRemoteAdmin.Communication.RaClipboard+RaClipBoardType"] = {
    ["Ip"] = "Ip",
    ["PlayerId"] = "PlayerId",
    ["UserId"] = "UserId",
}
Enums.RaClipBoardType = Enums["Il2CppRemoteAdmin.Communication.RaClipboard+RaClipBoardType"]

---@enum Enum_Il2CppRemoteAdmin_002e_Communication_002e_RaPlayerList_002b_PlayerSorting
Enums["Il2CppRemoteAdmin.Communication.RaPlayerList+PlayerSorting"] = {
    ["Alphabetical"] = "Alphabetical",
    ["Class"] = "Class",
    ["Ids"] = "Ids",
    ["Team"] = "Team",
}
Enums.PlayerSorting = Enums["Il2CppRemoteAdmin.Communication.RaPlayerList+PlayerSorting"]

---@enum Enum_Il2CppRemoteAdmin_002e_Menus_002e_DataRequestMenu_002b_RequestType
Enums["Il2CppRemoteAdmin.Menus.DataRequestMenu+RequestType"] = {
    ["Auth"] = "Auth",
    ["ExternalLookup"] = "ExternalLookup",
    ["Info"] = "Info",
    ["ShortInfo"] = "ShortInfo",
}

---@enum Enum_Il2CppRemoteAdmin_002e_Menus_002e_RoundMenu_002b_Panel
Enums["Il2CppRemoteAdmin.Menus.RoundMenu+Panel"] = {
    ["ServerEvents"] = "ServerEvents",
    ["TicketSystem"] = "TicketSystem",
}
Enums.Panel = Enums["Il2CppRemoteAdmin.Menus.RoundMenu+Panel"]

---@enum Enum_Il2CppRespawning_002e_Graphics_002e_RespawnSetting
Enums["Il2CppRespawning.Graphics.RespawnSetting"] = {
    ["ObjectiveFeedVisible"] = "ObjectiveFeedVisible",
    ["RespawnInterfaceVisible"] = "RespawnInterfaceVisible",
}
Enums.RespawnSetting = Enums["Il2CppRespawning.Graphics.RespawnSetting"]
RespawnSetting = Enums["Il2CppRespawning.Graphics.RespawnSetting"]

---@enum Enum_Il2CppRespawning_002e_Graphics_002e_RespawnTooltipTranslation
Enums["Il2CppRespawning.Graphics.RespawnTooltipTranslation"] = {
    ["InfluenceTranslation"] = "InfluenceTranslation",
    ["RespawnsTokensTranslation"] = "RespawnsTokensTranslation",
    ["TimerBarTranslation"] = "TimerBarTranslation",
}
Enums.RespawnTooltipTranslation = Enums["Il2CppRespawning.Graphics.RespawnTooltipTranslation"]
RespawnTooltipTranslation = Enums["Il2CppRespawning.Graphics.RespawnTooltipTranslation"]

---@enum Enum_Il2CppRespawning_002e_Objectives_002e_FootprintsTranslation
Enums["Il2CppRespawning.Objectives.FootprintsTranslation"] = {
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
Enums.FootprintsTranslation = Enums["Il2CppRespawning.Objectives.FootprintsTranslation"]
FootprintsTranslation = Enums["Il2CppRespawning.Objectives.FootprintsTranslation"]

---@enum Enum_Il2CppRespawning_002e_UpdateMessageFlags
Enums["Il2CppRespawning.UpdateMessageFlags"] = {
    ["All"] = "All",
    ["Max"] = "Max",
    ["None"] = "None",
    ["Pause"] = "Pause",
    ["Spawn"] = "Spawn",
    ["Timer"] = "Timer",
    ["Tokens"] = "Tokens",
    ["Trigger"] = "Trigger",
}
Enums.UpdateMessageFlags = Enums["Il2CppRespawning.UpdateMessageFlags"]
UpdateMessageFlags = Enums["Il2CppRespawning.UpdateMessageFlags"]

---@enum Enum_Il2CppRespawning_002e_WaveQueueState
Enums["Il2CppRespawning.WaveQueueState"] = {
    ["Idle"] = "Idle",
    ["WaveSelected"] = "WaveSelected",
    ["WaveSpawned"] = "WaveSpawned",
    ["WaveSpawning"] = "WaveSpawning",
}
Enums.WaveQueueState = Enums["Il2CppRespawning.WaveQueueState"]
WaveQueueState = Enums["Il2CppRespawning.WaveQueueState"]

---@enum Enum_Il2CppRoundRestarting_002e_RoundRestartType
Enums["Il2CppRoundRestarting.RoundRestartType"] = {
    ["FastRestart"] = "FastRestart",
    ["FullRestart"] = "FullRestart",
    ["RedirectRestart"] = "RedirectRestart",
}
Enums.RoundRestartType = Enums["Il2CppRoundRestarting.RoundRestartType"]
RoundRestartType = Enums["Il2CppRoundRestarting.RoundRestartType"]

---@enum Enum_Il2CppScp914_002e_Scp914InteractCode
Enums["Il2CppScp914.Scp914InteractCode"] = {
    ["Activate"] = "Activate",
    ["ChangeMode"] = "ChangeMode",
}
Enums.Scp914InteractCode = Enums["Il2CppScp914.Scp914InteractCode"]
Scp914InteractCode = Enums["Il2CppScp914.Scp914InteractCode"]

---@enum Enum_Il2CppScp914_002e_Scp914KnobSetting
Enums["Il2CppScp914.Scp914KnobSetting"] = {
    ["Coarse"] = "Coarse",
    ["Fine"] = "Fine",
    ["OneToOne"] = "OneToOne",
    ["Rough"] = "Rough",
    ["VeryFine"] = "VeryFine",
}
Enums.Scp914KnobSetting = Enums["Il2CppScp914.Scp914KnobSetting"]
Scp914KnobSetting = Enums["Il2CppScp914.Scp914KnobSetting"]

---@enum Enum_Il2CppScp914_002e_Scp914Mode
Enums["Il2CppScp914.Scp914Mode"] = {
    ["Dropped"] = "Dropped",
    ["DroppedAndHeld"] = "DroppedAndHeld",
    ["DroppedAndInventory"] = "DroppedAndInventory",
    ["DroppedAndPlayerTeleport"] = "DroppedAndPlayerTeleport",
    ["Held"] = "Held",
    ["Inventory"] = "Inventory",
}
Enums.Scp914Mode = Enums["Il2CppScp914.Scp914Mode"]
Scp914Mode = Enums["Il2CppScp914.Scp914Mode"]

---@enum Enum_Il2CppScp914_002e_Scp914Sound
Enums["Il2CppScp914.Scp914Sound"] = {
    ["KnobChange"] = "KnobChange",
    ["Upgrading"] = "Upgrading",
}
Enums.Scp914Sound = Enums["Il2CppScp914.Scp914Sound"]
Scp914Sound = Enums["Il2CppScp914.Scp914Sound"]

---@enum Enum_Il2CppSecurity_002e_ServerRateLimit
Enums["Il2CppSecurity.ServerRateLimit"] = {
    ["commands"] = "commands",
    ["playerInteract"] = "playerInteract",
}
Enums.ServerRateLimit = Enums["Il2CppSecurity.ServerRateLimit"]
ServerRateLimit = Enums["Il2CppSecurity.ServerRateLimit"]

---@enum Enum_Il2CppServerOutput_002e_OutputCodes
Enums["Il2CppServerOutput.OutputCodes"] = {
    ["ExitActionReset"] = "ExitActionReset",
    ["ExitActionRestart"] = "ExitActionRestart",
    ["ExitActionShutdown"] = "ExitActionShutdown",
    ["ExitActionSilentShutdown"] = "ExitActionSilentShutdown",
    ["Heartbeat"] = "Heartbeat",
    ["IdleEnter"] = "IdleEnter",
    ["IdleExit"] = "IdleExit",
    ["RoundRestart"] = "RoundRestart",
}
Enums.OutputCodes = Enums["Il2CppServerOutput.OutputCodes"]
OutputCodes = Enums["Il2CppServerOutput.OutputCodes"]

---@enum Enum_Il2CppSteam_002e_ServerOperativeSystem
Enums["Il2CppSteam.ServerOperativeSystem"] = {
    ["Linux"] = "Linux",
    ["Windows"] = "Windows",
}
Enums.ServerOperativeSystem = Enums["Il2CppSteam.ServerOperativeSystem"]
ServerOperativeSystem = Enums["Il2CppSteam.ServerOperativeSystem"]

---@enum Enum_Il2CppSteam_002e_ServerType
Enums["Il2CppSteam.ServerType"] = {
    ["Dedicated"] = "Dedicated",
    ["Listen"] = "Listen",
    ["SourceTV"] = "SourceTV",
}
Enums.ServerType = Enums["Il2CppSteam.ServerType"]
ServerType = Enums["Il2CppSteam.ServerType"]

---@enum Enum_Il2CppSubtitles_002e_CassieAnnouncementType
Enums["Il2CppSubtitles.CassieAnnouncementType"] = {
    ["Decontamination"] = "Decontamination",
    ["Detonation"] = "Detonation",
    ["None"] = "None",
    ["Normal"] = "Normal",
}
Enums.CassieAnnouncementType = Enums["Il2CppSubtitles.CassieAnnouncementType"]
CassieAnnouncementType = Enums["Il2CppSubtitles.CassieAnnouncementType"]

---@enum Enum_Il2CppSubtitles_002e_SubtitleType
Enums["Il2CppSubtitles.SubtitleType"] = {
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
Enums.SubtitleType = Enums["Il2CppSubtitles.SubtitleType"]
SubtitleType = Enums["Il2CppSubtitles.SubtitleType"]

---@enum Enum_Il2CppUserSettings_002e_AudioSettings_002e_MixerAudioSettings_002b_VolumeSliderSetting
Enums["Il2CppUserSettings.AudioSettings.MixerAudioSettings+VolumeSliderSetting"] = {
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
Enums.VolumeSliderSetting = Enums["Il2CppUserSettings.AudioSettings.MixerAudioSettings+VolumeSliderSetting"]

---@enum Enum_Il2CppUserSettings_002e_AudioSettings_002e_OtherAudioSetting
Enums["Il2CppUserSettings.AudioSettings.OtherAudioSetting"] = {
    ["NoiseReduction"] = "NoiseReduction",
    ["ProxVcReverbIntensity"] = "ProxVcReverbIntensity",
    ["SpatialAnnouncements"] = "SpatialAnnouncements",
}
Enums.OtherAudioSetting = Enums["Il2CppUserSettings.AudioSettings.OtherAudioSetting"]
OtherAudioSetting = Enums["Il2CppUserSettings.AudioSettings.OtherAudioSetting"]

---@enum Enum_Il2CppUserSettings_002e_ControlsSettings_002e_MiscControlsSetting
Enums["Il2CppUserSettings.ControlsSettings.MiscControlsSetting"] = {
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
Enums.MiscControlsSetting = Enums["Il2CppUserSettings.ControlsSettings.MiscControlsSetting"]
MiscControlsSetting = Enums["Il2CppUserSettings.ControlsSettings.MiscControlsSetting"]

---@enum Enum_Il2CppUserSettings_002e_ControlsSettings_002e_SensitivitySetting
Enums["Il2CppUserSettings.ControlsSettings.SensitivitySetting"] = {
    ["AdsReductionMultiplier"] = "AdsReductionMultiplier",
    ["Invert"] = "Invert",
    ["SensMultiplier"] = "SensMultiplier",
}
Enums.SensitivitySetting = Enums["Il2CppUserSettings.ControlsSettings.SensitivitySetting"]
SensitivitySetting = Enums["Il2CppUserSettings.ControlsSettings.SensitivitySetting"]

---@enum Enum_Il2CppUserSettings_002e_GUIElements_002e_UserSettingDependency_002b_Dependency_002b_Condition
Enums["Il2CppUserSettings.GUIElements.UserSettingDependency+Dependency+Condition"] = {
    ["EqualsTo"] = "EqualsTo",
    ["GreaterThan"] = "GreaterThan",
    ["LessThan"] = "LessThan",
}
Enums.Condition = Enums["Il2CppUserSettings.GUIElements.UserSettingDependency+Dependency+Condition"]

---@enum Enum_Il2CppUserSettings_002e_GUIElements_002e_UserSettingDependency_002b_Dependency_002b_SettingType
Enums["Il2CppUserSettings.GUIElements.UserSettingDependency+Dependency+SettingType"] = {
    ["Dropdown"] = "Dropdown",
    ["Slider"] = "Slider",
    ["Toggle"] = "Toggle",
}
Enums.SettingType = Enums["Il2CppUserSettings.GUIElements.UserSettingDependency+Dependency+SettingType"]

---@enum Enum_Il2CppUserSettings_002e_OtherSettings_002e_MiscPrivacySetting
Enums["Il2CppUserSettings.OtherSettings.MiscPrivacySetting"] = {
    ["BadgePreferences"] = "BadgePreferences",
    ["DoNotTrack"] = "DoNotTrack",
    ["GlobalBadgeVisibility"] = "GlobalBadgeVisibility",
    ["HideSteamProfile"] = "HideSteamProfile",
    ["LocalBadgeVisibility"] = "LocalBadgeVisibility",
    ["RichPresence"] = "RichPresence",
    ["SteamLobbyPrivacy"] = "SteamLobbyPrivacy",
}
Enums.MiscPrivacySetting = Enums["Il2CppUserSettings.OtherSettings.MiscPrivacySetting"]
MiscPrivacySetting = Enums["Il2CppUserSettings.OtherSettings.MiscPrivacySetting"]

---@enum Enum_Il2CppUserSettings_002e_OtherSettings_002e_ScpSetting
Enums["Il2CppUserSettings.OtherSettings.ScpSetting"] = {
    ["ScpOptOut"] = "ScpOptOut",
}
Enums.ScpSetting = Enums["Il2CppUserSettings.OtherSettings.ScpSetting"]
ScpSetting = Enums["Il2CppUserSettings.OtherSettings.ScpSetting"]

---@enum Enum_Il2CppUserSettings_002e_OtherSettings_002e_SteamLobbyPrivacy
Enums["Il2CppUserSettings.OtherSettings.SteamLobbyPrivacy"] = {
    ["Friends"] = "Friends",
    ["Private"] = "Private",
    ["Public"] = "Public",
}
Enums.SteamLobbyPrivacy = Enums["Il2CppUserSettings.OtherSettings.SteamLobbyPrivacy"]
SteamLobbyPrivacy = Enums["Il2CppUserSettings.OtherSettings.SteamLobbyPrivacy"]

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_Examples_002e_SSAbilitiesExample_002b_ExampleId
Enums["Il2CppUserSettings.ServerSpecific.Examples.SSAbilitiesExample+ExampleId"] = {
    ["HealAlly"] = "HealAlly",
    ["SpeedBoostKey"] = "SpeedBoostKey",
    ["SpeedBoostToggle"] = "SpeedBoostToggle",
}

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_Examples_002e_SSLightSpawnerExample_002b_ExampleId
Enums["Il2CppUserSettings.ServerSpecific.Examples.SSLightSpawnerExample+ExampleId"] = {
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

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_Examples_002e_SSPrimitiveSpawnerExample_002b_ExampleId
Enums["Il2CppUserSettings.ServerSpecific.Examples.SSPrimitiveSpawnerExample+ExampleId"] = {
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

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_SSDropdownSetting_002b_DropdownEntryType
Enums["Il2CppUserSettings.ServerSpecific.SSDropdownSetting+DropdownEntryType"] = {
    ["Hybrid"] = "Hybrid",
    ["HybridLoop"] = "HybridLoop",
    ["Regular"] = "Regular",
    ["Scrollable"] = "Scrollable",
    ["ScrollableLoop"] = "ScrollableLoop",
}
Enums.DropdownEntryType = Enums["Il2CppUserSettings.ServerSpecific.SSDropdownSetting+DropdownEntryType"]

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_SSDropdownSetting_002b_UpdateType
Enums["Il2CppUserSettings.ServerSpecific.SSDropdownSetting+UpdateType"] = {
    ["None"] = "None",
    ["Options"] = "Options",
    ["Selection"] = "Selection",
}

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_SSPlaintextSetting_002b_UpdateType
Enums["Il2CppUserSettings.ServerSpecific.SSPlaintextSetting+UpdateType"] = {
    ["Clear"] = "Clear",
    ["None"] = "None",
    ["Settings"] = "Settings",
    ["Text"] = "Text",
}

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_SSSliderSetting_002b_UpdateType
Enums["Il2CppUserSettings.ServerSpecific.SSSliderSetting+UpdateType"] = {
    ["Constraints"] = "Constraints",
    ["None"] = "None",
    ["Value"] = "Value",
}

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_SSTextArea_002b_FoldoutMode
Enums["Il2CppUserSettings.ServerSpecific.SSTextArea+FoldoutMode"] = {
    ["CollapseOnEntry"] = "CollapseOnEntry",
    ["CollapsedByDefault"] = "CollapsedByDefault",
    ["ExtendOnEntry"] = "ExtendOnEntry",
    ["ExtendedByDefault"] = "ExtendedByDefault",
    ["NotCollapsable"] = "NotCollapsable",
}
Enums.FoldoutMode = Enums["Il2CppUserSettings.ServerSpecific.SSTextArea+FoldoutMode"]

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_SSTwoButtonsSetting_002b_UpdateType
Enums["Il2CppUserSettings.ServerSpecific.SSTwoButtonsSetting+UpdateType"] = {
    ["None"] = "None",
    ["Options"] = "Options",
    ["Value"] = "Value",
}

---@enum Enum_Il2CppUserSettings_002e_ServerSpecific_002e_ServerSpecificSettingBase_002b_UserResponseMode
Enums["Il2CppUserSettings.ServerSpecific.ServerSpecificSettingBase+UserResponseMode"] = {
    ["AcquisitionAndChange"] = "AcquisitionAndChange",
    ["ChangeOnly"] = "ChangeOnly",
    ["None"] = "None",
}
Enums.UserResponseMode = Enums["Il2CppUserSettings.ServerSpecific.ServerSpecificSettingBase+UserResponseMode"]

---@enum Enum_Il2CppUserSettings_002e_ToggleOrHoldInput_002b_InputActivationMode
Enums["Il2CppUserSettings.ToggleOrHoldInput+InputActivationMode"] = {
    ["Hold"] = "Hold",
    ["Toggle"] = "Toggle",
}
Enums.InputActivationMode = Enums["Il2CppUserSettings.ToggleOrHoldInput+InputActivationMode"]

---@enum Enum_Il2CppUserSettings_002e_UserInterfaceSettings_002e_UISetting
Enums["Il2CppUserSettings.UserInterfaceSettings.UISetting"] = {
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
Enums.UISetting = Enums["Il2CppUserSettings.UserInterfaceSettings.UISetting"]
UISetting = Enums["Il2CppUserSettings.UserInterfaceSettings.UISetting"]

---@enum Enum_Il2CppUserSettings_002e_VideoSettings_002e_DisplayVideoSetting
Enums["Il2CppUserSettings.VideoSettings.DisplayVideoSetting"] = {
    ["AspectRatio"] = "AspectRatio",
    ["FpsLimiter"] = "FpsLimiter",
    ["FullscreenMode"] = "FullscreenMode",
    ["GraphicsAPI"] = "GraphicsAPI",
    ["Resolution"] = "Resolution",
    ["VSyncCount"] = "VSyncCount",
}
Enums.DisplayVideoSetting = Enums["Il2CppUserSettings.VideoSettings.DisplayVideoSetting"]
DisplayVideoSetting = Enums["Il2CppUserSettings.VideoSettings.DisplayVideoSetting"]

---@enum Enum_Il2CppUserSettings_002e_VideoSettings_002e_LightingVideoSetting
Enums["Il2CppUserSettings.VideoSettings.LightingVideoSetting"] = {
    ["RenderLights"] = "RenderLights",
    ["RenderShadows"] = "RenderShadows",
    ["ShadowResolution"] = "ShadowResolution",
}
Enums.LightingVideoSetting = Enums["Il2CppUserSettings.VideoSettings.LightingVideoSetting"]
LightingVideoSetting = Enums["Il2CppUserSettings.VideoSettings.LightingVideoSetting"]

---@enum Enum_Il2CppUserSettings_002e_VideoSettings_002e_MiscVideoSetting
Enums["Il2CppUserSettings.VideoSettings.MiscVideoSetting"] = {
    ["BrightnessBoost"] = "BrightnessBoost",
    ["ExplosionShake"] = "ExplosionShake",
    ["FlashbangDarkMode"] = "FlashbangDarkMode",
    ["HeadBobbing"] = "HeadBobbing",
    ["Scp939VisionBlur"] = "Scp939VisionBlur",
    ["ShowNeedles"] = "ShowNeedles",
}
Enums.MiscVideoSetting = Enums["Il2CppUserSettings.VideoSettings.MiscVideoSetting"]
MiscVideoSetting = Enums["Il2CppUserSettings.VideoSettings.MiscVideoSetting"]

---@enum Enum_Il2CppUserSettings_002e_VideoSettings_002e_PerformanceVideoSetting
Enums["Il2CppUserSettings.VideoSettings.PerformanceVideoSetting"] = {
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
Enums.PerformanceVideoSetting = Enums["Il2CppUserSettings.VideoSettings.PerformanceVideoSetting"]
PerformanceVideoSetting = Enums["Il2CppUserSettings.VideoSettings.PerformanceVideoSetting"]

---@enum Enum_Il2CppUtf8Json_002e_CollectionDeserializeToBehaviour
Enums["Il2CppUtf8Json.CollectionDeserializeToBehaviour"] = {
    ["Add"] = "Add",
    ["OverwriteReplace"] = "OverwriteReplace",
}
Enums.CollectionDeserializeToBehaviour = Enums["Il2CppUtf8Json.CollectionDeserializeToBehaviour"]
CollectionDeserializeToBehaviour = Enums["Il2CppUtf8Json.CollectionDeserializeToBehaviour"]

---@enum Enum_Il2CppUtf8Json_002e_Internal_002e_DoubleConversion_002e_DoubleToStringConverter_002b_DtoaMode
Enums["Il2CppUtf8Json.Internal.DoubleConversion.DoubleToStringConverter+DtoaMode"] = {
    ["SHORTEST"] = "SHORTEST",
    ["SHORTEST_SINGLE"] = "SHORTEST_SINGLE",
}
Enums.DtoaMode = Enums["Il2CppUtf8Json.Internal.DoubleConversion.DoubleToStringConverter+DtoaMode"]

---@enum Enum_Il2CppUtf8Json_002e_Internal_002e_DoubleConversion_002e_DoubleToStringConverter_002b_FastDtoaMode
Enums["Il2CppUtf8Json.Internal.DoubleConversion.DoubleToStringConverter+FastDtoaMode"] = {
    ["FAST_DTOA_SHORTEST"] = "FAST_DTOA_SHORTEST",
    ["FAST_DTOA_SHORTEST_SINGLE"] = "FAST_DTOA_SHORTEST_SINGLE",
}
Enums.FastDtoaMode = Enums["Il2CppUtf8Json.Internal.DoubleConversion.DoubleToStringConverter+FastDtoaMode"]

---@enum Enum_Il2CppUtf8Json_002e_Internal_002e_DoubleConversion_002e_DoubleToStringConverter_002b_Flags
Enums["Il2CppUtf8Json.Internal.DoubleConversion.DoubleToStringConverter+Flags"] = {
    ["EMIT_POSITIVE_EXPONENT_SIGN"] = "EMIT_POSITIVE_EXPONENT_SIGN",
    ["EMIT_TRAILING_DECIMAL_POINT"] = "EMIT_TRAILING_DECIMAL_POINT",
    ["EMIT_TRAILING_ZERO_AFTER_POINT"] = "EMIT_TRAILING_ZERO_AFTER_POINT",
    ["NO_FLAGS"] = "NO_FLAGS",
    ["UNIQUE_ZERO"] = "UNIQUE_ZERO",
}

---@enum Enum_Il2CppUtf8Json_002e_Internal_002e_DoubleConversion_002e_StringToDoubleConverter_002b_Flags
Enums["Il2CppUtf8Json.Internal.DoubleConversion.StringToDoubleConverter+Flags"] = {
    ["ALLOW_CASE_INSENSIBILITY"] = "ALLOW_CASE_INSENSIBILITY",
    ["ALLOW_HEX"] = "ALLOW_HEX",
    ["ALLOW_LEADING_SPACES"] = "ALLOW_LEADING_SPACES",
    ["ALLOW_OCTALS"] = "ALLOW_OCTALS",
    ["ALLOW_SPACES_AFTER_SIGN"] = "ALLOW_SPACES_AFTER_SIGN",
    ["ALLOW_TRAILING_JUNK"] = "ALLOW_TRAILING_JUNK",
    ["ALLOW_TRAILING_SPACES"] = "ALLOW_TRAILING_SPACES",
    ["NO_FLAGS"] = "NO_FLAGS",
}

---@enum Enum_Il2CppUtf8Json_002e_JsonToken
Enums["Il2CppUtf8Json.JsonToken"] = {
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
Enums.JsonToken = Enums["Il2CppUtf8Json.JsonToken"]
JsonToken = Enums["Il2CppUtf8Json.JsonToken"]

---@enum Enum_Il2CppUtils_002e_CustomParser_002b_ParseResult
Enums["Il2CppUtils.CustomParser+ParseResult"] = {
    ["Failed"] = "Failed",
    ["FullSuccess"] = "FullSuccess",
    ["PartialSuccess"] = "PartialSuccess",
}
Enums.ParseResult = Enums["Il2CppUtils.CustomParser+ParseResult"]

---@enum Enum_Il2CppUtils_002e_Networking_002e_HintEffectReaderWriter_002b_HintEffectType
Enums["Il2CppUtils.Networking.HintEffectReaderWriter+HintEffectType"] = {
    ["Alpha"] = "Alpha",
    ["AlphaCurve"] = "AlphaCurve",
    ["Outline"] = "Outline",
}
Enums.HintEffectType = Enums["Il2CppUtils.Networking.HintEffectReaderWriter+HintEffectType"]

---@enum Enum_Il2CppUtils_002e_Networking_002e_HintParameterReaderWriter_002b_HintParameterType
Enums["Il2CppUtils.Networking.HintParameterReaderWriter+HintParameterType"] = {
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
Enums.HintParameterType = Enums["Il2CppUtils.Networking.HintParameterReaderWriter+HintParameterType"]

---@enum Enum_Il2CppUtils_002e_Networking_002e_HintReaderWriter_002b_HintType
Enums["Il2CppUtils.Networking.HintReaderWriter+HintType"] = {
    ["Text"] = "Text",
    ["Translation"] = "Translation",
}
Enums.HintType = Enums["Il2CppUtils.Networking.HintReaderWriter+HintType"]

---@enum Enum_Il2CppUtils_002e_Networking_002e_NullableBoolReaderWriter_002b_NullableBoolValue
Enums["Il2CppUtils.Networking.NullableBoolReaderWriter+NullableBoolValue"] = {
    ["False"] = "False",
    ["Null"] = "Null",
    ["True"] = "True",
}
Enums.NullableBoolValue = Enums["Il2CppUtils.Networking.NullableBoolReaderWriter+NullableBoolValue"]

---@enum Enum_Il2CppVoiceChat_002e_Codec_002e_Enums_002e_OpusApplicationType
Enums["Il2CppVoiceChat.Codec.Enums.OpusApplicationType"] = {
    ["Audio"] = "Audio",
    ["RestrictedLowDelay"] = "RestrictedLowDelay",
    ["Voip"] = "Voip",
}
Enums.OpusApplicationType = Enums["Il2CppVoiceChat.Codec.Enums.OpusApplicationType"]
OpusApplicationType = Enums["Il2CppVoiceChat.Codec.Enums.OpusApplicationType"]

---@enum Enum_Il2CppVoiceChat_002e_Codec_002e_Enums_002e_OpusCtlGetRequest
Enums["Il2CppVoiceChat.Codec.Enums.OpusCtlGetRequest"] = {
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
Enums.OpusCtlGetRequest = Enums["Il2CppVoiceChat.Codec.Enums.OpusCtlGetRequest"]
OpusCtlGetRequest = Enums["Il2CppVoiceChat.Codec.Enums.OpusCtlGetRequest"]

---@enum Enum_Il2CppVoiceChat_002e_Codec_002e_Enums_002e_OpusCtlSetRequest
Enums["Il2CppVoiceChat.Codec.Enums.OpusCtlSetRequest"] = {
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
Enums.OpusCtlSetRequest = Enums["Il2CppVoiceChat.Codec.Enums.OpusCtlSetRequest"]
OpusCtlSetRequest = Enums["Il2CppVoiceChat.Codec.Enums.OpusCtlSetRequest"]

---@enum Enum_Il2CppVoiceChat_002e_Codec_002e_Enums_002e_OpusStatusCode
Enums["Il2CppVoiceChat.Codec.Enums.OpusStatusCode"] = {
    ["AllocFail"] = "AllocFail",
    ["BadArguments"] = "BadArguments",
    ["BufferTooSmall"] = "BufferTooSmall",
    ["InternalError"] = "InternalError",
    ["InvalidPacket"] = "InvalidPacket",
    ["InvalidState"] = "InvalidState",
    ["OK"] = "OK",
    ["Unimplemented"] = "Unimplemented",
}
Enums.OpusStatusCode = Enums["Il2CppVoiceChat.Codec.Enums.OpusStatusCode"]
OpusStatusCode = Enums["Il2CppVoiceChat.Codec.Enums.OpusStatusCode"]

---@enum Enum_Il2CppVoiceChat_002e_Playbacks_002e_GlobalChatIconType
Enums["Il2CppVoiceChat.Playbacks.GlobalChatIconType"] = {
    ["Avatar"] = "Avatar",
    ["Intercom"] = "Intercom",
    ["None"] = "None",
    ["Radio"] = "Radio",
}
Enums.GlobalChatIconType = Enums["Il2CppVoiceChat.Playbacks.GlobalChatIconType"]
GlobalChatIconType = Enums["Il2CppVoiceChat.Playbacks.GlobalChatIconType"]

---@enum Enum_Il2CppVoiceChat_002e_VcMuteFlags
Enums["Il2CppVoiceChat.VcMuteFlags"] = {
    ["GlobalIntercom"] = "GlobalIntercom",
    ["GlobalRegular"] = "GlobalRegular",
    ["LocalIntercom"] = "LocalIntercom",
    ["LocalRegular"] = "LocalRegular",
    ["None"] = "None",
}
Enums.VcMuteFlags = Enums["Il2CppVoiceChat.VcMuteFlags"]
VcMuteFlags = Enums["Il2CppVoiceChat.VcMuteFlags"]

---@enum Enum_Il2CppVoiceChat_002e_VcPrivacyFlags
Enums["Il2CppVoiceChat.VcPrivacyFlags"] = {
    ["AllowMicCapture"] = "AllowMicCapture",
    ["AllowRecording"] = "AllowRecording",
    ["None"] = "None",
    ["SettingsSelected"] = "SettingsSelected",
}
Enums.VcPrivacyFlags = Enums["Il2CppVoiceChat.VcPrivacyFlags"]
VcPrivacyFlags = Enums["Il2CppVoiceChat.VcPrivacyFlags"]

---@enum Enum_Il2CppVoiceChat_002e_VoiceChatChannel
Enums["Il2CppVoiceChat.VoiceChatChannel"] = {
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
Enums.VoiceChatChannel = Enums["Il2CppVoiceChat.VoiceChatChannel"]
VoiceChatChannel = Enums["Il2CppVoiceChat.VoiceChatChannel"]

---@enum Enum_Il2CppWaterPhysics_002e_BuoyancyMode
Enums["Il2CppWaterPhysics.BuoyancyMode"] = {
    ["Floater"] = "Floater",
    ["LongTimeFloaters"] = "LongTimeFloaters",
    ["NonBuoyant"] = "NonBuoyant",
    ["ShortTimeFloaters"] = "ShortTimeFloaters",
    ["SuperHeavy"] = "SuperHeavy",
    ["SuperLight"] = "SuperLight",
}
Enums.BuoyancyMode = Enums["Il2CppWaterPhysics.BuoyancyMode"]
BuoyancyMode = Enums["Il2CppWaterPhysics.BuoyancyMode"]

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
---@field Type string # Read-only.
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

--- Fired when CASSIE schedules a line for playback (before audio starts). Pair with for the started-playing notification.
---@class AnnouncerLineScheduledEventArgs
---@field ApiName string|nil # Read-only. Convenience: API name (e.g. "ALERT") of the scheduled line.
---@field ScheduledStartDspTime number # Read-only. The DSP-time at which playback is scheduled to begin. Compare against to compute lead time.
---@field Word AnnouncerWord|nil # Read-only. Wrapped for ergonomic access.
local _type_AnnouncerLineScheduledEventArgs = {}

--- Fired when a CASSIE line begins audible playback (engine's CassieLineSource.OnStartedPlaying static event). Useful for mods that want to flash subtitles in sync with audio.
---@class AnnouncerLineStartedEventArgs
---@field ApiName string|nil # Read-only. Convenience: API name (e.g. "ALERT") of the line.
---@field Word AnnouncerWord|nil # Read-only. Wrapped for ergonomic access.
local _type_AnnouncerLineStartedEventArgs = {}

---@class AnnouncerWord
---@field ApiName string|nil # Read-only.
---@field Duration number # Read-only.
---@field Type string # Read-only.
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
---@field Channel string # Read-only. Which mixer channel the sound was going to play on (DefaultSfx, Voice, etc.).
---@field ClipName string|nil # Read-only. The local clip name, or an empty string when no clip is available.
---@field IsAllowed boolean # Read-only. Observed permission at capture time, not a cancellation control.
---@field Volume number # Read-only. Volume scalar in [0, 1].
local _type_AudioPlayingEventArgs = {}

--- Body armor configuration and owning-client wear state.
---@class BodyArmorItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_BodyArmorItem = {}

--- A typed world armor pickup. Inventory armor configuration is exposed by .
---@class BodyArmorPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_BodyArmorPickup = {}

--- Wrapper around . Adds health / damage-type state on top of the base surface for HUD overlays (door HP bars, "this door is about to break" warnings, faction-specific damage indicators).
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

--- Wrapper around — multi-stage checkpoint gates (LCZ entrance, EZ/HCZ checkpoints) that control multiple sub-doors in sequence. Adds the sub-door list and current sequence state on top of the base surface.
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
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_ConsumableItem = {}

---@class CurrentItemChangedEventArgs
---@field CurrentSerial integer # Read-only. The serial held after the observed change.
---@field CurrentType string # Read-only. The item type held after the observed change.
---@field Player Player|nil # Read-only.
---@field PreviousSerial integer # Read-only. The serial held before the observed change.
---@field PreviousType string # Read-only. The item type held before the observed change.
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

--- Wrapper around . Exposes sequence state, destination level, and direction for floor-indicator HUDs and elevator-music mods. Read-only — call/recall is server-authoritative.
---@class Elevator
---@field CurrentSequence string # Read-only. The current sequence state (Ready / Arrived / DoorOpening / Moving / DoorClosing).
---@field DestinationLevel integer # Read-only. The level the chamber is currently moving to (or has stopped at).
---@field GoingUp boolean # Read-only. True if the chamber is currently traveling upward.
---@field IsDestroyed boolean # Read-only.
---@field IsReady boolean # Read-only. True if the chamber is in phase.
---@field IsReadyForUserInput boolean # Read-only. True if the chamber is ready to accept user input (e.g. a button press).
---@field NextLevel integer # Read-only. The level above the current one.
---@field PreviousLevel integer # Read-only. The level below the current one.
---@field Room Room|nil # Read-only. The room containing the chamber, or null.
---@field SequenceElapsed number # Read-only. Seconds elapsed in the current sequence phase.
local _type_Elevator = {}

--- Wrapper around — the doors mounted on an elevator chamber's entry/exit. Adds the chamber link and elevator group on top of the base surface.
---@class ElevatorDoor
---@field ActiveLocks integer # Read-only.
---@field CanSeeThrough boolean # Read-only.
---@field Id integer # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsMoving boolean # Read-only.
---@field IsOpen boolean # Read-only.
---@field Name string|nil # Read-only.
local _type_ElevatorDoor = {}

--- Wraps an reference for chamber lifecycle / movement events.
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
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_ExplosiveGrenadeProjectile = {}

--- Wrapper around — the in-facility CCTV cameras that SCP-079 uses. Useful for SCP-079 spectator UIs, camera-feed mods, minimap markers showing camera coverage, etc.
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

--- Wraps a reference for CCTV lifecycle / state events.
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

--- Wrapper around . Exposes magazine state, fire-mode info, and module access for HUD overlays (custom ammo counters, hit-marker mods, attachment indicators) without requiring consumers to dig through the modular firearm subcomponent system.
---@class FirearmItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_FirearmItem = {}

--- Wrapper around a world-spawned . Adds a typed downcast and the firearm-pickup-specific flag on top of the base surface.
---@class FirearmPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_FirearmPickup = {}

---@class FirearmShotEventArgs
---@field Firearm string # Read-only.
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
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_FlashbangProjectile = {}

---@class GameContext
---@field IsRoundEnded boolean # Read-only.
---@field IsRoundStarted boolean # Read-only.
---@field LocalPlayer Player|nil # Read-only.
---@field PlayerCount integer # Read-only.
---@field RoundPhase string # Read-only.
---@field SpectatedPlayer Player|nil # Read-only.
local _type_GameContext = {}
---@param playerId integer
---@return Item|nil
function _type_GameContext:GetCurrentItem(playerId) end
---@param playerId integer
---@return FirearmState|nil
function _type_GameContext:GetFirearmState(playerId) end
---@param playerId integer
---@return number|nil
function _type_GameContext:GetHealth(playerId) end
---@param playerId integer
---@return number|nil
function _type_GameContext:GetHumeShield(playerId) end
---@param playerId integer
---@return Player|nil
function _type_GameContext:GetPlayer(playerId) end
---@param index integer
---@return Player|nil
function _type_GameContext:GetPlayerAt(index) end
---@param playerId integer
---@return Position|nil
function _type_GameContext:GetPosition(playerId) end
---@param playerId integer
---@return Room|nil
function _type_GameContext:GetRoom(playerId) end
---@param playerId integer
---@return number|nil
function _type_GameContext:GetStamina(playerId) end

--- Wrapper around . Exposes activation / engagement state, cooldown timing, and door state for HUD overlays (charge percentages, "generators remaining" counters, faction-tinted glow indicators).
---@class Generator
---@field IsActivating boolean # Read-only. True while the generator is in its activation countdown phase (lever pulled, not yet engaged).
---@field IsActivationReady boolean # Read-only. True if the generator's activation cooldown is satisfied (matches game's ActivationReady).
---@field IsDestroyed boolean # Read-only.
---@field IsEngaged boolean # Read-only. True once the generator has finished its activation sequence.
---@field IsOpen boolean # Read-only. True if the door panel is open (lever / interior accessible).
---@field IsUnlocked boolean # Read-only. True if the unlock cooldown has elapsed and the generator can be engaged.
---@field Room Room|nil # Read-only. The containing this generator, or null if the generator isn't placed in any tracked room.
---@field TotalActivationTime number # Read-only. Total seconds the activation sequence takes from lever-pull to engaged.
---@field TotalDeactivationTime number # Read-only. Total seconds the deactivation sequence takes.
local _type_Generator = {}

--- Fires when an reaches the engaged state.
---@class GeneratorEngagedEventArgs
---@field Generator Generator|nil # Read-only. The generator that just engaged.
local _type_GeneratorEngagedEventArgs = {}

--- An observed generator flag transition after replication, without an inferred interacting player.
---@class GeneratorStateChangedEventArgs
---@field Flags string # Read-only.
---@field Generator Generator|nil # Read-only.
---@field PreviousFlags string # Read-only.
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
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Item = {}

--- Wrapper around . Exposes the keycard's resolved permissions for door-permission HUDs (which doors can I open? access tier indicators, faction-themed keycard colors).
---@class KeycardItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_KeycardItem = {}

--- Wrapper around a world-spawned . Adds a typed downcast plus the keycard-pickup-specific flag.
---@class KeycardPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
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
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
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
---@field RoleType string # Read-only. The current role ID, or when unavailable.
---@field Team string # Read-only. The current team, or when no role is available.
---@field Zone string # Read-only. The facility zone the player is currently in. Returns if the player isn't in any tracked room.
local _type_LocalPlayer = {}

--- Wrapper around . Aggregates the locker's array for chamber-aware HUDs (loot indicators, "what's inside" overlays, faction-themed locker tints).
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
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
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
---@field Type string # Read-only.
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
---@field RoleType string # Read-only. The current role ID, or when unavailable.
---@field Team string # Read-only. The current team, or when no role is available.
---@field Zone string # Read-only. The facility zone the player is currently in. Returns if the player isn't in any tracked room.
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

--- Fires when a tracked player's HumeShieldStat value crosses a polling tick boundary. Delta is signed — negative for shield damage, positive for regeneration. The convenience property is true on the tick where the shield drops to zero from a non-zero value (mirrors LabAPI's server-side HumeShieldBroken event).
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
---@field Type string # Read-only.
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
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_RadioItem = {}

--- A dropped radio's replicated enabled and range state. Pickup battery is server-only and is not exposed here.
---@class RadioPickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_RadioPickup = {}

--- Wrapper around . Exposes the death snapshot (role, owner, nickname, creation time) for death-cam mods, ragdoll-cleanup mods, and "who killed whom" replays.
---@class Ragdoll
---@field CreationTime number # Read-only. Engine creation timestamp (DSP-time-style double).
---@field IsDestroyed boolean # Read-only.
---@field IsFrozen boolean # Read-only. True if the ragdoll has been frozen (no further physics).
---@field Nickname string|nil # Read-only. The nickname of the dead player at time of death.
---@field RoleType string # Read-only. The role the dead player was when the ragdoll was created.
local _type_Ragdoll = {}

--- Wraps a reference for spawn / removed events.
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
---@field Team string # Read-only.
---@field Type string # Read-only.
local _type_Role = {}

---@class Room
---@field IsDestroyed boolean # Read-only.
---@field Name string|nil # Read-only.
---@field RoomName string # Read-only.
---@field Shape string # Read-only.
---@field Zone string # Read-only.
local _type_Room = {}

--- Fired when the server-reported changes. Carries both the previous and new phases so handlers can react to specific transitions (e.g. only reset state when transitioning to ).
---@class RoundPhaseChangedEventArgs
---@field NewPhase string # Read-only.
---@field PreviousPhase string # Read-only.
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
---@field Type string # Read-only.
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
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp1344Item = {}

--- SCP-1576 with the common usable-item facade and transmission configuration.
---@class Scp1576Item
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp1576Item = {}

--- SCP-1576's world pickup and replicated horn position.
---@class Scp1576Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp1576Pickup = {}

--- Read-only client wrapper for Scp2176Projectile.
---@class Scp2176Projectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp2176Projectile = {}

--- A deployed SCP-244 pickup and its replicated fog state.
---@class Scp244Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp244Pickup = {}

--- Wrapper around — the SCP-330 candy bag item. Exposes the carried candy list and selected-candy index for HUD overlays (candy picker, current-candy indicator, "what did I get" toasts).
---@class Scp330Item
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp330Item = {}

--- Wrapper around a world-spawned — the SCP-330 candy bag dropped on the ground. Exposes the bag's contents and the currently exposed (visually displayed) candy.
---@class Scp330Pickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_Scp330Pickup = {}

---@class ScriptOutput
local _type_ScriptOutput = {}
function _type_ScriptOutput:ClearText() end
---@param text string|nil
---@return boolean
function _type_ScriptOutput:Log(text) end
---@param text string|nil
---@param durationSeconds number
---@return boolean
function _type_ScriptOutput:ShowText(text, durationSeconds) end

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

--- Wrapper around . Exposes the in-progress flag and inactivity timer for HUD overlays (pre-fire warnings, tesla counters, faction-tinted gate visualization).
---@class Tesla
---@field InProgress boolean # Read-only. True while the tesla is in its shock animation.
---@field InactiveTime number # Read-only. Seconds the gate has been idle since the last burst.
---@field IsDestroyed boolean # Read-only.
---@field Room Room|nil # Read-only. The the tesla is mounted in, or null if not placed in any tracked room.
local _type_Tesla = {}

--- Wraps a reference for tesla lifecycle / burst events.
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

--- Wrapper around . Exposes the weak/full throw settings (force, fuse) for HUD overlays (throw-strength indicator, fuse-time tooltips, custom telemetry).
---@class ThrowableItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_ThrowableItem = {}

--- Wrapper around a world-spawned — a dropped (un-thrown) grenade that's still in its idle state. Once thrown, grenades become s instead.
---@class TimedGrenadePickup
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_TimedGrenadePickup = {}

--- Read-only client wrapper for TimeGrenade.
---@class TimedGrenadeProjectile
---@field CanSearch boolean # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsInUse boolean # Read-only.
---@field IsLocked boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
---@field Weight number # Read-only.
local _type_TimedGrenadeProjectile = {}

--- A usable item, including consumables and wearable SCP items.
---@class UsableItem
---@field CanDrop boolean # Read-only.
---@field CanEquip boolean # Read-only.
---@field CanHolster boolean # Read-only.
---@field Category string # Read-only.
---@field IsDestroyed boolean # Read-only.
---@field IsEquipped boolean # Read-only.
---@field IsLocalPlayerItem boolean # Read-only.
---@field Serial integer # Read-only.
---@field Type string # Read-only.
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
---@field Channel string # Read-only.
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

--- Fires when 's InProgress state transitions. reflects the new state.
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
---@field Status string # Read-only. The replicated workstation status from the game.
local _type_Workstation = {}

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
---@param callback fun(ev: AdminToyAddedEventArgs|nil)
---@return string subscriptionToken
function Events.AdminToy.Added.Add(callback) end
---@param callbackOrToken fun(ev: AdminToyAddedEventArgs|nil)|string
---@return boolean removed
function Events.AdminToy.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.AdminToy.Removed = {}
---@param callback fun(ev: AdminToyRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.AdminToy.Removed.Add(callback) end
---@param callbackOrToken fun(ev: AdminToyRemovedEventArgs|nil)|string
---@return boolean removed
function Events.AdminToy.Removed.Remove(callbackOrToken) end
Events.Announcer = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Announcer.LineScheduled = {}
---@param callback fun(ev: AnnouncerLineScheduledEventArgs|nil)
---@return string subscriptionToken
function Events.Announcer.LineScheduled.Add(callback) end
---@param callbackOrToken fun(ev: AnnouncerLineScheduledEventArgs|nil)|string
---@return boolean removed
function Events.Announcer.LineScheduled.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Announcer.LineStarted = {}
---@param callback fun(ev: AnnouncerLineStartedEventArgs|nil)
---@return string subscriptionToken
function Events.Announcer.LineStarted.Add(callback) end
---@param callbackOrToken fun(ev: AnnouncerLineStartedEventArgs|nil)|string
---@return boolean removed
function Events.Announcer.LineStarted.Remove(callbackOrToken) end
Events.Asset = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Asset.OverrideApplied = {}
---@param callback fun(ev: AssetOverrideAppliedEventArgs|nil)
---@return string subscriptionToken
function Events.Asset.OverrideApplied.Add(callback) end
---@param callbackOrToken fun(ev: AssetOverrideAppliedEventArgs|nil)|string
---@return boolean removed
function Events.Asset.OverrideApplied.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Asset.OverrideCleared = {}
---@param callback fun(ev: AssetOverrideClearedEventArgs|nil)
---@return string subscriptionToken
function Events.Asset.OverrideCleared.Add(callback) end
---@param callbackOrToken fun(ev: AssetOverrideClearedEventArgs|nil)|string
---@return boolean removed
function Events.Asset.OverrideCleared.Remove(callbackOrToken) end
Events.Audio = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Audio.Playing = {}
---@param callback fun(ev: AudioPlayingEventArgs|nil)
---@return string subscriptionToken
function Events.Audio.Playing.Add(callback) end
---@param callbackOrToken fun(ev: AudioPlayingEventArgs|nil)|string
---@return boolean removed
function Events.Audio.Playing.Remove(callbackOrToken) end
Events.Camera = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Camera.Shaking = {}
---@param callback fun(ev: CameraShakingEventArgs|nil)
---@return string subscriptionToken
function Events.Camera.Shaking.Add(callback) end
---@param callbackOrToken fun(ev: CameraShakingEventArgs|nil)|string
---@return boolean removed
function Events.Camera.Shaking.Remove(callbackOrToken) end
Events.Client = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.CanvasReady = {}
---@param callback fun(ev: CanvasReadyEventArgs|nil)
---@return string subscriptionToken
function Events.Client.CanvasReady.Add(callback) end
---@param callbackOrToken fun(ev: CanvasReadyEventArgs|nil)|string
---@return boolean removed
function Events.Client.CanvasReady.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.Connected = {}
---@param callback fun(ev: ClientConnectedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Connected.Add(callback) end
---@param callbackOrToken fun(ev: ClientConnectedEventArgs|nil)|string
---@return boolean removed
function Events.Client.Connected.Remove(callbackOrToken) end
--- Reports a transport disconnect, including unsuccessful handshake and retry attempts that never reached . Cleanup handlers must be idempotent; do not count this as one event per completed gameplay session.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.Disconnected = {}
---@param callback fun(ev: ClientDisconnectedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Disconnected.Add(callback) end
---@param callbackOrToken fun(ev: ClientDisconnectedEventArgs|nil)|string
---@return boolean removed
function Events.Client.Disconnected.Remove(callbackOrToken) end
--- Enrollment was disclosed or cleared. This does not replace transport or scene readiness.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.EnrollmentChanged = {}
---@param callback fun(ev: EnrollmentChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.EnrollmentChanged.Add(callback) end
---@param callbackOrToken fun(ev: EnrollmentChangedEventArgs|nil)|string
---@return boolean removed
function Events.Client.EnrollmentChanged.Remove(callbackOrToken) end
--- Reports the client's scene-ready lifecycle callback. This can precede the local player hub, populated world collections, and server enrollment disclosure.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.Ready = {}
---@param callback fun(ev: ClientReadyEventArgs|nil)
---@return string subscriptionToken
function Events.Client.Ready.Add(callback) end
---@param callbackOrToken fun(ev: ClientReadyEventArgs|nil)|string
---@return boolean removed
function Events.Client.Ready.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Client.SceneLoaded = {}
---@param callback fun(ev: SceneLoadedEventArgs|nil)
---@return string subscriptionToken
function Events.Client.SceneLoaded.Add(callback) end
---@param callbackOrToken fun(ev: SceneLoadedEventArgs|nil)|string
---@return boolean removed
function Events.Client.SceneLoaded.Remove(callbackOrToken) end
Events.Door = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.Added = {}
---@param callback fun(ev: DoorEventArgs|nil)
---@return string subscriptionToken
function Events.Door.Added.Add(callback) end
---@param callbackOrToken fun(ev: DoorEventArgs|nil)|string
---@return boolean removed
function Events.Door.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.LocksChanged = {}
---@param callback fun(ev: DoorLocksChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Door.LocksChanged.Add(callback) end
---@param callbackOrToken fun(ev: DoorLocksChangedEventArgs|nil)|string
---@return boolean removed
function Events.Door.LocksChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.Removed = {}
---@param callback fun(ev: DoorEventArgs|nil)
---@return string subscriptionToken
function Events.Door.Removed.Add(callback) end
---@param callbackOrToken fun(ev: DoorEventArgs|nil)|string
---@return boolean removed
function Events.Door.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Door.StateChanged = {}
---@param callback fun(ev: DoorStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Door.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: DoorStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Door.StateChanged.Remove(callbackOrToken) end
Events.Effect = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Effect.Disabled = {}
---@param callback fun(ev: EffectDisabledEventArgs|nil)
---@return string subscriptionToken
function Events.Effect.Disabled.Add(callback) end
---@param callbackOrToken fun(ev: EffectDisabledEventArgs|nil)|string
---@return boolean removed
function Events.Effect.Disabled.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Effect.Enabled = {}
---@param callback fun(ev: EffectEnabledEventArgs|nil)
---@return string subscriptionToken
function Events.Effect.Enabled.Add(callback) end
---@param callbackOrToken fun(ev: EffectEnabledEventArgs|nil)|string
---@return boolean removed
function Events.Effect.Enabled.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Effect.IntensityChanged = {}
---@param callback fun(ev: EffectIntensityChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Effect.IntensityChanged.Add(callback) end
---@param callbackOrToken fun(ev: EffectIntensityChangedEventArgs|nil)|string
---@return boolean removed
function Events.Effect.IntensityChanged.Remove(callbackOrToken) end
Events.Elevator = {}
--- Fires when an elevator chamber starts a movement sequence (door close → travel → door open at destination).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Elevator.Moved = {}
---@param callback fun(ev: ElevatorEventArgs|nil)
---@return string subscriptionToken
function Events.Elevator.Moved.Add(callback) end
---@param callbackOrToken fun(ev: ElevatorEventArgs|nil)|string
---@return boolean removed
function Events.Elevator.Moved.Remove(callbackOrToken) end
--- Fires when an elevator chamber is removed from the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Elevator.Removed = {}
---@param callback fun(ev: ElevatorEventArgs|nil)
---@return string subscriptionToken
function Events.Elevator.Removed.Add(callback) end
---@param callbackOrToken fun(ev: ElevatorEventArgs|nil)|string
---@return boolean removed
function Events.Elevator.Removed.Remove(callbackOrToken) end
--- Fires when a new elevator chamber spawns into the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Elevator.Spawned = {}
---@param callback fun(ev: ElevatorEventArgs|nil)
---@return string subscriptionToken
function Events.Elevator.Spawned.Add(callback) end
---@param callbackOrToken fun(ev: ElevatorEventArgs|nil)|string
---@return boolean removed
function Events.Elevator.Spawned.Remove(callbackOrToken) end
Events.FacilityCamera = {}
--- Fires when a CCTV camera is created (typically at scene load).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.Created = {}
---@param callback fun(ev: FacilityCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.Created.Add(callback) end
---@param callbackOrToken fun(ev: FacilityCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.Created.Remove(callbackOrToken) end
--- Fires when a CCTV camera is destroyed.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.Removed = {}
---@param callback fun(ev: FacilityCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.Removed.Add(callback) end
---@param callbackOrToken fun(ev: FacilityCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.Scp079Switching = {}
---@param callback fun(ev: Scp079SwitchingCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.Scp079Switching.Add(callback) end
---@param callbackOrToken fun(ev: Scp079SwitchingCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.Scp079Switching.Remove(callbackOrToken) end
--- Fires when any CCTV camera's active/inactive state changes (e.g. SCP-079 switches the active camera). Use to observe the new state.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FacilityCamera.StateChanged = {}
---@param callback fun(ev: FacilityCameraEventArgs|nil)
---@return string subscriptionToken
function Events.FacilityCamera.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: FacilityCameraEventArgs|nil)|string
---@return boolean removed
function Events.FacilityCamera.StateChanged.Remove(callbackOrToken) end
Events.FileTransfer = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FileTransfer.Complete = {}
---@param callback fun(ev: FileTransferCompleteEventArgs|nil)
---@return string subscriptionToken
function Events.FileTransfer.Complete.Add(callback) end
---@param callbackOrToken fun(ev: FileTransferCompleteEventArgs|nil)|string
---@return boolean removed
function Events.FileTransfer.Complete.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.FileTransfer.Progress = {}
---@param callback fun(ev: FileTransferProgressEventArgs|nil)
---@return string subscriptionToken
function Events.FileTransfer.Progress.Add(callback) end
---@param callbackOrToken fun(ev: FileTransferProgressEventArgs|nil)|string
---@return boolean removed
function Events.FileTransfer.Progress.Remove(callbackOrToken) end
Events.Firearm = {}
--- Local equipped ADS state changed in an observed snapshot. Remote ADS-only updates are not tracked.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.AimingChanged = {}
---@param callback fun(ev: FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.AimingChanged.Add(callback) end
---@param callbackOrToken fun(ev: FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.AimingChanged.Remove(callbackOrToken) end
--- Magazine ammo, capacity or insertion state changed in an observed snapshot.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.AmmoChanged = {}
---@param callback fun(ev: FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.AmmoChanged.Add(callback) end
---@param callbackOrToken fun(ev: FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.AmmoChanged.Remove(callbackOrToken) end
--- Local equipped reload/unload prediction changed in an observed snapshot.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.ReloadStateChanged = {}
---@param callback fun(ev: FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.ReloadStateChanged.Add(callback) end
---@param callbackOrToken fun(ev: FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.ReloadStateChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.Shooting = {}
---@param callback fun(ev: PlayerShootingEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.Shooting.Add(callback) end
---@param callbackOrToken fun(ev: PlayerShootingEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.Shooting.Remove(callbackOrToken) end
--- Fires after a shot resolves. Observer-only.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.Shot = {}
---@param callback fun(ev: FirearmShotEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.Shot.Add(callback) end
---@param callbackOrToken fun(ev: FirearmShotEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.Shot.Remove(callbackOrToken) end
--- Observer snapshot after magazine sync or a local equipped-firearm update. Includes first observation; intermediate changes between local updates can be coalesced. This is not proof that the server accepted a reload or input action.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Firearm.StateChanged = {}
---@param callback fun(ev: FirearmStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Firearm.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: FirearmStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Firearm.StateChanged.Remove(callbackOrToken) end
Events.Generator = {}
--- Fires once per generator the moment it transitions to Engaged.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Generator.Engaged = {}
---@param callback fun(ev: GeneratorEngagedEventArgs|nil)
---@return string subscriptionToken
function Events.Generator.Engaged.Add(callback) end
---@param callbackOrToken fun(ev: GeneratorEngagedEventArgs|nil)|string
---@return boolean removed
function Events.Generator.Engaged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Generator.StateChanged = {}
---@param callback fun(ev: GeneratorStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Generator.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: GeneratorStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Generator.StateChanged.Remove(callbackOrToken) end
Events.Hazard = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hazard.Added = {}
---@param callback fun(ev: HazardEventArgs|nil)
---@return string subscriptionToken
function Events.Hazard.Added.Add(callback) end
---@param callbackOrToken fun(ev: HazardEventArgs|nil)|string
---@return boolean removed
function Events.Hazard.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hazard.Removed = {}
---@param callback fun(ev: HazardEventArgs|nil)
---@return string subscriptionToken
function Events.Hazard.Removed.Add(callback) end
---@param callbackOrToken fun(ev: HazardEventArgs|nil)|string
---@return boolean removed
function Events.Hazard.Removed.Remove(callbackOrToken) end
Events.Hud = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hud.BroadcastShowing = {}
---@param callback fun(ev: BroadcastShowingEventArgs|nil)
---@return string subscriptionToken
function Events.Hud.BroadcastShowing.Add(callback) end
---@param callbackOrToken fun(ev: BroadcastShowingEventArgs|nil)|string
---@return boolean removed
function Events.Hud.BroadcastShowing.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Hud.HintDisplaying = {}
---@param callback fun(ev: HintDisplayingEventArgs|nil)
---@return string subscriptionToken
function Events.Hud.HintDisplaying.Add(callback) end
---@param callbackOrToken fun(ev: HintDisplayingEventArgs|nil)|string
---@return boolean removed
function Events.Hud.HintDisplaying.Remove(callbackOrToken) end
Events.InputRegistry = {}
--- Fires when a binding transitions from not-pressed to pressed. Dispatched by the internal .
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
---@param callback fun(ev: CurrentItemChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.CurrentItemChanged.Add(callback) end
---@param callbackOrToken fun(ev: CurrentItemChangedEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.CurrentItemChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.CurrentItemChanging = {}
---@param callback fun(ev: CurrentItemChangingEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.CurrentItemChanging.Add(callback) end
---@param callbackOrToken fun(ev: CurrentItemChangingEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.CurrentItemChanging.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.DroppingItem = {}
---@param callback fun(ev: PlayerDroppingItemEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.DroppingItem.Add(callback) end
---@param callbackOrToken fun(ev: PlayerDroppingItemEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.DroppingItem.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.ItemsChanged = {}
---@param callback fun(ev: InventoryChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.ItemsChanged.Add(callback) end
---@param callbackOrToken fun(ev: InventoryChangedEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.ItemsChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.UiClosing = {}
---@param callback fun(ev: InventoryUiClosingEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.UiClosing.Add(callback) end
---@param callbackOrToken fun(ev: InventoryUiClosingEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.UiClosing.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Inventory.UiOpening = {}
---@param callback fun(ev: InventoryUiOpeningEventArgs|nil)
---@return string subscriptionToken
function Events.Inventory.UiOpening.Add(callback) end
---@param callbackOrToken fun(ev: InventoryUiOpeningEventArgs|nil)|string
---@return boolean removed
function Events.Inventory.UiOpening.Remove(callbackOrToken) end
Events.LocalPlayerMotion = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.LocalPlayerMotion.Applied = {}
---@param callback fun(obj: MotionDiscontinuity|nil)
---@return string subscriptionToken
function Events.LocalPlayerMotion.Applied.Add(callback) end
---@param callbackOrToken fun(obj: MotionDiscontinuity|nil)|string
---@return boolean removed
function Events.LocalPlayerMotion.Applied.Remove(callbackOrToken) end
Events.Locker = {}
--- Fires for changed replicated chamber masks after the initial spawn snapshot.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Locker.StateChanged = {}
---@param callback fun(ev: LockerStateChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Locker.StateChanged.Add(callback) end
---@param callbackOrToken fun(ev: LockerStateChangedEventArgs|nil)|string
---@return boolean removed
function Events.Locker.StateChanged.Remove(callbackOrToken) end
Events.NetworkMessage = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.NetworkMessage.AnomalyMessageReceived = {}
---@param callback fun(ev: AnomalyMessageReceivedEventArgs|nil)
---@return string subscriptionToken
function Events.NetworkMessage.AnomalyMessageReceived.Add(callback) end
---@param callbackOrToken fun(ev: AnomalyMessageReceivedEventArgs|nil)|string
---@return boolean removed
function Events.NetworkMessage.AnomalyMessageReceived.Remove(callbackOrToken) end
Events.Pickup = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Pickup.Added = {}
---@param callback fun(ev: PickupAddedEventArgs|nil)
---@return string subscriptionToken
function Events.Pickup.Added.Add(callback) end
---@param callbackOrToken fun(ev: PickupAddedEventArgs|nil)|string
---@return boolean removed
function Events.Pickup.Added.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Pickup.Removed = {}
---@param callback fun(ev: PickupRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.Pickup.Removed.Add(callback) end
---@param callbackOrToken fun(ev: PickupRemovedEventArgs|nil)|string
---@return boolean removed
function Events.Pickup.Removed.Remove(callbackOrToken) end
Events.Player = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Added = {}
---@param callback fun(ev: PlayerAddedEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Added.Add(callback) end
---@param callbackOrToken fun(ev: PlayerAddedEventArgs|nil)|string
---@return boolean removed
function Events.Player.Added.Remove(callbackOrToken) end
--- A resolved local interaction; carries typed targets but does not imply server acceptance.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Interacted = {}
---@param callback fun(ev: PlayerInteractedEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Interacted.Add(callback) end
---@param callbackOrToken fun(ev: PlayerInteractedEventArgs|nil)|string
---@return boolean removed
function Events.Player.Interacted.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Interacting = {}
---@param callback fun(ev: PlayerInteractingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Interacting.Add(callback) end
---@param callbackOrToken fun(ev: PlayerInteractingEventArgs|nil)|string
---@return boolean removed
function Events.Player.Interacting.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Jumping = {}
---@param callback fun(ev: PlayerJumpingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Jumping.Add(callback) end
---@param callbackOrToken fun(ev: PlayerJumpingEventArgs|nil)|string
---@return boolean removed
function Events.Player.Jumping.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.NoclipToggling = {}
---@param callback fun(ev: PlayerNoclipTogglingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.NoclipToggling.Add(callback) end
---@param callbackOrToken fun(ev: PlayerNoclipTogglingEventArgs|nil)|string
---@return boolean removed
function Events.Player.NoclipToggling.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Removed = {}
---@param callback fun(ev: PlayerRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Removed.Add(callback) end
---@param callbackOrToken fun(ev: PlayerRemovedEventArgs|nil)|string
---@return boolean removed
function Events.Player.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Player.Sprinting = {}
---@param callback fun(ev: PlayerSprintingEventArgs|nil)
---@return string subscriptionToken
function Events.Player.Sprinting.Add(callback) end
---@param callbackOrToken fun(ev: PlayerSprintingEventArgs|nil)|string
---@return boolean removed
function Events.Player.Sprinting.Remove(callbackOrToken) end
Events.Projectile = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Projectile.Removed = {}
---@param callback fun(ev: ProjectileRemovedEventArgs|nil)
---@return string subscriptionToken
function Events.Projectile.Removed.Add(callback) end
---@param callbackOrToken fun(ev: ProjectileRemovedEventArgs|nil)|string
---@return boolean removed
function Events.Projectile.Removed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Projectile.Spawned = {}
---@param callback fun(ev: ProjectileSpawnedEventArgs|nil)
---@return string subscriptionToken
function Events.Projectile.Spawned.Add(callback) end
---@param callbackOrToken fun(ev: ProjectileSpawnedEventArgs|nil)|string
---@return boolean removed
function Events.Projectile.Spawned.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Projectile.Throwing = {}
---@param callback fun(ev: PlayerThrowingEventArgs|nil)
---@return string subscriptionToken
function Events.Projectile.Throwing.Add(callback) end
---@param callbackOrToken fun(ev: PlayerThrowingEventArgs|nil)|string
---@return boolean removed
function Events.Projectile.Throwing.Remove(callbackOrToken) end
Events.Ragdoll = {}
--- Fires when a ragdoll is removed from the scene (cleanup, round restart).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Ragdoll.Removed = {}
---@param callback fun(ev: RagdollEventArgs|nil)
---@return string subscriptionToken
function Events.Ragdoll.Removed.Add(callback) end
---@param callbackOrToken fun(ev: RagdollEventArgs|nil)|string
---@return boolean removed
function Events.Ragdoll.Removed.Remove(callbackOrToken) end
--- Fires when a new ragdoll spawns into the scene (someone died).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Ragdoll.Spawned = {}
---@param callback fun(ev: RagdollEventArgs|nil)
---@return string subscriptionToken
function Events.Ragdoll.Spawned.Add(callback) end
---@param callbackOrToken fun(ev: RagdollEventArgs|nil)|string
---@return boolean removed
function Events.Ragdoll.Spawned.Remove(callbackOrToken) end
Events.Role = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Role.Changed = {}
---@param callback fun(ev: PlayerRoleChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Role.Changed.Add(callback) end
---@param callbackOrToken fun(ev: PlayerRoleChangedEventArgs|nil)|string
---@return boolean removed
function Events.Role.Changed.Remove(callbackOrToken) end
Events.Round = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Ended = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Ended.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Ended.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Ending = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Ending.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Ending.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.PhaseChanged = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.PhaseChanged.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.PhaseChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Restarted = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Restarted.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Restarted.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Started = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Started.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Started.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.Starting = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.Starting.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.Starting.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Round.WaitingForPlayers = {}
---@param callback fun(ev: RoundPhaseChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Round.WaitingForPlayers.Add(callback) end
---@param callbackOrToken fun(ev: RoundPhaseChangedEventArgs|nil)|string
---@return boolean removed
function Events.Round.WaitingForPlayers.Remove(callbackOrToken) end
Events.Scp079 = {}
--- Fires for the first known or changed power value while the SCP-079 is locally controlled or spectated. This is not a per-frame prediction event.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Scp079.AuxiliaryPowerChanged = {}
---@param callback fun(ev: Scp079AuxiliaryPowerChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Scp079.AuxiliaryPowerChanged.Add(callback) end
---@param callbackOrToken fun(ev: Scp079AuxiliaryPowerChangedEventArgs|nil)|string
---@return boolean removed
function Events.Scp079.AuxiliaryPowerChanged.Remove(callbackOrToken) end
--- Fires for the first known or changed total experience for a role life.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Scp079.ExperienceChanged = {}
---@param callback fun(ev: Scp079ExperienceChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Scp079.ExperienceChanged.Add(callback) end
---@param callbackOrToken fun(ev: Scp079ExperienceChangedEventArgs|nil)|string
---@return boolean removed
function Events.Scp079.ExperienceChanged.Remove(callbackOrToken) end
--- Fires for the first known or changed one-based tier for a role life.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Scp079.TierChanged = {}
---@param callback fun(ev: Scp079TierChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Scp079.TierChanged.Add(callback) end
---@param callbackOrToken fun(ev: Scp079TierChangedEventArgs|nil)|string
---@return boolean removed
function Events.Scp079.TierChanged.Remove(callbackOrToken) end
Events.Spectator = {}
--- Fires after the stock tracker changes its player target, including when the target is cleared. This does not observe remote spectators.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Spectator.TargetChanged = {}
---@param callback fun(ev: SpectatedPlayerChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Spectator.TargetChanged.Add(callback) end
---@param callbackOrToken fun(ev: SpectatedPlayerChangedEventArgs|nil)|string
---@return boolean removed
function Events.Spectator.TargetChanged.Remove(callbackOrToken) end
Events.Stats = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.ArtificialHealthChanged = {}
---@param callback fun(ev: PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.ArtificialHealthChanged.Add(callback) end
---@param callbackOrToken fun(ev: PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.ArtificialHealthChanged.Remove(callbackOrToken) end
--- Observed value or maximum changes. First samples and visibility changes rebase silently.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.Changed = {}
---@param callback fun(ev: PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.Changed.Add(callback) end
---@param callbackOrToken fun(ev: PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.Changed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Stats.Healed = {}
---@param callback fun(ev: PlayerHealedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.Healed.Add(callback) end
---@param callbackOrToken fun(ev: PlayerHealedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.Healed.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.HealthChanged = {}
---@param callback fun(ev: PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.HealthChanged.Add(callback) end
---@param callbackOrToken fun(ev: PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.HealthChanged.Remove(callbackOrToken) end
--- Fires when a player's Hume shield value changes. Use the convenience flag to detect the shield-broken transition specifically.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.HumeShieldChanged = {}
---@param callback fun(ev: PlayerHumeShieldChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.HumeShieldChanged.Add(callback) end
---@param callbackOrToken fun(ev: PlayerHumeShieldChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.HumeShieldChanged.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.MaximumChanged = {}
---@param callback fun(ev: PlayerStatChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.MaximumChanged.Add(callback) end
---@param callbackOrToken fun(ev: PlayerStatChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.MaximumChanged.Remove(callbackOrToken) end
--- Fires when a player's stamina value changes. Driven by the polling tracker in the loader, so deltas reflect the per-tick change rather than the underlying continuous stamina drain.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Stats.StaminaChanged = {}
---@param callback fun(ev: PlayerStaminaChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Stats.StaminaChanged.Add(callback) end
---@param callbackOrToken fun(ev: PlayerStaminaChangedEventArgs|nil)|string
---@return boolean removed
function Events.Stats.StaminaChanged.Remove(callbackOrToken) end
Events.Tesla = {}
--- Fires when a tesla gate spawns into the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Tesla.Added = {}
---@param callback fun(ev: TeslaEventArgs|nil)
---@return string subscriptionToken
function Events.Tesla.Added.Add(callback) end
---@param callbackOrToken fun(ev: TeslaEventArgs|nil)|string
---@return boolean removed
function Events.Tesla.Added.Remove(callbackOrToken) end
--- Fires when a tesla gate completes its shock burst.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Tesla.Bursted = {}
---@param callback fun(ev: TeslaEventArgs|nil)
---@return string subscriptionToken
function Events.Tesla.Bursted.Add(callback) end
---@param callbackOrToken fun(ev: TeslaEventArgs|nil)|string
---@return boolean removed
function Events.Tesla.Bursted.Remove(callbackOrToken) end
--- Fires when a tesla gate is removed from the scene.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Tesla.Removed = {}
---@param callback fun(ev: TeslaEventArgs|nil)
---@return string subscriptionToken
function Events.Tesla.Removed.Add(callback) end
---@param callbackOrToken fun(ev: TeslaEventArgs|nil)|string
---@return boolean removed
function Events.Tesla.Removed.Remove(callbackOrToken) end
Events.UsableItem = {}
--- An explicit cancel notification, not normal completion or removal.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.UsableItem.Cancelled = {}
---@param callback fun(ev: UsableItemStatusEventArgs|nil)
---@return string subscriptionToken
function Events.UsableItem.Cancelled.Add(callback) end
---@param callbackOrToken fun(ev: UsableItemStatusEventArgs|nil)|string
---@return boolean removed
function Events.UsableItem.Cancelled.Remove(callbackOrToken) end
--- A received cooldown applied to a usable item in the local inventory.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.UsableItem.CooldownChanged = {}
---@param callback fun(ev: UsableItemCooldownEventArgs|nil)
---@return string subscriptionToken
function Events.UsableItem.CooldownChanged.Add(callback) end
---@param callbackOrToken fun(ev: UsableItemCooldownEventArgs|nil)|string
---@return boolean removed
function Events.UsableItem.CooldownChanged.Remove(callbackOrToken) end
--- A start notification received for a serial; the item/owner can be unavailable locally.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.UsableItem.Started = {}
---@param callback fun(ev: UsableItemStatusEventArgs|nil)
---@return string subscriptionToken
function Events.UsableItem.Started.Add(callback) end
---@param callbackOrToken fun(ev: UsableItemStatusEventArgs|nil)|string
---@return boolean removed
function Events.UsableItem.Started.Remove(callbackOrToken) end
Events.Voice = {}
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Received = {}
---@param callback fun(ev: VoiceMessageReceivedEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Received.Add(callback) end
---@param callbackOrToken fun(ev: VoiceMessageReceivedEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Received.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Receiving = {}
---@param callback fun(ev: VoiceMessageReceivingEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Receiving.Add(callback) end
---@param callbackOrToken fun(ev: VoiceMessageReceivingEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Receiving.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Sending = {}
---@param callback fun(ev: VoiceMessageSendingEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Sending.Add(callback) end
---@param callbackOrToken fun(ev: VoiceMessageSendingEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Sending.Remove(callbackOrToken) end
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Latest pending observation per registration; intervening observations may be coalesced.
Events.Voice.Sent = {}
---@param callback fun(ev: VoiceMessageSentEventArgs|nil)
---@return string subscriptionToken
function Events.Voice.Sent.Add(callback) end
---@param callbackOrToken fun(ev: VoiceMessageSentEventArgs|nil)|string
---@return boolean removed
function Events.Voice.Sent.Remove(callbackOrToken) end
Events.Warhead = {}
--- Fires once when the warhead actually detonates.
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Warhead.Detonated = {}
---@param callback fun(ev: WarheadDetonatedEventArgs|nil)
---@return string subscriptionToken
function Events.Warhead.Detonated.Add(callback) end
---@param callbackOrToken fun(ev: WarheadDetonatedEventArgs|nil)|string
---@return boolean removed
function Events.Warhead.Detonated.Remove(callbackOrToken) end
--- Fires when the countdown starts or stops (cancellation / detonation).
--- Queued read-only observation; Lua callbacks cannot cancel or modify the originating operation.
--- Ordered queued delivery; callbacks must remain within the documented traffic limits.
Events.Warhead.ProgressChanged = {}
---@param callback fun(ev: WarheadProgressChangedEventArgs|nil)
---@return string subscriptionToken
function Events.Warhead.ProgressChanged.Add(callback) end
---@param callbackOrToken fun(ev: WarheadProgressChangedEventArgs|nil)|string
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
