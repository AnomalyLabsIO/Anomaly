using System;
using System.Linq;
using System.Reflection;
using Anomaly.Client.Auth;
using Anomaly.Client.Content;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Client.Internal.Map;
using Anomaly.Client.Internal.Hosting;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using Xunit;

namespace Anomaly.Shared.Tests;

// Real coordinator/gate with inert transport, facility and native readiness calls.
public sealed class ConnectionContentBootstrapTests : IDisposable
{
    private static byte[] Hash(byte value) => Enumerable.Repeat(value, 32).ToArray();
    public ConnectionContentBootstrapTests()
    {
        Dispose();
        AuthHandler.UsingAnomalyAuth = true;
        NetworkClient.connection = new NetworkConnection();
        ConnectionContentBootstrap.Initialize();
        ConnectionContentBootstrap.BeginConnection();
    }

    private static void InitializeContent()
    {
        ConnectionContentBootstrap.BindOffer(1, Hash(1), Hash(2));
        ConnectionContentBootstrap.Consent(1, Hash(1), Hash(2));
        ConnectionContentBootstrap.Loaded(1, Hash(1), Hash(2), true);
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("crypto")]
    [InlineData("generation")]
    public void MessageAdmissionRequiresTheExactPreparedAndConfirmedConnection(string replacement)
    {
        ConnectionContentBootstrap.BeginFacilityGeneration(1);
        InitializeContent();
        Assert.False(ConnectionContentBootstrap.IsAdmitted);
        AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
        { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) });
        Assert.False(ConnectionContentBootstrap.IsAdmitted);
        FacilityRecipeClient.IsReady = true;
        ConnectionContentBootstrap.TryRelease();
        Assert.True(ConnectionContentBootstrap.IsAdmitted);
        ScriptingBootstrap.ScriptsReady = false;
        Assert.False(ConnectionContentBootstrap.IsAdmitted);
        ScriptingBootstrap.ScriptsReady = true;
        Assert.True(ConnectionContentBootstrap.IsAdmitted);

        if (replacement == "connection") NetworkClient.connection = new NetworkConnection { Pointer = new IntPtr(2) };
        else if (replacement == "crypto") AnomalyMessaging.ClearTransportSession();
        else ConnectionContentBootstrap.BeginFacilityGeneration(2);
        Assert.False(ConnectionContentBootstrap.IsAdmitted);
    }

    [Fact]
    public void Native_and_public_ready_wait_for_local_initialization_server_confirmation_and_facility()
    {
        Assert.True(ConnectionContentBootstrap.DeferReady());
        Assert.True(ConnectionContentBootstrap.DeferPlayer());
        Assert.True(ConnectionContentBootstrap.DeferClientReady(new Il2Cpp.CustomNetworkManager()));
        InitializeContent();
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, NetworkClient.ReadyCalls);
        AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
        { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) });
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        FacilityRecipeClient.IsReady = true;
        ConnectionContentBootstrap.Tick();
        ConnectionContentBootstrap.Tick();
        Assert.Equal(1, NetworkClient.ReadyCalls);
        Assert.Equal(1, NetworkClient.AddPlayerCalls);
        Assert.Equal(1, ClientRuntime.ReadyPublications);
    }

    [Fact]
    public void Timeout_covers_silent_missing_offer_and_revokes_pending_native_work()
    {
        ConnectionContentBootstrap.DeferReady();
        ConnectionContentBootstrap.DeferPlayer();
        var gate = (ConnectionContentGate)typeof(ConnectionContentBootstrap)
            .GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        gate.Begin(-ConnectionContentGate.TimeoutSeconds); // Expired monotonic deadline, without sleeping.
        var connection = NetworkClient.connection;
        ConnectionContentBootstrap.Tick();
        Assert.Equal(1, connection.Disconnects);
        Assert.True(ScriptingBootstrap.Revocations > 0);
        Assert.Equal(0, NetworkClient.ReadyCalls);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
    }

    [Fact]
    public void Facility_completion_after_the_deadline_cannot_release_before_the_next_tick()
    {
        InitializeContent();
        ConnectionContentBootstrap.DeferReady();
        ConnectionContentBootstrap.DeferPlayer();
        AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
        { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) });
        var gate = (ConnectionContentGate)typeof(ConnectionContentBootstrap)
            .GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        typeof(ConnectionContentGate).GetField("_deadline", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(gate, -1d);
        FacilityRecipeClient.IsReady = true;
        ConnectionContentBootstrap.TryRelease();
        Assert.True(ConnectionContentBootstrap.DeferReady());
        Assert.Equal(0, NetworkClient.ReadyCalls);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        ConnectionContentBootstrap.Tick();
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void Crypto_revocation_and_replacement_connection_cannot_replay_previous_completion()
    {
        InitializeContent();
        ConnectionContentBootstrap.DeferPlayer();
        AnomalyMessaging.ClearTransportSession();
        NetworkClient.connection = new NetworkConnection { Pointer = new IntPtr(2) };
        ConnectionContentBootstrap.BeginConnection();
        FacilityRecipeClient.IsReady = true;
        Assert.Throws<System.IO.InvalidDataException>(() => AnomalyMessaging.ContentBootstrapMessageReceived(
            new ContentBootstrapReadyMessage { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) }));
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
    }

    [Fact]
    public void Script_cleanup_failure_does_not_skip_transfer_and_offer_invalidation()
    {
        var field = typeof(ContentCatalogClient).GetField("_offerGeneration", BindingFlags.NonPublic | BindingFlags.Static);
        var before = (long)field.GetValue(null);
        ScriptingBootstrap.ThrowOnRevoke = true;
        Assert.Throws<InvalidOperationException>(ConnectionContentBootstrap.Revoke);
        Assert.True((long)field.GetValue(null) > before);
        Assert.False(ConnectionContentBootstrap.IsAcquiring);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
    }

    [Fact]
    public void A_completed_previous_scene_gets_a_fresh_deadline_even_after_scene_readiness_is_cleared()
    {
        ConnectionContentBootstrap.BeginFacilityGeneration(1);
        InitializeContent();
        FacilityRecipeClient.IsReady = true;
        AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
        { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) });
        var gate = (ConnectionContentGate)typeof(ConnectionContentBootstrap)
            .GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var deadline = typeof(ConnectionContentGate).GetField("_deadline", BindingFlags.NonPublic | BindingFlags.Instance);
        deadline.SetValue(gate, -1d); // The previous admitted scene ran longer than 120 seconds.
        FacilityRecipeClient.IsReady = false;
        ConnectionContentBootstrap.BeginFacilityGeneration(2);
        Assert.True((double)deadline.GetValue(gate) > 0);
        Assert.True(ConnectionContentBootstrap.DeferPlayer());
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
    }

    [Fact]
    public void Data_readiness_cannot_substitute_for_failed_script_preparation()
    {
        ConnectionContentBootstrap.BindOffer(1, Hash(1), Hash(2));
        ConnectionContentBootstrap.Consent(1, Hash(1), Hash(2));
        Assert.Throws<InvalidOperationException>(() => ConnectionContentBootstrap.Loaded(1, Hash(1), Hash(2), false));
        Assert.Throws<InvalidOperationException>(() => AnomalyMessaging.ContentBootstrapMessageReceived(
            new ContentBootstrapReadyMessage { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) }));
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
    }

    [Fact]
    public void Confirmed_gate_cannot_release_after_its_prepared_script_set_is_revoked()
    {
        Assert.True(ConnectionContentBootstrap.DeferReady());
        Assert.True(ConnectionContentBootstrap.DeferPlayer());
        Assert.True(ConnectionContentBootstrap.DeferClientReady(new Il2Cpp.CustomNetworkManager()));
        InitializeContent();
        AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
        { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) });
        var connection = NetworkClient.connection;
        ScriptingBootstrap.ScriptsReady = false;
        FacilityRecipeClient.IsReady = true;
        ConnectionContentBootstrap.TryRelease();
        Assert.True(ConnectionContentBootstrap.DeferReady());
        Assert.True(ConnectionContentBootstrap.DeferPlayer());
        Assert.True(ConnectionContentBootstrap.DeferClientReady(new Il2Cpp.CustomNetworkManager()));
        Assert.Equal(0, NetworkClient.ReadyCalls);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        ConnectionContentBootstrap.Tick();
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Equal(0, NetworkClient.ReadyCalls);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, ClientRuntime.ReadyPublications);
    }

    [Fact]
    public void Early_facility_awake_preserves_initialized_scripts_until_confirmed_readiness()
    {
        ConnectionContentBootstrap.DeferReady();
        ConnectionContentBootstrap.DeferPlayer();
        ConnectionContentBootstrap.DeferClientReady(new Il2Cpp.CustomNetworkManager());
        InitializeContent();
        int revocations = ScriptingBootstrap.Revocations;
        ContentCatalogClient.BeginFacilityScene();
        Assert.Equal(revocations, ScriptingBootstrap.Revocations);
        Assert.True(ScriptingBootstrap.IsConnectionPrepared);
        FacilityRecipeClient.IsReady = true;
        AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
        { SessionId = 1, CatalogSha256 = Hash(1), ManifestSha256 = Hash(2) });
        Assert.Equal(1, NetworkClient.ReadyCalls);
        Assert.Equal(1, NetworkClient.AddPlayerCalls);
        Assert.Equal(1, ClientRuntime.ReadyPublications);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void Local_public_ready_waits_for_verified_host_proof_and_releases_once()
    {
        BeginLocalHost();
        var manager = new Il2Cpp.CustomNetworkManager();
        Assert.True(ConnectionContentBootstrap.DeferClientReady(manager));
        Assert.False(ConnectionContentBootstrap.DeferReady());
        Assert.False(ConnectionContentBootstrap.DeferPlayer());
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        LocalHostingRuntime.IsVerifiedScriptHost = true;
        ConnectionContentBootstrap.Tick();
        ConnectionContentBootstrap.Tick();
        Assert.Equal(1, ClientRuntime.ReadyPublications);
        Assert.Same(manager, ClientRuntime.LastReadyManager);
        Assert.Equal(0, NetworkClient.ReadyCalls);
        Assert.Equal(0, NetworkClient.AddPlayerCalls);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
        Assert.False(ConnectionContentBootstrap.DeferClientReady(manager));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_ready_timeout_is_absolute_and_late_host_proof_cannot_release(bool lateProof)
    {
        BeginLocalHost();
        var connection = NetworkClient.connection;
        var manager = new Il2Cpp.CustomNetworkManager();
        Assert.True(ConnectionContentBootstrap.DeferClientReady(manager));
        var deadline = typeof(ConnectionContentBootstrap).GetField("_localReadyDeadline", BindingFlags.Static | BindingFlags.NonPublic);
        deadline.SetValue(null, -1d); // Expired monotonic deadline without sleeping.
        Assert.True(ConnectionContentBootstrap.DeferClientReady(manager));
        Assert.Equal(-1d, (double)deadline.GetValue(null));
        LocalHostingRuntime.IsVerifiedScriptHost = lateProof;
        ConnectionContentBootstrap.Tick();
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Equal(1, connection.Disconnects);
    }

    [Fact]
    public void Revocation_discards_pending_local_public_ready()
    {
        BeginLocalHost();
        Assert.True(ConnectionContentBootstrap.DeferClientReady(new Il2Cpp.CustomNetworkManager()));
        ConnectionContentBootstrap.Revoke();
        LocalHostingRuntime.IsVerifiedScriptHost = true;
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        Assert.Null(ClientRuntime.LastReadyManager);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void Replacement_local_connection_cannot_publish_the_previous_managers_ready()
    {
        BeginLocalHost();
        var previous = new Il2Cpp.CustomNetworkManager();
        Assert.True(ConnectionContentBootstrap.DeferClientReady(previous));
        var replacement = new NetworkConnection { Pointer = new IntPtr(202) };
        NetworkClient.connection = replacement;
        LocalHostingRuntime.IsVerifiedScriptHost = true;
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        LocalHostingRuntime.IsVerifiedScriptHost = false;
        var current = new Il2Cpp.CustomNetworkManager();
        Assert.True(ConnectionContentBootstrap.DeferClientReady(current));
        LocalHostingRuntime.IsVerifiedScriptHost = true;
        ConnectionContentBootstrap.Tick();
        Assert.Equal(1, ClientRuntime.ReadyPublications);
        Assert.Same(current, ClientRuntime.LastReadyManager);
        Assert.Equal(0, replacement.Disconnects);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Loss_of_either_host_role_discards_pending_local_public_ready(bool serverRole)
    {
        BeginLocalHost();
        Assert.True(ConnectionContentBootstrap.DeferClientReady(new Il2Cpp.CustomNetworkManager()));
        if (serverRole) NetworkServer.active = false;
        else NetworkClient.activeHost = false;
        ConnectionContentBootstrap.Tick();
        NetworkServer.active = NetworkClient.activeHost = LocalHostingRuntime.IsVerifiedScriptHost = true;
        ConnectionContentBootstrap.Tick();
        Assert.Equal(0, ClientRuntime.ReadyPublications);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    private static void BeginLocalHost()
    {
        ConnectionContentBootstrap.Revoke();
        AuthHandler.UsingAnomalyAuth = false;
        NetworkServer.active = NetworkClient.activeHost = true;
        LocalHostingRuntime.IsVerifiedScriptHost = false;
        NetworkClient.connection = new NetworkConnection { Pointer = new IntPtr(101) };
    }

    public void Dispose()
    {
        AuthHandler.UsingAnomalyAuth = false;
        NetworkServer.active = NetworkClient.activeHost = LocalHostingRuntime.IsVerifiedScriptHost = false;
        ScriptingBootstrap.ThrowOnRevoke = false;
        ConnectionContentBootstrap.Revoke();
        AnomalyMessaging.ResetForTests();
        FacilityRecipeClient.IsReady = false;
        NetworkClient.ReadyCalls = NetworkClient.AddPlayerCalls = ClientRuntime.ReadyPublications = 0;
        ClientRuntime.LastReadyManager = null;
        ScriptingBootstrap.Revocations = 0;
        ScriptingBootstrap.ScriptsReady = true;
    }
}
