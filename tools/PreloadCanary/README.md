# Pre-load trust canary

This intentionally observable MelonMod proves whether an unapproved assembly executes before `Anomaly.Bootstrap` can quarantine it.

1. Build `PreloadCanary.csproj` without deploying it.
2. Set `ANOMALY_PRELOAD_CANARY_MARKER` to a new writable marker path.
3. Copy the canary DLL into an isolated test slot's `Mods` directory without adding its SHA-256 to `UserData/Anomaly/mod-trust.json`.
4. Launch through the isolated qualification workflow.

Acceptance requires the DLL and any sidecar to move under `UserData/Anomaly/Quarantine` and the marker file to remain absent. Any `module-initializer`, `static-constructor`, or `melon-initialize` line means the pre-load gate failed and launcher-owned staging must replace it.
