# Replicated player poses

`Anomaly.Server.Api.Features.PlayerPoseOverrides` publishes cosmetic player bone
poses to compatible base Anomaly clients. Viewers do not need the producing mod.
Unmodified SCP:SL clients retain normal animation. This does not replicate VR
tracking input or give clients movement, hitbox, or combat authority.
A server plugin can separately authorize the narrowly scoped client acquisition
bridge through [PlayerCombatPoseOverrides](COMBAT_POSES.md).

## Ownership and updates

Create one scope for each independent plugin owner with
`PlayerPoseOverrides.CreateScope("your.plugin")`. On the game thread, call
`scope.TryCreate(player, initialFrame, options, out handle, out failure)`.
Call `handle.TryUpdate(frame, out failure)` whenever the contribution changes.
Dispose the scope in the LabAPI plugin's `Disable()` method. Two scopes with the
same namespace remain independent owners.

`PlayerPoseOptions` specifies `Priority`, `Weight` (0â€“1), `InactivityTimeout`
(500 ms by default), and `IncludeLocalPlayer` (false by default). Use
`Timeout.InfiniteTimeSpan` only for deliberately persistent poses. Capture options
are copied at creation. Each frame replaces the entire contribution; omitted
bones release ownership. Zero effective weight also releases a bone.

Highest priority wins per whole bone; ties use the earliest-created handle.
The winner's weight is multiplied by its per-bone weight and blends against
native animation. Lower priority contributions are retained but are not part of
the blend. Clearing a higher-priority contribution reveals the next owner.
Position-only and rotation-only contributions leave the other component under
native animation/hierarchy behavior.

`TryGetState` returns a detached authored frame. `ResetInterpolation` restarts
presentation after a tracking-space recenter without moving the gameplay root.
`Clear`, `Dispose`, inactivity timeout, model replacement, disconnect and round
reset permanently invalidate a handle. Create a new handle to resume. Scope
disposal releases only its own handles; all these operations require the game
thread.

## Rig and coordinate contract

Use `PlayerPoseRigs.TryGet(player, out rig)` and `rig.Bones` to discover stable
semantic slots. `PoseRigCatalog` in `Anomaly.Shared.Poses` contains schema v1 for
11 stock SCP:SL 14.2.7 families: ClassD, Scientist, Guard/Tutorial, MTF, Chaos,
SCP-049, SCP-049-2, SCP-096, SCP-106, SCP-3114 and SCP-939. Unsupported rigs fail
closed. The descriptor preserves exact authored paths or human-avatar mappings;
IDs are not hierarchy traversal indices. Each descriptor has at most 128 slots.

`PoseFrame(rig.Id, rig.Version, bones)` is immutable. Each `PoseBone` identifies
its slot and `PoseComponents.Position`, `Rotation`, or both. Position is in the
current model root's local coordinates, including its authored scale. Quaternion
rotation is relative to that root, normalized on construction. These are final
bone transforms, not controller poses, animator IK targets, additive rotations,
or movement coordinates. No root or bone scale override is supported.

Capability 1.1 retains millimeter quantization for bones inside +/-32.767 model
units and uses the explicit per-bone flag 8 for centimeter quantization beyond
that range. Position data still occupies six bytes; API coordinates remain in
model units. Old 1.0 decoders are excluded by recipient capability checks.

Positions must be finite and within Â±327.67 model units; weights must be 0â€“1 and
quaternions must be finite and nonzero. Duplicate and unknown bone IDs are
rejected. Wire positions use millimeter steps, quaternions use four normalized
signed 16-bit components, and weights use bytes. Public frames retain full
precision until encoding.

Static authored-transform validation for the pinned recovered 14.2.7 assets
resolved nested prefab instances, stripped transform aliases and each avatar's
primary skeleton. The largest absolute model-space coordinate for a mapped slot
was 64.613862, on SCP-049's head. All eleven authored rigs fit the expanded bound:

| Rig | Maximum absolute model coordinate | Slot |
| --- | ---: | --- |
| Class-D | 1.520893 | Head (5) |
| Scientist | 1.520890 | Head (5) |
| Guard | 1.520771 | Head (5) |
| MTF | 1.527491 | Head (5) |
| Chaos | 1.520890 | Head (5) |
| SCP-049 | 64.613862 | Head (5) |
| SCP-049-2 | 1.589233 | Head (5) |
| SCP-106 | 7.413296 | Head (5) |
| SCP-3114 | 1.605431 | Head (5) |
| SCP-096 | 2.231497 | Head (5) |
| SCP-939 | 2.562775 | Neck (4) |

