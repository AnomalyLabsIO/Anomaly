using System;
using System.Diagnostics;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Auth;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Internal.Map;
using Anomaly.Client.Internal.Hosting;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2Cpp;
using Il2CppMirror;

namespace Anomaly.Client.Content;

/// <summary>Connection-owned pre-player acquisition and the single native/client readiness release.</summary>
internal static class ConnectionContentBootstrap
{
    private static readonly ConnectionContentGate Gate = new();
    private static NetworkConnection _connection;
    private static CustomNetworkManager _readyManager;
    private static CustomNetworkManager _localReadyManager;
    private static double _localReadyDeadline;
    private static IntPtr _localConnection;
    private static ulong _facilityGeneration;
    private static long _transportGeneration;
    private static bool _readyPending, _playerPending, _revoking, _admitted;
    internal static bool IsAcquiring => Gate.IsActive && !Gate.IsFailed;
    internal static bool IsAdmitted => _admitted && CanRelease;
    internal static bool IsCurrentScriptScope => Required && IsCurrentConnection && Gate.IsActive && !Gate.IsFailed &&
        (_admitted || !Gate.DeadlineExpired(Now));
    internal static bool IsCurrentScriptOffer(ulong activationId, byte[] catalogHash, byte[] manifestHash) =>
        IsCurrentScriptScope && Gate.OwnsOffer(activationId, catalogHash, manifestHash);

    internal static void EnsureInitializing()
    {
        if (!IsCurrentScriptScope) throw new InvalidOperationException("The script initialization connection is stale or expired.");
    }
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    private static bool Required => AuthHandler.UsingAnomalyAuth && !NetworkServer.active;
    private static bool CanRelease => Required && IsCurrentConnection && Gate.IsReady && ScriptingBootstrap.IsConnectionPrepared && FacilityRecipeClient.IsReady &&
        (_admitted || !Gate.DeadlineExpired(Now));
    private static bool IsCurrentConnection => _connection != null && NetworkClient.connection != null &&
        _connection.Pointer == NetworkClient.connection.Pointer &&
        _transportGeneration == AnomalyMessaging.TransportGeneration;

    internal static void Initialize()
    {
        AnomalyMessaging.ContentBootstrapMessageReceived = Receive;
        AnomalyMessaging.TransportSessionRevoked = Revoke;
    }

    internal static void Shutdown()
    {
        Revoke();
        AnomalyMessaging.ContentBootstrapMessageReceived = null;
        AnomalyMessaging.TransportSessionRevoked = null;
    }

    internal static void BeginConnection()
    {
        if (!Required) return;
        if (IsCurrentConnection) return;
        Revoke();
        _connection = NetworkClient.connection ?? throw new InvalidOperationException("Content bootstrap has no connection.");
        _transportGeneration = AnomalyMessaging.TransportGeneration;
        Gate.Begin(Now);
    }

    private static void Receive(IAnomalyMessage message)
    {
        if (!Required) throw new InvalidOperationException("A remote bootstrap was received outside authenticated remote play.");
        BeginConnection();
        if (Gate.IsFailed) throw new InvalidOperationException("The connection's content scope was revoked.");
        if (Gate.HasTimedOut(Now)) throw new TimeoutException("Required content initialization exceeded 120 seconds.");
        switch (message)
        {
            case ContentBootstrapReadyMessage ready:
                Gate.Confirm(ready.SessionId, ready.CatalogSha256, ready.ManifestSha256);
                TryRelease();
                break;
            case ContentCatalogMessage:
                ContentCatalogClient.ReceiveBootstrap(message);
                break;
            case FileManifestMessage:
            case FileChunkMessage:
            case FileTransferCompleteMessage:
            case FileTransferStatusMessage:
                FileTransferClient.ReceiveBootstrap(message);
                break;
            default:
                throw new InvalidOperationException("Unexpected connection bootstrap contract.");
        }
    }

    internal static void BeginFacilityGeneration(ulong generation)
    {
        if (!Required) return;
        BeginConnection();
        if (_facilityGeneration != 0 && generation > _facilityGeneration)
        {
            // Authenticated generation replacement precedes its fresh offer. Do not
            // reset the first offer when the initial scene Awake arrives later.
            var previouslyReady = _admitted;
            _admitted = false;
            ScriptingBootstrap.RevokeAll();
            FileTransferClient.Reset();
            ContentCatalogClient.Reset();
            Gate.Begin(Now, preserveDeadline: !previouslyReady);
        }
        _facilityGeneration = Math.Max(_facilityGeneration, generation);
    }

    internal static void BindOffer(ulong sessionId, byte[] catalog, byte[] manifest)
    {
        if (!Required) return;
        BeginConnection();
        Gate.BindOffer(sessionId, catalog, manifest);
    }

