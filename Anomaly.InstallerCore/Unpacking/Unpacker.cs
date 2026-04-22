using Anomaly.InstallerCore.Platform;

namespace Anomaly.InstallerCore.Unpacking;

public class Unpacker
{
    // Themida unpacking is usually fast (a few seconds) but can hang if the Themida
    // license-server probe is slow. 45 s is long enough for realistic cases; anything
    // longer almost always means something is wrong.
    private const int DefaultTimeoutMs = 45_000;

    private readonly string _unpackerPath;
    private readonly string _gameAssemblyPath;
    private readonly PlatformInfo _platform;
    private readonly int _timeoutMs;

    public Unpacker(string unpackerPath, string gameAssemblyPath, PlatformInfo platform, int timeoutMs = DefaultTimeoutMs)
    {
        _unpackerPath = unpackerPath;
        _gameAssemblyPath = gameAssemblyPath;
        _platform = platform;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Unpacks a Themida-protected GameAssembly.dll using unlicense.exe.
    /// </summary>
    public void UnpackGameAssembly()
    {
        Console.WriteLine("GameAssembly.dll is protected by Themida. Unpacking...");

        if (!File.Exists(_unpackerPath))
            throw new InstallerException($"Unpacker not found at: {_unpackerPath}");

        int exitCode = ProcessRunner.RunExe(_unpackerPath, new[] { _gameAssemblyPath }, _platform, _timeoutMs);
        if (exitCode != 0)
            throw new InstallerException($"Unpacker exited with code {exitCode}. See log for subprocess output.");

        string unpackerFolder = Path.GetDirectoryName(_unpackerPath) ?? string.Empty;
        string unpackedPath = Path.Combine(unpackerFolder, "unpacked_GameAssembly.dll");

        if (!File.Exists(unpackedPath))
            throw new InstallerException("Unpacking failed. unpacked_GameAssembly.dll was not created.");

        // Sanity check: an unpacked DLL of a few KB means the unpacker bailed silently.
        var unpackedInfo = new FileInfo(unpackedPath);
        if (unpackedInfo.Length < 1024 * 1024)
            throw new InstallerException(
                $"Unpacked GameAssembly.dll is suspiciously small ({unpackedInfo.Length} bytes). Aborting before overwriting the original.");

        // Atomic overwrite — no intermediate state where GameAssembly.dll is missing.
        File.Move(unpackedPath, _gameAssemblyPath, overwrite: true);

        Console.WriteLine("GameAssembly.dll unpacked successfully.");
    }
}