These are authored hierarchy coordinates, not live animation/headset acceptance
or an authorization to move any gameplay root. The finite bound still applies
to all producer frames; out-of-range data is rejected rather than clamped.

On clients, `Anomaly.Client.Api.Features.PlayerPoseRigs.TryCapture` captures all
or selected stable bone IDs after the producer's body solver. The producer owns
uploading and validating those frames; there is no built-in client-to-server pose
authority. A server should bind uploaded poses to the authenticated sender and
validate its current role, permitted behavior, update rate and tracking state.

## Delivery and rendering

Only clients advertising `anomaly:player-pose-renderer` v1.1 receive pose traffic.
Reliable model bindings accompany the actual native role deliveries. This covers
pooled same-role replacements, delayed spawning, late joining, visibility changes
and recipient-specific roles. Poses for hidden or mismatching models are
suppressed; private role information is not disclosed by the pose channel.

Reliable controls establish generations and clear tombstones. Unreliable samples
never create streams. Samples are complete frames split into bounded chunks;
incomplete assemblies expire after 250 ms. Authentication already binds traffic
to a connection; additional round/model/binding/generation checks prevent stale
samples from applying to a replacement model. Existing authentication protocol
v8 is unchanged by these optional capability-gated messages.

The server targets 20 Hz, coalesces updates, and shares 512 packets/192 KiB per
second per recipient across cosmetic/combat poses and their controls. The cosmetic
sample lane is paced at 116 KiB/s; the combat lane has its own reservation. See
[combat traffic and crowd qualification](COMBAT_POSES.md). Controls take priority;
snapshots are paced. Congestion lowers sample frequency, rather than growing an
unbounded queue or disconnecting viewers. Persistent poses receive freshness
updates while their source remains active.

The renderer interpolates with a 100 ms buffer against the stock displayed root;
it does not replace stock movement synchronization or promise an identical root
timeline. It does not extrapolate. After 500 ms without a fresh sample it fades
back to animation over 150 ms. Teleports and recenter reset interpolation.

Presentation follows native late animation, look-at and item IK. A separate visual
skeleton preserves original bones/colliders; secondary rigs, armor and rigid
held-item visuals follow the rendered pose. Presentation changes are restored
before gameplay updates, pooling and shutdown. Native visibility and item
ownership remain authoritative. Local-player rendering is explicit opt-in and
can supersede locally authored visual bones; VR producers should retain the
default remote-only setting.

The game strips the managed LOD editing APIs. Rigid mesh copies therefore select
their authored highest-detail (`LOD0`) meshes; skinned meshes retain native LOD
selection. This can increase rigid-mesh rendering cost at a distance. Native
particle systems, lights and gameplay item effects retain their stock transforms;
the copied presentation covers mesh geometry and does not clone item scripts.

See [HelloPlayerPoses](../samples/HelloPlayerPoses/README.md) for a buildable LabAPI
example with streaming, overlapping priorities, clear and plugin-disable cleanup.

## Qualification

Automated tests cover encoding bounds, detached ownership, priority, timeouts,
traffic limits and receiver ordering. Builds do not qualify native Harmony hook
execution, IL2CPP PlayerLoop order, all stock materials/attachments, or live
network delivery. Before release, exercise all 11 families on a non-SL-VR Anomaly
viewer, including armor, held items, culling, spectator/local views, role pooling,
disguises, teleports, reconnect and round restart. Confirm gameplay roots,
colliders and gameplay-phase raycasts remain unchanged.

The static rig check resolves the compiled catalog's 562 slots across all 11
families against recovered prefab hierarchy paths and human-avatar semantic
mappings. It also audits that the presentation source does not write native bone
transforms and restores skin palettes, bounds and rigid renderer visibility.
Run from the repository root after building Shared:

```powershell
New-Item -ItemType Directory -Path artifacts/pose-validation -Force | Out-Null
Add-Type -Path Anomaly.Shared/bin/Debug/Anomaly.Shared.dll
[Anomaly.Shared.Poses.PoseRigCatalog]::All | ConvertTo-Json -Depth 8 |
    Set-Content artifacts/pose-validation/catalog.json
python tools/pose-validation/validate_rigs.py --catalog artifacts/pose-validation/catalog.json `
    --source E:/Tools/SCPSL-14.2.7-Decomp/work/composed-project
```

This check reads local recovered assets without copying or launching them. Its
output includes source hashes. Source ownership checks and prefab mappings do
not establish runtime skinning, material compatibility, timing or network behavior.
