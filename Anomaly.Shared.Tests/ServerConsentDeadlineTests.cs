using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Xunit;
using ServerMessaging = Anomaly.Server.Networking.AnomalyMessaging;

namespace Anomaly.Shared.Tests;

public sealed class ServerConsentDeadlineTests : IDisposable
{
    [Fact]
    public void Replaced_pending_offer_cannot_publish_after_scheduler_cancellation_fails()
    {
        var hub = new ReferenceHub { netId = 25 };
        int stale = 0, current = 0;
        FileTransferSession.StartForPlayer(hub, (_, __) => stale++);
        MEC.Timing.ThrowOnKill = true;
        FileTransferSession.StartForPlayer(hub, (_, __) => current++);
        MEC.Timing.RunPendingStarts();
        Assert.Equal(0, stale);
        Assert.Equal(1, current);
        Assert.Single(Sessions);
        Assert.Single(ServerMessaging.Sent.OfType<FileManifestMessage>());
    }

    [Fact]
    public void Pending_offer_start_failure_revokes_admission_without_leaking_a_handle()
    {
        var hub = new ReferenceHub { netId = 26 };
        MEC.Timing.ThrowOnRun = true;
        FileTransferSession.StartForPlayer(hub);
        Assert.Empty((IDictionary)Field("PendingStarts", true).GetValue(null));
        Assert.Empty(Sessions);
        Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
        Assert.False(hub.connectionToClient.isReady);
    }

