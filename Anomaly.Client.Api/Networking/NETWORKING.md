# Anomaly Custom Networking

Anomaly provides a custom message system built on top of Mirror's `EncryptedMessageOutside` envelope. Messages are identified by namespaced string names (e.g., `"anomaly:blendshapes"`), hashed to 4 bytes on the wire using FNV-1a. The registry detects hash collisions at startup.

Message definitions, the `IAnomalyMessage` interface, the `MessageChannel` enum, the `AnomalyChannel` marker byte, the `HalfHelper` half-precision utilities, and the `AnomalyMessageRegistry` all live in `Anomaly.Shared` under `Anomaly.Shared.Networking`. Both client and server reference a single source of truth, so there's no risk of the two sides drifting on wire format. Only `AnomalyMessaging` (the send/receive entry point) is side-specific because client-to-server and server-to-clients are inherently asymmetric operations.

## Registering a New Message

### 1. Define your message class once in Anomaly.Shared

```csharp
// Anomaly.Shared/Networking/Messages/MyMessage.cs
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class MyMessage : IAnomalyMessage
    {
        public string MessageName => "mymod:greeting";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public string Text { get; set; }
        public int Value { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Text ?? "");
            writer.Write(Value);
        }

        public void Deserialize(BinaryReader reader)
        {
            Text = reader.ReadString();
            Value = reader.ReadInt32();
        }
    }
}
```

Anomaly.Shared targets .NET Framework 4.8 and does not reference `UnityEngine`, `Mirror`, `LabApi`, or `MelonLoader`. If your message needs `Vector3`/`Quaternion`, serialize the float components in Shared and convert on each side (see `BoneTransformsMessage` for the pattern).

Don't forget to add a `<Compile Include="Networking\Messages\MyMessage.cs" />` entry to `Anomaly.Shared.Shared.csproj` — it's a legacy MSBuild project so new files must be listed explicitly.

### 2. Register with the registry

The built-in Anomaly messages are registered by `AnomalyMessageRegistry.RegisterDefaults()`, which both sides call during bootstrap. For your own messages, call `Register` after `RegisterDefaults()`:

```csharp
// Client -- inside the Harmony patch at Anomaly.Client/Patches/Mirror/NetworkManagerRegisterClientMessages.cs
AnomalyMessageRegistry.Register("mymod:greeting", () => new MyMessage());

// Server -- inside Plugin.Enable() after RegisterDefaults()
AnomalyMessageRegistry.Register("mymod:greeting", () => new MyMessage());
```

The registry logs through `AnomalyMessageRegistry.Log` / `AnomalyMessageRegistry.Warn` delegates, which each side wires up during bootstrap (client → `MelonLogger`, server → `LabApi.Logger`).

### 3. Send a message

```csharp
// From client to server
AnomalyMessaging.Send(new MyMessage { Text = "hello", Value = 42 });

// From server to a specific client
AnomalyMessaging.SendToClient(hub, new MyMessage { Text = "world", Value = 7 });

// From server to all clients
AnomalyMessaging.SendToAll(new MyMessage { Text = "broadcast", Value = 1 });

// From server to all clients except one (e.g., relay to others)
AnomalyMessaging.SendToAllExcept(senderHub.connectionToClient, msg);
```

### 4. Receive messages

```csharp
// Client -- generic event (all message types)
AnomalyMessaging.MessageReceived += msg =>
{
    if (msg is MyMessage my)
    {
        // handle
    }
};

// Client -- via the event system (AnomalyEventHandler pattern)
NetworkMessageEvents.AnomalyMessageReceived += ev =>
{
    if (ev.Message is MyMessage my)
    {
        // handle
    }
};

// Server
AnomalyMessaging.MessageReceived += (senderHub, msg) =>
{
    if (msg is MyMessage my)
    {
        // senderHub is the ReferenceHub of the client that sent it
    }
};
```

## Transport Channels

Each message declares a `TransportChannel` that controls delivery guarantees:

| Channel | Delivery           | Use For                                    |
|---------|--------------------|--------------------------------------------|
| 0       | ReliableOrdered    | One-shot events, state changes, commands   |
| 1       | Unreliable         | Fire-and-forget, lowest latency            |
| 2       | Sequenced          | High-frequency state (only latest arrives) |
| 3       | ReliableSequenced  | Reliable but only latest matters           |
| 4       | ReliableUnordered  | Reliable, order doesn't matter             |

For data that updates every frame (blend shapes, bone transforms), use channel **2 (Sequenced)** so stale updates are automatically dropped by the transport.

## Wire Format

```
EncryptedMessageOutside envelope (AES-GCM encrypted, except for local-host Unsecured):
  [1 byte: 0xFE]                        Anomaly channel marker (AnomalyChannel.ChannelId)
  [4 bytes: uint counter]               Per-(hub × TransportChannel) monotonic replay counter
  [4 bytes: uint FNV-1a hash]           Message type identifier
  [N bytes: message-specific payload]   Output of Serialize()
```

### Replay protection

The counter is maintained per `(hub, MessageChannel)` pair on both sides. On receive,
it's validated against a strict monotonic check (for ordered channels) or a 128-bit
sliding window (for `ReliableUnordered` and `Unreliable`). Counter starts at 1 per
channel per session and wraps cleanly at `uint.MaxValue`; session counters are
cleared on disconnect.

Mods using the public `AnomalyMessaging.Send` / `MessageReceived` API need no
changes — the counter is written and validated at the transport layer. Custom
wire-format parsers must account for the 4 extra bytes and assume protocol
version `AnomalyProtocol.CurrentVersion` (currently `1`).

## Naming Convention

Use the format `"yourmod:messagename"` to avoid collisions with other mods. The registry will throw at startup if two different names produce the same FNV-1a hash (astronomically unlikely, but handled).

## Half-Precision Floats

For bandwidth-sensitive data like blend shape weights (0.0-1.0), use `HalfHelper` from `Anomaly.Shared.Networking`:

```csharp
HalfHelper.WriteHalf(writer, weight);
float w = HalfHelper.ReadHalf(reader);
```

This works identically on both sides regardless of whether the runtime has native `System.Half` support. The wire format is IEEE-754 half-precision, 2 bytes per value.

## Unity Math Types

`Anomaly.Shared` does not reference `UnityEngine`, so messages that need `Vector3`/`Quaternion` should serialize the float components directly and let each side convert:

```csharp
// In Anomaly.Shared -- just primitive floats
public struct BoneEntry
{
    public byte Index;
    public float PositionX, PositionY, PositionZ;
    public float RotationX, RotationY, RotationZ, RotationW;
    // ...
}

// On the client/server when constructing an entry from Vector3/Quaternion:
new BoneEntry(boneIndex, pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w)
```

## Project Setup (Legacy .csproj)

`Anomaly.Shared` and `Anomaly.Server` use legacy MSBuild — new source files need explicit `<Compile Include>` entries. `Anomaly.Client` is SDK-style but also lists sources explicitly (it sets `EnableDefaultCompileItems=false`), so add your new files there too. `Anomaly.Client.Api` uses default compile-item inclusion — new `.cs` files dropped into its directory auto-compile.
