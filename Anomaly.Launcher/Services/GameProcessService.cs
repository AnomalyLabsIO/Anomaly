using System.Diagnostics;
using Anomaly.Launcher.Platform;

namespace Anomaly.Launcher.Services;

public class GameProcessService
{
    private readonly PathService _paths;

    public Process? CurrentProcess { get; private set; }
    public bool IsRunning => CurrentProcess is { HasExited: false };
    public DateTime? GameStartTime { get; private set; }

    public event EventHandler<int>? GameExited;

    public GameProcessService(PathService paths)
    {
        _paths = paths;
    }

    public bool LaunchVanilla()
    {
        var vanillaPath = _paths.GetVanillaExePath();
        if (vanillaPath == null || !File.Exists(vanillaPath))
            return false;

        return StartProcess(vanillaPath);
    }

    public bool LaunchModded()
    {
        var moddedPath = _paths.GetModdedExePath();
        if (!File.Exists(moddedPath))
            return false;

        return StartProcess(moddedPath);
    }

    public void KillGame()
    {
        if (CurrentProcess is { HasExited: false })
        {
            try
            {
                CurrentProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited
            }
        }
    }

    public bool LaunchInstaller(string installerPath, string? vanillaExePath)
    {
        if (IsRunning) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)
            };

            if (vanillaExePath != null)
            {
                psi.Arguments = $"--vanilla-path \"{vanillaExePath}\"";
            }

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool StartProcess(string exePath)
    {
        if (IsRunning)
            return false;

        try
        {
            var psi = new ProcessStartInfo();

            if (PlatformHelper.IsLinux)
            {
                psi.FileName = "wine";
                psi.ArgumentList.Add(exePath);
            }
            else
            {
                psi.FileName = exePath;
            }

            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(exePath);

            var process = Process.Start(psi);
            if (process == null)
                return false;

            CurrentProcess = process;
            GameStartTime = DateTime.UtcNow;
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = 0;
        try
        {
            exitCode = CurrentProcess?.ExitCode ?? 0;
        }
        catch
        {
            // May throw if process was killed
        }

        GameExited?.Invoke(this, exitCode);
    }
}
