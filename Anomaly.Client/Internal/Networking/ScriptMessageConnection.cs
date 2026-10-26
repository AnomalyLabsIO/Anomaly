using System;
using System.Collections.Generic;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;

namespace Anomaly.Client.Internal.Networking;

/// <summary>Immutable approved pack grants and traffic budgets for one script activation.</summary>
internal sealed class ScriptMessageConnection
{
    // At most 8 packs, 256 schemas and 8,192 fields. Two MiB covers their
    // bounded names, copied lists/dictionaries and both 256-channel rate tables.
    // Bootstrap binds this parent reservation to the connection's lifetime.
    internal const int ControlPlaneAllocationBytes = 2 * 1024 * 1024;

    private sealed class Pack
    {
        internal string IndexSha256 = string.Empty;
        internal readonly Dictionary<string, ScriptMessageSchema> Schemas = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Pack> _packs = new(StringComparer.Ordinal);
    private readonly ulong _activationId;
    private readonly Func<bool> _current;
    private readonly Func<bool> _ready;
    private readonly ScriptMessageRateLimiter _incoming;
    private readonly ScriptMessageRateLimiter _outgoing;

    internal ScriptMessageConnection(ulong activationId,
        IEnumerable<(string PackId, string IndexSha256, IReadOnlyList<ScriptMessageSchema> Schemas)> packs,
        Func<bool> current, Func<bool> ready, Func<double> clock = default!)
    {
        if (activationId == 0) throw new ArgumentOutOfRangeException(nameof(activationId));
        ArgumentNullException.ThrowIfNull(packs);
        _activationId = activationId;
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _ready = ready ?? throw new ArgumentNullException(nameof(ready));
        _incoming = new ScriptMessageRateLimiter(clock);
        _outgoing = new ScriptMessageRateLimiter(clock);
        int total = 0;
        foreach (var offered in packs)
        {
            if (!AssetPackIndexCodec.IsPackId(offered.PackId, remote: true) ||
                !AssetPackIndexCodec.IsHash(offered.IndexSha256) || offered.Schemas == null ||
                offered.Schemas.Count > ScriptMessageLimits.MaxSchemasPerPack || _packs.Count >= ScriptPackLimits.MaxScripts || _packs.ContainsKey(offered.PackId))
                throw new ArgumentException("Invalid approved script messaging pack.", nameof(packs));
            var pack = new Pack { IndexSha256 = offered.IndexSha256 };
            foreach (var schema in offered.Schemas)
            {
                if (schema == null || ++total > ScriptMessageLimits.MaxSchemasPerCatalog) throw new ArgumentException("Invalid message schema inventory.", nameof(packs));
                var copy = schema.Clone();
                copy.Validate();
                if (!pack.Schemas.TryAdd(copy.Name, copy)) throw new ArgumentException("Duplicate pack message name.", nameof(packs));
            }
            _packs.Add(offered.PackId, pack);
        }
    }

    internal bool TrySend(ScriptToServerMessage message) =>
        Authorize(message, AnomalyMessageDirection.ClientToServer) &&
        _outgoing.TryAccept(message.PackId, message.Name, message.Payload.EncodedBytes);

    internal bool TryReceive(ScriptToClientMessage message) =>
        Authorize(message, AnomalyMessageDirection.ServerToClient) &&
        _incoming.TryAccept(message.PackId, message.Name, message.Payload.EncodedBytes);

    private bool Authorize(IScriptMessage message, AnomalyMessageDirection direction)
    {
        if (message == null || !_current() || !_ready() || message.ActivationId != _activationId ||
            !_packs.TryGetValue(message.PackId, out var pack) || pack.IndexSha256 != message.IndexSha256 ||
            !pack.Schemas.TryGetValue(message.Name, out var schema) || schema.Direction != direction) return false;
        try { schema.ValidatePayload(message.Payload); return true; }
        catch (Exception exception) when (exception is ArgumentException or System.IO.InvalidDataException or OverflowException)
        { return false; }
    }
}
