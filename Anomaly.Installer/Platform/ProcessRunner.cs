using System.Diagnostics;

namespace Anomaly.Installer.Platform;

public static class ProcessRunner
{
    /// <summary>
    /// Runs a Windows executable. On Linux, prefer Steam Proton when available and fall
    /// back to Wine. Forwards stdout and stderr to the console so subprocess failures stay
    /// visible in the install log.
    /// </summary>
    public static int RunExe(string exePath, IReadOnlyList<string> arguments, PlatformInfo platform, int timeoutMs = 120000)
    {
        ProcessStartInfo psi = BuildProcessStartInfo(exePath, arguments, platform);

        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

        using var process = new Process { StartInfo = psi };

        string exeLabel = Path.GetFileNameWithoutExtension(exePath);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"    [{exeLabel}] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.Error.WriteLine($"    [{exeLabel}] {e.Data}");
        };

        if (!process.Start())
            throw new InstallerException($"Failed to start process: {exePath}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(); } catch { /* best-effort */ }
            throw new InstallerException($"Process timed out after {timeoutMs / 1000} seconds: {exePath}");
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static ProcessStartInfo BuildProcessStartInfo(string exePath, IReadOnlyList<string> arguments, PlatformInfo platform)
    {
        if (platform.IsWindows)
        {
            var psi = new ProcessStartInfo(exePath);
            foreach (string argument in arguments)
                psi.ArgumentList.Add(argument);
            return psi;
        }

        if (!platform.IsLinux || platform.LinuxRuntime == null)
            throw new InstallerException("Unsupported operating system.");

        if (platform.LinuxRuntime.HasProton)
            return BuildProtonStartInfo(exePath, arguments, platform.LinuxRuntime);

        if (platform.LinuxRuntime.HasWine)
            return BuildWineStartInfo(exePath, arguments, platform.LinuxRuntime.WineCommand!);

        throw new InstallerException(
            "This Linux install needs a Windows compatibility runtime, but neither Proton nor Wine was found. " +
            "Install a Proton tool through Steam or install Wine and retry.");
    }

    private static ProcessStartInfo BuildWineStartInfo(string exePath, IReadOnlyList<string> arguments, string wineCommand)
    {
        var psi = new ProcessStartInfo(wineCommand);
        psi.ArgumentList.Add(exePath);
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);
        return psi;
    }

    private static ProcessStartInfo BuildProtonStartInfo(string exePath, IReadOnlyList<string> arguments, LinuxRuntimeInfo runtime)
    {
        ProtonTool proton = runtime.PreferredProton
            ?? throw new InstallerException("Proton was selected but no Proton tool is available.");

        Directory.CreateDirectory(runtime.CompatDataPath);

        var psi = new ProcessStartInfo(proton.LauncherPath);
        psi.ArgumentList.Add("waitforexitandrun");
        psi.ArgumentList.Add(exePath);
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);

        psi.Environment["STEAM_COMPAT_DATA_PATH"] = runtime.CompatDataPath;
        if (!string.IsNullOrWhiteSpace(runtime.SteamRootPath))
            psi.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = runtime.SteamRootPath;

        string? installPath = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrWhiteSpace(installPath))
            psi.Environment["STEAM_COMPAT_INSTALL_PATH"] = installPath;

        return psi;
    }
}
