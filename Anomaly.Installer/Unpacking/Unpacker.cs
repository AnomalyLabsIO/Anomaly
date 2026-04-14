using Anomaly.Installer.Platform;

namespace Anomaly.Installer.Unpacking;

internal class Unpacker
{
    private readonly string _unpackerPath;
    private readonly string _gameAssemblyPath;
    private readonly PlatformInfo _platform;

    public Unpacker(string unpackerPath, string gameAssemblyPath, PlatformInfo platform)
    {
        _unpackerPath = unpackerPath;
        _gameAssemblyPath = gameAssemblyPath;
        _platform = platform;
    }

    /// <summary>
    /// Unpacks a Themida-protected GameAssembly.dll using unlicense.exe.
    /// </summary>
    public void UnpackGameAssembly()
    {
        Console.WriteLine("GameAssembly.dll is protected by Themida. Unpacking...");

        if (!File.Exists(_unpackerPath))
            throw new InstallerException($"Unpacker not found at: {_unpackerPath}");

        int exitCode = ProcessRunner.RunExe(_unpackerPath, $"\"{_gameAssemblyPath}\"", _platform);

        string unpackerFolder = Path.GetDirectoryName(_unpackerPath) ?? string.Empty;
        string unpackedPath = Path.Combine(unpackerFolder, "unpacked_GameAssembly.dll");

        if (!File.Exists(unpackedPath))
            throw new InstallerException("Unpacking failed. unpacked_GameAssembly.dll was not created.");

        File.Delete(_gameAssemblyPath);
        File.Move(unpackedPath, _gameAssemblyPath);

        Console.WriteLine("GameAssembly.dll unpacked successfully.");
    }
}
