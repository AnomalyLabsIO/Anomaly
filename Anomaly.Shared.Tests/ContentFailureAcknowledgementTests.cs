using System.Collections;
using System.Linq;
using System.Reflection;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Components;
using Anomaly.Client.Content;
using Anomaly.Client.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Il2CppMirror;
using MelonLoader;
using Xunit;

namespace Anomaly.Shared.Tests;

// Runs the real linked coordinator. Only validation, Mirror, persistence and
// coroutine/time boundaries are controlled; no engine or network is emulated.
public sealed class ContentFailureAcknowledgementTests : IDisposable
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public ContentFailureAcknowledgementTests()
    {
        ContentCatalogClient.Init();
        Dispose();
        ContentConsentFixture.Begin();
    }

    [Fact]
    public void Failed_acknowledgement_is_queued_and_flushed_before_crypto_revocation_and_delayed_disconnect()
    {
        var connection = new NetworkConnection();
        NetworkClient.connection = connection;
        RejectApprovedOffer();

        var acknowledgement = Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>());
        Assert.Equal(ContentCatalogActivationStatus.Failed, acknowledgement.Status);
        Assert.Contains("fixture:invalid", acknowledgement.FailedKeys);
        Assert.Equal(new[] { "ack", "flush", "revoke", "schedule" }, AnomalyMessaging.TransportEvents);
        Assert.Equal(1, AnomalyMessaging.SessionRevocations);
        Assert.Equal(0, connection.Disconnects);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.False(AssetBundleManager.Active);
        Assert.False(ClientContentRegistrar.Active);

        IEnumerator routine = Assert.Single(MelonCoroutines.Started);
        Assert.True(routine.MoveNext());
        UnityEngine.Time.realtimeSinceStartupAsDouble = 0.249;
        Assert.True(routine.MoveNext());
        Assert.Equal(0, connection.Disconnects);
        UnityEngine.Time.realtimeSinceStartupAsDouble = 0.25;
        Assert.False(routine.MoveNext());
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Equal(new[] { "ack", "flush", "revoke", "schedule", "revoke", "disconnect" }, AnomalyMessaging.TransportEvents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unavailable_failed_acknowledgement_queue_disconnects_immediately(bool throws)
    {
        var connection = new NetworkConnection();
        NetworkClient.connection = connection;
        AnomalyMessaging.TrySendResult = message => message is not ContentCatalogAckMessage;
        if (throws) AnomalyMessaging.TrySendException = message => message is ContentCatalogAckMessage ? new IOException("injected queue failure") : null;

        RejectApprovedOffer();

        Assert.Equal(new[] { "ack", "revoke", "disconnect" }, AnomalyMessaging.TransportEvents);
        Assert.Equal(0, connection.Updates);
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Empty(MelonCoroutines.Started);
        Assert.False(ContentCatalogClient.IsActivated);
    }

    [Fact]
    public void Missing_connection_revokes_immediately_without_scheduling_a_grace_period()
    {
        CompleteApprovedOffer("Pack 'fixture:invalid' validation failed", () => NetworkClient.connection = null);

        Assert.Equal(new[] { "ack", "revoke" }, AnomalyMessaging.TransportEvents);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Empty(MelonCoroutines.Started);
    }

    [Fact]
    public void Mirror_flush_failure_disconnects_immediately()
    {
        var connection = new NetworkConnection { ThrowOnUpdate = true };
        NetworkClient.connection = connection;
        RejectApprovedOffer();

        Assert.Equal(new[] { "ack", "flush", "revoke", "disconnect" }, AnomalyMessaging.TransportEvents);
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Empty(MelonCoroutines.Started);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Grace_scheduling_failure_disconnects_immediately_after_crypto_is_revoked(bool throws)
    {
        var connection = new NetworkConnection();
        NetworkClient.connection = connection;
        MelonCoroutines.ReturnNull = !throws;
        MelonCoroutines.ThrowOnStart = throws;
        RejectApprovedOffer();

        Assert.Equal(new[] { "ack", "flush", "revoke", "schedule", "revoke", "disconnect" }, AnomalyMessaging.TransportEvents);
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
        Assert.Empty(MelonCoroutines.Started);
    }

    [Fact]
    public void Resetting_offer_generation_cannot_leave_the_same_failed_connection_open()
    {
        var connection = new NetworkConnection();
        NetworkClient.connection = connection;
        RejectApprovedOffer();
        IEnumerator routine = Assert.Single(MelonCoroutines.Started);
        Assert.True(routine.MoveNext());
        long generation = (long)typeof(ContentCatalogClient).GetField("_offerGeneration", PrivateStatic).GetValue(null);

        ContentCatalogClient.BeginFacilityScene();
        Assert.True((long)typeof(ContentCatalogClient).GetField("_offerGeneration", PrivateStatic).GetValue(null) > generation);
        UnityEngine.Time.realtimeSinceStartupAsDouble = 0.25;

        Assert.False(routine.MoveNext());
        Assert.Equal(1, connection.Disconnects);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    public void Replacing_the_connection_during_grace_never_closes_or_revokes_the_replacement(double time)
    {
        var original = new NetworkConnection { Pointer = new IntPtr(11) };
        NetworkClient.connection = original;
        RejectApprovedOffer();
        IEnumerator routine = Assert.Single(MelonCoroutines.Started);
        Assert.True(routine.MoveNext());
        var replacement = new NetworkConnection { Pointer = new IntPtr(12) };
        NetworkClient.connection = replacement;
        int revocations = AnomalyMessaging.SessionRevocations;
        UnityEngine.Time.realtimeSinceStartupAsDouble = time;

        Assert.False(routine.MoveNext());
        Assert.Equal(0, original.Disconnects);
        Assert.Equal(0, replacement.Disconnects);
        Assert.Equal(revocations, AnomalyMessaging.SessionRevocations);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Theory]
    [InlineData(false, 0.1)]
    [InlineData(true, 0.1)]
    [InlineData(false, 0.3)]
    [InlineData(true, 0.3)]
    public void Replacing_crypto_during_grace_preserves_the_same_native_connection(bool startBeforeRekey, double time)
    {
        var connection = new NetworkConnection { Pointer = new IntPtr(11) };
        NetworkClient.connection = connection;
        RejectApprovedOffer();
        IEnumerator routine = Assert.Single(MelonCoroutines.Started);
        if (startBeforeRekey) Assert.True(routine.MoveNext());
        long oldGeneration = AnomalyMessaging.TransportGeneration;

        // The real configure path erases old keys and advances this lifetime
        // before installing replacement material. The native pointer can stay.
        AnomalyMessaging.ClearTransportSession();
        Assert.True(AnomalyMessaging.TransportGeneration > oldGeneration);
        int revocations = AnomalyMessaging.SessionRevocations;
        UnityEngine.Time.realtimeSinceStartupAsDouble = time;

        Assert.False(routine.MoveNext());
        Assert.Equal(0, connection.Disconnects);
        Assert.Equal(revocations, AnomalyMessaging.SessionRevocations);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void A_new_crypto_lifetime_invalidates_readiness_even_when_the_native_connection_is_unchanged()
    {
        var previousObserver = AnomalyMessaging.TransportSessionRevoked;
        bool previousAuth = Anomaly.Client.Auth.AuthHandler.UsingAnomalyAuth;
        try
        {
            Anomaly.Client.Auth.AuthHandler.UsingAnomalyAuth = true;
            NetworkClient.connection = new NetworkConnection();
            ConnectionContentBootstrap.Initialize();
            ConnectionContentBootstrap.BeginConnection();
            var catalogHash = Enumerable.Repeat((byte)1, 32).ToArray();
            var manifestHash = Enumerable.Repeat((byte)2, 32).ToArray();
            ConnectionContentBootstrap.BindOffer(1, catalogHash, manifestHash);
            ConnectionContentBootstrap.Consent(1, catalogHash, manifestHash);
            ConnectionContentBootstrap.Loaded(1, catalogHash, manifestHash, true);
            Anomaly.Client.Internal.Map.FacilityRecipeClient.IsReady = true;
            AnomalyMessaging.ContentBootstrapMessageReceived(new ContentBootstrapReadyMessage
            { SessionId = 1, CatalogSha256 = catalogHash, ManifestSha256 = manifestHash });
            Assert.False(ConnectionContentBootstrap.DeferReady());

            // Generation is a second check even if the normal revocation
            // observer was unavailable when old transport material was erased.
            AnomalyMessaging.TransportSessionRevoked = null;
            AnomalyMessaging.ClearTransportSession();
            Assert.True(ConnectionContentBootstrap.DeferReady());
            ConnectionContentBootstrap.TryRelease();
            Assert.Throws<InvalidDataException>(() => AnomalyMessaging.ContentBootstrapMessageReceived(
                new ContentBootstrapReadyMessage { SessionId = 1, CatalogSha256 = catalogHash, ManifestSha256 = manifestHash }));
        }
        finally
        {
            Anomaly.Client.Internal.Map.FacilityRecipeClient.IsReady = false;
            Anomaly.Client.Auth.AuthHandler.UsingAnomalyAuth = previousAuth;
            AnomalyMessaging.TransportSessionRevoked = previousObserver;
            ConnectionContentBootstrap.Revoke();
        }
    }

    [Fact]
    public void Already_disconnected_connection_does_not_trigger_duplicate_teardown()
    {
        var connection = new NetworkConnection();
        NetworkClient.connection = connection;
        RejectApprovedOffer();
        IEnumerator routine = Assert.Single(MelonCoroutines.Started);
        Assert.True(routine.MoveNext());
        NetworkClient.connection = null;

        Assert.False(routine.MoveNext());
        Assert.Equal(0, connection.Disconnects);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
    }

    [Fact]
    public void Repeated_stale_validation_callback_cannot_send_another_acknowledgement_or_coroutine()
    {
        NetworkClient.connection = new NetworkConnection();
        RejectApprovedOffer();
        AssetBundleManager.LastActivationCallback(false, "a repeated validation failure");

        Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>());
        Assert.Single(MelonCoroutines.Started);
        Assert.Equal(new[] { "ack", "flush", "revoke", "schedule" }, AnomalyMessaging.TransportEvents);
    }

    [Fact]
    public void Throwing_native_binding_refresh_retains_its_lease_but_still_reports_failure_and_closes_transport()
    {
        var connection = new NetworkConnection();
        NetworkClient.connection = connection;
        // A native resource may remain owned when an earlier binding cleanup
        // failed; rejecting this offer must not unload it beneath that consumer.
        int clears = AssetBundleManager.ClearRemoteCalls;
        AssetBundleManager.NativeLeaseOwned = true;
        AssetBundleManager.ThrowOnRevokeRemote = true;
        RejectApprovedOffer();

        Assert.False(ContentCatalogClient.IsActivated);
        Assert.False(AssetBundleManager.Active);
        Assert.True(AssetBundleManager.NativeLeaseOwned);
        Assert.Equal(clears, AssetBundleManager.ClearRemoteCalls);
        Assert.Equal(ContentCatalogActivationStatus.Failed,
            Assert.Single(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>()).Status);
        Assert.Equal(new[] { "ack", "flush", "revoke", "schedule" }, AnomalyMessaging.TransportEvents);
        IEnumerator routine = Assert.Single(MelonCoroutines.Started);
        Assert.True(routine.MoveNext());
        UnityEngine.Time.realtimeSinceStartupAsDouble = 0.25;
        Assert.False(routine.MoveNext());
        Assert.Equal(1, connection.Disconnects);
        Assert.True(AssetBundleManager.NativeLeaseOwned);
    }

    [Fact]
    public void Strict_reset_clears_logical_content_and_rejects_readiness_when_native_binding_refresh_throws()
    {
        CompleteApprovedOffer(validationError: null);
        Assert.True(ContentCatalogClient.IsActivated);
        Assert.True(AssetBundleManager.NativeLeaseOwned);
        AssetBundleManager.ThrowOnRevokeRemote = true;
        int clears = AssetBundleManager.ClearRemoteCalls;

        var exception = Assert.Throws<InvalidOperationException>(ContentCatalogClient.ResetForShutdown);

        Assert.Contains("injected native binding refresh failure", exception.InnerException.Message);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.False(AssetBundleManager.Active);
        Assert.Null(typeof(ContentCatalogClient).GetField("_catalog", PrivateStatic).GetValue(null));
        Assert.Null(typeof(ContentCatalogClient).GetField("_manifest", PrivateStatic).GetValue(null));
        Assert.True((bool)typeof(ContentCatalogClient).GetField("_failureLatched", PrivateStatic).GetValue(null));
        Assert.Null(PasswordPrompt.Approve);
        Assert.Equal(clears, AssetBundleManager.ClearRemoteCalls);
        Assert.True(AssetBundleManager.NativeLeaseOwned);
    }

    private static void RejectApprovedOffer() => CompleteApprovedOffer("Pack 'fixture:invalid' validation failed");

    private static void CompleteApprovedOffer(string validationError, Action beforeApproval = null)
    {
        ConnectionContentBootstrap.BeginConnection();
        FileCache.HasFileResult = _ => true;
        AssetBundleManager.ActivationError = validationError;
        byte[] bundleHash = Enumerable.Repeat((byte)0x51, 32).ToArray();
        byte[] indexHash = Enumerable.Repeat((byte)0x52, 32).ToArray();
        var manifest = new FileManifestMessage { SessionId = 9123, Files = {
            new FileManifestMessage.FileEntry("invalid.bundle", bundleHash, 64, FileContentKind.Bundle),
            new FileManifestMessage.FileEntry("invalid.index.json", indexHash, 512, FileContentKind.PackIndex),
        } };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, manifest.ManifestSha256,
            new[] { new CatalogPack { PackId = "fixture:invalid", IndexHash = indexHash, IndexSize = 512, BundleHash = bundleHash, BundleSize = 64 } });
        typeof(ContentCatalogClient).GetMethod("HandleMessage", PrivateStatic)
            .Invoke(null, new object[] { new ContentCatalogMessage { Catalog = catalog } });
        typeof(FileTransferClient).GetMethod("HandleManifest", PrivateStatic).Invoke(null, new object[] { manifest });
        Assert.NotNull(PasswordPrompt.Approve);
        beforeApproval?.Invoke();
        PasswordPrompt.Approve();
    }

    public void Dispose() => ContentConsentFixture.Reset();
}
