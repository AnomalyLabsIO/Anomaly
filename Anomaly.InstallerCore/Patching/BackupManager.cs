namespace Anomaly.InstallerCore.Patching;

/// <summary>
/// Manages sidecar .bak copies of files the Installer will mutate. The first successful install
/// captures the vanilla copy; subsequent runs reuse it. On failure, <see cref="RestoreFile"/>
/// reverts to the last known-good state.
/// </summary>
public static class BackupManager
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

        // Verify the backup was written correctly — a silent truncation here would
        // leave us with a bad .bak that passes HasBackup() but fails restore.
        string sourceHash = ResourceIntegrity.ComputeFileSha256(filePath);
        string backupHash = ResourceIntegrity.ComputeFileSha256(backupPath);
        ResourceIntegrity.VerifyHash($"backup of {Path.GetFileName(filePath)}", sourceHash, backupHash);
    }

    /// <summary>
    /// Restores <paramref name="filePath"/> from its sidecar .bak. Returns true if a
    /// restore happened, false if no backup was available. Throws if the restore
    /// completed but the on-disk bytes do not match the backup (disk full, AV lock, etc.).
    /// </summary>
    public static bool RestoreFile(string filePath)
    {
        string backupPath = filePath + BackupExtension;
        if (!File.Exists(backupPath))
            return false;

        File.Copy(backupPath, filePath, overwrite: true);

        string backupHash = ResourceIntegrity.ComputeFileSha256(backupPath);
        string restoredHash = ResourceIntegrity.ComputeFileSha256(filePath);
        ResourceIntegrity.VerifyHash(
            $"restored {Path.GetFileName(filePath)} (backup at {backupPath})",
            backupHash, restoredHash);

        return true;
    }

    public static bool HasBackup(string filePath) => File.Exists(filePath + BackupExtension);
}
