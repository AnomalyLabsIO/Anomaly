# Anomaly custom networking

Anomaly carries application messages inside the game's
`EncryptedMessageOutside` envelope. Message names use a stable namespace such
as `mymod:greeting` and are represented on the wire by a four-byte FNV-1a
hash. Registration fails if two names collide.

The wire DTOs, policy interfaces, channels, registry, and crypto-independent
helpers live in `Anomaly.Shared.Networking`. Client and server companions
reference that same assembly. The client and server `AnomalyMessaging` entry
points are runtime-specific.

The current application/auth protocol is `12`. Older protocol peers are rejected
clearly during authentication; there is no downgrade path. Optional player poses
use a separately versioned renderer capability and do not change that protocol.

## Recipient-safe payload budgets

Client code can call `AnomalyMessaging.TryGetPayloadBudget(contract, out budget)`;
server code uses `ClientMessaging.TryGetPayloadBudget(player, contract, out budget)`.
The contract must already be registered for the outgoing direction. The query
does not serialize, send, advance counters or disconnect, and returns false when
the authenticated session or transport is unavailable.

`budget.MaxPayloadBytes` limits bytes written by `Serialize`.
`budget.MaxUnfragmentedPayloadBytes` additionally avoids transport fragmentation.
Both account for the selected channel, actual peer MTU, Mirror admission limit,
message contract, Anomaly framing, and directional encryption's complete marker,
nonce and tag. Local unsecured delivery reserves no AEAD overhead. The send path
rechecks because peer state and MTU can change after querying. A budget is a size
limit, not a reservation of bandwidth; pace high-frequency producers separately.

## Message contract

Every `IAnomalyMessage` must declare:

- exactly one direction: client-to-server or server-to-client;
- authenticated-session phase;
- a message-specific maximum payload size; and
- one deployed transport channel.

Inherit the matching authenticated directional base and still implement
`IAnomalyMessage`. Use separate request and response types when traffic must
flow in both directions; the registry rejects a bidirectional declaration.

```csharp
// Anomaly.Shared/Networking/Messages/MyGreetingRequest.cs
using System;
using System.IO;
using System.Text;
using Anomaly.Shared.Networking;

namespace MyMod.Shared.Messages
{
    public sealed class MyGreetingRequest :
        AuthenticatedClientToServerMessage,
        IAnomalyMessage
    {
        private const int MaxTextBytes = 256;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public string MessageName => "mymod:greeting";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 2 + MaxTextBytes + 4;

        public string Text { get; set; } = "";
        public int Value { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            byte[] bytes = Utf8.GetBytes(Text ?? "");
            if (bytes.Length > MaxTextBytes)
                throw new InvalidDataException("Greeting text is too long.");

            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
            writer.Write(Value);
        }

        public void Deserialize(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length > MaxTextBytes)
                throw new InvalidDataException("Greeting text is too long.");

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException();

            Text = Utf8.GetString(bytes);
            Value = reader.ReadInt32();
        }
    }

    public sealed class MyGreetingResponse :
        AuthenticatedServerToClientMessage,
        IAnomalyMessage
    {
        public string MessageName => "mymod:greeting-response";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 4;

        public int Value { get; set; }

        public void Serialize(BinaryWriter writer) => writer.Write(Value);
        public void Deserialize(BinaryReader reader) => Value = reader.ReadInt32();
    }
}
```

`Anomaly.Shared` targets .NET Framework 4.8 and does not reference
`UnityEngine`, Mirror, LabAPI, or MelonLoader. Serialize Unity math values as
primitive components and convert on each runtime. New files in the legacy
`Anomaly.Shared.csproj` must have explicit `<Compile Include="..."/>`
entries.

## Registration

The framework calls `AnomalyMessageRegistry.RegisterDefaults()` for its
built-in messages. A client mod and its server companion must register their
shared DTO during their own initialization on both runtimes:

```csharp
AnomalyMessageRegistry.Register(
    "mymod:greeting",
    () => new MyGreetingRequest());
AnomalyMessageRegistry.Register(
    "mymod:greeting-response",
    () => new MyGreetingResponse());
```

Register before sending. Registration samples one instance and validates its
name hash, direction, authentication phase, payload ceiling, and channel before
publishing any registry entry.

## Sending and receiving

Script packs use the two built-in `ScriptToClientMessage` and
`ScriptToServerMessage` carriers instead of a companion DLL's custom codec.
Remote script execution remains disabled until memory containment is qualified;
these transport contracts do not enable execution by themselves.
Anomaly registers these through `RegisterDefaults`. Script-pack index schema 1 declares
the exact named primitive records, already bound by the authenticated index hash.
Server plugins continue to use `ClientMessaging.TrySend` and
`ClientEvents.MessageReceived`; the runtime checks the recipient's retained pack
grant and activation before sending or publishing a received record. Lua receives
only its pack-bound `Messages.On` / `Off` / `Send` capability and bounded records.
See the [server author guide](../../Anomaly.Server/MODDING.md) and
[Lua messaging guide](../../Anomaly.Client.Scripting/MESSAGING-DESIGN.md).

