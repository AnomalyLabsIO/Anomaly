# Private file-transfer qualification

This tooling creates real Unity AssetBundles and observes the production protocol.
It is excluded from release packages. No helper replaces reliable delivery, changes
credit limits, bypasses validation, or pretends that an existing cache file is absent.

## Build the fixtures

```powershell
pwsh -NoProfile -File tools/NetworkTransferSmoke/Build-Fixtures.ps1 `
  -OutputDirectory artifacts/net29/transfer-fixtures-02 `
  -BasePackIndexPath artifacts/assetbundles/qualified-fixture/anomaly-qualified-fixture.index.json
```

The builder copies the existing production exporter into a **new private Unity
project**. It never opens or changes `Anomaly.AssetAuthoring`. Unity 6000.0.43f1
imports deterministic, high-entropy TGA pixels and produces LZ4 native bundles.
The production bounded validator and index policy must pass before a fixture is
published. Four 1024×2040 RGBA32 textures make each bundle approximately 31.875 MiB.
This leaves room for LZ4 block accounting under the per-texture 16 MiB encoded
limit. Individual bundles stay below 64 MiB.

Cases 32, 128 and 256 contain 1, 4 and 8 independent bundles. These are **nominal
sizes**: exact file and wire bytes are recorded. The largest case is approximately
255 MiB plus metadata, below the actual 256 MiB aggregate manifest/native cache
limits. A literal 256 MiB of pixels plus bundle/index overhead would exceed those
limits and is not a valid positive performance fixture. The optional base pack
supplies the existing ServerApiSmoke checker/tone assets. Every case also contains
one exact benign remote Lua source; its nonempty script set must prepare before
Ready. `Pin-Content.ps1` also runs the production content-pack codec to derive the
script index. `expected-content.json` pins the entire expected wire manifest,
including that generated index, every bundle/asset index and the exact Lua source.

The release layout also stages a local UI skin pack (3.3 MB encoded, about 40 MB
decoded). The nominal 256 MiB fixture cannot activate together with that pack
under the existing combined cache/native budgets. The normal case must reject
this known copy-budget conflict before consent/download. For a separate maximum
remote-content control, `Run-Smoke.ps1 -OmitBundledSkin` skips only the three skin
files that this runner would otherwise stage and records that choice in inputs.
It refuses to remove any already installed local pack. Set
`ANOMALY_TRANSFER_EXPECT_NO_LOCAL_ASSETS=1` to assert the actual native layer is
empty. This control does not qualify the ordinary bundled-skin combination;
32/128 MiB cases retain the bundled pack.

## Integration and run inputs

Current runtime packs require asset index schema 2 and catalog schema 5. Keep the
old `transfer-fixtures-02` inputs frozen with their earlier evidence. Build fresh
fixtures, or use [AssetPackIndexUpgrade](../AssetPackIndexUpgrade/README.md) to
measure indexes into new fixture copies, update exact index/decoded-size pins,
then run `Pin-Content.ps1` again. A re-encoded index changes manifest bytes and
approval hashes even when its bundle is unchanged.

The existing harness selects `-ServerApi -ServerApiMode network-transfer`.
Root integration calls `PollNetworkTransferSmoke()` once per client update and
`NetworkTransferProbe.Initialize/Shutdown` from the server fixture. The existing
smoke message command `transfer-report` returns the server observation summary.

Set these variables in **both** private child environments:

```powershell
$env:ANOMALY_NETWORK_TRANSFER_SMOKE = '1'
$env:ANOMALY_TRANSFER_RTT_MS = '50'
$env:ANOMALY_TRANSFER_LOSS_PERCENT = '0'
```

Set the client-only pins and optional acceptance deadline:

```powershell
$env:ANOMALY_TRANSFER_EXPECTED_CONTENT = 'E:/Projects/AnomalyLabs/Anomaly/artifacts/net29/transfer-fixtures-02/case-128/expected-content.json'
$env:ANOMALY_TRANSFER_EXPECT_COLD = '1'
$env:ANOMALY_TRANSFER_EXPECT_MODE = 'pool'
$env:ANOMALY_TRANSFER_MAX_SECONDS = '60'
```

For the native negative capacity case, set
`ANOMALY_TRANSFER_EXPECT_BUDGET_REJECTION=1` and retain the normally staged skin.
The harness requires the `CONTENT-BUNDLE-BUDGET` notice before any content/script
approval, zero observed requested/consumed file bytes and no Ready. It records
`capacity-rejection.json`; independently inspect server lane totals to corroborate
zero bulk body traffic. `ANOMALY_TRANSFER_EXPECT_REJECTION_CODE` can select another
explicit code for a separately arranged capacity case. Do not use
`-OmitBundledSkin` for this negative control.

For an established native-player control, set `ANOMALY_TRANSFER_HOLD_SECONDS` to
5–600. After Ready and independent cache verification, the game stays connected,
sending one ordinary authenticated `ping` request per second. The
`transfer-hold-ready.json` marker permits a separate concurrency fixture to start.
`transfer-established-player.jsonl` records actual response times and peer ping;
the summary includes response and frame-gap percentiles. Create
`release-hold.flag` in that run's evidence directory after the other peers finish
to end the hold early. This measures message responsiveness, not voice quality.

