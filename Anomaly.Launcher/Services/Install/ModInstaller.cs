using Anomaly.Launcher.Models;
using Anomaly.Launcher.Services.Platform;

namespace Anomaly.Launcher.Services.Install;

/// <summary>
/// Downloads + lays out a mod from the platform onto the local filesystem. Walks the declared
/// dependency graph, resolves each dep against the live catalog, and only commits files after
/// every blob hashes correctly — so a bad download leaves the prior install intact.
/// </summary>
public sealed class ModInstaller
{
    private readonly PathService _paths;
    private readonly PlatformApiClient _client;
    private readonly InstalledModsService _state;

    public ModInstaller(PathService paths, PlatformApiClient client, InstalledModsService state)
    {
        _paths = paths;
        _client = client;
        _state = state;
    }

    public sealed record InstallProgress(string ModId, string Stage, double Fraction);

    public sealed record InstallResult(
        IReadOnlyList<InstalledMod> Installed,
        IReadOnlyList<string> Skipped,
        IReadOnlyList<string> Warnings);

    public async Task<InstallResult> InstallAsync(
        string modId,
        string? gameVersion,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(modId, gameVersion, ct);
        return await DownloadAndCommitAsync(resolved, progress, ct);
    }

    public Task<InstallResult> UpdateAsync(string modId, string? gameVersion, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        => InstallAsync(modId, gameVersion, progress, ct);

    /// <summary>
    /// Removes files belonging to <paramref name="modId"/>, but only when no other installed mod
    /// still references the same (target, relPath, sha256). This keeps a shared lib in place when
    /// other mods need it.
    /// </summary>
    public Task<bool> UninstallAsync(string modId, CancellationToken ct = default)
    {
        var state = _state.Load();
        var entry = state.Mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return Task.FromResult(false);

        var stillReferenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in state.Mods)
        {
            if (other == entry) continue;
            foreach (var f in other.Files)
                stillReferenced.Add(FileKey(f.Target, f.RelPath));
        }

        foreach (var f in entry.Files)
        {
            if (stillReferenced.Contains(FileKey(f.Target, f.RelPath))) continue;
            var path = ResolveLocalPath(f.Target, f.RelPath);
            if (path == null) continue;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                CleanupEmptyParents(path);
            }
            catch
            {
                // Best-effort delete — uninstall stays robust if a file is locked
            }
        }

