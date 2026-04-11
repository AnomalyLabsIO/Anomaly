namespace Anomaly.Client.Internal;

internal static class SilentRoleChange
{
    /// <summary>
    /// When true, the next role change for the local player will suppress
    /// the RoleHelpMenu overlay and AloneSoundtrack reset.
    /// Cleared automatically in PlayerRoleManagerPatch.Postfix.
    /// </summary>
    internal static bool IsPending { get; set; }
}
