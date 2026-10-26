using Anomaly.Client.Scripting;
using System;
using System.Linq;
using Anomaly.Client.Api.Networking;
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
    private static ConnectionScriptSet _scripts;
    private static Func<bool> _isCurrent;
    private static ScriptMessageConnection _messages;

    internal static bool IsConnectionPrepared => _scripts?.IsHealthy == true && _isCurrent?.Invoke() == true;

    internal static void Initialize()
    {
        if (_runtime != null) return;
        var runtime = new ScriptRuntime(Report, textSink: new ScriptTextOverlay(), outputLog: LogOutput);
        try
        {
            runtime.DiscoverApi();
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

    private static void Report(string message) => MelonLogger.Warning("Scripting: " + message);
    private static void LogOutput(string message) => MelonLogger.Msg(message);

    private static bool AllowMessageSend(ScriptToServerMessage message) =>
        IsConnectionPrepared && _messages?.TrySend(message) == true;

    private static bool ReceiveMessage(ScriptToClientMessage message) =>
        IsConnectionPrepared && _messages?.TryReceive(message) == true && _scripts.Receive(message);

    internal static void Tick()
    {
        var scripts = _scripts;
        if (scripts == null) return;
        if (_isCurrent?.Invoke() != true) { RevokeAll(); return; }
        if (scripts.IsHealthy)
            _runtime.Tick(() => ReferenceEquals(_scripts, scripts) && scripts.IsHealthy && _isCurrent?.Invoke() == true);
        // A failed send can synchronously revoke transport and this script set.
        // Never inspect or retire a replacement installed by lifecycle cleanup.
        if (!ReferenceEquals(_scripts, scripts)) return;
        if (!scripts.IsHealthy)
        {
            RevokeAll();
            AnomalyMessaging.FailClosedTransport("A required script stopped or exceeded its limits.");
        }
    }

    internal static void RevokeAll()
    {
        var scripts = _scripts;
        _scripts = null;
        _isCurrent = null;
        _messages = null;
        try { scripts?.Dispose(); }
        finally { _runtime?.RevokeAll(); }
    }

    internal static bool PrepareConnection(ContentCatalog catalog, bool executionApproved, ulong activationId)
    {
        RevokeAll();
        if (catalog?.SchemaVersion != ContentCatalog.CurrentSchemaVersion) return false;
        ConnectionContentBootstrap.EnsureInitializing();
        var sources = ScriptPackManager.TakeRemoteScripts();
        if (sources.Count != catalog.ScriptPacks.Sum(pack => pack.Sources.Count) ||
            sources.Sum(source => source.SourceBytes) != catalog.ScriptPacks.Sum(pack => pack.Sources.Sum(source => (long)source.Size)))
            throw new InvalidOperationException("Validated Lua sources did not match the approved inventory.");
        if (sources.Count != 0) RemoteExecutionGate.EnsureAllowed();
        long generation = AnomalyMessaging.TransportGeneration;
        var pointer = NetworkClient.connection.Pointer;
        var catalogHash = (byte[])catalog.CatalogSha256.Clone();
        var manifestHash = (byte[])catalog.ManifestSha256.Clone();
        Func<bool> current = () =>
            ConnectionContentBootstrap.IsCurrentScriptOffer(activationId, catalogHash, manifestHash) && AnomalyMessaging.TransportGeneration == generation &&
            NetworkClient.connection != null && NetworkClient.connection.Pointer == pointer;
        if (sources.Count != 0)
        {
            Initialize();
            using var allocationScope = _runtime.EnterAllocationScope();
            // Bounded grouping scratch is transient. The immutable grants and
            // traffic buckets remain charged while the connection bridge lives.
            using var scratch = MoonSharp.Interpreter.MemoryBudget.Reserve(16 * 1024);
            var grants = sources.GroupBy(source => source.PackId, StringComparer.Ordinal).Select(pack => pack.First())
                .Select(source => (source.PackId, source.IndexSha256, source.MessageSchemas)).ToArray();
            _messages = MoonSharp.Interpreter.MemoryBudget.Allocate(ScriptMessageConnection.ControlPlaneAllocationBytes,
                () => new ScriptMessageConnection(activationId, grants, current,
                    () => ContentCatalogClient.IsActivated && ConnectionContentBootstrap.IsAdmitted));
        }
        return Prepare(sources, executionApproved, current, activationId);
    }

    internal static void PrepareLocalHostConnection()
    {
        if (!LocalHostingRuntime.IsVerifiedScriptHost) return;
        if (_scripts != null && _isCurrent?.Invoke() == true) return;
        RevokeAll();
        var pointer = NetworkClient.connection.Pointer;
        // Host identity is a paired Mirror connection; local sources are read only in this scope.
        if (!Prepare(ScriptPackManager.ReadLocalScripts(), true, () =>
            LocalHostingRuntime.IsVerifiedScriptHost && NetworkClient.connection.Pointer == pointer, local: true))
            throw new InvalidOperationException("Local hosted scripts failed to initialize.");
    }

    private static bool Prepare(System.Collections.Generic.IReadOnlyList<ScriptSource> sources, bool approved, Func<bool> current,
        ulong activationId = 0, bool local = false)
    {
        Initialize();
        var scripts = new ConnectionScriptSet(_runtime, current, AnomalyMessaging.TrySend, activationId);
        string error;
        bool initialized = local ? scripts.InitializeLocal(sources, approved, out error) : scripts.Initialize(sources, approved, out error);
        if (!initialized)
        {
            scripts.Dispose();
            _messages = null;
            Report(error);
            return false;
        }
        _scripts = scripts;
        _isCurrent = current;
        return true;
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
