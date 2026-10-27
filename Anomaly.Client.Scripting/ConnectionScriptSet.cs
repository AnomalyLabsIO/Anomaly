using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Anomaly.Client.Scripting.Messaging;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting;

/// <summary>One verified local host's all-or-nothing in-process initialization on its owner thread.</summary>
internal sealed class ConnectionScriptSet : IDisposable
{
    private readonly ScriptRuntime _runtime;
    private readonly Func<bool> _isCurrent;
    private readonly List<ScriptSession> _sessions;
    private readonly List<MessageGrant> _grants;
    private readonly Func<ScriptToServerMessage, bool>? _send;
    private readonly ulong _activationId;
    private bool _attempted, _initialized, _disposed;

    internal ConnectionScriptSet(ScriptRuntime runtime, Func<bool> isCurrent,
        Func<ScriptToServerMessage, bool>? send = null, ulong activationId = 0)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(isCurrent);
        _runtime = runtime;
        _isCurrent = isCurrent;
        _send = send;
        _activationId = activationId;
        using var memoryScope = runtime.EnterAllocationScope();
        _sessions = MemoryBudget.Allocate(64L + 8L * ScriptPackLimits.MaxScripts,
            () => new List<ScriptSession>(ScriptPackLimits.MaxScripts));
        _grants = MemoryBudget.Allocate(64L + 8L * ScriptPackLimits.MaxScripts,
            () => new List<MessageGrant>(ScriptPackLimits.MaxScripts));
    }

    internal bool IsHealthy
    {
        get
        {
            if (!_initialized || _disposed) return false;
            try
            {
                if (!_isCurrent()) return false;
                foreach (var session in _sessions) if (session.IsRetired) return false;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            { return false; }
        }
    }

    internal bool InitializeLocal(IReadOnlyList<ScriptSource> sources, bool approved, out string error)
    {
        error = string.Empty;
        if (_disposed)
        {
            error = "The connection script set has been disposed.";
            return false;
        }
        try
        {
            if (_attempted) throw new InvalidOperationException("A connection script set initializes once.");
            _attempted = true;
            ArgumentNullException.ThrowIfNull(sources);
            if (!_isCurrent()) throw new InvalidOperationException("The script offer is no longer current.");
            if (sources.Count > ScriptPackLimits.MaxScripts)
                throw new InvalidDataException("The connection script count exceeds its limit.");
            if (sources.Count != 0 && !approved)
                throw new InvalidOperationException("Script execution was not approved for this exact offer.");
            if (sources.Count == 0)
            {
                _initialized = true;
                return true;
            }

            using var memoryScope = _runtime.EnterAllocationScope();
            // Freeze the inventory before any script can run. Validate the entire
            // batch before constructing the first VM, including duplicate identities.
            var ordered = MemoryBudget.Allocate(32L + 8L * sources.Count,
                () => sources.OrderBy(source => source?.PackId, StringComparer.Ordinal)
                    .ThenBy(source => source?.Address, StringComparer.Ordinal).ToArray());
            var total = 0;
            for (var index = 0; index < ordered.Length; index++)
            {
                var source = ordered[index] ?? throw new InvalidDataException("The script inventory contains a null entry.");
                source.Validate();
                if (source.MessageSchemas != null)
                    source = ordered[index] = MemoryBudget.Allocate(256L + source.MessageSchemas.Count * 512L +
                        source.MessageSchemas.Sum(schema => schema.Fields.Count * 256L),
                        () => source with { MessageSchemas = Array.AsReadOnly(source.MessageSchemas.Select(schema => schema.Clone()).ToArray()) });
                total = checked(total + source.SourceBytes);
                if (total > ScriptPackLimits.MaxScriptBytes)
                    throw new InvalidDataException("The connection script source exceeds its aggregate byte limit.");
                for (var previous = 0; previous < index; previous++)
                {
                    if (ordered[previous].Address == source.Address)
                        throw new InvalidDataException("The connection script inventory repeats an address.");
                    if (ordered[previous].PackId == source.PackId && !ordered[previous].HasSameContracts(source))
                        throw new InvalidDataException("Scripts in the same pack must retain the same approved message contracts.");
                }
            }
            foreach (var source in ordered)
            {
                if (!_isCurrent()) throw new InvalidOperationException("The script offer changed before initialization.");
                var context = source.MessageSchemas == null ? null : MemoryBudget.Allocate(256,
                    () => new ScriptMessageContext(source.PackId, source.IndexSha256!, _activationId,
                        source.MessageSchemas, _isCurrent, _send));
                var session = _runtime.CreateLocalSession(source, messageContext: context);
                _sessions.Add(session);
                // Once compilation owns the source, dispatch needs only these
                // bounded identities, not another full source/contract graph.
                _grants.Add(MemoryBudget.Allocate(128, () => new MessageGrant(source.PackId, source.IndexSha256)));
                var result = session.Load(source.Source, source.Address);
                if (!result.Succeeded)
                    throw new InvalidOperationException(source.Address + ": " + result.Status + ": " + result.Message);
                if (!_isCurrent()) throw new InvalidOperationException("The script offer changed during initialization.");
            }
            if (!_isCurrent()) throw new InvalidOperationException("The script offer changed before publication.");
            _initialized = true;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            _initialized = false;
            _runtime.RevokeAll();
            _sessions.Clear();
            _grants.Clear();
            // Preserve bounded source-address diagnostics without retaining an
            // exception graph or exposing internal CLR stack traces.
            var message = exception.Message;
            error = message.Length <= 1024 ? message : new string(message.AsSpan(0, 1024));
            return false;
        }
        catch
        {
            _initialized = false;
            _runtime.RevokeAll();
            _sessions.Clear();
            _grants.Clear();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;
        _runtime.RevokeAll();
        _sessions.Clear();
        _grants.Clear();
    }

    internal bool Receive(ScriptToClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsHealthy || _activationId == 0 || message.ActivationId != _activationId) return false;
        var found = false;
        for (var index = 0; index < _grants.Count; index++)
        {
            var grant = _grants[index];
            if (grant.PackId != message.PackId) continue;
            if (grant.IndexSha256 != message.IndexSha256 || !_sessions[index].ReceiveMessage(message)) return false;
            found = true;
        }
        return found;
    }


    private sealed record MessageGrant(string PackId, string? IndexSha256);
}
