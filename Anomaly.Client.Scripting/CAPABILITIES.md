# Script lifecycle and mutation options

## What is available now

The script surface exposes only events marked `[ScriptEvent]` and selected
`[ScriptVisible]` members on `[ScriptType]` gameplay objects. Each approved concrete
subclass is annotated explicitly; no public-member or automatic subclass allowlist
remains. `Game` resolves current players, equipped items,
rooms, detached positions, observable health/shield/stamina, firearm observations,
spectating and round state. The annotations and generated JSON/LuaLS/baseline files are
the contract; public C# members are **not** automatically callable Lua capabilities.

Event values are frozen notifications. `IsAllowed` is the value at publication,
**not** a writable cancellation control. Snapshot objects cannot be passed back as
live host arguments; resolve a current object by ID for subsequent local queries.
Unknown subclasses, Unity handles, transforms, renderers, animators, reflection,
voice buffers and untyped network payloads are not exposed.

Scripts initialize before public ClientReady, which can still precede the local hub.
They observe only future events. Initial CanvasReady/download/Connected notifications
may already have happened. Disconnect, offer replacement, host stop, VM retirement
and shutdown invalidate queued work and dispose host resources without Lua cleanup.
The disconnected event is registered for inventory completeness, not guaranteed as
an unload callback. Do not delay revocation just to deliver one.

**No direct game mutation binding is enabled by this change.** Pack-bound
`Messages.Send` can request a server-approved action, but the declared message schema
is not authorization: the LabAPI plugin must validate sender, state, arguments and
rate before performing it. Remote execution remains disabled pending containment
qualification; local verified-host/development paths retain their existing gates.

## Existing implementation and recommended bindings

| Goal | Existing foundation | Safe script-facing approach |
|---|---|---|
| Local facial emotion | `Api/Features/Player.cs` resolves `EmotionSubcontroller`; the configured client export declares `SetPreset(EmotionPresetType)`, `SetWeight(EmotionBlendshape,float)`, `ResetWeights()`. | Local-model-only methods such as `SetEmotion(preset)` and a bounded blendshape weight. Validate enum/value, bind to current player life/model, restore/reset on release, and re-resolve on role/model pooling. No renderer arrays or raw subcontroller. |
| Local character animation / poses | `Api/Features/CharacterModels.cs` provides `RegisterLocalHook`, before/after animator timing, layer lookup, look-at suppression and secondary-rig synchronization. `PlayerPoseRigs` is the existing pose subsystem. | Expose named, preapproved local actions/pose handles with bounded parameters and lifetime. Keep per-frame interpolation and engine callbacks in trusted C#, not queued Lua callbacks. Separate cosmetic poses from server-authorized combat/hitbox pose streams. |
| Texture replacement on spawned objects | `Client/Assets/OverrideRegistry.cs` and `OverrideApplier.cs` process server texture overrides by netId/componentPath/materialIndex, validate active remote assets, retry/reapply, and restore originals before unload. | Prefer existing server APIs/messages now. A future local override lease should accept only activated, pack-owned typed asset addresses and approved target slots, retain asset ownership, cap instances/materials and restore on disposal. Do not grant arbitrary hierarchy traversal or material/shader setters. |
| Sprites, textures, audio and static meshes from the server | `Api/Assets/AnomalyResources.cs` resolves typed catalog addresses. `Shared/Assets/AssetKind.cs` admits Texture2D, Sprite, Mesh, AudioClip and bounded DecorativeVisual descriptions. | Opaque resource references scoped to the exact admitted pack/offer; resolve through existing validation, never paths/URLs or arbitrary Unity loading. A resolver alone is not a replacement hook. |
| Stock CASSIE audio replacement | `Client/Assets/PathOverrideRegistry.cs` and `Patches/Audio/CassieLineSourceSchedulePatch.cs` replace supported `game/cassie/...` entries while checking stock clip timing. | Use existing catalog replacements. A script audio facade can later play an admitted address with bounded volume/duration/concurrency; it must not rewrite arbitrary stock timing. General pooled-audio replacement is not already provided by the CASSIE hook. |
| Extra decorative visuals | `Client/Assets/DecorativeVisualClient.cs` constructs only validated Transform/MeshFilter/MeshRenderer hierarchies with scene/triangle bounds, managed by server snapshot/update messages. | Continue through server-authoritative decorative messages, or design a separate capped, disposable local cosmetic handle. Not arbitrary downloaded prefabs/components. |
| Prevent local interactions | `Patches/Runtime/InteractionCoordinatorPatch.cs` raises Interacting synchronously before raycast/local action/RPC; other hooks cover shooting, throwing, dropping, item switching, sprint/jump/noclip, inventory UI, camera switching, HUD/audio effects and voice. | Add host-evaluated declarative rules/leases set **before** input, e.g. block interaction or item switching while a script-owned modal is active. C# checks the current rules synchronously in the existing hooks. Queued event callbacks cannot veto the input that produced them. |
| Hints, broadcasts and simple screen effects | `Api/Features/LocalHint.cs`, `LocalBroadcast.cs`, `ScreenEffects.cs`, `Hud.cs`, `Subtitles.cs`. | Bounded text/duration/colour methods with per-session quotas and ownership. Existing shared hint/broadcast/overlay helpers need arbitration: retiring one script must not clear another owner's display. Prevent recursive hint/event loops. |
| New GUI elements | `Api/UI/UiCanvas.cs` supplies ordered layers; `UiAnchor`, `SafeArea` and `UiParameterBinder` provide layout helpers. | A trusted factory for text/image/panel/button handles, not GameObject/RectTransform access. Limit nodes, text, texture pixels, geometry, draw/raycast/focus cost and update rate. Queue bounded click notifications. Use approved sprites, escape/limit rich text, scope input capture to a modal lease, and destroy all owned nodes on retirement. |

The emotion export establishes signatures only: several method bodies are failed
AssetRipper reconstructions. Actual preset rendering, animator ordering, pooling and
restoration require live client tests. Local emotion/animation changes are not a
promise of replication to other players or server acceptance.

## Boundaries that must remain in place

- The asset profile does not admit AnimationClip, AnimatorController, arbitrary
  MonoBehaviour/prefab trees, executable assemblies or custom shaders. Downloaded
  animations/controllers would require a separately designed validator/format and
  asset lifetime contract; do not bypass the existing bundle checks to add them.
- `UiPrefabValidator` is a convenience check for plugin UI, not a complete untrusted
  prefab safety boundary. Build remote script UI from trusted components/data instead.
- Client-side suppression is UX/prediction, not anticheat or gameplay authority.
  Server plugins must enforce access, damage, movement and inventory decisions.
- Every mutation must carry session/pack/offer ownership and a host-owned disposable
  cleanup path. Role pooling and object despawn need narrower life identities too.
  Use the existing session resource tracking and reverse-order disposal; never rely
  on a Lua callback to restore state. Handle partial application and cleanup failures.
- Bound CLR/native work and reserve resources before allocation. VM instruction,
  wall-time and memory budgets do not interrupt Unity calls or account every native
  allocation. High-rate events (audio, voice, stats) still consume the shared 32
  subscriptions / 64 queued deliveries and can retire an overloaded script.

Recommended implementation order: owned text/image UI and simple hints; declarative
interaction/modal leases; local emotion presets; admitted resource/cosmetic leases;
then explicitly reviewed animation/pose actions. Keep each capability additive and
test revocation, partial failures and conflicting owners before exposing it to Lua.
