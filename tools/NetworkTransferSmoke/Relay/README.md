# Private UDP impairment relay

This standalone .NET 8 qualification helper delays and drops **actual UDP
datagrams**, including reliable ACKs and pings. The shipped SCP:SL 14.2.9 server
retains LiteNetLib simulation fields but omits their release-build call sites;
setting those fields alone is not WAN evidence.

The relay forwards opaque bytes. It changes no game protocol, credentials,
encryption, transfer scheduling, or production assembly. It is never packaged.
Only `127.0.0.2:<port>` to `127.0.0.1:<same port>` is accepted. The existing server
continues to listen on its original game port. Clients connect to the second
loopback address using that same port number; no production port requirement is
introduced. Each client source endpoint gets an independent ordinary ephemeral
upstream UDP socket, retaining separate server peers.

```powershell
dotnet build tools/NetworkTransferSmoke/Relay/NetworkImpairmentRelay.csproj `
  -c Release -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=false

dotnet tools/NetworkTransferSmoke/Relay/bin/Release/net8.0/NetworkImpairmentRelay.dll `
  --self-test --output artifacts/network-release/relay-self-test

dotnet tools/NetworkTransferSmoke/Relay/bin/Release/net8.0/NetworkImpairmentRelay.dll `
  --front 127.0.0.2:29149 --upstream 127.0.0.1:29149 `
  --rtt-ms 50 --loss-percent 0 --seed 129734 `
  --stats artifacts/network-release/relay-rtt50.json `
  --stop-file artifacts/network-release/relay-rtt50.stop
```

Start the existing private server bound specifically to `127.0.0.1` before the
relay; a wildcard server binding may prevent the second loopback binding. Point
the existing ModLaunch smoke harness at `127.0.0.2:29149`. Disable stock simulation
flags for these runs and describe the relay settings separately from measured
peer ping. Use a fresh evidence path/stop file for each run. Creating the stop file
or pressing Ctrl+C cancels I/O, closes all sockets, and writes final JSON. JSON is
also atomically refreshed each second. Exit 0 requires `qualificationValid: true`.

`--rtt-ms` is the fixed added round-trip delay: half is applied independently
before forwarding in each direction, scheduled using `Stopwatch` monotonic ticks.
`--loss-percent` is an independent probability in **each direction**, with separate
seeded random streams per flow/direction. A UDP request/echo therefore survives
with probability `(1-p)^2`. Seeding makes loss decisions repeatable for the same
per-flow arrival sequence; concurrency can change that sequence. No bandwidth
cap, congestion, jitter, packet reordering, or internet-routing model is implied.
Report measured RTT, scheduling lateness, native retransmits, and achieved goodput.

The delayed payload queue is bounded to 64 MiB and 65,536 datagrams; at most 16
live flows exist. Each socket has bounded kernel buffers and a 65,535-byte receive
buffer. Idle flows expire after 120 seconds. CLI limits can reduce these bounds:
`--max-queue-mib`, `--max-flows`, `--idle-seconds`. Queue overflow, a flow-limit
violation, unexpected socket errors, or a non-loopback source invalidates the run
and closes the relay. JSON distinguishes intentional loss from shutdown/expired
flow discards. Any invalid relay report invalidates the game qualification.
Windows `SIO_UDP_CONNRESET` notifications are disabled on front and upstream
sockets: normal auth retries close retired client ports, and their ICMP
port-unreachable responses must not terminate unrelated flows. Any remaining
transient reset/refused ICMP notifications are counted and ignored; protocol
timeouts still detect a disappeared endpoint. JSON records this suppression and
the observed notification counts (suppressed notifications cannot be counted).

The self-test uses an isolated loopback echo server on an OS-selected port. It
verifies exact SHA-256 payload preservation (including zero-byte and 60,000-byte
datagrams), three independent client flows, measured added RTT at 0/20/50/100 ms,
actual bidirectional seeded loss, and fail-closed byte/flow limits. It never starts
a game or modifies an owner cache/profile. It also closes eight client ports
before their delayed echo responses and verifies a replacement flow still works.