For a queued-native-client test, `ANOMALY_TRANSFER_CONNECT_GATE` may name a fresh
file inside that run's evidence directory. The game finishes startup, then waits
at `transfer-connect-gate` for up to 240 seconds before connecting. Start two
large protocol peers, confirm their real server sessions are Sending, then create
the gate file. This permits the actual native client to be the queued third join
without counting game startup as queue wait. Record its Queued duration and final
Ready result; protocol peers alone do not prove the native phase timer.

Use the existing `tools/ServerApiSmoke/Run-Server.ps1` with the chosen case's
`ContentPacks` directory, a newly staged private 14.2.9 server and port 29149.
Use `tools/EarlyAccessUiSmoke/Run-Smoke.ps1 -UseModLaunch` with the authorized main
game, an explicit backup directory, `-ServerApi -ServerApiMode network-transfer`,
and `-LoopbackServer 127.0.0.1:29149`. Rebuild and deploy matching protocol versions
before either child starts; an earlier protocol-12 private stage is not suitable.
These tools never start the game directly or introduce a second transport port.

The shipped 14.2.9 LiteNetLib release omits calls to its conditional latency/loss
helpers. These stock settings are **metadata only**, not network impairment.
Use zero for native simulation and use the private [UDP relay](Relay/README.md)
for actual 20/50/100 ms added RTT or loss. It delays real packets including ACKs
and pings, preserving bytes and the game port number. Capture measured peer ping,
MTU, channel count, update interval and relay statistics; do not substitute a
requested setting for measured RTT. A fixed-delay relay is not an internet
congestion model.
The default expected mode is `pool`; a cold positive run must both consume frames
from multiple domains and record actual unicast batches on multiple physical lanes.
For an explicitly arranged fallback control, set `ANOMALY_TRANSFER_EXPECT_MODE` to
`fallback`. This variable checks the result; it does not force or alter negotiation.

## Exact cold-cache preparation

With the authorized game and ModLaunch fully stopped/restored:

```powershell
pwsh -File tools/NetworkTransferSmoke/Cold-Cache.ps1 -Action Prepare `
  -GameDirectory 'E:/Steam/steamapps/common/SCP Secret Laboratory' `
  -BackupDirectory artifacts/net29/cache-before-128-rtt50 `
  -ExpectedContentPath artifacts/net29/transfer-fixtures-02/case-128/expected-content.json
```

Generated expectations already include every manifest hash. The optional
`-TransferManifestPath` can include the prior client's observed manifest. Preparation
backs up the prior cache index and individually named hash files, verifies backup
bytes, then removes only the pinned files. It refuses a run that could trigger the
runtime's 500 MiB cache eviction budget. It never recursively deletes a cache.
After the client exits and ModLaunch restores, use the same game/backup arguments
with `-Action Restore`. It restores original bytes, attributes, timestamps and index;
unlisted files are left alone. Preserve the backup until restoration is verified.
Do not run this against an active process or an unowned cache.

## Evidence and acceptance

* Client: `transfer-client.jsonl`, `transfer-manifest.json`, `transfer-result.json`.
  Measures connect/consent/first consumed bytes/completion/Ready separately, exact
  requested bytes, independent off-thread SHA-256 verification of every cached
  manifest file, selected pool/fallback mode, consumed domain counts, queue peaks,
  frame-gap percentiles and nonempty script preparation. Hash verification occurs
  after the recorded Ready timestamp and cannot inflate the transfer timing.
  `readiness-messages.jsonl` passively observes normal API publication, recording
  exact content admission and local-hub availability. It can witness legitimate
  cross-channel control arriving after content commit but before hub spawn;
  a successful transfer with only post-spawn observations does not prove that
  particular ordering occurred. Observation errors invalidate that witness.
* Server: `transfer-server.jsonl`, `transfer-server-summary.json`.
  Records actual unicast bulk-lane batch/byte counts, per-domain sent/released
  counters, per-connection credits, measured MTU/ping, reliable outgoing queues,
  global reservation/queue state, and active-transfer Tick durations.
  `pumpQueuedEntries` observes the physical ready queue separately from the live
  source dictionary. Cancellation qualification must check both; zero live
  sources alone does not prove that cancelled sessions were released. Admission
  rejection receipts also capture each active transfer's remaining work and
  original deadline so staggered-arrival failures can be attributed correctly.
  Reliable ordered/unordered outgoing queues are sampled only for Connected
  peers. Rejected, disconnecting, or channel-storage-unavailable peers have
  `queuedPackets: null` with an explicit reason and a summary skip count.
  These queues exclude packets already in flight and reliable-sequenced traffic.
* `transfer-probe-errors.log` must be absent/empty. Reflection failures invalidate
  the affected observations; missing fields are not proof of zero backlog.

Transport queues are sampled every 250 ms; client disk queue and server credit
peaks are observed per frame. They are observed maxima, not a formal bound on every
intermediate instant. Server Tick includes session maintenance in addition to the
2 ms pump. Client frame gaps include scene loading and observer overhead. Compare
idle and established-player controls, cold versus warm cache, multiple simultaneous
joins, cancellation/reconnect, and forced fallback. A single joining client cannot
establish fairness for other players. Do not label qualification complete without
the corresponding live evidence.
