using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Launcher.Models.Steam;
using Newtonsoft.Json;
using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;

namespace Anomaly.Launcher.Services.Steam;

/// <summary>
/// Wraps SteamKit2 for QR-code authentication and depot download of SCP:SL (AppID 700330).
/// No password is ever handled by this service — users approve sign-in on the Steam mobile app
/// and we persist only the opaque refresh token SteamKit2 returns.
/// </summary>
public sealed class SteamDownloadService : IDisposable
{
    public const uint ScpSlAppId = 700330;
    public const uint ScpSlDepotId = 700331;

    private const string DeviceFriendlyName = "Anomaly Launcher";

    // Files the installer excludes from its copy-from-vanilla step. We mirror SL-AC.dll here so
    // the Steam anti-cheat never lands next to the modded install — it would defeat the patches.
    // SCPSL.exe is intentionally NOT skipped: the installer's clean-launcher step overwrites it
    // in the normal patch flow, and when --no-anomaly is passed the vanilla exe is what we want.
    private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SL-AC.dll",
    };

    private readonly PathService _paths;
    private readonly object _lock = new();

    private SteamClient? _client;
    private CallbackManager? _callbackManager;
    private Task? _callbackPumpTask;
    private CancellationTokenSource? _pumpCts;

    // Session state after a successful LogOn — used by the download pipeline.
    private bool _isLoggedOn;
    private string? _loggedOnAccountName;
    private ulong _loggedOnSteamId;

    public SteamDownloadService(PathService paths)
    {
        _paths = paths;
    }

    // ===================== Token persistence =====================

    private string SessionDir => Path.Combine(_paths.AnomalyDataDir, "steam");
    private string SessionFilePath => Path.Combine(SessionDir, "session.bin");

    public bool HasStoredToken() => File.Exists(SessionFilePath);

    public void Logout()
    {
        try
        {
            if (File.Exists(SessionFilePath))
                File.Delete(SessionFilePath);
        }
        catch
        {
            // Best-effort; the next login will overwrite anyway.
        }
    }

    private void PersistSession(string accountName, string refreshToken)
    {
        try
        {
            Directory.CreateDirectory(SessionDir);
            var payload = JsonConvert.SerializeObject(
                new StoredSession(accountName, refreshToken));
            var plaintext = Encoding.UTF8.GetBytes(payload);

            byte[] toStore;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                toStore = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            }
            else
            {
                toStore = plaintext;
            }

            File.WriteAllBytes(SessionFilePath, toStore);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { File.SetUnixFileMode(SessionFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch { /* pre-net7 or unsupported FS */ }
            }
        }
        catch
        {
            // If we can't persist, silent re-login just won't work next time — not fatal.
        }
    }

    private StoredSession? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionFilePath))
                return null;

            var bytes = File.ReadAllBytes(SessionFilePath);

            byte[] plaintext;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                plaintext = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            }
            else
            {
                plaintext = bytes;
            }

            var json = Encoding.UTF8.GetString(plaintext);
            return JsonConvert.DeserializeObject<StoredSession>(json);
        }
        catch
        {
            // Corrupt or unreadable — treat as no token.
            return null;
        }
    }

    private sealed record StoredSession(string AccountName, string RefreshToken);

    // ===================== Connection management =====================

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        SteamClient client;
        CallbackManager manager;

        lock (_lock)
        {
            if (_client != null && _client.IsConnected)
                return;

            _client?.Disconnect();
            StopCallbackPump();

            _client = new SteamClient();
            _callbackManager = new CallbackManager(_client);
            _pumpCts = new CancellationTokenSource();

            var pumpToken = _pumpCts.Token;
            var pumpManager = _callbackManager;
            _callbackPumpTask = Task.Run(() =>
            {
                while (!pumpToken.IsCancellationRequested)
                {
                    try { pumpManager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100)); }
                    catch { /* swallow — loop keeps running */ }
                }
            }, pumpToken);

            client = _client;
            manager = _callbackManager;
        }

        var connectedTcs = new TaskCompletionSource();
        var disconnectedTcs = new TaskCompletionSource();

        using var connectedSub = manager.Subscribe<SteamClient.ConnectedCallback>(_ =>
            connectedTcs.TrySetResult());
        using var disconnectedSub = manager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            if (!connectedTcs.Task.IsCompleted)
                connectedTcs.TrySetException(new IOException("Disconnected from Steam before login"));
        });

        client.Connect();

        using var reg = ct.Register(() => connectedTcs.TrySetCanceled(ct));
        await connectedTcs.Task.ConfigureAwait(false);
    }

    private void StopCallbackPump()
    {
        try { _pumpCts?.Cancel(); } catch { }
        try { _callbackPumpTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _pumpCts?.Dispose();
        _pumpCts = null;
        _callbackPumpTask = null;
    }

    // ===================== QR login =====================

    public async Task<SteamLoginResult> LoginWithQrAsync(
        IProgress<QrChallenge> challengeCallback,
        bool remember,
        CancellationToken ct)
    {
        try
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            var session = await _client!.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
            {
                DeviceFriendlyName = DeviceFriendlyName,
                IsPersistentSession = remember,
            }).ConfigureAwait(false);

            // Emit the initial challenge, then subscribe for rotations.
            EmitChallenge(session.ChallengeURL, challengeCallback);
            session.ChallengeURLChanged = () => EmitChallenge(session.ChallengeURL, challengeCallback);

            AuthPollResult pollResult;
            try
            {
                pollResult = await session.PollingWaitForResultAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new SteamLoginResult.Cancelled();
            }
            catch (AuthenticationException authEx)
            {
                return MapAuthException(authEx);
            }

            var logonResult = await CompleteLogOnAsync(
                pollResult.AccountName, pollResult.RefreshToken, ct).ConfigureAwait(false);

            if (logonResult is SteamLoginResult.Success && remember)
            {
                PersistSession(pollResult.AccountName, pollResult.RefreshToken);
            }

            return logonResult;
        }
        catch (OperationCanceledException)
        {
            return new SteamLoginResult.Cancelled();
        }
        catch (Exception ex)
        {
            return new SteamLoginResult.UnexpectedError(ex.Message);
        }
    }

    public async Task<SteamLoginResult> LoginWithStoredTokenAsync(CancellationToken ct)
    {
        var stored = LoadSession();
        if (stored == null)
            return new SteamLoginResult.TokenExpired();

        try
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            var result = await CompleteLogOnAsync(stored.AccountName, stored.RefreshToken, ct)
                .ConfigureAwait(false);

            // If the stored token was rejected, wipe it so we fall through to QR next time.
            if (result is SteamLoginResult.TokenExpired)
                Logout();

            return result;
        }
        catch (OperationCanceledException)
        {
            return new SteamLoginResult.Cancelled();
        }
        catch (Exception ex)
        {
            return new SteamLoginResult.UnexpectedError(ex.Message);
        }
    }

    private async Task<SteamLoginResult> CompleteLogOnAsync(
        string accountName, string refreshToken, CancellationToken ct)
    {
        // If we're already logged on with this account, reuse the existing session. SteamKit2
        // silently no-ops a duplicate LogOn (or worse, the server kicks us) instead of emitting
        // a fresh LoggedOnCallback, which produced an alternating hang across dialog reopens.
        if (_isLoggedOn && string.Equals(_loggedOnAccountName, accountName, StringComparison.Ordinal))
            return new SteamLoginResult.Success(accountName, _loggedOnSteamId);

        var steamUser = _client!.GetHandler<SteamUser>();
        if (steamUser == null)
            return new SteamLoginResult.UnexpectedError("SteamUser handler not available");

        var loggedOnTcs = new TaskCompletionSource<SteamUser.LoggedOnCallback>();

        using var loggedOnSub = _callbackManager!.Subscribe<SteamUser.LoggedOnCallback>(cb =>
            loggedOnTcs.TrySetResult(cb));
        using var loggedOffSub = _callbackManager!.Subscribe<SteamUser.LoggedOffCallback>(cb =>
            loggedOnTcs.TrySetException(new InvalidOperationException($"Steam logged us off: {cb.Result}")));
        using var disconnectSub = _callbackManager!.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            loggedOnTcs.TrySetException(new IOException("Disconnected from Steam during log-on")));

        steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = accountName,
            AccessToken = refreshToken,
            ShouldRememberPassword = true,
        });

        using var reg = ct.Register(() => loggedOnTcs.TrySetCanceled(ct));
        SteamUser.LoggedOnCallback result;
        try
        {
            result = await loggedOnTcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SteamLoginResult.Cancelled();
        }
        catch (IOException)
        {
            // Connection dropped mid-logon — reset state so the next attempt gets a fresh client.
            ResetSession();
            return new SteamLoginResult.NetworkFailure("Disconnected from Steam");
        }
        catch (InvalidOperationException ex)
        {
            ResetSession();
            return new SteamLoginResult.UnexpectedError(ex.Message);
        }

        if (result.Result != EResult.OK)
            return MapEResult(result.Result);

        _isLoggedOn = true;
        _loggedOnAccountName = accountName;
        _loggedOnSteamId = result.ClientSteamID?.ConvertToUInt64() ?? 0;

        return new SteamLoginResult.Success(accountName, _loggedOnSteamId);
    }

    /// <summary>
    /// Tears down the SteamClient + callback pump so the next login gets a clean connection.
    /// Called when the server disconnects us or login fails with a network error.
    /// </summary>
    private void ResetSession()
    {
        lock (_lock)
        {
            try { _client?.Disconnect(); } catch { }
            StopCallbackPump();
            _client = null;
            _callbackManager = null;
            _isLoggedOn = false;
            _loggedOnAccountName = null;
            _loggedOnSteamId = 0;
        }
    }

    private static void EmitChallenge(string url, IProgress<QrChallenge> callback)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var pngQr = new PngByteQRCode(qrData);
            var pngBytes = pngQr.GetGraphic(10);
            callback.Report(new QrChallenge(url, pngBytes));
        }
        catch
        {
            // QR encoding failures are non-fatal; the next ChallengeURLChanged will retry.
        }
    }

    private static SteamLoginResult MapEResult(EResult result) => result switch
    {
        EResult.InvalidPassword => new SteamLoginResult.TokenExpired(),
        EResult.AccessDenied => new SteamLoginResult.TokenExpired(),
        EResult.Revoked => new SteamLoginResult.TokenExpired(),
        EResult.Expired => new SteamLoginResult.TokenExpired(),
        EResult.InvalidSignature => new SteamLoginResult.TokenExpired(),
        EResult.AccountLogonDenied => new SteamLoginResult.TokenExpired(),
        EResult.NoConnection => new SteamLoginResult.NetworkFailure("No connection to Steam"),
        EResult.Timeout => new SteamLoginResult.NetworkFailure("Steam login timed out"),
        _ => new SteamLoginResult.UnexpectedError(result.ToString())
    };

    private static SteamLoginResult MapAuthException(AuthenticationException ex)
    {
        var message = ex.Message ?? string.Empty;
        if (message.Contains("Expired", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new SteamLoginResult.QrExpired();
        }
        if (message.Contains("Denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("decline", StringComparison.OrdinalIgnoreCase))
        {
            return new SteamLoginResult.UserDeclined();
        }
        return new SteamLoginResult.UnexpectedError(message);
    }

    // ===================== Manifest discovery (PICS) =====================

    /// <summary>
    /// Fetches the currently-published manifests for SCP:SL's depot via an authenticated PICS
    /// call. Returns one entry per branch (public, beta, etc.) with manifest id, release date,
    /// and the branch's build id. Steam does not expose historical manifest ids through PICS —
    /// for older builds use the embedded builds_catalog.json as a seed list.
    /// </summary>
    public async Task<IReadOnlyList<SteamBuildCatalogEntry>> FetchManifestListAsync(CancellationToken ct)
    {
        if (!_isLoggedOn || _client == null)
            throw new InvalidOperationException("Not logged in to Steam");

        var steamApps = _client.GetHandler<SteamApps>()
            ?? throw new InvalidOperationException("SteamApps handler unavailable");

        var appRequest = new SteamApps.PICSRequest(ScpSlAppId);
        var resultSet = await steamApps
            .PICSGetProductInfo(new[] { appRequest }, Array.Empty<SteamApps.PICSRequest>())
            .ToTask().WaitAsync(ct).ConfigureAwait(false);

        if (resultSet?.Results == null)
            return Array.Empty<SteamBuildCatalogEntry>();

        var entries = new List<SteamBuildCatalogEntry>();
        foreach (var callback in resultSet.Results)
        {
            if (!callback.Apps.TryGetValue(ScpSlAppId, out var appInfo))
                continue;

            var depots = appInfo.KeyValues["depots"];
            var branches = depots["branches"];
            var manifestsKv = depots[ScpSlDepotId.ToString()]["manifests"];

            foreach (var branch in manifestsKv.Children)
            {
                var branchName = branch.Name ?? "public";
                var gidStr = branch["gid"].AsString();
                if (!ulong.TryParse(gidStr, out var manifestId) || manifestId == 0)
                    continue;

                DateTimeOffset? releasedAt = null;
                var timeStr = branch["timeupdated"].AsString();
                if (long.TryParse(timeStr, out var unixSec) && unixSec > 0)
                    releasedAt = DateTimeOffset.FromUnixTimeSeconds(unixSec);

                var buildId = branches[branchName]["buildid"].AsString();

                entries.Add(new SteamBuildCatalogEntry(
                    ManifestId: manifestId,
                    ReleasedAt: releasedAt,
                    BuildId: string.IsNullOrEmpty(buildId) ? null : buildId,
                    GameVersion: null,                    // enrichment hook
                    VersionLabel: $"Branch: {branchName}",
                    Notes: null,
                    Status: SupportStatus.Unknown));
            }
        }

        return entries;
    }

    // ===================== Download =====================

    public async Task<SteamDownloadResult> DownloadBuildAsync(
        ulong manifestId,
        string destDir,
        IProgress<SteamDownloadProgress> progress,
        CancellationToken ct)
    {
        if (!_isLoggedOn || _client == null || _callbackManager == null)
            return new SteamDownloadResult.UnexpectedError("Not logged in to Steam");

        try
        {
            var steamApps = _client.GetHandler<SteamApps>()
                ?? throw new InvalidOperationException("SteamApps handler unavailable");
            var steamContent = _client.GetHandler<SteamContent>()
                ?? throw new InvalidOperationException("SteamContent handler unavailable");

            // 1. Depot decryption key (also verifies the account owns the game)
            SteamApps.DepotKeyCallback depotKeyResult;
            try
            {
                depotKeyResult = await steamApps.GetDepotDecryptionKey(ScpSlDepotId, ScpSlAppId)
                    .ToTask().WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new SteamDownloadResult.NetworkFailure(ex.Message);
            }

            if (depotKeyResult.Result != EResult.OK)
            {
                if (depotKeyResult.Result == EResult.AccessDenied ||
                    depotKeyResult.Result == EResult.NoMatch)
                {
                    return new SteamDownloadResult.NotOwned();
                }
                return new SteamDownloadResult.UnexpectedError(
                    $"GetDepotDecryptionKey failed: {depotKeyResult.Result}");
            }

            var depotKey = depotKeyResult.DepotKey;

            // 2. Manifest request code
            ulong manifestRequestCode;
            try
            {
                manifestRequestCode = await steamContent
                    .GetManifestRequestCode(ScpSlDepotId, ScpSlAppId, manifestId, "public")
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new SteamDownloadResult.NetworkFailure(ex.Message);
            }

            if (manifestRequestCode == 0)
                return new SteamDownloadResult.UnexpectedError(
                    "Steam did not issue a manifest request code (manifest may not exist or be restricted)");

            // 3. CDN servers
            IReadOnlyCollection<Server>? cdnServers;
            try
            {
                cdnServers = await steamContent.GetServersForSteamPipe().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new SteamDownloadResult.NetworkFailure(ex.Message);
            }

            if (cdnServers == null || cdnServers.Count == 0)
                return new SteamDownloadResult.NetworkFailure("No CDN servers available");

            var server = cdnServers.First();

            // 4. Manifest download
            using var cdnClient = new Client(_client);
            DepotManifest manifest;
            try
            {
                manifest = await cdnClient
                    .DownloadManifestAsync(ScpSlDepotId, manifestId, manifestRequestCode, server, depotKey)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new SteamDownloadResult.NetworkFailure(ex.Message);
            }

            ct.ThrowIfCancellationRequested();

            // 5. Disk-space precheck
            var totalUncompressed = (long)manifest.TotalUncompressedSize;
            var availableBytes = GetAvailableBytes(destDir);
            if (availableBytes >= 0 && availableBytes < totalUncompressed)
                return new SteamDownloadResult.InsufficientDiskSpace(totalUncompressed, availableBytes);

            Directory.CreateDirectory(destDir);

            // 5b. Pre-download restore of any existing .bak files. If a previous install left
            // patched files in place, roll them back to their pre-patch state and drop the
            // backups — they reference the old vanilla and would be stale after this download.
            // The installer will create fresh backups during its next patch pass.
            RestoreExistingBackups(destDir);

            // 6. Extract files/directories
            var bytesDownloaded = 0L;
            var sw = Stopwatch.StartNew();
            var lastReportBytes = 0L;
            var lastReportTicks = sw.ElapsedTicks;

            var allFiles = manifest.Files ?? new List<DepotManifest.FileData>();

            // Create all directories first
            foreach (var file in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                if ((file.Flags & EDepotFileFlag.Directory) != 0)
                {
                    var dir = Path.Combine(destDir, NormalizePath(file.FileName));
                    Directory.CreateDirectory(dir);
                }
            }

            // Download files in parallel, chunks sequentially per file
            var downloadable = allFiles
                .Where(f => (f.Flags & EDepotFileFlag.Directory) == 0
                            && !SkipFileNames.Contains(Path.GetFileName(f.FileName)))
                .ToList();

            await Parallel.ForEachAsync(
                downloadable,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (file, fileCt) =>
                {
                    var relPath = NormalizePath(file.FileName);
                    var fullPath = Path.Combine(destDir, relPath);
                    var parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    // Update progress hook for current file
                    lock (_progressLock) { _currentFile = relPath; }

                    if ((file.Flags & EDepotFileFlag.Symlink) != 0)
                    {
                        // Unity expects these depot entries to exist; Steam stores the payload
                        // in LinkTarget instead of ordinary file chunks.
                        var written = await MaterializeSymlinkFileAsync(fullPath, file.LinkTarget, fileCt)
                            .ConfigureAwait(false);

                        var total = Interlocked.Add(ref bytesDownloaded, written);
                        ReportProgress(total, totalUncompressed, progress,
                            sw, ref lastReportBytes, ref lastReportTicks);
                        return;
                    }

                    if (file.Chunks == null || file.Chunks.Count == 0)
                    {
                        // 0-byte file
                        await using var _ = File.Create(fullPath);
                        return;
                    }

                    // Open file for random-offset writes
                    await using var fs = new FileStream(
                        fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: 81920, useAsync: true);
                    fs.SetLength((long)file.TotalSize);

                    // Track consecutive 5xx/429 failures within this file. A successful chunk
                    // resets the counter; hitting MaxConsecutiveTransientFailures aborts the
                    // file (and therefore the whole download) with a clear error.
                    int consecutiveTransientFailures = 0;

                    foreach (var chunk in file.Chunks)
                    {
                        fileCt.ThrowIfCancellationRequested();

                        var buffer = ArrayPool<byte>.Shared.Rent((int)chunk.UncompressedLength);
                        try
                        {
                            int written = await DownloadChunkWithRetryAsync(
                                cdnClient, chunk, server, buffer, depotKey, relPath,
                                () => consecutiveTransientFailures,
                                v => consecutiveTransientFailures = v,
                                fileCt).ConfigureAwait(false);

                            fs.Position = (long)chunk.Offset;
                            await fs.WriteAsync(buffer.AsMemory(0, written), fileCt)
                                .ConfigureAwait(false);

                            var total = Interlocked.Add(ref bytesDownloaded, written);
                            ReportProgress(total, totalUncompressed, progress,
                                sw, ref lastReportBytes, ref lastReportTicks);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                }).ConfigureAwait(false);

            // 7. Write the sidecar
            var buildId = await TryResolveBuildIdAsync(manifestId).ConfigureAwait(false);
            WriteSidecar(destDir, manifestId, buildId);

            return new SteamDownloadResult.Success(destDir, buildId ?? string.Empty, manifestId);
        }
        catch (OperationCanceledException)
        {
            return new SteamDownloadResult.Cancelled();
        }
        catch (Exception ex)
        {
            return new SteamDownloadResult.UnexpectedError(ex.Message);
        }
    }

    private readonly object _progressLock = new();
    private string _currentFile = string.Empty;

    // CDN retry policy: how many consecutive 5xx/429 responses for a single file we'll
    // tolerate before giving up. The counter resets on any successful chunk within that file.
    private const int MaxConsecutiveTransientFailures = 5;
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Downloads a single chunk with retry on transient CDN errors (HTTP 5xx and 429). The
    /// caller-supplied consecutive-failure counter is bumped on each transient failure and
    /// reset when the chunk eventually succeeds. If the counter reaches
    /// <see cref="MaxConsecutiveTransientFailures"/>, throws <see cref="IOException"/> with the
    /// last observed error — the per-file retry budget is spent.
    /// </summary>
    private static async Task<int> DownloadChunkWithRetryAsync(
        Client cdnClient,
        DepotManifest.ChunkData chunk,
        Server server,
        byte[] buffer,
        byte[] depotKey,
        string relPath,
        Func<int> getFailureCount,
        Action<int> setFailureCount,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                int written = await cdnClient
                    .DownloadDepotChunkAsync(ScpSlDepotId, chunk, server, buffer, depotKey)
                    .ConfigureAwait(false);
                setFailureCount(0);   // reset — we got a chunk after any prior failures
                return written;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientCdnError(ex))
            {
                int failures = getFailureCount() + 1;
                setFailureCount(failures);

                if (failures >= MaxConsecutiveTransientFailures)
                {
                    throw new IOException(
                        $"CDN kept returning transient errors for {relPath} after {failures} retries. " +
                        $"Last error: {ex.Message}", ex);
                }

                try
                {
                    await Task.Delay(TransientRetryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                // loop and retry
            }
            catch (Exception ex)
            {
                // Non-transient — fail fast, no retry.
                throw new IOException($"Chunk download failed for {relPath}: {ex.Message}", ex);
            }
        }
    }

    private static async Task<int> MaterializeSymlinkFileAsync(string fullPath, string? linkTarget, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(linkTarget ?? string.Empty);
        await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
        return bytes.Length;
    }

    private static bool IsTransientCdnError(Exception ex)
    {
        int? code = ex switch
        {
            SteamKitWebRequestException webEx => (int)webEx.StatusCode,
            HttpRequestException httpEx when httpEx.StatusCode.HasValue => (int)httpEx.StatusCode.Value,
            _ => null,
        };

        return code is 408 or 425 or 429 or (>= 500 and <= 599);
    }

    private void ReportProgress(
        long bytesDownloaded,
        long bytesTotal,
        IProgress<SteamDownloadProgress> progress,
        Stopwatch sw,
        ref long lastReportBytes,
        ref long lastReportTicks)
    {
        // Throttle reports to ~10/sec and compute rolling speed
        var nowTicks = sw.ElapsedTicks;
        var elapsed = TimeSpan.FromTicks(nowTicks - lastReportTicks);
        if (elapsed.TotalMilliseconds < 100 && bytesDownloaded < bytesTotal)
            return;

        var deltaBytes = bytesDownloaded - lastReportBytes;
        var speed = elapsed.TotalSeconds > 0 ? deltaBytes / elapsed.TotalSeconds : 0;
        lastReportBytes = bytesDownloaded;
        lastReportTicks = nowTicks;

        string file;
        lock (_progressLock) { file = _currentFile; }

        progress.Report(new SteamDownloadProgress(bytesDownloaded, bytesTotal, file, speed));
    }

    private static string NormalizePath(string manifestPath)
    {
        // Manifests use Windows-style separators; keep them on Windows, convert on Unix.
        return Path.DirectorySeparatorChar == '\\'
            ? manifestPath
            : manifestPath.Replace('\\', '/');
    }

    private static void RestoreExistingBackups(string destDir)
    {
        if (!Directory.Exists(destDir))
            return;

        string[] backups;
        try
        {
            backups = Directory.GetFiles(destDir, "*.bak", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        foreach (var bak in backups)
        {
            var original = bak[..^".bak".Length];
            try
            {
                File.Copy(bak, original, overwrite: true);
                File.Delete(bak);
            }
            catch
            {
                // Best-effort. If this file can't be restored the subsequent overwrite by
                // the depot download will still replace it with the fresh vanilla content.
            }
        }
    }

    private static long GetAvailableBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return -1;
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    private async Task<string?> TryResolveBuildIdAsync(ulong manifestId)
    {
        // Best-effort: query the public api.steamcmd.net endpoint the VersionCheckService also uses.
        // If it fails, we write the sidecar without a buildId — not fatal.
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var url = $"https://api.steamcmd.net/v1/info/{ScpSlAppId}";
            var response = await http.GetStringAsync(url).ConfigureAwait(false);
            var root = Newtonsoft.Json.Linq.JObject.Parse(response);
            var depots = root["data"]?[$"{ScpSlAppId}"]?["depots"];
            var manifests = depots?[$"{ScpSlDepotId}"]?["manifests"];
            if (manifests is Newtonsoft.Json.Linq.JObject manifestsObj)
            {
                foreach (var branch in manifestsObj.Properties())
                {
                    var branchData = branch.Value as Newtonsoft.Json.Linq.JObject;
                    var gid = (string?)branchData?["gid"];
                    if (gid == manifestId.ToString())
                    {
                        // Branch build ID lives under depots.branches.<branch>.buildid
                        var branchBuildId = (string?)depots?["branches"]?[branch.Name]?["buildid"];
                        if (!string.IsNullOrEmpty(branchBuildId))
                            return branchBuildId;
                    }
                }
            }
        }
        catch
        {
            // Ignored
        }

        return null;
    }

    private static void WriteSidecar(string destDir, ulong manifestId, string? buildId)
    {
        try
        {
            var path = Path.Combine(destDir, ".anomaly_source.json");
            var payload = JsonConvert.SerializeObject(new
            {
                source = "steamcmd",
                manifestId = manifestId.ToString(),
                buildId,
                downloadedAt = DateTime.UtcNow.ToString("O"),
            }, Formatting.Indented);
            File.WriteAllText(path, payload);
        }
        catch
        {
            // Sidecar is informational; failure is non-fatal.
        }
    }

    // ===================== Dispose =====================

    public void Dispose()
    {
        lock (_lock)
        {
            try { _client?.Disconnect(); } catch { }
            StopCallbackPump();
            _client = null;
            _callbackManager = null;
            _isLoggedOn = false;
            _loggedOnAccountName = null;
            _loggedOnSteamId = 0;
        }
    }
}
