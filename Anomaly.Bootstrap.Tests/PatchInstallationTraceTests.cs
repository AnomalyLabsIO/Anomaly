using System.Reflection;
using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class PatchInstallationTraceTests : IDisposable
{
    private readonly string _userData = Path.Combine(Path.GetTempPath(), "Anomaly.Bootstrap.Trace.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Persistent_capture_preserves_incomplete_trace_when_another_startup_runs()
    {
        Enable();
        string first = PatchInstallationTrace.ResolvePath(null, _userData)!;
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        using (var trace = Create(first))
        {
            trace.Begin(1, typeof(PatchInstallationTraceTests).GetMethod(nameof(Target), BindingFlags.Static | BindingFlags.NonPublic)!, "prefix", "finalizer");
            // Read before disposal: the last attempted hook must already be on disk.
            using var reader = new StreamReader(new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Assert.Contains("BEGIN patch=1", reader.ReadToEnd());
        }
        byte[] incomplete = File.ReadAllBytes(first);
        string second = PatchInstallationTrace.ResolvePath(null, _userData)!;
        Assert.NotEqual(first, second);
        using (var trace = Create(second)) trace.Write("INSTALL_COMPLETE patches=52");
        Assert.Equal(incomplete, File.ReadAllBytes(first));
        Assert.DoesNotContain("INSTALL_COMPLETE", File.ReadAllText(first));
        Assert.Contains("INSTALL_COMPLETE", File.ReadAllText(second));
    }

    [Fact]
    public void Removing_marker_disables_capture_without_deleting_existing_evidence()
    {
        Enable();
        string path = PatchInstallationTrace.ResolvePath(null, _userData)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var trace = Create(path)) trace.Write("BEGIN patch=1");
        File.Delete(Path.Combine(_userData, "Anomaly", "bootstrap-patch-trace.enabled"));
        Assert.Null(PatchInstallationTrace.ResolvePath(null, _userData));
        Assert.Contains("BEGIN patch=1", File.ReadAllText(path));
    }

    private void Enable()
    {
        string root = Path.Combine(_userData, "Anomaly");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "bootstrap-patch-trace.enabled"), string.Empty);
    }

    private static PatchInstallationTrace Create(string path) =>
        (PatchInstallationTrace)Activator.CreateInstance(typeof(PatchInstallationTrace), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { path, false }, culture: null)!;

    private static void Target() { }

    public void Dispose()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Anomaly.Bootstrap.Trace.Tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_userData).StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Trace test cleanup escaped its temporary directory.");
        if (Directory.Exists(_userData)) Directory.Delete(_userData, recursive: true);
    }
}
