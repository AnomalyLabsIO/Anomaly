using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.Utils;

namespace Anomaly.Bootstrap;

// Prototype: checks one protected executable canary and its page protection.
// Ordinary managed entrypoints change during CLR code-version transitions; a
// profiler is needed before monitoring real JIT bodies without false positives.
internal static class NativeCodeIntegrity
{
    private const int EntryBytes = 16;
    private static readonly string[] Names = { "Bootstrap canary" };
    private static AddRegion? _add;
    private static CheckRegion? _check;
    private static int[]? _regions;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AddRegion(IntPtr address, nuint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CheckRegion(int index);

    internal static bool Enabled => _regions != null;

    internal static void Install()
    {
        if (_regions != null) throw new InvalidOperationException(BootstrapLocalization.Get(TrKeys.Bootstrap_NativeAlreadyInstalled));
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException(BootstrapLocalization.Get(TrKeys.Bootstrap_NativePlatformUnsupported));
        string path = Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly", "Native", "Anomaly.Bootstrap.Native.dll");
        if (!File.Exists(path)) throw new FileNotFoundException(BootstrapLocalization.Get(TrKeys.Bootstrap_NativePrototypeMissing), path);
        IntPtr library = NativeLibrary.Load(path);
        _add = Marshal.GetDelegateForFunctionPointer<AddRegion>(NativeLibrary.GetExport(library, "AnomalyGuardAdd"));
        _check = Marshal.GetDelegateForFunctionPointer<CheckRegion>(NativeLibrary.GetExport(library, "AnomalyGuardCheck"));

        MethodInfo canary = typeof(NativeCodeIntegrity).GetMethod(nameof(Canary), BindingFlags.NonPublic | BindingFlags.Static)!;
        Canary();
        var methods = new[] { canary };
        var indices = new int[methods.Length];
        for (int i = 0; i < methods.Length; ++i)
        {
            RuntimeHelpers.PrepareMethod(methods[i].MethodHandle);
            IntPtr entry = methods[i].MethodHandle.GetFunctionPointer();
            indices[i] = _add(entry, EntryBytes);
            if (indices[i] < 0)
                throw new InvalidOperationException(BootstrapLocalization.Get(TrKeys.Bootstrap_NativeBaselineFailed, Names[i]));
        }
        _regions = indices;
    }

    internal static bool Verify(out string? reason)
    {
        reason = null;
        if (_regions == null) return true;
        for (int i = 0; i < _regions.Length; ++i)
        {
            if (_check!(_regions[i]) == 0) continue;
            reason = BootstrapLocalization.Get(TrKeys.Bootstrap_NativeEntryChanged, Names[i]);
            return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    internal static int Canary() => 0x6A1C;
}