        _state.Remove(modId);
        return Task.FromResult(true);
    }

    // ---------- internals ----------

    private sealed record ResolvedFile(string Target, string RelPath, string Sha256, long Size, string Scope);
    private sealed record ResolvedMod(string Id, string Semver, IReadOnlyList<ResolvedFile> Files);

    private async Task<List<ResolvedMod>> ResolveAsync(string rootId, string? gameVersion, CancellationToken ct)
    {
        var resolved = new Dictionary<string, ResolvedMod>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (resolved.ContainsKey(id)) continue;

            var mod = await _client.GetModAsync(id, ct)
                ?? throw new InvalidOperationException($"mod '{id}' not found on platform");

            var pickedSemver = PickCompatibleSemver(mod, gameVersion)
                ?? throw new InvalidOperationException(
                    $"no APPROVED version of '{id}' is compatible with game version '{gameVersion ?? "(unknown)"}'");

            var detail = await _client.GetVersionAsync(id, pickedSemver, ct)
                ?? throw new InvalidOperationException($"version detail missing for {id}@{pickedSemver}");

            var files = detail.Files
                // Skip server-only LabAPI plugin files; client launcher doesn't need them.
                .Where(f => !string.Equals(f.Target, "labapi", StringComparison.OrdinalIgnoreCase))
                // Skip server-scope userData; client launcher only installs client+both.
                .Where(f => !(string.Equals(f.Target, "userdata", StringComparison.OrdinalIgnoreCase)
                              && string.Equals(f.Scope, "server", StringComparison.OrdinalIgnoreCase)))
                .Select(f => new ResolvedFile(f.Target, f.RelPath, f.Sha256, f.Size, f.Scope))
                .ToList();

            resolved[id] = new ResolvedMod(id, pickedSemver, files);

            foreach (var dep in detail.Dependencies)
                if (!resolved.ContainsKey(dep.ModId))
                    queue.Enqueue(dep.ModId);
        }

        return resolved.Values.ToList();
    }

    private static string? PickCompatibleSemver(CatalogModDto mod, string? gameVersion)
    {
        // For dep resolution we trust the platform's pick: latestVersion is already the highest
        // APPROVED semver. If it's tagged for the current gameVersion (or gv is null), use it.
        // Otherwise we'd need /versions to find an older compatible release; deferred to a
        // dedicated endpoint when this becomes an issue.
        var latest = mod.LatestVersion;
        if (latest == null) return null;
        if (string.IsNullOrWhiteSpace(gameVersion)) return latest.Semver;
        return latest.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase)
            ? latest.Semver
            : null;
    }

    private async Task<InstallResult> DownloadAndCommitAsync(
        IReadOnlyList<ResolvedMod> mods,
        IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.ModsDir);
        Directory.CreateDirectory(_paths.UserLibsDir);
        Directory.CreateDirectory(_paths.UserDataDir);

        var staging = Path.Combine(_paths.AnomalyDataDir, "install-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        var totalFiles = mods.Sum(m => m.Files.Count);
        if (totalFiles == 0) totalFiles = 1;
        var doneFiles = 0;
        var warnings = new List<string>();
        var skipped = new List<string>();

        try
        {
            // Phase 1: download every blob into staging, hash-verifying each.
            var blobToStaged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mods)
            {
                progress?.Report(new InstallProgress(m.Id, $"downloading {m.Id}@{m.Semver}", (double)doneFiles / totalFiles));
                foreach (var f in m.Files)
                {
                    if (blobToStaged.ContainsKey(f.Sha256)) { doneFiles++; continue; }

                    var stagedPath = Path.Combine(staging, f.Sha256);
                    await using (var fs = File.Create(stagedPath))
                    {
                        await _client.DownloadBlobAsync(f.Sha256, fs, f.Sha256, ct);
                    }
                    blobToStaged[f.Sha256] = stagedPath;
                    doneFiles++;
                    progress?.Report(new InstallProgress(m.Id, $"verified {f.RelPath}", (double)doneFiles / totalFiles));
                }
            }

            // Phase 2: commit staged blobs to their final paths and update state.
            var installedNow = new List<InstalledMod>();
            foreach (var m in mods)
            {
                var entry = new InstalledMod
                {
                    Id = m.Id,
                    Version = m.Semver,
                    InstalledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Files = new List<InstalledFile>(),
                };

                foreach (var f in m.Files)
                {
                    var dest = ResolveLocalPath(f.Target, f.RelPath);
                    if (dest == null) { skipped.Add($"{m.Id}: unknown target {f.Target}"); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    var staged = blobToStaged[f.Sha256];

                    // Special-case userdata: don't clobber a file the user has edited locally.
                    if (string.Equals(f.Target, "userdata", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                    {
                        warnings.Add($"{m.Id}: kept existing UserData {f.RelPath}");
                    }
                    else
                    {
                        File.Copy(staged, dest, overwrite: true);
                    }

                    entry.Files.Add(new InstalledFile
                    {
                        Target = f.Target,
                        RelPath = f.RelPath,
                        Sha256 = f.Sha256,
                    });
                }

                _state.Upsert(entry);
                installedNow.Add(entry);
                progress?.Report(new InstallProgress(m.Id, $"installed {m.Id}@{m.Semver}", 1.0));
            }

            return new InstallResult(installedNow, skipped, warnings);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private string? ResolveLocalPath(string target, string relPath)
    {
        var safeRel = SanitizeRelPath(relPath);
        if (safeRel == null) return null;

        return target.ToLowerInvariant() switch
        {
            "mod"      => Path.Combine(_paths.ModsDir, safeRel),
            "userlib"  => Path.Combine(_paths.UserLibsDir, safeRel),
            "userdata" => Path.Combine(_paths.UserDataDir, safeRel),
            _ => null,
        };
    }

    private static string? SanitizeRelPath(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return null;
        // Reject absolute paths and parent-directory traversal.
        var normalized = relPath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized)) return null;
        if (normalized.Split('/').Any(seg => seg == "..")) return null;
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private void CleanupEmptyParents(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            while (dir != null
                   && !string.Equals(dir, _paths.ModsDir, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(dir, _paths.UserLibsDir, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(dir, _paths.UserDataDir, StringComparison.OrdinalIgnoreCase)
                   && Directory.Exists(dir))
            {
                if (Directory.EnumerateFileSystemEntries(dir).Any()) break;
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
    }

    private static string FileKey(string target, string relPath) => $"{target.ToLowerInvariant()}::{relPath}";
}
