using System;
using System.IO;
using LabApi.Loader;

namespace Anomaly.Server.Auth;

internal static class AcceptedClientIdentityStore
{
    private const string FileName = "accepted_client_identities.json";
    private static readonly object Sync = new object();
    private static AcceptedClientIdentityJournal _journal;

    public static bool Initialize(out string error)
    {
        error = null;
        lock (Sync)
            _journal = null;
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            error = "Anomaly plugin is not initialized.";
            return false;
        }

        try
        {
            var path = Path.Combine(plugin.GetConfigDirectory().FullName, FileName);
            var journal = new AcceptedClientIdentityJournal(path);
            if (!journal.TryLoad(out error))
                return false;
            lock (Sync)
                _journal = journal;
            return true;
        }
        catch (Exception ex)
        {
            error = "Accepted-client identity store initialization failed: " + ex.Message;
            return false;
        }
    }

    public static bool TryValidateAndCommit(
        string enrollmentId,
        Anomaly.Shared.Auth.ClientHandshake handshake,
        out string error)
    {
        AcceptedClientIdentityJournal journal;
        lock (Sync)
            journal = _journal;
        if (journal == null)
        {
            error = "Accepted-client identity store is unavailable.";
            return false;
        }
        return journal.TryValidateAndCommit(enrollmentId, handshake, out error);
    }

    public static void Reset()
    {
        lock (Sync)
            _journal = null;
    }
}
