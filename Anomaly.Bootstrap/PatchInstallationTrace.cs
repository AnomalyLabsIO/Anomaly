using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace Anomaly.Bootstrap;

// Opt-in startup breadcrumbs must reach disk before entering the native patcher.
internal sealed class PatchInstallationTrace : IDisposable
{
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private bool _failed;

    private PatchInstallationTrace(string path, bool overwrite)
    {
        _stream = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true);
    }

    internal static PatchInstallationTrace? Open()
    {
        try
        {
            string? configuredPath = Environment.GetEnvironmentVariable("ANOMALY_BOOTSTRAP_PATCH_TRACE");
            string? path = ResolvePath(configuredPath, string.IsNullOrWhiteSpace(configuredPath)
                ? MelonEnvironment.UserDataDirectory : string.Empty);
            if (path == null) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var trace = new PatchInstallationTrace(path, overwrite: !string.IsNullOrWhiteSpace(configuredPath));
            trace.Write($"START pid={Environment.ProcessId} runtime={RuntimeInformation.FrameworkDescription} " +
                $"architecture={RuntimeInformation.ProcessArchitecture} bootstrap={typeof(Core).Module.ModuleVersionId} " +
                $"loader={typeof(MelonPlugin).Assembly.FullName} harmony={typeof(HarmonyLib.Harmony).Assembly.FullName} " +
                $"monomod={typeof(MonoMod.RuntimeDetour.Detour).Assembly.FullName}");
            return trace;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning("Bootstrap patch trace could not open: " + exception.Message);
            return null;
        }
    }

    internal static string? ResolvePath(string? configuredPath, string userDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return Path.GetFullPath(configuredPath);
        string root = Path.Combine(userDataDirectory, "Anomaly");
        if (!File.Exists(Path.Combine(root, "bootstrap-patch-trace.enabled"))) return null;
        return Path.GetFullPath(Path.Combine(root, "BootstrapPatchTraces",
            $"bootstrap-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log"));
    }

    internal void Begin(int ordinal, MethodBase target, string prefix, string finalizer) =>
        Write($"BEGIN patch={ordinal} assembly={target.Module.Assembly.FullName} mvid={target.Module.ModuleVersionId} " +
            $"token=0x{target.MetadataToken:X8} target={target.DeclaringType?.FullName}::{target} " +
            $"prefix={prefix} finalizer={finalizer}");

    internal void Write(string message)
    {
        if (_failed) return;
        try
        {
            _writer.WriteLine($"{DateTime.UtcNow:O} elapsedMs={_elapsed.ElapsedMilliseconds} thread={Environment.CurrentManagedThreadId} {message}");
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
        }
        catch (Exception exception)
        {
            _failed = true;
            MelonLogger.Warning("Bootstrap patch trace stopped: " + exception.Message);
        }
    }

    public void Dispose()
    {
        try { _writer.Dispose(); }
        catch (Exception exception) { MelonLogger.Warning("Bootstrap patch trace close failed: " + exception.Message); }
        finally { _stream.Dispose(); }
    }
}