The custom DTO registration below remains the path for compiled client/server
companions. All peers use the current protocol.

```csharp
// Client to server
Anomaly.Client.Api.Networking.AnomalyMessaging.Send(
    new MyGreetingRequest { Text = "hello", Value = 42 });

// Server call sites use native LabAPI Player objects.
var response = new MyGreetingResponse { Value = 42 };
Anomaly.Server.Api.Networking.ClientMessaging.Send(player, response);
Anomaly.Server.Api.Networking.ClientMessaging.SendToAll(response);
Anomaly.Server.Api.Networking.ClientMessaging.SendToAllExcept(
    excludedPlayer,
    response);
```

Receive through the API event (retain named handlers for cleanup in real plugins):

```csharp
// Client
Anomaly.Client.Api.Networking.AnomalyMessaging.MessageReceived += message =>
{
    if (message is MyGreetingResponse response)
    {
        // Handle the authenticated server message.
    }
};

// Server
Anomaly.Server.Api.Events.Handlers.ClientEvents.MessageReceived += ev =>
{
    if (ev.Message is MyGreetingRequest request)
    {
        // ev.Player is the authenticated client's native LabAPI Player.
    }
};
```

Client mods may instead subscribe to
`NetworkMessageEvents.AnomalyMessageReceived`. Each broadcast recipient and
each event subscriber is invoked in isolation: one throwing recipient or
handler is logged without suppressing later recipients, later subscribers, or
framework-internal routing.

## Transport channels

Registration and every send bind the exact runtime type, name, channel,
direction, authentication phase, payload bound, and round-scoping contract.
Undefined channels, changed factories, and replacement types borrowing a
registered name are rejected. Validation and serialization limits are checked
before a transmit replay counter is consumed.

| Value | Channel | Use |
|---:|---|---|
| 0 | `ReliableOrdered` | Commands and ordered state changes |
| 1 | `Unreliable` | Loss-tolerant, lowest-latency samples |
| 2 | `Sequenced` | High-frequency state where only the newest value matters |
| 3 | `ReliableSequenced` | Reliable state where only the newest value matters |
| 4 | `ReliableUnordered` | Reliable transfers with no ordering requirement |

Do not assume all LiteNetLib channels have the same MTU or fragmentation
behavior. In addition to the DTO's `MaxPayloadBytes`, the send path queries
the deployed transport's maximum packet size for the selected channel and
rejects an encoded envelope that does not fit.

## Wire format and containment

```text
EncryptedMessageOutside (AES-GCM for remote authenticated sessions):
  1 byte   0xFE Anomaly channel marker
  4 bytes  per-session, per-transport-channel replay counter
  4 bytes  FNV-1a message-name hash
  N bytes  message payload
```

Only an explicitly local transport may use the local unsecured path. Remote
messages require an authenticated encryption session. Before invoking a
handler, each runtime verifies the envelope size, channel, hash registration,
direction, authentication phase, declared payload limit, strict decoding, and
absence of trailing bytes. Aggregate message/byte and per-hash rate limits are
applied before payload deserialization.

Counters start at one for each channel and session. Ordered channels require a
strict increase; unordered channels use a 128-bit sliding replay window.
Counter zero and `uint.MaxValue` are invalid, and the connection is
disconnected before a transmit counter can wrap. Replay and rate-limit state is
cleared on disconnect; a new authenticated session starts with fresh counters.

## Naming and compact numeric data

Use lowercase `yourmod:messagename` names so another mod cannot accidentally
reuse your logical contract.

For bandwidth-sensitive values, `HalfHelper.WriteHalf` and
`HalfHelper.ReadHalf` encode IEEE-754 half precision identically on the two
target runtimes:

```csharp
HalfHelper.WriteHalf(writer, weight);
float weight = HalfHelper.ReadHalf(reader);
```

Custom networking carries authenticated, bounded application records. Content
delivery uses its dedicated catalog, consent, manifest and transfer flow.
Script packs declare bounded scalar messages in their generated indexes; their
Lua code runs only within the connection's approved sandbox capabilities.

## Project setup

`Anomaly.Shared` and `Anomaly.Server` are legacy MSBuild projects and
enumerate source files explicitly. `Anomaly.Client` also disables default
compile items. Add new `.cs` files to each affected project. The SDK-style
`Anomaly.Client.Api` project uses default compile inclusion.

The authenticated catalog binds data-only AssetBundle/index pairs and independent
script-pack indexes/source files. Asset-dependent state requires the exact
Activated acknowledgement after validation and native publication; scripts also
require execution consent and successful initialization before ClientReady.
See [AssetBundles](../../ASSETBUNDLES.md) and
[script packs](../../Anomaly.Client.Scripting/SCRIPT-PACKS.md).
