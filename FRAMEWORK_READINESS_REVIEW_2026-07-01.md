# Anomaly — Framework Readiness Review (Pre-Release)

> **Superseded for release decisions on 2026-08-22.** This file is retained as historical audit evidence. Its findings describe an older codebase and must not be treated as the current public-v1 contract; use `anomaly-docs/RELEASE_REVIEW_2026-08-22.md` for the current classification and release gates.

**Date:** 2026-07-01
**Author:** Claude (Fable 5), synthesized from an 8-agent parallel deep-dive audit + first-hand verification
**Scope:** All four framework projects (`Anomaly.Client`, `Anomaly.Client.Api`, `Anomaly.Server`, `Anomaly.Shared`), the separate `anomaly-launcher` repo (`Anomaly.Launcher` + `Anomaly.InstallerCore`), the `anomaly-docs` Astro site, and the modder-facing distribution story.
**Lens:** Not "is the code correct" (the [2026-06-21 review](PRERELEASE_REVIEW_2026-06-21.md) already covers bug-level correctness). This review asks the release-readiness questions the owner posed: **What is missing for a framework like this? What needs improvement or doesn't work? Is everything a modder needs accessible externally? Can an EXILED/LabAPI-experienced dev pick this up? Is the end-user launcher experience solid?**
**Status:** Review only — **no code changes were made.** Findings are cross-checked against the AssetRipper decomp, LabAPI source, and the live-tree code; every finding cites `file:line`.

> **Relationship to the 2026-06-21 review.** That review's Phase-0 blockers (B1–B7) are now fixed or gated (verified below). Its entire Phase-1 (H1–H13) and Phase-2 (S1–S5/S7–S8) backlog is **still open in code** — none were touched by the commits since. This document does **not** re-litigate those; it focuses on the *framework-readiness* layer (completeness, accessibility, DX, distribution, end-user UX) that the bug review deliberately left out. The prior-review open-issue status is summarized in [Appendix A](#appendix-a--prior-review-fix-status-verified-in-current-tree).

---

## How to read this

Severities are calibrated to **a first public release that freezes the wire protocol and the public API**:

- **BLOCKER** — must be resolved before tagging a public release. Breaks a clean build, leaks secrets, ships a public API that can never work, or has no path for a modder/end-user to actually use the thing.
- **HIGH** — real capability gap or DX failure that will embarrass the launch or generate immediate "this is broken" reports; fix before or immediately after v1.
- **MEDIUM / LOW** — polish and hardening; acceptable as fast-follow if explicitly tracked.
- **CAPABILITY GAP** — not a bug; a thing a framework of this class needs that isn't built yet. Decide whether v1 ships without it or labels it experimental.

Confidence is stated per finding. "Verified directly" means I re-read the code/ran the check during synthesis rather than relying on an agent alone.

---

## Executive verdict

**The engineering is genuinely strong; the framework is not release-shaped yet.** The gap is not code quality — it is the ring of things *around* the code that turn a good engine into a product a stranger can use: **distribution, accessibility of the public surface, documentation truthfulness, and one half-built flagship feature.** None of it requires redesign. Most of it is cheap. But several items are hard release blockers because, as it stands today:

