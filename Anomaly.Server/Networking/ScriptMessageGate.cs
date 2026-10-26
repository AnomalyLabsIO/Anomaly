using System;
using System.Collections.Generic;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;

namespace Anomaly.Server.Networking;

/// <summary>Checks the recipient's retained approval before script traffic enters the transport or public API.</summary>
internal static class ScriptMessageGate
{
    private static readonly object Sync = new object();
    private static readonly Dictionary<ReferenceHub, ConnectionBudget> Budgets = new Dictionary<ReferenceHub, ConnectionBudget>();

    internal static bool TryBindOutgoing(ReferenceHub hub, IAnomalyMessage message, out IAnomalyMessage bound)
    {
        bound = message;
        if (!(message is IScriptMessage script)) return true;
        if (!(message is ScriptToClientMessage outgoing) ||
            !TryGetScope(hub, script, AnomalyMessageDirection.ServerToClient, false, out string hash, out ulong activation))
            return false;
        bound = outgoing.Bind(hash, activation);
        return TryConsume(hub, activation, true, script.PackId, script.Name, script.Payload.EncodedBytes);
    }

    internal static bool TryAcceptIncoming(ReferenceHub hub, IAnomalyMessage message)
    {
        if (!(message is IScriptMessage script)) return true;
        return message is ScriptToServerMessage &&
            TryGetScope(hub, script, AnomalyMessageDirection.ClientToServer, true, out _, out ulong activation) &&
            TryConsume(hub, activation, false, script.PackId, script.Name, script.Payload.EncodedBytes);
    }

    // Rechecked between callbacks: a preceding handler can revoke content without revoking transport authentication.
    internal static bool IsCurrent(ReferenceHub hub, IAnomalyMessage message)
    {
        if (!(message is IScriptMessage script)) return true;
        return TryGetScope(hub, script, message is ScriptToClientMessage
            ? AnomalyMessageDirection.ServerToClient : AnomalyMessageDirection.ClientToServer,
            true, out _, out _);
    }

    private static bool TryGetScope(ReferenceHub hub, IScriptMessage message, AnomalyMessageDirection direction,
        bool requireBinding, out string indexHash, out ulong activation)
    {
        indexHash = null;
        activation = 0;
        try
        {
            if (!Plugin.IsAuthenticatedAdmissionCurrent(hub) ||
                !FileTransferSession.TryGetScriptMessageScope(hub, message.PackId, message.Name, direction,
                    out var schema, out indexHash, out activation) ||
                requireBinding && (message.ActivationId != activation || message.IndexSha256 != indexHash))
                return false;
            schema.ValidatePayload(message.Payload);
            return true;
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException) && !(exception is StackOverflowException))
        { return false; }
    }

    private static bool TryConsume(ReferenceHub hub, ulong activation, bool outgoing, string packId, string name, int bytes)
    {
        lock (Sync)
        {
            if (!Budgets.TryGetValue(hub, out var budget) || budget.ActivationId != activation)
                Budgets[hub] = budget = new ConnectionBudget(activation);
            return (outgoing ? budget.Outgoing : budget.Incoming).TryAccept(packId, name, bytes);
        }
    }

    internal static void Remove(ReferenceHub hub)
    {
        if (hub == null) return;
        lock (Sync) Budgets.Remove(hub);
    }

    private sealed class ConnectionBudget
    {
        internal readonly ulong ActivationId;
        internal readonly ScriptMessageRateLimiter Incoming = new ScriptMessageRateLimiter();
        internal readonly ScriptMessageRateLimiter Outgoing = new ScriptMessageRateLimiter();
        internal ConnectionBudget(ulong activation) => ActivationId = activation;
    }
}
