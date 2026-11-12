# Private concurrent protocol peers

These opt-in peers run inside the private **14.2.9 Unity server process** through
`ServerApiSmoke`. The game fork's span signatures use Unity's mscorlib identity,
so a stock .NET executable cannot faithfully load this transport. The fixture
uses the actual game `NetManager`, privately owned Mirror readers/writers and the
actual Mirror compression routines for timestamp/message-length framing, plus
production Shared authentication, AEAD, message codecs and replay windows. It does
not patch transport logic or inject server sessions. Stock `Batcher`/`Unbatcher`
cannot safely run on these workers: their global writer pool uses an unlocked
`Stack<T>` shared with the server main thread. The fixture avoids that pool rather
than changing or locking production behavior.

The peers create ephemeral identities, pin the private server's public identity,
verify the exact generated catalog and whole manifest, write chunks before giving
cumulative credits, and hash/flush each file before final ACK. All peers wait at
the pinned-offer barrier before issuing near-simultaneous cold requests. Reliable
delivery events follow final ACKs; an independent server completion observation
confirms application handling rather than relying on an elapsed grace period.
They **never send activation, Ready or AddPlayer** and never execute scripts.
The peers enforce a fixed 120-second connection deadline beginning with the signed
login, 30-second awaiting-admission/receiving inactivity limits, and the bounded
queued wait advertised by the server. Repeated status messages cannot extend those
timers. They independently assert 2 MiB / 128 outstanding frames, eight frames per
domain, and four active receive files against credits actually handed to transport.
Bulk profile 2 starts each domain at the manifest's crypto-session counter base.
Out-of-order disk-consumed counters are kept in bounded sparse sets; a credit's
absolute counter watermark advances only through contiguous consumed counters.
The fixture also checks that a sender never advances more than eight counters
beyond its last credited watermark. Released frame counts alone cannot free that
counter span. Final acquisition requires every counter hole to be closed.
Since these workers write synchronously, this is a receive-window assertion, not a
measurement of the native client's asynchronous storage queue.
The native `Delay` rejection (17) is parsed as its one-byte advertised delay and
retried only inside a fixed 30-second preauthentication deadline. Idle/startup
admission checks are preserved; the fixture never disables server delay behavior.

This is a synthetic protocol implementation. Its admission inventory includes a
core-version claim derived from the current client artifact and the distinct
`anomaly.protocol-test` marker. It explicitly reports `CoverageIncomplete` and
requires the supported `allow_risky_client_modifications: true` setting on the
private server. It does not claim that MelonLoader or client protection hooks ran.
Leave that option false for ordinary native-client qualification cases.

Before starting a new private server, set:

```powershell
$env:ANOMALY_TRANSFER_PEERS = '1'
$env:ANOMALY_TRANSFER_PEER_COUNT = '3' # 1 for the first interoperability check
$env:ANOMALY_TRANSFER_PEER_HOST = '127.0.0.1' # or explicit private UDP relay: 127.0.0.2
$env:ANOMALY_TRANSFER_SERVER_IDENTITY = '<private artifacts/net29/.../configs/29149/Anomaly/server_identity.yml>'
$env:ANOMALY_TRANSFER_EXPECTED_CONTENT = '<case/expected-content.json>'
$env:ANOMALY_TRANSFER_CLIENT_ARTIFACT = 'E:/Projects/AnomalyLabs/Anomaly/Anomaly.Client/bin/Release/net6.0/Anomaly.Client.dll'
```

After the normal ModLaunch native client reaches Ready and starts its established
player hold/control probe, create the empty file `start-protocol-peers` in that
server run's evidence directory. The coordinator launches once. It connects only
to `127.0.0.1:29149` or the explicitly selected private relay at
`127.0.0.2:29149`; all other addresses are rejected. The signed authentication
context uses that exact selected endpoint. Client UDP sockets bind ephemeral loopback ports. No new
server listening-port requirement is introduced. Three peers plus the native
client require the existing private server's four slots.

Results are in `protocol-peers/result.json`, per-peer `events.jsonl`/`result.json`,
and `protocol-peers-exit-code.txt` in the server evidence directory. Successful
acquisition requires every pinned byte, independent final SHA-256 verification,
and actual delivery across the negotiated physical lanes. The coordinator reads
production sessions only on the server main thread and publishes immutable
completion proofs keyed by session ID. Each peer, and the aggregate result, requires
the actual server `Complete` state with every requested file marked verified and
the exact requested-byte total. Reliable transport ACKs alone cannot make a pass.
An owned MEC observer samples production state only on the main thread. Its
`protocol-peer-server.jsonl` records session/reader progress, domain credits,
idle state, time scale, target frame rate, hub count and observer frame gaps. It
does not depend on a scene's `StaticUnityMethods` component surviving. Missing
observations or an observer error cannot certify completion.

The protocol workers and their disk/crypto allocations share the server process.
Their measurements therefore include this extra fixture CPU, GC and I/O load.
Report them separately from native-only transfer benchmarks. They can demonstrate
real concurrent transport, queue admission, bounded receiver credits, and native
control responsiveness during that load. They cannot establish Unity/script
activation, multiple native client frame times, WAN congestion or voice quality.
Stock simulation flags are not treated as impairment evidence: the inspected
14.2.9 release transport omits their simulation calls. A WAN-delay/loss case must
use the external private UDP relay and retain its actual forwarding/impairment
records together with measured peer RTT. No simulation setting in these peers
pretends to create that delay or loss.
Samples label the stock `Ping` value as `pingHalfRttMs` and record `roundTripMs`
as twice that value, matching the inspected game implementation. Initial zero
samples are not evidence of a zero-latency WAN path.

Build via the regular `ServerApiSmoke.csproj`; three explicit compile includes link
these sources into that private fixture. The separate `NetworkTransferPeers.csproj`
is a compile-check library, not a runnable standalone alternative or release input.
`PolicyChecks/PeerPolicyChecks.csproj` runs 16 pure offline checks for deadline,
receive-window, and out-of-order consumed-counter assertions without game runtime
references.

The initial interoperability failures remain preserved: interop01 did not handle
the native idle/startup Delay response, and interop02 timed out with invalid probe
and worker-pool assumptions. Corrected interop03 then observed stock idle entry
during authenticated Sending before the acquisition idle-ownership fix.
[Current schema-5 correctness receipts](E:/Projects/AnomalyLabs/Anomaly/artifacts/network-release/peer-correctness-schema5-inputs/results.json)
now pass normal acquisition, cancellation followed by verified cleanup and a fresh
same-identity rejoin, and the held-real-chunk counter-barrier case. Every completed
attempt independently verifies all 18 files/225,912,043 bytes and actual server
`Complete` within its original 120-second deadline; no activation or Ready is sent.
Cancellation cleanup was observed 41.5419 ms after the 16 MiB threshold-triggered
request. The held chunk proves the eight-counter credit barrier before releasing
the original; it is application dispatch reordering, not packet loss. All owned
servers were stopped and prior private configuration restored. Shared-process
CPU/GC/I/O and concurrent offline builds/tests make these correctness results,
not native activation, clean throughput, WAN, QoS, gameplay or voice benchmarks.
