using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;
using ServerMessaging = Anomaly.Server.Networking.AnomalyMessaging;

namespace Anomaly.Shared.Tests;

public sealed class ServerConsentDeadlineTests : IDisposable
{
    [Fact]
    public void Consent_and_replaced_incomplete_offer_share_one_absolute_deadline()
    {
        double elapsed = 0;
        var gate = new ConnectionContentState(() => elapsed);
        byte[] hash = new byte[32];
        Assert.True(gate.Offer(1, hash, hash));
        elapsed = 119;
        Assert.True(gate.Approve(hash, hash));
        var next = gate.PreserveDeadline();
        Assert.True(next.Offer(2, hash, hash));
        elapsed = 120;
        Assert.True(gate.Expired);
        Assert.True(next.Expired);
        Assert.False(gate.BeginTransfer(1));
        Assert.False(next.Approve(hash, hash));
    }

    [Fact]
    public void Pending_consent_reserves_no_active_transfer_budget()
    {
        for (uint id = 1; id <= 12; id++) ServerBootstrapFixture.Start(new ReferenceHub { netId = id });
        Assert.Equal(12, Sessions.Count);
        Assert.Equal(0, Field("_reservedTransferCount", true).GetValue(null));
        Assert.Equal(0L, Field("_globalReservedBytes", true).GetValue(null));
        Assert.Equal(0, Anomaly.Server.Plugin.QuarantineCount);
    }

    [Fact]
    public void Decline_disconnects_once_and_removes_connection_owned_offer()
    {
        var hub = new ReferenceHub { netId = 22 };
        ServerBootstrapFixture.Start(hub);
        var catalog = ServerBootstrapFixture.Catalog;
        var decline = new ContentCatalogDecisionMessage
        { Decision = ContentCatalogDecision.Declined, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 };
        ServerBootstrapFixture.Handle(hub, decline);
        ServerBootstrapFixture.Handle(hub, decline);
        Assert.Empty(Sessions);
        Assert.Equal(1, hub.connectionToClient.DisconnectCount);
        Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
        Assert.False(AuthenticatedTransportSessions.IsActiveConnection(hub.connectionToClient));
    }

    [Fact]
    public void File_request_cannot_approve_a_pending_session()
    {
        var hub = new ReferenceHub { netId = 23 };
        ServerBootstrapFixture.Start(hub);
        ServerBootstrapFixture.Handle(hub, new FileRequestMessage { SessionId = ServerBootstrapFixture.Manifest.SessionId });
        Assert.Empty(Sessions);
        Assert.False(hub.connectionToClient.isReady);
    }

    [Fact]
    public void Duplicate_approval_is_terminal_and_does_not_start_another_deadline()
    {
        var hub = new ReferenceHub { netId = 24 };
        ServerBootstrapFixture.Start(hub);
        ServerBootstrapFixture.Approve(hub);
        ServerBootstrapFixture.Approve(hub);
        Assert.Empty(Sessions);
        Assert.Equal(1, hub.connectionToClient.DisconnectCount);
    }

    [Fact]
    public void Approved_progress_watchdog_still_aborts_an_idle_transfer()
    {
        var hub = new ReferenceHub { netId = 25 };
        ServerBootstrapFixture.Start(hub);
        ServerBootstrapFixture.Approve(hub);
        object session = ServerBootstrapFixture.Session(hub);
        Field("_lastActivityUtc", false).SetValue(session, DateTime.UtcNow.AddMinutes(-1));
        var routine = (IEnumerator<float>)typeof(FileTransferSession).GetMethod("Watchdog", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(session, null);
        Assert.True(routine.MoveNext());
        Assert.False(routine.MoveNext());
        Assert.Empty(Sessions);
        Assert.Equal(1, hub.connectionToClient.DisconnectCount);
    }

    [Fact]
    public void Approved_watchdog_start_failure_revokes_the_connection()
    {
        var hub = new ReferenceHub { netId = 26 };
        ServerBootstrapFixture.Start(hub);
        MEC.Timing.ThrowOnRun = true;
        ServerBootstrapFixture.Approve(hub);
        Assert.Empty(Sessions);
        Assert.False(hub.connectionToClient.isReady);
        Assert.Equal(1, hub.connectionToClient.DisconnectCount);
    }

    [Fact]
    public void Cleanup_removes_transfer_ownership_even_if_scheduler_cancellation_throws()
    {
        var hub = new ReferenceHub { netId = 27 };
        ServerBootstrapFixture.Start(hub);
        ServerBootstrapFixture.Approve(hub);
        MEC.Timing.ThrowOnKill = true;
        ConnectionContentBootstrap.Cleanup(hub.connectionToClient);
        Assert.Empty(Sessions);
        Assert.False(ConnectionContentBootstrap.IsComplete(hub.connectionToClient));
        Assert.Equal(0, Field("_reservedTransferCount", true).GetValue(null));
    }

    [Fact]
    public void Activated_connection_is_adopted_once_without_sending_another_offer()
    {
        var hub = new ReferenceHub { netId = 28 };
        ServerBootstrapFixture.Start(hub);
        ServerBootstrapFixture.Approve(hub);
        ServerBootstrapFixture.Handle(hub, new FileRequestMessage { SessionId = ServerBootstrapFixture.Manifest.SessionId });
        Assert.Equal(Anomaly.Server.Api.Content.ContentReadiness.Unknown, ContentGrantGate.GetReadiness(hub.netId));
        Assert.Equal(0, Anomaly.Server.Api.Content.CustomItems.ReadyPublications);
        Assert.True(ServerBootstrapFixture.ActivateAndAdopt(hub));
        int sent = ServerMessaging.Sent.Count;
        Assert.Equal(Anomaly.Server.Api.Content.ContentReadiness.Activated, ContentGrantGate.GetReadiness(hub.netId));
        Assert.Equal(1, Anomaly.Server.Api.Content.CustomItems.ReadyPublications);
        Assert.False(ContentCatalogBridge.AdoptForPlayer(hub));
        Assert.Equal(1, Anomaly.Server.Api.Content.CustomItems.ReadyPublications);
        Assert.Equal(sent, ServerMessaging.Sent.Count);
        ConnectionContentBootstrap.OnGenerationStarted();
        Assert.Equal(Anomaly.Server.Api.Content.ContentReadiness.Unknown, ContentGrantGate.GetReadiness(hub.netId));
        Assert.False(ConnectionContentBootstrap.IsComplete(hub.connectionToClient));
    }

    private static IDictionary Sessions => (IDictionary)Field("ActiveSessions", true).GetValue(null);
    private static FieldInfo Field(string name, bool isStatic) => typeof(FileTransferSession).GetField(
        name, BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance));

    public void Dispose()
    {
        MEC.Timing.ThrowOnRun = MEC.Timing.ThrowOnKill = false;
        ConnectionContentBootstrap.Reset();
        FileTransferSession.Reset(); ContentGrantGate.Reset();
        ServerMessaging.ResetForTests(); ServerAssetManager.SetAssets();
        ContentRegistry.Reset(); Anomaly.Server.Plugin.ResetForTests();
        Anomaly.Server.Api.Content.CustomItems.ReadyPublications = 0;
    }
}
