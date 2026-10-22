# Client game API

Use `Anomaly.Client.Api.Features` for wrappers and
`Anomaly.Client.Api.Events.Handlers` for subscriptions. Ordinary MelonLoader
mods remain the primary authoring path. All access to Unity objects and game
wrappers belongs on the game's main thread.

## Familiar LabAPI conventions

Wrappers provide `Base` for advanced native access, `Get`/`TryGet` for lookup,
and `IsDestroyed` on game-object wrappers. Collection properties are snapshots;
keep a wrapper only as long as its underlying object or role life remains valid.
`Item`, `Pickup`, `Door`, `AdminToy`, and `Hazard` resolve native subtypes.

| LabAPI concept | Client API |
| --- | --- |
| `Player.List`, numeric `Get`/`TryGet` | `Player.List`, player-id and network-id overloads; `PlayerList` remains supported |
| Identity and display | `PlayerId`, `Nickname`, `DisplayName` use client-known game fields |
| `Player.Role`, `RoleBase`, `Team` | `RoleType`, `RoleBase`, `Team`; existing `Player.Role` still returns the Anomaly `Role` wrapper |
| Native role access | `Player.GetRole<T>()`, `TryGetRole<T>()`, and `Role.As<T>()` / `TryGet<T>()` |
| Role/voice state | `IsAlive`, `IsHuman`, `IsSCP`, `LifeId`, `VoiceModule`, `VoiceChannel`, `IsSpeaking` |
| Room relationships | `Room.Doors`, `Players`, `Cameras`, `Lights`; `Room.Get` filters by name, zone, or shape |
| Event interfaces | `IPlayerEvent`, `IItemEvent`, `IDoorEvent`, `IHazardEvent` |

The existing namespaces and member types remain available. For example,
`Room.Name` remains a string and `Room.RoomName` supplies the game enum;
`Item.Owner` remains available alongside `CurrentOwner`.

## Inventory and world objects

`UsableItem` exposes local-owner use state, cooldown, and the local use gate.
`ConsumableItem`, `Scp1344Item`, `Scp1576Item`, and `Scp330Item` inherit that
surface. `Scp330Item` now derives from `UsableItem`, which still derives from
`Item`. Configured use durations do not imply that server-side modifiers match.

Other item wrappers include `RadioItem`, `BodyArmorItem`, `LightItem`, and
`MicroHIDItem`. Pickup additions include `AmmoPickup`, `RadioPickup`,
`BodyArmorPickup`, `Scp244Pickup`, and `Scp1576Pickup`. A remote player's full
inventory is not replicated; continue using `CurrentItem` for their held item.

`FirearmItem.GetState()` returns a snapshot with nullable ammo, magazine,
aiming, and reload fields. Null means unavailable or unsupported, not zero or
false. Reload/unload state is local-owner prediction. Magazine notifications
observe received data; local equipped ADS/reload changes are sampled per frame.

Facility wrappers include `Workstation`, `LightsController`, `Window`,
`Intercom`, and `Hazard` with tantrum, sinkhole, amnestic-cloud, and temporary
hazard specializations. These expose local/replicated state. They do not grant
server authority over lights, doors, damage, or intercom speakers. `Window`
exposes broken state rather than unreplicated server health.

Admin-toy specializations include text, interactable, SCP-079 camera, waypoint,
and capybara toys. Thrown projectile specializations include timed grenades,
explosive grenades, flashbangs, SCP-018, and SCP-2176. Fuse deadlines are client
estimates from replicated network time; actual detonation belongs to the server.

## State availability

Stock health, stamina, Hume shield, and AHP synchronization normally reaches
the owner and non-alive viewers. An alive client cannot treat every remote
player's cached stat as current. Raw scalar getters retain their previous
behavior. `PlayerStat.IsObservable` and `PlayerStatsWrapper.TryGetValue<T>` check
the locally known synchronization policy; they do not prove receipt or freshness,
and server plugins can customize the policy.

```csharp
using Anomaly.Client.Api.Features;
using Il2CppPlayerStatsSystem;

static string HealthLabel(Player player)
{
    if (player == null || player.IsDestroyed ||
        !player.Stats.TryGetValue<HealthStat>(out float health))
        return "Health unavailable";
    return $"{player.DisplayName}: {health:0}";
}
```

`Spectating.CurrentPlayer` and `LocalPlayer.SpectatedPlayer` describe this
client's target. `SpectatorEvents.TargetChanged` covers changes that keep the
same Unity camera.

`Player.Scp079` exposes one role life. `AuxiliaryPower`, `Experience`, and `Tier`
remain null until a corresponding RPC is observed. First observations can be
known zero; change-event `Previous` is null in that case. Tier is one-based.
Changing spectator targets invalidates auxiliary-power observations until a
fresh receipt. `MaximumAuxiliaryPower` and `AuxiliaryPowerRegeneration` are
client-computed settings while power is observable, not a promise about
server-only modifiers.

## Events and ordering

Subscribe during mod initialization and unsubscribe during deinitialization.
State is updated before notifications. Events are not replayed to subscribers
that attach later; read the corresponding wrapper for a current snapshot.

| Event family | Meaning |
| --- | --- |
| `DoorEvents` | Observed addition/removal, open-target changes, and lock-mask changes |
| `HazardEvents` | Local hazard component lifecycle |
| `LockerEvents.StateChanged`, `GeneratorEvents.StateChanged` | Replicated state changes; no inferred initiating player |
| `PlayerEvents.Interacting` | Existing cancellable local input hook, before targeting |
| `PlayerEvents.Interacted` | Resolved local target after the local callback, before a possible server command; typed `Door`, `Locker`, `Generator`, and `Workstation` accessors |
| `UsableItemEvents` | Received use-start/cancel signals and owner cooldown updates; no inferred completion |
| `FirearmEvents` | Observed state/ammo/ADS/reload changes, including first availability |
| `StatsEvents.Changed`, `HealthChanged`, `ArtificialHealthChanged`, `MaximumChanged` | Changes between observable local samples; no damage cause or attacker attribution |
| `Scp079Events` | Received auxiliary-power, experience, and tier observations |
| `ClientEvents.EnrollmentChanged` | Server-disclosed enrollment appears, changes, or is cleared |

The stat tracker silently rebases on the first sample, lost visibility, and
player/viewer role-life changes. Its observations can coalesce changes within
a frame. Existing healing/stamina/Hume events remain available.

`ICancellableEvent.IsAllowed` vetoes are retained between subscribers, including
when a handler throws after denying an action. Later subscribers still execute
and see the denied state. Cancellation affects the documented local hook; a
received world-state notification cannot veto an authoritative server change.

`ClientEvents.Ready` remains a scene-ready signal. It does not imply a local
player exists or enrollment has arrived. Check each operation's prerequisites.

## Validation boundary

These surfaces are built against the configured SCP:SL client interop set.
Automated tests cover managed contracts and observation transitions. They do
not qualify Harmony detours, Unity lifecycle ordering, or actual network/game
behavior; those require a separate live client/server pass.
