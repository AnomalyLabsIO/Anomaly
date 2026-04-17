namespace Anomaly.Installer.Patching;

/// <summary>
/// Manages sidecar .bak copies of files the Installer will mutate. The first successful install
/// captures the vanilla copy; subsequent runs reuse it. On failure, <see cref="RestoreFile"/>
/// reverts to the last known-good state.
/// </summary>
internal static class BackupManager
{
    public const string BackupExtension = ".bak";

    /// <summary>
    /// Creates <paramref name="filePath"/>.bak if it does not already exist. No-op if the
    /// source file is missing or the backup already exists (so re-running the Installer
    /// doesn't clobber the original vanilla snapshot).
    /// </summary>
    public static void BackupFile(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        string backupPath = filePath + BackupExtension;
        if (File.Exists(backupPath))
            return;

        File.Copy(filePath, backupPath);
    }

    /// <summary>
    /// Restores <paramref name="filePath"/> from its sidecar .bak. Returns true if a
    /// restore happened, false if no backup was available.
    /// </summary>
    public static bool RestoreFile(string filePath)
    {
        string backupPath = filePath + BackupExtension;
        if (!File.Exists(backupPath))
            return false;

        File.Copy(backupPath, filePath, overwrite: true);
        return true;
    }

    public static bool HasBackup(string filePath) => File.Exists(filePath + BackupExtension);
}
