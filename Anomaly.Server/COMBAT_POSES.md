# Scoped combat poses

`PlayerCombatPoseOverrides` explicitly authorizes native client hitboxes for the
SCP-049 `CanFindTarget` and SCP-173 `TryHitTarget` acquisition raycasts. This is
separate from `PlayerPoseOverrides`: cosmetic ownership never authorizes a combat
query. Both APIs accept the same immutable, validated model-root `PoseFrame`.

```csharp
using Anomaly.Server.Api.Features;

// On the LabAPI game thread, retain this scope until plugin Disable.
var scope = PlayerCombatPoseOverrides.CreateScope("slvr");
if (scope.TryCreate(player, acceptedFrame, new PlayerPoseOptions
{
    InactivityTimeout = TimeSpan.FromMilliseconds(500),
    IncludeLocalPlayer = false,
}, out var handle, out var failure))
{
    handle.TryUpdate(nextAcceptedFrame, out failure);
    handle.ResetInterpolation(); // only after a tracking recenter
    handle.Dispose();
}
scope.Dispose();
```

`PlayerCombatPoseScope.OwnerId`, `TryCreate` and `Dispose` mirror the cosmetic
scope. `PlayerCombatPoseOverride` exposes `IsValid`, `TryUpdate`, `TryGetState`,
`ResetInterpolation`, `Clear` and `Dispose`. Updates replace the complete bone
mask. Options are copied; priorities and blend weights use the existing per-bone
composition rules. Clear, timeout, role/model replacement, disconnect and round
reset permanently invalidate the handle. Owners with the same diagnostic name
remain independent. Use separate scopes and publish the same accepted frame to
both APIs when a mod needs visual and combat representations.

The companion remains responsible for authenticating the producer, validating
tracking input and authorizing each frame. The core bridge neither changes
server colliders nor accepts client damage claims. Server shot/melee validation
and history remain the companion's responsibility. It does not change player
roots, bone scales, collider shapes, dimensions, layers or collision flags.

`PlayerCombatPoseOverrides.IsSupported(viewer)` checks the capability
`anomaly:player-combat-pose` version 1.1. The base client advertises it only after
both required query patches and the native model receipt hooks are installed.
A spectator/viewer needs the updated base Anomaly client, not the producing VR
mod. Other clients retain native acquisition behavior.

Combat state uses its own server-to-client hashes,
`anomaly:combat-pose:control` and `anomaly:combat-pose:sample`. It has independent
owners, leases, model bindings, generations, receiver state and clear tombstones.
The two pose services share each recipient's 512-packet/192-KiB-per-second budget,
including controls. Combat ticks run first; separate sample rates of 52 KiB/s
combat and 116 KiB/s cosmetic prevent starvation. Each has at most 100 ms of
accumulated credit, initialized to 50 ms. The remaining capacity covers a 16-KiB
control reservation and 8 KiB of whole-frame rounding slack. Controls retain a
16-packet reservation. Per-tick output is bounded to 8 combat and 12 cosmetic
packets. Client optional admission and pre-decryption aggregate admission include
the same extra 512-packet/192-KiB allowance; ordinary producer traffic is unchanged.

Combat transmission retains every mapped ancestor of an actual primary native
hitbox and omits branches that cannot affect one. Pruning is based on the current
model and hitbox hierarchy, revalidated after topology changes. Unknown mappings
fall back to the complete frame. This only projects transmitted data; owner
frames, state inspection, component weights and authorization remain intact.
The two rigid stock families retain pelvis slot 0 for the attachment below.
Snapshots, visibility checks, complete-frame chunks, stale sample handling and
late joins follow [the pose contract](PLAYER_POSES.md). Recipient role bindings
are independently tracked for each authority against the actual native receipt.
The 1.1 capability is required for the wide-position flag; 1.0 viewers receive no
pose traffic from this server. Authentication protocol version 8 is unchanged.

For each scoped query, the client resolves only current, server-bound rigs and
interpolates the authorized pose with the same 100-ms buffer as presentation.
Authorization becomes unavailable after 500 ms without a fresh sample; combat
hitboxes do not fade into fallback positions. Native bones are snapshotted before
any parent is written, changed for the synchronous raycast, and restored in
reverse order. `Physics.SyncTransforms` runs before and after acquisition. Nested
queries preserve the outer baseline; postfix and finalizer disposal are
idempotent. Role/pool invalidation restores outstanding mutations before reuse.
For stock SCP-049 and SCP-096, the primary `Hitbox` is a direct child of the model
root rather than a skeleton bone. During the query it follows the authorized
pelvis rigid delta from its authored bind transform. Mesh bind poses supply the
neutral pelvis; `PoseRigidAnchorCatalog` supplies the exact 14.2.7 authored fallback
if mesh data is unavailable. The anchor never uses a current animation snapshot.
This rigid attachment preserves the native collider type, dimensions and scale.
No native pose remains installed between queries. The cosmetic renderer still
uses its separate visual skeleton and never writes native bones.

The catalog covers the existing 11 stock rig families. Unsupported models fail
closed. This bridge intentionally hooks only the two native acquisition methods
above; it does not rewrite firearm claims, Micro HID or other SCP routines.

A production-packetizer fixture offers 53 full cosmetic slots and 23 combat slots
(the largest stock hitbox ancestor union) for 12 simulated seconds. At 40 subjects
and 576-byte payloads it measured at least 2.25 combat / 2.33 cosmetic complete
frames per subject per second, with maximum 450-ms refresh gaps. At 918 bytes the
combat rate was at least 2.50 Hz with 400-ms gaps; cosmetic remained at least
2.33 Hz / 450 ms. Peak rolling traffic was 298 packets / 182,172 bytes including
four small controls per second. The 20-subject cases also pass. These figures
assume the fixture's lossless 50-ms scheduling and stock masks; they do not promise
20 Hz per avatar or freshness through loss, stalls or arbitrary larger hitbox rigs.

Automated checks cover separate wire contracts, bounded encoding, authorization
isolation, stale fallback, nested rollback and partial-failure restoration, plus
the existing generation, ownership and packet-budget fixtures. Release still
requires a live non-VR Anomaly viewer attacking accepted VR bodies with 049 and
173, all supported target rigs, obstacles, nested/cancelled queries, death,
role/pool reuse, reconnect and packet loss. Confirm collider dimensions and
out-of-query native transforms remain unchanged. Compilation and managed fixtures
do not establish Harmony/IL2CPP execution or Unity physics synchronization.
