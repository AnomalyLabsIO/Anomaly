using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MelonLoader;

namespace Anomaly.Bootstrap;

// Opt-in startup breadcrumbs must reach disk before entering the native patcher.
internal sealed class PatchInstallationTrace : IDisposable
{
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private bool _failed;

    private PatchInstallationTrace(string path)
    {
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true);
    }

    internal static PatchInstallationTrace? Open()
    {
        string? path = Environment.GetEnvironmentVariable("ANOMALY_BOOTSTRAP_PATCH_TRACE");
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var trace = new PatchInstallationTrace(path);
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