    internal static void Consent(ulong sessionId, byte[] catalog, byte[] manifest)
    {
        if (Required) Gate.Consent(sessionId, catalog, manifest);
    }

    internal static void Loaded(ulong sessionId, byte[] catalog, byte[] manifest, bool scriptsReady)
    {
        if (!scriptsReady) throw new InvalidOperationException("Required script initialization is unavailable.");
        if (Required) Gate.Loaded(sessionId, catalog, manifest, scriptsReady);
    }

    internal static bool DeferReady()
    {
        if (!Required || CanRelease) return false;
        if (!TryBeginForReadiness()) return true;
        _readyPending = true;
        return true;
    }

    internal static bool DeferPlayer()
    {
        if (!Required || CanRelease) return false;
        if (!TryBeginForReadiness()) return true;
        _playerPending = true;
        return true;
    }

    internal static bool DeferClientReady(CustomNetworkManager manager)
    {
        if (NetworkServer.active && NetworkClient.activeHost && !LocalHostingRuntime.IsVerifiedScriptHost)
        {
            if (_localReadyManager == null)
            {
                _localReadyDeadline = Now + ConnectionContentGate.TimeoutSeconds;
                _localConnection = NetworkClient.connection?.Pointer ?? IntPtr.Zero;
            }
            _localReadyManager = manager;
            return true;
        }
        if (!Required || CanRelease) return false;
        if (!TryBeginForReadiness()) return true;
        _readyManager = manager;
        return true;
    }

    private static bool TryBeginForReadiness()
    {
        try { BeginConnection(); return true; }
        catch (Exception exception)
        {
            AnomalyMessaging.FailClosedTransport("Content readiness scope could not start: " + exception.Message);
            return false;
        }
    }

    internal static void Tick()
    {
        if (_localReadyManager != null)
        {
            var manager = _localReadyManager;
            if (!NetworkServer.active || !NetworkClient.activeHost ||
                (_localConnection != IntPtr.Zero && NetworkClient.connection?.Pointer != _localConnection))
                _localReadyManager = null;
            else if (Now >= _localReadyDeadline)
            {
                _localReadyManager = null;
                AnomalyMessaging.FailClosedTransport("Local hosted script readiness exceeded 120 seconds.");
            }
            else if (LocalHostingRuntime.IsVerifiedScriptHost)
            {
                _localReadyManager = null;
                ClientRuntime.HandleClientReady(manager);
            }
        }
        if (!Required || !Gate.IsActive || Gate.IsFailed) return;
        if (!IsCurrentConnection || (!_admitted && Gate.DeadlineExpired(Now)))
        {
            AnomalyMessaging.FailClosedTransport("Required content initialization was interrupted or exceeded 120 seconds.");
            return;
        }
        if (Gate.IsReady && !ScriptingBootstrap.IsConnectionPrepared)
        {
            AnomalyMessaging.FailClosedTransport("Required script initialization was revoked before readiness.");
            return;
        }
        TryRelease();
    }

    internal static void TryRelease()
    {
        if (!CanRelease) return;
        _admitted = true;
        var ready = _readyPending;
        var player = _playerPending;
        var manager = _readyManager;
        _readyPending = _playerPending = false;
        _readyManager = null;
        try
        {
            if (ready && !NetworkClient.Ready()) throw new InvalidOperationException("Mirror Ready was rejected.");
            if (player && !NetworkClient.AddPlayer()) throw new InvalidOperationException("Mirror AddPlayer was rejected.");
            if (manager != null && CanRelease) ClientRuntime.HandleClientReady(manager);
        }
        catch (Exception exception)
        {
            AnomalyMessaging.FailClosedTransport("Required content readiness release failed: " + exception.Message);
        }
    }

    internal static void Revoke()
    {
        if (_revoking) return;
        _revoking = true;
        Gate.Fail();
        _readyPending = _playerPending = false;
        _readyManager = null;
        _connection = null;
        _localReadyManager = null;
        _localConnection = IntPtr.Zero;
        _transportGeneration = 0;
        _facilityGeneration = 0;
        _admitted = false;
        Exception failure = null;
        try
        {
            try { ScriptingBootstrap.RevokeAll(); }
            catch (Exception exception) { failure = exception; }
            try { FileTransferClient.Reset(); }
            catch (Exception exception) { failure ??= exception; }
            try { ContentCatalogClient.Reset(); }
            catch (Exception exception) { failure ??= exception; }
        }
        finally { _revoking = false; }
        if (failure != null) throw new InvalidOperationException("Connection scope cleanup failed after logical revocation.", failure);
    }
}