1. **A fresh `git clone` of `master` does not compile.** The entire public `ModSettings` API folder is untracked in git while committed runtime code depends on it. ([BLOCKER-1](#blocker-1))
2. **Private, admin-impersonation-capable identity keypairs sit in the working tree**, unprotected by a 6-line `.gitignore`, one `git add .` away from being published. ([BLOCKER-2](#blocker-2))
3. **The marquee "custom items/roles/effects" framework is a server half with no client half.** Its public API (`Content.GiveItem`/`SetRole`) returns `false` for every player, forever, by construction. ([BLOCKER-3](#blocker-3))
4. **A modder has no supported way to obtain the SDK.** No NuGet, no GitHub releases, no CI, and no shipped XML docs — the only way to get compile-time DLLs is to copy them out of an installed game, and IntelliSense shows nothing. ([BLOCKER-4](#blocker-4))
5. **There is no implemented path that delivers the Anomaly mod itself to end users**, and the docs advertise a "Public Repository" distribution pipeline that is compile-time-disabled and empty. ([BLOCKER-5](#blocker-5), [BLOCKER-6](#blocker-6))

Underneath those, the framework's actual capabilities are real and, in places, better than the incumbents: a genuinely open custom-networking layer (EXILED/LabAPI never offered one), a broad and fully-wired client event surface, a defensively-built launcher/installer with rollback and fail-safe patching, and an unusually complete docs site. The problem is that the parts a stranger touches first — the clone, the SDK download, the first pasted snippet, the "how do I ship this" question — are the parts that are broken or missing.

**What's genuinely good and should be preserved** (details in each area, but worth stating up front so it isn't lost in a findings list):

- **Custom networking is a real differentiator.** `AnomalyMessageRegistry.Register<T>` + `AnomalyMessaging.Send*` give third-party mods an authenticated, replay-protected, collision-checked message channel on both client and server — a paired client-mod/server-plugin story EXILED and LabAPI simply do not have. ([shared-protocol positives](#area-8--wire-protocol--extensibility-anomalyshared))
- **The client event surface is broad and, post-B1/B2, fully alive.** ~63 public events across 24 handler classes, every one with a verified live raiser; voice event coverage exceeds what a client-side EXILED dev expects. ([Area 5](#area-5--client-runtime--event-coverage-anomalyclient))
- **The custom-content architecture, where it exists, is well-designed** — deterministic id allocation that auto-adapts to game updates, load-order-independent freeze, fail-closed grant gating, integrity-hashed catalog. It's a half-built *good* design, not a misdesign. ([Area 1](#area-1--custom-content-framework-new-uncommitted))
- **The launcher/installer is above the bar for the category** — never patches the vanilla copy in place, backs up and rolls back every mutated file, fails *safe* on unknown game builds, production-grade Steam downloader, strong launch-failure diagnostics. ([Area 6](#area-6--end-user-experience-launcher--installer))
- **The docs site and in-code XML docs are more complete than EXILED/LabAPI at 1.0** — the problem is truthfulness, not volume.

---

## The owner's questions, answered directly

**1. What is missing for a framework like this?** (capability gaps, not bugs)
The distribution and lifecycle ring: a **versioned SDK artifact** (NuGet/GitHub release + shipped `.xml` docs), a **cloneable sample/template mod**, a **delivery path for the Anomaly mod trio to end users**, a **mod-distribution channel** (the documented one is disabled), **protocol version negotiation/capability bits** (today every wire change is a flag day), **cross-mod capability discovery** (a server plugin can't tell if a client has its counterpart mod), and an **API-stability/versioning policy** for the moment the public surface freezes. See the [Capability-Gap Matrix](#capability-gap-matrix).

**2. What needs improvement / doesn't work?**
Doesn't work at all today: the custom-content framework end-to-end (no client half), custom roles (never materialized → `None` role), custom-content logic hooks (`OnUsed`/`OnSpawned` never invoked), effects (catalog-only, no apply path), the documented Public Repository mod store (compile-time disabled), the live hotfix channel (points at a `/main/` branch that 404s), and several docs snippets (won't compile). Needs improvement: event coverage vs EXILED, public-surface hygiene (runtime capabilities not reachable from the API; framework internals reachable that shouldn't be), doc truthfulness, and the prior review's untouched H/S backlog.

**3. Is everything a modder needs accessible externally?**
**No.** Three distinct accessibility failures: (a) capabilities that exist only as public types on `Anomaly.Client` — the assembly modders are told never to reference — with no API-side facade (path overrides, spawned-asset queries, the mod's own `@anomaly` identity, and, alarmingly, the session AES key); (b) the SDK itself is not obtainable except by copying DLLs out of a game install; (c) NETWORKING.md's own instructions require editing framework source a third party can't touch. Details in [Area 2](#area-2--public-client-api-surface-anomalyclientapi).

**4. Ease of pickup for EXILED/LabAPI devs?**
Conceptually good — the wrapper facades mirror LabAPI shapes, `IsAllowed` cancellation is familiar, the architecture docs are honest about client-authority limits. But the *first-contact experience* is rough: the first pasted snippet doesn't compile, there's no template to clone, IntelliSense is empty, and the events an EXILED dev reaches for first (damage/death, door/interaction-with-target, item usage) are either host-only, coverage gaps, or targetless. See the [EXILED→Anomaly migration assessment](#exiledlabapi-migration-assessment).

**5. End-user launcher experience?**
Strong core, two trust/hotfix gaps. The install pipeline is defensively excellent, but there's **no code signing** (SmartScreen/AV bounce on a security-sensitive patcher is the #1 first-run drop-off risk), the **live hotfix channel is misconfigured** (`/main/` vs `master` → 404, currently masked by a deprecated fallback), self-update verification is **fail-open**, and MelonLoader is downloaded **unpinned**. Details in [Area 6](#area-6--end-user-experience-launcher--installer).

---

# BLOCKERS

<a name="blocker-1"></a>
## BLOCKER-1 — A clean clone of `master` does not compile (untracked public `ModSettings` API folder)

- **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Client.Api/UI/ModSettings/` (whole folder untracked) — `ModSettings.cs`, `ModSettingMetadata.cs`, `ModSettingCategoryMetadata.cs`, `Internal/ModSettingsRegistry.cs`. Committed consumers: `Anomaly.Client/Patches/Menu/ModSettings/ModSettingsRenderer.cs:7-8`, `Anomaly.Client/Patches/Menu/ModSettings/ModSettingsTabInstaller.cs:7`, and the four `Anomaly.Client/Components/ModSettings/*Binder.cs` files.

**Why this is a blocker.** `git status` shows `?? Anomaly.Client.Api/UI/ModSettings/` — the four files are on disk but not in the index, and `git check-ignore` confirms they are *not* gitignored, just never added. Meanwhile the tracked HEAD `Anomaly.Client.csproj` (which sets `EnableDefaultCompileItems=false`) explicitly `<Compile Include>`s all six consumers, and those consumers `using Anomaly.Client.Api.UI.ModSettings[.Internal]`. `Anomaly.Client.Api` is SDK-style, so on a fresh checkout it compiles *without* the ModSettings types (silently — the public API ships missing its whole settings facade), and then `Anomaly.Client` fails with CS0246. The local machine builds only because the files happen to exist on disk. This is the exact class of footgun the repo's own conventions warn about — the git-tracking twin of the 2026-06-21 review's B2 (which was csproj-omission). `tools/AssetCatalog/Program.cs` and `tools/PathProbe/Program.cs` are similarly untracked.

**Fix.** `git add Anomaly.Client.Api/UI/ModSettings/` (all four files) before any tag. Add a CI job that builds from a *fresh clone* — it catches both this (tracked-code-refs-untracked-file) and the prior review's CC1 (csproj-vs-disk), which a compile-list check alone cannot.

<a name="blocker-2"></a>
## BLOCKER-2 — Private admin-impersonation identity keypairs sit in the working tree, one `git add .` from publication

- **Confidence:** high · *Verified directly* · **SECURITY**
- **Location:** `tools/vanity-results.jsonl` (+ `.bak`, `.bak2`), `tools/README.md`, `.gitignore`

**Why this is a blocker.** `tools/vanity-results.jsonl` contains `identity_base64` blobs — full secp256r1 identity keypairs "compatible with `user.json`" per `tools/README.md` — for **admin-lookalike** vanity IDs (verified: the first records target `admin`, e.g. `ADmiN4Gag13WRB…@anomaly`). Anomaly's `@anomaly` IDs are the basis for remote-admin permissioning, so anyone holding one of these blobs can authenticate **as that identity** on any Anomaly server. The files are currently untracked, but the repo's `.gitignore` is six lines (`bin/ obj/ publish/ /packages/ riderModule.iml /_ReSharper.Caches/`) and excludes **none** of `tools/` output, `.vs/`, `.idea/`, `*.DotSettings.user`, or the `steamdb_patch_manifest_map.csv` scratch file. A single `git add .` before the public push leaks impersonation-capable keys into public history. The tool's README even defaults its output to `--jsonl vanity-results.jsonl` inside the repo.

**Fix.** Delete/move the `vanity-results*` files out of the repo **now**; if any of those identities were ever used, treat them as compromised. Expand `.gitignore` to cover `tools/vanity-results*`, `__pycache__/`, `.vs/`, `.idea/`, `*.DotSettings.user`, and the CSV. Change the tool's default output path to outside the repo. Also decide whether the internal analysis docs sitting untracked in root (`PRERELEASE_REVIEW_2026-06-21.md`, `SCRIPT_LUA_THREAT_MODEL_2026-06-24.md`, `LUA_CLIENT_SCRIPTING_INVESTIGATION_2026-06-24.md`, this file) belong in public history before flipping the repo public.

<a name="blocker-3"></a>
## BLOCKER-3 — The custom-content framework is server-half-only: `Content.GiveItem`/`SetRole` can never succeed

- **Confidence:** high · *Verified directly (agent-confirmed adversarially)*
- **Location:** `Anomaly.Server/Content/ContentCatalogBridge.cs:47`, `Anomaly.Server/Content/ContentGrantGate.cs:36`, `Anomaly.Server/Content/Content.cs:38,60`, `Anomaly.Shared/Content/IContentRegistrar.cs:8`

**Why this is a blocker (for shipping/advertising it, not as a regression).** This is the newest, most-promoted subsystem (uncommitted, ~1,040 lines). The server side is complete and well-built: plugins call `Content.Register(...)`, the catalog freezes deterministically on first `ClientReady`, item templates are cloned into `InventoryItemLoader.AvailableItems`, and the catalog is pushed to each client over the encrypted channel. **But there is zero client-side code.** A repo-wide grep for `ContentCatalog`/`CatalogEntry`/`IContentRegistrar` returns no files under `Anomaly.Client` or `Anomaly.Client.Api`; `IContentRegistrar` has **zero implementations** anywhere; nothing ever constructs or sends `ContentCatalogAckMessage`. The client *does* deserialize the catalog (it's in `RegisterDefaults`) but no subscriber acts on it. Because `ContentGrantGate.CanGrant` requires a recorded matching-hash ack with **no timeout, retry, or fallback**, the gate stays closed forever, so `Content.GiveItem`/`SetRole` return `false` and log a warning **on every call, for every player, permanently.** The public XML docs advertise these as usable ("calls `Register`… then hands it out with `GiveItem`/`SetRole`").

Even treating the client half as known future work, **three additional server-side gaps** mean a plugin author still couldn't ship a working item today — see [Area 1](#area-1--custom-content-framework-new-uncommitted) HIGH findings: custom roles are never materialized (→ `None` role), the `ItemLogicBase`/`RoleLogicBase` hooks are never invoked, and effects have no apply path. The code is honestly marked "pilot"/"RUNTIME-VERIFY" in comments — this is a spike, not a finished feature.

**Fix.** Either land the client half (catalog receiver + IL2CPP `IContentRegistrar` + ack sender + disconnect `Unregister`) in the same change-set, **or keep the entire `Content/*` and `Shared/Content/*` addition out of the release branch.** Do not ship the public `Content` API, `IContentRegistrar`, or `ContentAssetId` in a state where every call fails — a public API that can never return `true` is worse than a smaller surface, and shipping the wire message names (`anomaly:content:catalog`/`catalogack`) freezes protocol you haven't finished designing.

<a name="blocker-4"></a>
## BLOCKER-4 — No supported way for a modder to obtain the SDK; no shipped XML docs; no main-repo CI

- **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Client.Api/Anomaly.Client.Api.csproj` (`IsPackable=false`, no `<GenerateDocumentationFile>`), main repo has **no `.github/` directory** (verified: `NO .github DIR`), `anomaly-docs/.../02-environment-setup.mdx`

**Why this is a blocker.** The only documented way to get compile-time `Anomaly.Client.Api.dll` + `Anomaly.Shared.dll` is to install the modded client, run the game once so MelonLoader generates the Il2Cpp assemblies, then hand-reference the DLLs out of `UserLibs/`. That means a modder **cannot build a mod in CI or without a full game+MelonLoader setup on their machine** — a bar EXILED and LabAPI don't impose (both ship NuGet/reference assemblies or release artifacts). Compounding it: **no `<GenerateDocumentationFile>` exists anywhere in the repo** (verified: `NONE`), so the 577 XML `<summary>` blocks that *do* exist are stripped at compile and never reach IntelliSense or a DocFX-style site — and the API tour explicitly tells modders "hover in your IDE is the fastest reference," which shows nothing. And the main repo has **no CI at all** (no `.github/`), so there's no release workflow producing any artifact.

**Fix (all cheap, high-leverage):**
- Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (and `<NoWarn>CS1591</NoWarn>` until coverage improves) to `Anomaly.Client.Api` **and** `Anomaly.Shared`; ship the `.xml` next to the DLLs in `UserLibs/` and in any SDK artifact.
- Publish a versioned **"Anomaly SDK" GitHub release zip** (Api + Shared + Server DLLs + `.xml`), or NuGet reference packages, so mods build without a game install.
- Add a minimal main-repo CI that at least builds `Anomaly.Client.Api` + `Anomaly.Shared` and fails a fresh-clone build (catches BLOCKER-1's class too).

<a name="blocker-5"></a>
## BLOCKER-5 — No implemented path delivers the Anomaly mod trio to end users in v1

- **Confidence:** medium (a bootstrap could exist outside these two repos) · *Verified the in-repo paths directly*
- **Location:** `anomaly-launcher/Anomaly.Launcher/Services/Loadouts/LoadoutMaterializer.cs:24` (`AnomalyClientTrackingEnabled = false`), `anomaly-launcher/Anomaly.Launcher/Models/Loadout.cs:32` (`TrackAnomalyClient = false`), `Anomaly.InstallerCore/InstallerService.cs` (step 7 only *verifies* Mods/UserLibs)

**Why this is a blocker.** `LoadoutMaterializer` has `private const bool AnomalyClientTrackingEnabled = false` with the comment "Anomaly.Client isn't on the catalog yet, so the resolve attempt always fails." The installer's final step only **verifies** `Mods/`/`UserLibs/` contents and downloads nothing; a grep across the launcher repo finds no path that deploys `Anomaly.Client.dll`/`Anomaly.Client.Api.dll`/`Anomaly.Shared.dll`. So a fresh user ends up with a patched game + MelonLoader — but **how the Anomaly mod itself lands in the mods folder is not implemented in either repo.** The end-user funnel dead-ends after game patching. (Marked medium confidence only because a bootstrap mechanism could live outside the two audited repos; if so, point to it — the in-repo answer is "nothing.")

**Fix.** Decide and implement the v1 core-mod bootstrap: (a) publish `anomaly-client` on the mods platform and flip `AnomalyClientTrackingEnabled`, (b) embed the mod trio in installer resources the way MelonLoader is embedded, or (c) ship a GitHub release zip with documented manual placement — then make player docs match.

<a name="blocker-6"></a>
## BLOCKER-6 — Docs present a mod-distribution "Public Repository" as the preferred path, but it is compile-time disabled and empty

- **Confidence:** high · *Verified directly*
- **Location:** `anomaly-launcher/Anomaly.Launcher/FeatureFlags.cs:14` (`PublicRepoEnabled = false`), `anomaly-docs/.../modding/08-packaging.mdx`, `anomaly-docs/.../player/07-installing-mods.mdx`, `scpsl-plugin-registry` (empty index)

**Why this is a blocker for the docs/onboarding story.** `08-packaging.mdx` says "The Public Repository is the preferred path for player-facing releases," with review, malware scanning, dependency install, and `anomaly-launcher://install` deep links; `player/07` documents a Public Repository tab with Install/Update/Uninstall. In code, `FeatureFlags.PublicRepoEnabled = false` ("Off in v1 — mods load from the active loadout's `local/Mods` folder"), the tab is hidden, the deep-link handler is gated off, and the registry index repo (`scpsl-plugin-registry`) is empty (`packages: []`). So the docs describe a submission pipeline that does not exist, while the **actual** v1 story ("zip a DLL, drop it in `loadouts/<name>/local/Mods/`") is treated as secondary. The docs repo even *has* a `PlannedFeature.astro` "not yet shipped" callout — used by zero pages.

**Fix.** Wrap every Public-Repository/deep-link doc section in the existing `PlannedFeature` component (or delete it), and rewrite `modding/08` + `player/07` around the real v1 path: manual DLL drop into the loadout's `local/Mods/`. State plainly that manual distribution *is* the launch story so modders don't wait on a store that isn't coming in v1.

---

# HIGH — Framework completeness & the custom-content subsystem

## Area 1 — Custom-content framework (new, uncommitted)

The architecture is sound and the code honest; the subsystem is roughly a well-built server half of a two-sided system. Beyond BLOCKER-3 (no client half), these gate a *working* custom item even after the client lands:

- **H · Custom roles are never materialized server-side** (`Anomaly.Server/Content/ServerContentRegistrar.cs:32`, `ContentCatalogBridge.cs:36`, `Content.cs:65`). `EnsureFrozen` calls only `RegisterItems()`; nothing inserts a cloned role into `PlayerRoleLoader.AllRoles` (`ContentRegistry.RoleEntries` has zero consumers). `Content.SetRole` calls `ServerSetRole((RoleTypeId)id, …)` with an id the loader doesn't know; the game's `PlayerRoleManager.GetRoleBase` falls back to `RoleTypeId.None`. So `SetRole` **silently turns players into the None role while returning `true`** (confirmed against the AssetRipper server export). *Verified adversarially.* → Implement role template injection symmetric to `RegisterItems`, or cut `CustomRoleDefinition`/`SetRole` from v1 and ship items-only.

- **H · `ItemLogicBase.OnUsed` / `RoleLogicBase.OnSpawned` are never invoked** (`ContentRegistry.cs:125-126`, `ItemLogicBase.cs:17`, `RoleLogicBase.cs:12`). `TryGetItemLogic`/`TryGetRoleLogic` have **zero callers** repo-wide; no LabAPI event or patch dispatches to them. The "server-authoritative logic" pillar — the reason to use this over EXILED's overlay approach — is declaration-only. An author who subclasses `ItemLogicBase` per the XML docs gets a hook that never fires. *Verified adversarially.* → Wire a dispatcher (subscribe LabAPI `PlayerEvents.UsedItem`/role-change, look up by custom id, invoke in try/catch); expand the hook set (`OnPickedUp`/`OnDropped`/`OnSelected` — one hook per kind is far below EXILED CustomItems).

- **H · Effects are catalog-only** (`CustomEffectDefinition.cs`, `ContentRegistry.cs:160`, `CatalogBuilder.cs:64`, `Content.cs:19`). Registrable and serialized, but no `ApplyEffect`, no server registrar, no id ceiling, and the gate is never consulted for effects. Worse: effect keys go into `_keyToId`, so `Content.GiveItem(player, "mymod:someeffect")` would resolve the effect's 0-based index and `ServerAddItem((ItemType)index)` — colliding with vanilla ids (index 0 = KeycardJanitor). *Verified adversarially.* → Drop `CatalogEntryKind.Effect`/`CustomEffectDefinition` from the shipped surface until a Phase-3 apply path exists, or mark experimental and add a kind check to `GiveItem`/`SetRole`.

- **H · Plugin authors can't recognize their own content in events** (`ContentRegistry.cs:122,127`, `ContentGrantGate.cs:33`, `Content.cs:17`). The only public API is `Register×3` + `GiveItem`/`SetRole`. `TryGetId`, `IsCustomItemId`, catalog enumeration, and ack-state queries are all `internal`, and there's no `InternalsVisibleTo`. Even in a working build, a plugin subscribing to `PlayerSearchedPickup`/`PlayerDroppedItem` has **no way to ask "is this my `mymod:magic_coin`?"** because the assigned `ItemType` is unreachable, and there's no pickup-spawn wrapper or per-player readiness query. → Add public one-line delegations: `TryGetId`, `TryGetKey`, `IsCustomItem`/`IsCustomRole`, `SpawnPickup`, `IsPlayerReady`, and a `PlayerContentReady` event.

- **MEDIUM · `ItemIdCeiling=254` assumes a widened pickup wire that doesn't exist** (`ContentRegistry.cs:22-23`). The comment says ids "cross the (widened) wire as a byte," but no such patch exists anywhere, and the game's `PickupSyncInfoSerializer` writes `ItemType` as **sbyte** (`WriteSByte`). Beyond ~id 127 (roughly the first ~60 custom items given today's max `ItemType`), assigned ids **wrap negative on the pickup wire**. Latent but the ceiling encodes an unmet assumption. → Set `ItemIdCeiling = 127` until a symmetric widening patch exists; gate 254 behind a flag that patch flips.

- **MEDIUM · Grant gate is per-recipient but items/roles are broadcast to all observers** (`ContentGrantGate.cs:36`, `Content.cs:43,65`). A player who acked can hold/drop a custom item or take a custom role in front of a client who did not ack; `PickupSyncInfo`/`RoleSyncInfo` push the unknown id to *every* observer, including a mid-join spectator the gate never consulted. The gating model is structurally mismatched with the game's broadcast sync. → Decide the policy before the client ships (all-observers-ready gating, or default-kick-on-mismatch when custom content is registered).

- **MEDIUM · New catalog serializers repeat the R1-N3 silent-truncation pattern + no version field** (`CatalogTypes.cs:21,28,41`, `ContentCatalogMessage.cs:22`). `WriteStr` writes `(ushort)len` then all bytes (a >64KB `Description` wraps and desyncs every following field); `ReadStr`/`ReadHash` use `ReadBytes` (silent EOF truncation); `ReadHash` accepts any 1–255 length with no `==32` check; no schema-version byte. → Add a version byte now (format is uncommitted); cap lengths at registration; make reads verify counts.

Plus lower-severity items: bridge double-subscribe on plugin reload (`ContentCatalogBridge.cs:19` — no `_initialized` guard); `ContentMismatchKick` doc says failed-entries kick but code only kicks on hash mismatch (`Config.cs:35` vs `ContentCatalogBridge.cs:69,72`); `Weight` and all presentation fields silently non-functional with no doc signal (`ServerContentRegistrar.cs:26-28` admits Weight inherits the base template); round-restart gate lifecycle unverified (`ContentGrantGate` keyed by `netId`, cleared only on `PlayerEvents.Left`); public `IContentRegistrar`/`ContentAssetId` with zero implementations/callers in the shipped Shared surface; and no docs page or sample mod anywhere.

## Area 2 — Public client API surface (Anomaly.Client.Api)

The surface is substantial (~190 files, ~1,160 public members) and the architecture promise holds — a mod referencing only `Api` + `Shared` *can* build real client mods. The problems are accessibility and first-contact:

- **HIGH · Runtime capabilities exist only on the forbidden `Anomaly.Client` assembly, with no API facade** (`Anomaly.Client/Assets/PathOverrideRegistry.cs`, `OverrideRegistry.cs`, `SpawnedObjectRegistry.cs`, `LocalAudio.cs`, `LocalTextures.cs`, `FileTransfer/FileTransferClient.cs`). `AGENTS.md:101` states `Anomaly.Client` "never exposes public types that mods consume," yet ~90 public types live there, including genuinely mod-relevant ones (programmatic path overrides, spawned-asset queries, override state). From the sanctioned API a mod cannot register a path override or enumerate spawned server assets — each capability is reachable *only* by breaking the "don't reference `Anomaly.Client.dll`" rule. → Triage: demote runtime-only types to `internal`; for the asset registries, add an `Api`-side facade (via `InternalsVisibleTo`) or document the file-drop convention as the only supported channel. Cheapest before mods start linking the runtime.

- **HIGH · No API for the mod's own `@anomaly` identity/auth state — and the session AES key is a public static** (`Anomaly.Client/Auth/AuthHandler.cs:27-35`, `Anomaly.Client.Api/Features/Client.cs:49-53`). The sanctioned surface offers only `ServerIp`/`Port`/`IsConnected`/`IsClientReady`. A mod wanting its stable `@anomaly` id (e.g. to key server-side per-player data — the framework's own selling point) has no legitimate path, while `public static byte[] CurrentKey`, `UsingAnomalyAuth`, and `PlayerAuth` sit on `AuthHandler` in a shipping assembly. → Add a read-only `Client.Identity`/`AnomalyAuth` facade (id string + auth-state, **excluding** key material); reduce `AuthHandler.CurrentKey`/`PlayerAuth` to `internal`.

- **HIGH · NETWORKING.md teaches a framework-source-editing workflow third parties can't follow** (`Anomaly.Client.Api/Networking/NETWORKING.md:9-16,42,48-54`). *Verified directly:* step 1 says create your message at `Anomaly.Shared/Networking/Messages/MyMessage.cs` and add it to `Anomaly.Shared.Shared.csproj`; step 2 says register "inside the Harmony patch at `Anomaly.Client/Patches/Mirror/NetworkManagerRegisterClientMessages.cs`." A third party can do **neither**. The underlying API fully supports the external flow (public `Register`, order-independent, name-keyed) — but the only doc for the framework's flagship extension point is written for people editing the framework repo, and it lives *inside* the folder modders are told is the public API. → Rewrite steps 1–2 for external consumers (message class in the mod's own shared assembly referenced by both sides; register from each side's own init); move the internal recipe to a contributor doc.

- **HIGH · Any mod can wipe shared framework state** (`Scheduler.cs:129-138`, `AnomalyReplayTable.cs:15`, `FileCache.cs:15`). `Scheduler.ClearAll()`/`PauseAll()`, `AnomalyReplayTable.Reset()`, `FileCache.Init()`, `ClientConfig.DisposeAll()` are public statics on shared singletons. One mod calling `Scheduler.ClearAll()` cancels *every* mod's tasks plus the framework's debounced persistence; `AnomalyReplayTable.Reset()` desyncs replay counters mid-session so subsequent Anomaly messages drop. `Scheduler` tasks even take an `ownerId` but there's **no** `ClearOwner(ownerId)`, so the safe scoped path doesn't exist. This contradicts the framework's "a misbehaving mod can't break other mods" guarantee. → Make lifecycle mutators `internal`; add `Scheduler.ClearOwner(string)` (gives `ownerId` a purpose).

- **HIGH · No sample mod, no template, no packaging** (`MODDING.md:11-14`, `csproj` `IsPackable=false`). No `samples/` or `templates/` anywhere; the only real consumer (`TalkyPlus`) is machine-locked with ~110 absolute `HintPath`s. No end-to-end example of the marquee scenario (paired client mod + server plugin exchanging a message), and the one in-doc example has a compile error. EXILED/LabAPI devs expect `dotnet new` templates or working NuGet references. → Ship one buildable sample pair (client MelonMod + LabAPI plugin + shared message DLL) under `samples/`, built in CI.

- **MEDIUM · XML doc coverage ~48%, and the zero-doc files are the core facades** (`Client.cs` 0/20, `Room.cs` 0/17, `Role.cs` 0/11, `FileCache.cs` 0/14, `AnomalyMessaging.cs` 0/3). `AnomalyMessaging.Send` — the single most important gameplay-mod method — has no doc stating its precondition (encryption negotiated, i.e. post-`Connected`) or its silent-warn failure mode. → Docs pass on the zero-doc core facades + event-args classes before generating a reference site.

- **LOW · MODDING.md quick-start doesn't compile** (`MODDING.md:165` uses `Role.RoleType`; `Features/Role.cs:16` is `Type`), and the `ModNamespace` validator is `internal` in `Api` but a divergent **public** copy lives in `Anomaly.Shared/Content` with a "keep in sync" comment. → Fix the snippet; make one canonical public validator.

## Area 5 — Client runtime & event coverage (Anomaly.Client)

Post-B1/B2 the runtime is in good shape: 92/92 compile-list-vs-disk match, and **every one of ~63 events across 24 handler classes has a verified live raiser.** The gaps are coverage vs EXILED expectations:

- **HIGH (medium confidence) · `StatsEvents.Damaged`/`Died` are patched onto server-authoritative methods and likely never fire on a normal remote client** (`Anomaly.Client/Patches/Runtime/PlayerStatsPatch.cs:11,19`, `Internal/Stats/HealthStateTracker.cs:39`). `PlayerStats.DealDamage`/`KillPlayer`'s only client-build callers are server-gated; on a dedicated server the client learns health via synced stats, and the polling `HealthStateTracker` raises only on health *increase*. Net: on a remote client, **no event fires for damage taken or death** — the top pair an EXILED dev subscribes to first. Needs an in-game confirmation against a dedicated server, then either synthesize `Damaged` from the health-snapshot delta / `Died` from role→spectator transitions, or document both as host-mode-only. *This is the single most important event-coverage question to resolve before launch.*

- **HIGH · Event coverage gaps vs EXILED** (`Events/Handlers/` enumeration; `Features/Door.cs`, `Locker.cs`, `Room.cs`, `Decontamination.cs`, `Scp914.cs`). No Door, Locker, Room/lights, Decontamination, SCP-914, or Intercom events at all — despite wrappers existing for most (they're polling-only; the sole raw event in `Features/` is `CameraManager.Changed`). `GeneratorEvents` has only `Engaged`; `FirearmEvents` has no Reloading/ADS; no item-usage (medkit/207) or spectator-target events. An EXILED dev's checklist (`InteractingDoor`, `UpgradingItem/Scp914`, `UsingItem`, `ChangingSpectatedPlayer`) hits ~zero. → Publish an explicit **event-coverage matrix** in MODDING.md (covered / polling-only wrapper / not yet); prioritize `DoorEvents` (the #1 interactable) — the Tesla/Elevator static-hook pattern makes these cheap to add.

- **HIGH · `PlayerEvents.Interacting` fires before the raycast and carries no target** (`Anomaly.Client/Patches/Runtime/InteractionCoordinatorPatch.cs:22-36`). It can only veto *all* interaction, never "interacting with door X." An EXILED user expecting `InteractingDoor(ev.Door)` granularity gets an all-or-nothing signal. → Add a post-raycast hook exposing the resolved interactable, or rename/document the current one as `InteractKeyPressed`.

- **LOW · One `ShotEventManager.OnShot` hook skips the `TryHookStaticEvent` guard its ten siblings use** (`ClientRuntime.cs:48-49`); a throw there (plausible on a game update) aborts the rest of `Core.OnInitializeMelon`, taking out diagnostics, UI bootstrap, and the Mods tab. → Route it through `TryHookStaticEvent`.

- **LOW · `ModSettingsSaveScheduler` rides the shared `Scheduler`** (`ModSettingsSaveScheduler.cs:25`), so disconnect's `Scheduler.ClearAll()` cancels a pending settings save — a new instance of the prior review's H13 pattern. → Give it a lifecycle-independent timer, or `Flush()` before `ClearAll()`.

*Hostile-server lens:* nothing material is new since the 2026-06-21 review. Clipboard paste is strictly user-initiated and the raw password never leaves the machine; the new `ContentCatalog` wire types use bounded reads and the client has no handler, so a hostile server sending catalogs to current clients is harmless.

---

# HIGH — Server DX, protocol, launcher, docs

## Area 3 — Server-side developer experience (Anomaly.Server)

The integration model is **right for the audience**: a plain LabAPI `Plugin<TConfig>` referencing `Anomaly.Server.dll` + `Anomaly.Shared.dll`, with small static facades (`AnomalyMessaging`, `AssetSpawner`, `AssetOverrides`, `Announcer`, `Content`). Custom networking is genuinely open to third parties. The gaps:

- **HIGH · `FacilityLayout.Enabled` is a public *mutable* static field — any plugin can flip the B3 kill-switch and reintroduce the map desync** (`Anomaly.Server/Map/FacilityLayout.cs:49`). *Verified directly:* `public static bool Enabled = false`, with an XML doc that explains how to re-enable it. Any third-party plugin can set `FacilityLayout.Enabled = true`; `SendBootstrap` then ships recipes to clients whose `AtlasZoneGeneratorPatch` is hard-disabled → exactly the hard server/client map desync the team disabled the feature to avoid. → Make the setter `internal` (or a `const`) for this release.

- **HIGH · No `LoadPriority.Highest`, so companion plugins can enable before the framework** (`Plugin.cs:22`, `Announcer.cs:48`, `ServerAssetManager.cs:14`). Anomaly enables at default `Medium`; a companion plugin at the same priority can run `Enable()` first, and the docs' own `Announcer.AddWord`-in-`Enable` pattern then throws `FileNotFoundException` because `ServerAssetManager.Init` hasn't run. `Content.Register` is deliberately order-independent (good) but `Announcer`/`AssetRegistry` aren't. → `Priority => LoadPriority.Highest`; document companions using `Low`; consider deferring `AddWord` resolution.

- **HIGH · No cross-mod capability discovery** (`AnomalyMessaging.cs:26`, `AnomalyMessageRegistry.cs:35`). The only per-player signal is `ClientReady` ("is an Anomaly client"). A server plugin cannot ask **which client mods a player has**; a custom message to a client without the counterpart mod just produces a client-side "Unknown message hash" warning. Every mod must invent its own hello/ack handshake. → Add a built-in capability announcement (client sends its registered non-default message names / mod ids after `ClientReady`; expose `AnomalyMessaging.ClientHasMessage(hub, name)`).

- **HIGH · Security/lifecycle internals are public** (`AnomalyReplayTable.cs:23`, `FileTransferSession.cs:50,70`, `ServerAssetManager.cs:19`, `AssetFileWatcher.cs:34`, auth DTOs, patch classes). `AnomalyReplayTable.Remove(hub)` is public — a plugin calling it mid-session desyncs TX counters and silently kills that hub's server→client messaging; `FileTransferSession` lifecycle, `ServerAssetManager.Init` (re-points the asset root), and `AssetFileWatcher.Start/Stop` are all reachable with no plugin purpose. → Internalize; keep `SessionCompleted`/`AllAssets`/`TryGetAsset`/`AssetRegistry` as the read surface. Last cheap moment (no third-party code exists yet).

- **MEDIUM · Duplicate message-name registration silently keeps the first factory** (`AnomalyMessageRegistry.cs:39`). Two plugins shipping `mymod:greeting` with different classes → the second is bound to the first's factory, deserializing payloads into the wrong type, with no return value or exception to detect it. The FNV *hash*-collision case correctly throws; the far likelier same-name case doesn't. → Throw on same-name-different-type; add public `IsRegistered(name)`.

- **MEDIUM · Deleted `AnomalyServerPlugins` registry still documented; namespace ownership lost** (`Anomaly.Server/API/Plugins` empty dir, `AGENTS.md:108`). The bespoke plugin registry was removed (good simplification) but `AGENTS.md` still describes it, the empty folder remains, and with it went `IsKeyOwnedByRegisteredPlugin` — so `ContentRegistry.Add` validates key *syntax* only, and cross-plugin key squatting resolves first-wins with a warning. → Update `AGENTS.md`, delete the folder, decide whether namespace ownership matters for v1.

- Plus: `Content` has no public read/query surface (mirrors Area 1); docs-site server snippets don't compile (`AssetOverrides.SetOverride` doesn't exist; `OnEnabled()` is EXILED not LabAPI); `Announcer` is register-only (reload/duplicate throws out of `Enable`); `ContentCatalogBridge.Init` double-subscribes on reload; no example companion project.

## Area 8 — Wire protocol & extensibility (Anomaly.Shared)

The wire discipline is consistent and the catalog hashing is genuinely deterministic across net48/net6 (ordinal sorts, raw IEEE bits, FNV over UTF-8 — no culture-sensitive API in any hash path). The two format-freeze-critical items:

- **HIGH · No protocol version negotiation, no capability bits, nothing couples message-format changes to a version bump — every post-release wire change is an ecosystem flag day** (`AnomalyChannel.cs:26`, `ClientHandshake.cs:37`, `ServerHandshake.cs:34`). *Verified adversarially.* The version is a single byte asserted for **exact equality**; no min/max range, no server-picks-highest, no feature flags. The 0xFE envelope carries no version, and message ids are FNV of the **name only** — so changing a message's field layout is invisible on the wire, and a forgotten `CurrentVersion` bump degrades into silently dropped/misparsed messages rather than a clean rejection. Every legitimate bump forces **all servers and all clients to update simultaneously.** The version byte is in a fixed-offset layout, so widening it to a range *after* release is itself a breaking change. → Before freezing: put a supported-version **range** in the handshake (server selects); derive ids from name+layout or exchange a registry-manifest hash at handshake; add **capability bits** for optional subsystems (content catalog, file transfer, facility recipes) so features degrade gracefully.

- **HIGH · Shared exposes mutable global crypto/logging state to every mod** (`AuthCrypto.cs:17`, `AnomalyMessageRegistry.cs:23,28`). Mods must reference `Anomaly.Shared` for messaging — and that DLL hands them `public static X9ECParameters SingingCurve` (a writable field read by all signing/verification; one line silently swaps the identity curve framework-wide) and `public static Action<string> Log/Warn` (a mod assigning `null` NREs every subsequent `Register()`; any mod can overwrite the framework's sinks and suppress collision warnings). The whole `Auth` namespace (handshake codecs, password KDF, a dead `AuthCrypto.Main()` demo) is public in the modder-facing contract. → Make `SingingCurve` get-only; null-guard `Log`/`Warn`; ideally split the modder contract (`IAnomalyMessage`, registry, `MessageChannel`, content types) from the auth/crypto core.

- **MEDIUM · `TransportChannel` is contract but never on the wire** (`IAnomalyMessage.cs:17`, `AnomalyMessaging.cs`). Sender and receiver each resolve the channel from their own registry to pick the replay-counter lane; a one-sided channel change validates counters against the wrong lane → messages drop as stale with only a rate-limited warning. Nothing documents `TransportChannel` as frozen. → Carry the channel byte in the envelope and validate it, or document it as immutable protocol surface.

- Plus: registry duplicate/collision can abort the whole bootstrap block; the `anomaly:` prefix is advisory (never enforced via `ModNamespace`); `CatalogBuilder.Build` does no input validation and mutates caller entries in place; `FacilityLayoutRecipeMessage`/`FacilityAtlasEditsMessage` implement `IAnomalyMessage` but aren't registered (throw if a mod `Send`s them); unthrottled unknown-hash log-spam; version-mismatch UX reuses the game's generic popup with no "update Anomaly" hint; a client-side handshake byte-dump log remains (B6-adjacent, different file).

## Area 6 — End-user experience (Launcher & Installer)

The launcher/installer is a **mature, defensively-engineered product** — well above the bar for a hobby modding launcher (never patches the vanilla copy in place, backs up and rolls back every mutated file, fails *safe* on unknown builds, production-grade Steam downloader, strong launch diagnostics, crash surfacing that works). The consequential gaps cluster in trust and the hotfix channel:

- **HIGH · No code signing anywhere in CI** (`anomaly-launcher/.github/workflows/release.yml`, `Services/Updates/WindowsUpdateApplier.cs:35`, `RELEASE.md`). The launcher exe, its self-updated replacement, the patched `GameAssembly.dll`, and the MelonLoader `version.dll` proxy loader are all exactly the shapes SmartScreen and AV flag. The first-run browser-download path (carries Mark-of-the-Web, unsigned, no reputation) **reliably** hits SmartScreen "Unknown publisher" — the #1 first-time-user bounce risk for a security-sensitive patcher. (Nuance: the self-*update* relaunch is written without MOTW so it's not SmartScreen-gated, but a freshly-written unsigned single-file exe is a known Defender/AV false-positive class.) → Add Authenticode signing to the release workflow (EV cert if budget allows, for SmartScreen reputation); sign the staged self-update binary; document the expected prompt + AV-exclusion guidance in README/docs so users don't bounce.

- **HIGH · The live hotfix channel is hardcoded to a `/main/` branch that doesn't exist** (`Anomaly.InstallerCore/Resources/AnomalyResourcesClient.cs:13-14`, `RELEASE.md:7`). *Verified against the live remote:* the base URL is `…/anomaly-launcher/main/` but the repo has only `master` (no `main`); `raw.githubusercontent.com` returns **404** for `/main/…manifest.json` and 200 for `/master/…`. So every live fetch of `manifest.json`/`patchinfo.json`/`melonloader.json`/clean-launcher index from the *primary* source 404s and silently falls through to the deprecated `anomaly-resources` fallback (which still serves today, masking the break). This is the exact channel meant to ship a patch-Tuesday hotfix without a full launcher rebuild — the documented primary hotfix workflow is broken. → Point at `/master/` (or, better, a stable release tag / CDN path, not a moving branch); add a CI check that fetches each manifest-listed URL and fails on any 404.

- **MEDIUM · Self-update signature verification is fail-open** (`Services/Updates/UpdateService.cs:93,184`, `GitHubReleaseSource.cs:55`). It verifies only if `Sha256Url` is non-empty **and** the sidecar fetch succeeds; a missing/transiently-failing sidecar applies the update **with no integrity check**. And the sidecar sits beside the zip in the same release, so it's integrity-not-authenticity to begin with. → Fail closed when a hash is expected but unobtainable; for real supply-chain protection, sign releases with an offline key pinned in the launcher.

- **MEDIUM · MelonLoader is downloaded unpinned** (`Resources/melonloader.json` `"sha256": null`, `MelonLoaderInstaller.cs:45`). Everything else the installer fetches is SHA-256-verified; the one injected loader that runs with the game is not — it prints "No SHA-256 pinned … skipping" and extracts anyway. → Pin the MelonLoader zip hash; make null a hard failure for release builds.

- **MEDIUM · The highest-risk install step (in-process Il2CppDumper under single-file publish) has no automated coverage** (`Anomaly.Launcher.csproj:28`, `RELEASE.md:42,48`) — only a manual smoke gate the `.csproj` itself flags as fragile. A regression ships a launcher that fails install for every user. → Add a headless CI job running the full install pipeline against a fixture `GameAssembly.dll`+metadata.

- **MEDIUM · Primary game-acquisition asks users to sign a Steam account into the launcher and re-download the full depot** even when they already own/have SCP:SL installed (`Services/Steam/SteamDownloadService.cs:201,261`). A lower-friction "use local directory" path exists but is secondary. → Lead with the local-copy path when an existing install is detected; make the "credentials go straight to Steam" guarantee prominent.

- **LOW · Dead `LogTailService`** (defined, never instantiated, wrong path for the multi-version layout); **update check is startup-only + silent-on-failure** with no manual re-check and no crash-rollback after self-update; **Linux/Steam Deck is best-effort** with rough edges documented only in `RELEASE.md`, not the user-facing README.

## Area 7 — Documentation & the distribution story

Documentation is the project's paradox — **the most impressive and the least trustworthy part of the release.** 40 substantive pages across four audience tracks (more than EXILED/LabAPI at 1.0), but spot-checking ~15 claims against code found the first three snippets a modder pastes all fail to compile:

- **HIGH · Flagship snippets don't compile.** *Verified directly:* docs show `ICommand` with `Usage` + `Execute(string[] args, ICommandContext ctx)`; the real interface (`Commands/ICommand.cs:17`) is `Name`/`Aliases`/`Description`/`Execute(CommandContext)` — no `Usage`, no `ICommandContext` type anywhere (yet the API index lists it as a key type), and the tutorial's command name `ping.server` is rejected by the `^[a-z][a-z0-9_]{0,31}$` pattern. `Scheduler.Every(…, ownerId: "mymod")` and `ClearAll(ownerId:)` have no matching overloads. Input docs show an object-initializer + `Get(...).Pressed +=` API that `InputRegistry` (polling-only: `IsPressed`/`WasPressedThisFrame`) doesn't have. → Regenerate every snippet from the real API; add a docs-repo CI step compiling extracted snippets against the released DLLs.

- **MEDIUM · Event docs are stale** — lists a removed `CreditHook`, says "14 handler classes" (there are 24), omits Audio/Camera/Hud events, and **the entire cancellable-events surface is undocumented** (the feature EXILED devs look for first). → Regenerate the events table from source; add a "Cancellable events" section.

- Plus: `trackAnomalyClient` documented "on by default" but defaults `false`; reserved-prefix claim (`launcher.*`) only enforced by localization; a couple of wrong method names / repo links; `Tr.RegisterMod` XML doc still says JSON (loader reads YAML — a *carried-over* prior-review item, still unfixed in `Tr.cs:28`).

- **MEDIUM · Main-repo hygiene lags the launcher repo.** The launcher repo is exemplary (GPLv3, `TRADEMARK.md`, `CONTRIBUTING.md` with DCO + AI-disclosure, `RELEASE.md`, CI). The main repo has only a bare LGPL-2.1 `LICENSE` — no `CONTRIBUTING`, no templates, no `SECURITY.md`, no CI, and the README never states the license or what it means for mod authors (LGPL-2.1 dynamic-linking is *the* question every modder asks). → Add `CONTRIBUTING`/templates, a one-paragraph licensing section, mirror `TRADEMARK.md`, decide what belongs in public history.

---

# Capability-Gap Matrix

What a framework of this class needs that isn't built (not bugs — decisions):

| Capability | State | Evidence | Verdict for v1 |
|---|---|---|---|
| **SDK acquisition** (NuGet / release zip + `.xml` docs) | Missing | no CI, `IsPackable=false`, no `GenerateDocumentationFile` | **Blocker** — modders can't build without a game install |
| **Core-mod delivery to end users** | Not implemented | `AnomalyClientTrackingEnabled=false` | **Blocker** — funnel dead-ends |
| **Mod distribution channel** | Disabled + empty | `PublicRepoEnabled=false`, empty registry | **Blocker** to advertise; ship "manual drop" as the real v1 story |
| **Sample / template mod** | None | no `samples/`; `TalkyPlus` machine-locked | **High** — table stakes for the audience |
| **Protocol version negotiation / capability bits** | None (exact-match byte) | `ClientHandshake.cs:37` | **High** — freezes badly; cheap now, breaking later |
| **Cross-mod capability discovery** | None | only `ClientReady` | **High** for paired-mod story |
| **Custom items/roles/effects** | Server-half only | no client half; roles/logic/effects unwired | **Blocker** to ship as public API |
| **Event coverage vs EXILED** | Partial | no door/locker/room/914/decon; damage/death host-only | **High** — publish a coverage matrix; resolve damage/death |
| **API-stability / semver policy** | Undefined | — | **High** — the surface freezes at release |
| **Debugging story for mod authors** | Weak | no live log view, empty IntelliSense | **Medium** |
| **Code signing / update authenticity** | None | unsigned CI, fail-open self-update | **High** (end-user trust) |

---

# Recommended release sequencing

**Gate v1 on these (cannot ship without):**
1. **BLOCKER-1** — `git add` the ModSettings folder; fresh-clone CI build.
2. **BLOCKER-2** — purge the vanity private keys; fix `.gitignore`; decide what's in public history.
3. **BLOCKER-3** — remove the `Content/*` public surface from the release branch **or** land the client half; do not ship `Content.GiveItem`/`SetRole` that always fail.
4. **BLOCKER-4** — ship `GenerateDocumentationFile` + a release SDK artifact; add minimal main-repo CI.
5. **BLOCKER-5 / BLOCKER-6** — implement (or document) the core-mod delivery path; re-scope the distribution docs to the real manual-drop story (`PlannedFeature` callouts on the store pages).
6. **Launcher HIGH** — fix the `/main/`→`master` hotfix channel (currently masked by a deprecated fallback); add code signing or, at minimum, ship SmartScreen/AV guidance; make self-update fail-closed; pin MelonLoader's hash.
7. **`FacilityLayout.Enabled`** setter → `internal` (a public plugin can otherwise reintroduce the map desync you disabled the feature to prevent).
8. Resolve the **`StatsEvents.Damaged`/`Died` host-only question** — either wire them for remote clients or document them as host-only (an EXILED dev *will* build on these day one).

**Fast-follow (before or right after v1, in order):**
9. The prior review's **H1–H13 and S1–S5/S7–S8** — none are fixed; several (H7 `UnpatchAll`, S1 preauth flood, S3 client OOM, S7 stale session key) are cheap and real. See [Appendix A](#appendix-a--prior-review-fix-status-verified-in-current-tree).
10. **Accessibility pass**: internalize runtime-only publics, add an identity facade, add `Scheduler.ClearOwner`, internalize the Shared crypto/lifecycle mutators.
11. **Docs truthfulness pass**: fix the non-compiling snippets, regenerate the event table, document cancellable events, add an EXILED→Anomaly migration cheat-sheet, add a changelog page.
12. **Sample/template repo** (`anomaly-mod-template` with client-only and client+server+shared variants).
13. **Event coverage**: `DoorEvents` first, then decon/room-lights; publish the coverage matrix.

**Label explicitly experimental in v1 (don't freeze):**
- The custom-content framework (if any of it ships) — mark `[Obsolete("preview")]`/experimental so nobody builds on `Content.Register` while roles/logic/effects are unwired.
- Facility layout recipes (already inert end-to-end via B3).
- `ScreenEffects`/`Subtitles` (self-described v1 stubs), Scheduler pause infra, `Hud.PushHide`.

**Freeze-critical protocol decision to make *now* (cheapest before release):**
- Add version-range negotiation + capability bits to the handshake ([Area 8](#area-8--wire-protocol--extensibility-anomalyshared)). Once third parties ship, the exact-match byte turns every wire change into a coordinated flag day, and the byte can't be widened without a break.

---

# EXILED/LabAPI migration assessment

**What transfers cleanly:** the read-only wrapper facades deliberately mirror LabAPI shapes (`Round` documents "Mirrors LabAPI's server-side Round shape"; `Warhead` explains why `Start/Stop` are absent client-side); `IsAllowed` cancellation matches EXILED muscle memory; the server integration model is a plain LabAPI `Plugin<TConfig>` with nothing new to learn about lifecycle or config; namespaced ids with a reserved `anomaly` prefix prevent accidental collisions.

**What's genuinely better than the incumbents:** a real custom-networking channel (paired client mod + server plugin, authenticated + replay-protected) that neither EXILED nor LabAPI offers; auto-rendered `ModSettings` from typed preferences; client-side voice event coverage; the honest client-authority framing in the docs.

**What an EXILED dev will miss / trip on (the migration friction list):**
- **Damage/death events** — likely don't fire on a remote client (host-only). The #1 gap.
- **Interaction with a target** — `Interacting` is targetless veto-only; no `InteractingDoor(ev.Door)`.
- **Door/Locker/Room/914/Decontamination/Intercom events** — wrappers exist but are polling-only.
- **Item-usage and spectator-target events** — absent.
- **Custom items the EXILED way** — Anomaly's model (a real distinct `ItemType`, not an overlay-on-vanilla) is a genuine upgrade *in principle*, but the whole subsystem is inert today, and even the design has only one logic hook per kind vs EXILED CustomItems' rich hook set.
- **Getting started** — no `dotnet new` template, no NuGet, empty IntelliSense, first snippet doesn't compile. EXILED/LabAPI both ship templates/examples; this is the biggest first-impression delta.

A one-page **"Coming from EXILED/LabAPI"** doc (Player→`Anomaly.Player` equivalences, what's client-side-only, the event-coverage matrix, the manual-distribution story) would do more for adoption than any single code change.

---

# Appendix A — Prior-review fix status (verified in current tree)

Full item-by-item verification is in the audit data; the summary that matters for the release gate:

- **Blockers B1–B7:** all addressed. B1 (`Scp079GeneratorPatch` class-level `[HarmonyPatch]`), B2 (csproj compile entries), B4 (`MirrorDiagnosticPatches` deleted), B5a (`_storedPlayers`→`ConcurrentDictionary`+TTL), B5b (`AssetFileWatcher` main-thread pump) are **fixed in code**. B3 (facility recipes) **gated off end-to-end** (`FacilityLayout.Enabled=false`). B6 **partially** — test pref and `anomaly_debug.log` gone, but the type-4 login path still logs raw `serverIdentifier`/username/userId at Info ungated (`ProcessConnectionRequestPatch.cs:227,229,239,…`). B7 **deliberately left unenforced** (documented team decision).
- **HIGH H1–H13:** **none fixed** — all open. Notable cheap wins still on the table: **H7** (`Plugin.cs:113` still `Harmony.UnpatchAll()` with no id → strips other plugins' patches), **H8** (`AuthCrypto.cs:46` BigInteger fixed-width bug, ~0.4% identity-creation failures), **H2** (`AssetDirectory=""` default still silently disables all late-join replay).
- **SECURITY S1–S5/S7–S8:** **none fixed** — all open (preauth flood, file-transfer amplification, client OOM, unbound MITM on the handshake, unencrypted 0xFE from remote peers, stale session AES key, instantiate-active-then-strip on server bundles).
- **R1-N1…N5:** all open. **Feature-completeness table:** VirtualInputs/RagdollEvents/GeneratorEvents now live (via B1/B2); everything else (HintDisplaying, Hud.PushHide, pose/emotion relay, Scheduler pause, ScreenEffects/Subtitles, Item.Unregister, proxy/geoblock, path-override phase 2/3, bundle hot-reload) remains dead.

The 2026-06-21 review's Phase-0 was cleared; **its entire Phase-1 and Phase-2 remain the top of the correctness/hardening backlog** and should be sequenced right behind the framework-readiness blockers above.

---

# Appendix B — Method & evidence

- **Approach:** an 8-agent parallel deep-dive (custom-content framework, public API surface, prior-review fix-status, server DX, client runtime, launcher/installer UX, docs/distribution, wire protocol), each returning structured findings, with adversarial re-verification of new blocker/high findings. The gap-analysis synthesis stage and 14 of the verification agents were interrupted by an account spend limit; **I completed those inline** — re-reading the content-framework, API, server, and protocol code directly, and personally verifying every claim that appears here as a BLOCKER or as HIGH-with-"verified directly" (the untracked ModSettings folder, the vanity private keys, `.gitignore`, absent `GenerateDocumentationFile`, absent `.github/`, `FacilityLayout.Enabled` as a public field, `PublicRepoEnabled`/`AnomalyClientTrackingEnabled=false`, `ICommand`'s real shape, `InputRegistry`'s polling-only surface, NETWORKING.md's source-editing instructions, and — against the live GitHub remote — the `/main/`-vs-`master` 404).
- **References:** all `file:line` citations are to the current working tree (main repo `E:\Projects\AnomalyLabs\Anomaly`, launcher repo `E:\Projects\AnomalyLabs\anomaly-launcher`, docs `E:\Projects\AnomalyLabs\anomaly-docs`), cross-checked against the AssetRipper decomp and LabAPI source per `AGENTS.md`.
- **No code was modified.**