    [Fact]
    public void Client_approval_after_a_long_prompt_starts_a_fresh_progress_deadline()
    {
        var manifest = new FileManifestMessage
        {
            SessionId = 501,
            Files = { new FileManifestMessage.FileEntry(
                "textures/consent.index.json", new byte[32], 16, FileContentKind.PackIndex) },
        };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        var clientType = typeof(Anomaly.Client.FileTransfer.FileTransferClient);
        clientType.GetMethod("HandleManifest", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { manifest });
        clientType.GetField("_lastActivityUtc", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, DateTime.UtcNow.AddDays(-1));

        Assert.True(Anomaly.Client.FileTransfer.FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256));
        var watchdog = (IEnumerator)clientType.GetMethod("WatchTimeout", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { manifest.SessionId });
        Assert.True(watchdog.MoveNext());
        Assert.True(watchdog.MoveNext());
        Assert.Equal("Receiving", clientType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null).ToString());
        Assert.Single(Anomaly.Client.Api.Networking.AnomalyMessaging.Sent);
    }

    [Fact]
    public void Consent_can_remain_pending_without_watchdog_or_transfer_budget()
    {
        for (uint id = 1; id <= 12; id++)
        {
            var hub = new ReferenceHub { netId = id };
            Start(hub);
            object session = Session(hub);
            SetLastActivity(session, DateTime.UtcNow.AddDays(-1));
            Assert.False(((MEC.CoroutineHandle)Field("_watchdogHandle", false).GetValue(session)).IsRunning);
            IEnumerator<float> watchdog = Watchdog(session);
            Assert.True(watchdog.MoveNext());
            Assert.True(watchdog.MoveNext());
            Assert.True(hub.connectionToClient.isReady);
        }
        Assert.Equal(12, Sessions.Count);
        Assert.Equal(0, Field("_reservedTransferCount", true).GetValue(null));
        Assert.Equal(0L, Field("_globalReservedBytes", true).GetValue(null));
        Assert.Equal(0, Anomaly.Server.Plugin.QuarantineCount);
    }

    [Fact]
    public void Approval_starts_a_fresh_deadline_even_if_no_file_request_arrives()
    {
        var hub = new ReferenceHub { netId = 21 };
        ContentCatalog catalog = Start(hub);
        object session = Session(hub);
        SetLastActivity(session, DateTime.UtcNow.AddDays(-1));
        Approve(hub, catalog);
        Assert.True((DateTime)Field("_lastActivityUtc", false).GetValue(session) > DateTime.UtcNow.AddSeconds(-5));
        Assert.False(FileTransferSession.ApproveForPlayer(hub));
        Assert.Equal(0, Field("_reservedTransferCount", true).GetValue(null));

        SetLastActivity(session, DateTime.UtcNow.AddMinutes(-1));
        IEnumerator<float> watchdog = Watchdog(session);
        Assert.True(watchdog.MoveNext());
        Assert.False(watchdog.MoveNext());
        Assert.Empty(Sessions);
        Assert.False(hub.connectionToClient.isReady);
        Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
    }

    [Fact]
    public void Pending_replacement_and_decline_remove_only_the_owned_session()
    {
        var hub = new ReferenceHub { netId = 22 };
        Start(hub);
        object old = Session(hub);
        ContentCatalog catalog = Start(hub);
        Assert.NotSame(old, Session(hub));
        Assert.Single(Sessions);
        Assert.True(ContentGrantGate.TryRecordDecision(
            hub.netId, catalog.CatalogSha256, catalog.ManifestSha256, approved: false));
        FileTransferSession.CleanupPlayer(hub);
        Assert.Empty(Sessions);
        Assert.True(hub.connectionToClient.isReady);
        Assert.Equal(0, Anomaly.Server.Plugin.QuarantineCount);
    }

    [Fact]
    public void File_request_cannot_approve_a_pending_session()
    {
        var hub = new ReferenceHub { netId = 23 };
        Start(hub);
        FileManifestMessage manifest = ServerMessaging.Sent.OfType<FileManifestMessage>().Last();
        FileTransferSession.HandleMessage(hub, new FileRequestMessage { SessionId = manifest.SessionId });
        Assert.Empty(Sessions);
        Assert.False(hub.connectionToClient.isReady);
    }

    [Fact]
    public void Approved_watchdog_start_failure_revokes_the_session()
    {
        var hub = new ReferenceHub { netId = 24 };
        ContentCatalog catalog = Start(hub);
        Assert.True(ContentGrantGate.TryRecordDecision(
            hub.netId, catalog.CatalogSha256, catalog.ManifestSha256, approved: true));
        MEC.Timing.ThrowOnRun = true;
        Assert.False(FileTransferSession.ApproveForPlayer(hub));
        Assert.Empty(Sessions);
        Assert.False(hub.connectionToClient.isReady);
    }

    private static ContentCatalog Start(ReferenceHub hub)
    {
        ContentCatalog catalog = null;
        FileTransferSession.StartForPlayer(hub, (digest, snapshot) =>
        {
            catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 128, 127, digest);
            ContentGrantGate.RecordOffer(hub.netId, catalog);
        });
        MEC.Timing.RunPendingStarts();
        return catalog;
    }

    private static void Approve(ReferenceHub hub, ContentCatalog catalog)
    {
        Assert.True(ContentGrantGate.TryRecordDecision(
            hub.netId, catalog.CatalogSha256, catalog.ManifestSha256, approved: true));
        Assert.True(FileTransferSession.ApproveForPlayer(hub));
    }

    private static IDictionary Sessions => (IDictionary)Field("ActiveSessions", true).GetValue(null);
    private static object Session(ReferenceHub hub) => Sessions[hub];
    private static FieldInfo Field(string name, bool isStatic) => typeof(FileTransferSession).GetField(
        name, BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance));
    private static void SetLastActivity(object session, DateTime value) =>
        Field("_lastActivityUtc", false).SetValue(session, value);
    private static IEnumerator<float> Watchdog(object session) => (IEnumerator<float>)typeof(FileTransferSession)
        .GetMethod("Watchdog", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(session, null);

    public void Dispose()
    {
        MEC.Timing.ThrowOnRun = false;
        MEC.Timing.ThrowOnKill = false;
        FileTransferSession.Reset();
        ContentGrantGate.Reset();
        ServerMessaging.ResetForTests();
        ServerAssetManager.SetAssets();
        Anomaly.Server.Plugin.ResetForTests();
        Anomaly.Client.FileTransfer.FileTransferClient.Reset();
        Anomaly.Client.Api.Networking.AnomalyMessaging.ResetForTests();
    }
}
