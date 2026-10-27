using Anomaly.Client.Scripting;
using Anomaly.Client.Scripting.Remote;
using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Auth;
using Anomaly.Client.Content;
using Anomaly.Client.Internal.Hosting;
using Anomaly.Client.Internal.Networking;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using MelonLoader;

namespace Anomaly.Client.Internal.Bootstrap;

/// <summary>Runs only verified, approved script sources in the current connection scope.</summary>
internal static class ScriptingBootstrap
{
    private static ScriptRuntime _runtime;
    private static ConnectionScriptSet _localScripts;
    private static RemoteConnectionScriptSet _remoteScripts;
    private static RemoteConnectionScriptSet _initializingRemote;
    private static bool _emptyPrepared;
    private static Func<bool> _isCurrent;
    private static ScriptMessageConnection _messages;

    internal static bool IsConnectionPrepared => _isCurrent?.Invoke() == true &&
        (_emptyPrepared || _localScripts?.IsHealthy == true || _remoteScripts?.IsHealthy == true);
    internal static bool IsRemoteQualificationPending => RemoteWorkerFactory.IsQualifying;

    internal static void Initialize()
    {
        if (_runtime != null) return;
        var runtime = new ScriptRuntime(Report, textSink: new ScriptTextOverlay(), outputLog: LogOutput);
        try
        {
            runtime.DiscoverApi();
            RemoteWorkerFactory.BeginQualification(Paths.AnomalyPath);
            SynchronizePolicy();
            _runtime = runtime;
            AnomalyMessaging.ScriptMessageReceived = ReceiveMessage;
            AnomalyMessaging.ScriptMessageSendAllowed = AllowMessageSend;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static void SynchronizePolicy()
    {
        ScriptingPolicy.RemoteIsolationQualified = RemoteWorkerFactory.IsQualified;
        ScriptingPolicy.RemoteExecutionDisabledReason = RemoteWorkerFactory.DisabledReason;
    }

    private static void Report(string message) => MelonLogger.Warning("Scripting: " + message);
    private static void LogOutput(string message) => MelonLogger.Msg(message);

    private static bool AllowMessageSend(ScriptToServerMessage message) =>
        IsConnectionPrepared && _messages?.TrySend(message) == true;

    private static bool ReceiveMessage(ScriptToClientMessage message) =>
        IsConnectionPrepared && _messages?.TryReceive(message) == true &&
        (_remoteScripts?.Receive(message) == true || _localScripts?.Receive(message) == true);

    internal static void Tick()
    {
        RemoteWorkerFactory.Tick();
        SynchronizePolicy();
        ContentCatalogClient.Tick();
        var local = _localScripts;
        var remote = _remoteScripts ?? _initializingRemote;
        if (_isCurrent == null) return;
        if (!_isCurrent()) { RevokeAll(); return; }
        remote?.Tick();
        // Actor completion or native callbacks can replace/revoke this scope.
        if (!ReferenceEquals(_localScripts, local) ||
            !ReferenceEquals(_remoteScripts ?? _initializingRemote, remote)) return;
        _runtime?.Tick(() => _isCurrent?.Invoke() == true &&
            ReferenceEquals(_localScripts, local) && ReferenceEquals(_remoteScripts ?? _initializingRemote, remote));
        if (!ReferenceEquals(_localScripts, local) ||
            !ReferenceEquals(_remoteScripts ?? _initializingRemote, remote)) return;
        if ((local != null && !local.IsHealthy) || (_remoteScripts != null && !_remoteScripts.IsHealthy))
        {
            RevokeAll();
            AnomalyMessaging.FailClosedTransport("A required script stopped or exceeded its limits.");
        }
    }

    internal static void RevokeAll()
    {
        var local = _localScripts;
        var remote = _remoteScripts;
        var initializing = _initializingRemote;
        _localScripts = null;
        _remoteScripts = null;
        _initializingRemote = null;
        _emptyPrepared = false;
        _isCurrent = null;
        _messages = null;
        try { initializing?.Dispose(); }
        finally
        {
            try { remote?.Dispose(); }
            finally
            {
                try { local?.Dispose(); }
                finally { _runtime?.RevokeAll(); }
            }
        }
    }

    internal static void BeginPrepareConnection(ContentCatalog catalog, bool executionApproved, ulong activationId,
        Action<bool, string> completed)
    {
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        RevokeAll();
        if (catalog?.SchemaVersion != ContentCatalog.CurrentSchemaVersion)
        {
            completed(false, "The script catalog schema is unavailable.");
            return;
        }
        ConnectionContentBootstrap.EnsureInitializing();
        var sources = ScriptPackManager.TakeRemoteScripts();
        ValidateInventory(catalog, sources);
        long generation = AnomalyMessaging.TransportGeneration;
        string fingerprint = AuthHandler.CurrentServerFingerprint;
        var pointer = NetworkClient.connection.Pointer;
        var catalogHash = (byte[])catalog.CatalogSha256.Clone();
        var manifestHash = (byte[])catalog.ManifestSha256.Clone();
        Func<bool> current = () =>
            ConnectionContentBootstrap.IsCurrentScriptOffer(activationId, catalogHash, manifestHash) && AnomalyMessaging.TransportGeneration == generation &&
            NetworkClient.connection != null && NetworkClient.connection.Pointer == pointer &&
            string.Equals(fingerprint, AuthHandler.CurrentServerFingerprint, StringComparison.Ordinal);
        if (!current()) throw new InvalidOperationException("The approved script offer is no longer current.");
        if (sources.Count == 0)
        {
            _isCurrent = current;
            _emptyPrepared = true;
            completed(true, string.Empty);
            return;
        }
        if (!executionApproved) throw new InvalidOperationException("Script execution was not approved for this exact offer.");
        if (!RemoteWorkerFactory.IsQualified) throw new NotSupportedException(RemoteWorkerFactory.DisabledReason);
        Initialize();
        using var allocationScope = _runtime.EnterAllocationScope();
        // The immutable grants and traffic buckets live for this connection only.
        using var scratch = MoonSharp.Interpreter.MemoryBudget.Reserve(16 * 1024);
        var grants = sources.GroupBy(source => source.PackId, StringComparer.Ordinal).Select(pack => pack.First())
            .Select(source => (source.PackId, source.IndexSha256, source.MessageSchemas)).ToArray();
        var messages = MoonSharp.Interpreter.MemoryBudget.Allocate(ScriptMessageConnection.ControlPlaneAllocationBytes,
            () => new ScriptMessageConnection(activationId, grants, current,
                () => ContentCatalogClient.IsActivated && ConnectionContentBootstrap.IsAdmitted));
        var scripts = new RemoteConnectionScriptSet(_runtime, current, AnomalyMessaging.TrySend, activationId);
        _isCurrent = current;
        _messages = messages;
        _initializingRemote = scripts;
        try
        {
            scripts.BeginInitialize(sources, executionApproved, (bool success, string error) =>
            {
                if (!ReferenceEquals(_initializingRemote, scripts)) return;
                if (!current())
                {
                    RevokeAll();
                    return;
                }
                if (!success || !scripts.IsHealthy)
                {
                    RevokeAll();
                    completed(false, string.IsNullOrEmpty(error) ? "Required remote scripts failed to initialize." : error);
                    return;
                }
                _initializingRemote = null;
                _remoteScripts = scripts;
                completed(true, string.Empty);
            });
        }
        catch
        {
            if (ReferenceEquals(_initializingRemote, scripts)) RevokeAll();
            throw;
        }
    }

    private static void ValidateInventory(ContentCatalog catalog, IReadOnlyList<ScriptSource> sources)
    {
        if (sources.Count != catalog.ScriptPacks.Sum(pack => pack.Sources.Count))
            throw new InvalidOperationException("Validated Lua sources did not match the approved inventory.");
        foreach (var pack in catalog.ScriptPacks)
        {
            foreach (var declared in pack.Sources)
            {
                ScriptSource match = null;
                foreach (var source in sources)
                {
                    if (source.PackId != pack.PackId || source.Address != declared.Address) continue;
                    if (match != null) throw new InvalidOperationException("Validated Lua inventory repeats an approved source.");
                    match = source;
                }
                if (match == null || match.SourceBytes != declared.Size || !MatchesHash(match.IndexSha256, pack.IndexHash) ||
                    !MatchesHash(match.SourceSha256, declared.Hash))
                    throw new InvalidOperationException("Validated Lua sources did not match the exact approved inventory.");
            }
        }
    }

    private static bool MatchesHash(string actual, byte[] expected)
    {
        if (actual == null || expected == null || actual.Length != expected.Length * 2) return false;
        const string digits = "0123456789abcdef";
        for (var index = 0; index < expected.Length; index++)
            if (actual[index * 2] != digits[expected[index] >> 4] || actual[index * 2 + 1] != digits[expected[index] & 15])
                return false;
        return true;
    }

    internal static void PrepareLocalHostConnection()
    {
        if (!LocalHostingRuntime.IsVerifiedScriptHost) return;
        if (_localScripts != null && _isCurrent?.Invoke() == true) return;
        RevokeAll();
        var pointer = NetworkClient.connection.Pointer;
        Func<bool> current = () => LocalHostingRuntime.IsVerifiedScriptHost &&
            NetworkClient.connection != null && NetworkClient.connection.Pointer == pointer;
        Initialize();
        var scripts = new ConnectionScriptSet(_runtime, current, AnomalyMessaging.TrySend);
        if (!scripts.InitializeLocal(ScriptPackManager.ReadLocalScripts(), true, out var error))
        {
            scripts.Dispose();
            Report(error);
            throw new InvalidOperationException("Local hosted scripts failed to initialize.");
        }
        _localScripts = scripts;
        _isCurrent = current;
    }

    internal static void Shutdown()
    {
        RevokeAll();
        var runtime = _runtime;
        _runtime = null;
        AnomalyMessaging.ScriptMessageReceived = null;
        AnomalyMessaging.ScriptMessageSendAllowed = null;
        runtime?.Dispose();
    }
}
