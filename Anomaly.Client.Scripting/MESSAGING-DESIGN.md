# Pack script messaging

Option 1 is implemented: named, schema-checked `Messages.On`, `Messages.Off`, and
`Messages.Send` use the existing authenticated Anomaly transport. Server authors
remain ordinary LabAPI plugin authors using `ClientMessaging` and
`ClientEvents.MessageReceived`; no server Lua runtime, extra connection, or RPC
handshake is introduced. See the [server author guide](../Anomaly.Server/MODDING.md).

Remote script execution requires actual Windows AppContainer/job qualification and
explicit content/execution approval. It uses a separate bounded worker; local CLR
accounting remains unqualified. Messaging grants no filesystem, socket, reflection,
or raw transport access and cannot bypass unavailable isolation.

## Lua authoring

Register handlers during top-level initialization, before ClientReady:

```lua
local Enabled = false

local Subscription = Messages.On("GetState", function(Message)
    local Accepted, ErrorCode = Messages.Send("State", {
        RequestId = Message.RequestId,
        Enabled = Enabled
    })
    if not Accepted then
        -- ErrorCode is a bounded string; there is no arbitrary CLR exception.
        return
    end
end)

-- Optional early removal; disconnect removes all handlers automatically.
-- Messages.Off(Subscription)
```

Names and field spelling retain C# conventions. `Messages` is a read-only,
owner-bound capability, not a CLR networking object. A script supplies only the
declared message name and its primitive record. The host injects the pack ID,
exact approved index digest, and activation ID. A script cannot select another
pack, player, server, transport generation, or stock protocol contract.

`On(Name, Callback)` accepts only a declared server-to-client name and a Lua closure
owned by that VM. It returns an opaque subscription token. `Off(Token)` removes
exactly that registration; already queued deliveries for the old token are skipped,
even if the same closure is registered again. Dot and colon calls are supported.
An incoming valid message without a handler is dropped.

`Send(Name, Payload)` accepts only a declared client-to-server name. It returns
`true, nil` when the existing local transport accepts the message, which does not
mean the server received or applied it. Failures return `false, ErrorCode`:

| Code | Meaning |
| --- | --- |
| `NotReady` | No current admitted activation/player transport, or transport rejected the send |
| `UnknownMessage` | Name absent from this pack's approved index |
| `WrongDirection` | Name is declared for server-to-client traffic |
| `InvalidPayload` | Record does not match the approved types, fields, or bounds |

Top-level startup may install handlers but must not wait for a reply. General
script traffic is excluded from the pre-player bootstrap allowlist. Sends remain
`NotReady` until the existing content admission and player transport are ready.
The server can send initial application state from `ClientEvents.ContentReady`.
Local hosted scripts may register handlers before there is a remote activation;
they cannot invent a server-side pack grant to make `Send` succeed.

## Pack contracts

The automatically generated, hash-bound script-pack index uses schema 1 for a
`Messages` array. A content descriptor's optional `Scripts.Messages` selects the
JSON schema file (for example `scripts/messages.json`), including for exact-name
embedded resources. Carriers bind the script component's `Scripts.PackId`, not
the logical wrapper ID. The same fields appear in the generated index:

```json
{
  "Messages": [
    {
      "Name": "GetState",
      "Direction": "ServerToClient",
      "Version": 1,
      "Fields": [ { "Name": "RequestId", "Type": "UInt32" } ]
    },
    {
      "Name": "State",
      "Direction": "ClientToServer",
      "Version": 1,
      "Fields": [
        { "Name": "RequestId", "Type": "UInt32" },
        { "Name": "Enabled", "Type": "Boolean" }
      ]
    }
  ]
}
```

Every field is required and extra fields are rejected. There are five primitive
types: `Boolean`, `Int32`, `UInt32`, `Number`, and `String`. Integers must be whole
and within their named 32-bit range; Number must be finite. String fields require
`MaxBytes` between 1 and 1024 and strict UTF-8 without NUL. Names are case-sensitive
PascalCase identifiers, at most 64 ASCII characters. Version 1 is the supported
record version. Nested records, arrays, optional fields, enums, custom numeric
ranges, and dynamic schemas are not part of this implementation.

Lua payloads must be ordinary flat tables. The bridge reads raw fields rather
than invoking `__index`, `__pairs`, or `__tostring`. Functions, userdata, threads,
metatables, cycles, numeric dictionary keys, and nested tables are rejected. Text
in a message is data; receiving it never evaluates source.

## Queues and lifetime

| Resource | Ceiling |
| --- | --- |
| Declared schemas | 32 per pack; 256 across the catalog |
| Fields per record | 32 |
| Encoded payload | 4096 bytes |
| One string | 1024 UTF-8 bytes; a schema can set a lower maximum |
| Registrations | 32 per VM, shared with Events |
| Pending callbacks | 64 per VM, shared with Events |
| Pending message payload | 64 KiB per VM, including one charge per recipient |
| Dispatch | At most four callbacks per runtime tick within the existing 2 ms shared budget |

Existing authenticated channel limits and connection traffic limits also apply.
Queue slots, queued bytes, and allocation allowance are checked before capturing
payload snapshots or creating Lua tables. Snapshot and subscription tickets follow
their retained objects; dequeue releases queued-byte capacity, not memory still
retained by Lua or another host object. A full queue retires the receiving VM;
its required source set then fails closed. It does not enqueue an overflow error.

Network callbacks only queue immutable data. Every Lua callback runs on the owner
thread through the same instruction, deadline, depth, and experimental reclaimable
allocation checks as Events. A callback result is discarded. A terminal script
fault prevents other required VMs from being entered later in the same tick.

Both endpoints validate direction, exact fields, retained approved pack/index,
activation, and current connection generation. Disconnect, rekey, content
replacement, host stop, or session retirement invalidates subscriptions and pending
deliveries. A reconnect gets fresh state. Schema metadata is cloned before source
execution and cannot be widened by mutating producer objects afterward.

The server derives sender identity from the authenticated connection and treats
all client values as untrusted claims. Its plugin must validate permissions,
ownership, game state, distances, and cooldowns before applying gameplay effects.
`RequestId` can be an ordinary `UInt32` application field; existing plugin state
can correlate requests without a new RPC subsystem.

## Supported authoring boundary

Register handlers explicitly with `Messages.On`. Chunk results accept only bounded
primitives; returning a handler table yields `InvalidResult`. There are no
`Messages.Request` futures, request timers, retries, continuations or coroutine
API. Pair named messages with explicit application request IDs instead.
