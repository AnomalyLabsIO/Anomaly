using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Anomaly.Bootstrap;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

internal static class ClrEntryStubProbe
{
    internal static void Run(Action<bool, string> check)
    {
        AlternateEntryChain(check);
        MethodInfo original = typeof(VirtualFixture).GetMethod(nameof(VirtualFixture.Value))!;
        MethodInfo replacement = typeof(ClrEntryStubProbe).GetMethod(nameof(Replacement), BindingFlags.NonPublic | BindingFlags.Static)!;
        original.Pin();
        // Deliberately use the CLR entry stub, not MonoMod's resolved method body.
        // This recreates the exact source form that broke Apply in the full dump.
        IntPtr stub = original.MethodHandle.GetFunctionPointer();
        byte[] before = Read(stub, 16);
        check(ClrEntryStubPreserver.IsFixupPrecode(before), "regression fixture exposes a real CLR fixup entry stub");
        var platform = DetourHelper.Native;
        NativeDetourData data = platform.Create(stub, replacement.GetNativeStart(), null);
        check(data.Method != stub, "detour targets compiled implementation instead of CLR precode");
        IntPtr forged = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.Copy(before, 0, forged, before.Length);
            bool rejected = false;
            try { platform.Create(forged, replacement.GetNativeStart(), null); }
            catch (NotSupportedException) { rejected = true; }
            check(rejected, "forged precode cannot redirect a raw source onto a different method body");
            rejected = false;
            try { platform.Apply(new NativeDetourData { Method = stub, Target = data.Target, Type = 2, Size = 14 }); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && before.AsSpan().SequenceEqual(Read(stub, 16)), "preconstructed detour data cannot overwrite a CLR precode");
        }
        finally { Marshal.FreeHGlobal(forged); }
        IntPtr backup = Marshal.AllocHGlobal((int)data.Size);
        bool applied = false;
        try
        {
            platform.Copy(data.Method, backup, data.Type);
            platform.MakeWritable(data.Method, data.Size);
            platform.Apply(data);
            applied = true;
            platform.MakeExecutable(data.Method, data.Size);
            platform.FlushICache(data.Method, data.Size);
            check(before.AsSpan().SequenceEqual(Read(stub, 16)), "entire CLR entry stub survives patching unchanged");

            var caller = new DynamicMethod("CallerCompiledAfterEntryStubPatch", typeof(int), new[] { typeof(VirtualFixture) }, typeof(ClrEntryStubProbe).Module, true);
            ILGenerator il = caller.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, original);
            il.Emit(OpCodes.Ret);
            var invoke = (Func<VirtualFixture, int>)caller.CreateDelegate(typeof(Func<VirtualFixture, int>));
            check(invoke(new VirtualFixture()) == 77, "newly JIT-compiled caller resolves and invokes patched virtual method");
        }
        finally
        {
            if (applied)
            {
                platform.MakeWritable(data.Method, data.Size);
                platform.Copy(backup, data.Method, data.Type);
                platform.MakeExecutable(data.Method, data.Size);
                platform.FlushICache(data.Method, data.Size);
            }
            platform.Free(data);
            Marshal.FreeHGlobal(backup);
            original.Unpin();
        }
        check(before.AsSpan().SequenceEqual(Read(stub, 16)), "undo restores the full original CLR entry stub");
        check(ClrEntryStubPreserver.IsInstalled && RuntimeGuard.Verify(requireComplete: false), "all guard protection remains verified after stub regression");

        // Supported setter calls cannot replace the adapter or remove its required hook.
        var inner = (IDetourNativePlatform)typeof(ClrEntryStubPreserver)
            .GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(platform)!;
        DetourHelper.Native = inner;
        check(ReferenceEquals(platform, DetourHelper.Native), "native platform replacement through setter is blocked");
        MethodInfo setter = typeof(DetourHelper).GetProperty(nameof(DetourHelper.Native))!.SetMethod!;
        new HarmonyLib.Harmony("anomaly.guard.probe.remove-platform-protection")
            .Unpatch(setter, HarmonyLib.HarmonyPatchType.All, "*");
        check(RuntimeGuard.Verify(requireComplete: false), "native platform interceptor survives foreign unpatch");
    }

    private static void AlternateEntryChain(Action<bool, string> check)
    {
        MethodInfo original = typeof(AlternateFixture).GetMethod(nameof(AlternateFixture.Value))!;
        MethodInfo replacement = typeof(ClrEntryStubProbe).GetMethod(nameof(AlternateReplacement), BindingFlags.NonPublic | BindingFlags.Static)!;
        original.Pin();
        var platform = DetourHelper.Native;
        IntPtr canonical = original.MethodHandle.GetFunctionPointer();
        byte[] canonicalBytes = Read(canonical, 16);
        IntPtr temporary = TemporaryPrecode(original.MethodHandle);
        byte[] temporaryBytes = Read(temporary, 16);
        check(canonical != temporary && ClrEntryStubPreserver.IsFixupPrecode(canonicalBytes) &&
            ClrEntryStubPreserver.IsFixupPrecode(temporaryBytes), "fixture has two distinct CLR-owned precodes for the same pinned method");

        // Recreate the real CLR entry chain seen in the failing game run. Both
        // precodes belong to this method; only the canonical branch is redirected,
        // then restored. The production adapter must independently resolve the
        // incoming temporary precode rather than require canonical pointer equality.
        byte[] chain = canonicalBytes.ToArray();
        chain[0] = 0xE9;
        BitConverter.GetBytes(checked((int)(temporary.ToInt64() - canonical.ToInt64() - 5))).CopyTo(chain, 1);
        bool redirected = false;
        try
        {
            platform.MakeWritable(canonical, 5);
            Marshal.Copy(chain, 0, canonical, 5);
            redirected = true;
            platform.MakeExecutable(canonical, 5);
            platform.FlushICache(canonical, 5);
            check(original.GetNativeStart() == temporary, "loader independently resolves the alternate CLR precode");
            IntPtr forged = Marshal.AllocHGlobal(16 + temporaryBytes[7] * 8);
            try
            {
                Marshal.Copy(temporaryBytes, 0, forged, temporaryBytes.Length);
                IntPtr descriptorBase = Marshal.ReadIntPtr(new IntPtr(temporary.ToInt64() + 8L + temporaryBytes[7] * 8L));
                Marshal.WriteIntPtr(forged, 8 + temporaryBytes[7] * 8, descriptorBase);
                bool rejected = false;
                try { platform.Create(forged, replacement.GetNativeStart(), null); }
                catch (NotSupportedException) { rejected = true; }
                check(rejected, "copied alternate precode with genuine MethodDesc metadata is rejected");
            }
            finally { Marshal.FreeHGlobal(forged); }
            NativeDetourData data = platform.Create(temporary, replacement.GetNativeStart(), null);
            try
            {
                check(data.Method != temporary && data.Method != canonical, "alternate CLR precode resolves to the same compiled method body");
                check(temporaryBytes.AsSpan().SequenceEqual(Read(temporary, 16)), "alternate CLR precode stays intact during body resolution");
            }
            finally { platform.Free(data); }
        }
        finally
        {
            if (redirected)
            {
                platform.MakeWritable(canonical, 5);
                Marshal.Copy(canonicalBytes, 0, canonical, 5);
                platform.MakeExecutable(canonical, 5);
                platform.FlushICache(canonical, 5);
            }
            original.Unpin();
        }
        check(canonicalBytes.AsSpan().SequenceEqual(Read(canonical, 16)), "alternate entry-chain fixture restores the canonical precode");
    }

    private static IntPtr TemporaryPrecode(RuntimeMethodHandle handle)
    {
        // CoreCLR 6 x64: the MethodDesc chunk owns an array of eight-byte fixup
        // precodes. Read its trusted chunk pointer and find this method's entry.
        byte[] descriptor = Read(handle.Value, 8);
        IntPtr chunk = new(handle.Value.ToInt64() - 24 - descriptor[2] * 8L);
        byte[] metadata = Read(chunk, 24);
        if ((BitConverter.ToUInt16(metadata, 18) & 0xC000) != 0)
            throw new NotSupportedException("The fixture requires an ordinary CLR MethodDesc chunk.");
        int count = metadata[17] + 1;
        IntPtr entries = Marshal.ReadIntPtr(chunk, -8);
        for (int index = 0; index < count; index++)
        {
            IntPtr source = new(entries.ToInt64() + index * 8L);
            byte[] entry = Read(source, 8);
            if (!ClrEntryStubPreserver.IsFixupPrecode(entry)) continue;
            long methodDesc = Marshal.ReadIntPtr(new IntPtr(source.ToInt64() + 8L + entry[7] * 8L)).ToInt64() + entry[6] * 8L;
            if (methodDesc == handle.Value.ToInt64()) return source;
        }
        throw new InvalidOperationException("CLR did not expose the fixture's temporary precode.");
    }

    internal static void Tamper(Action<bool, string> check)
    {
        var platform = DetourHelper.Native;
        var inner = typeof(ClrEntryStubPreserver).GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(platform);
        // Deliberately bypass the setter, as an arbitrary private-field writer can.
        typeof(DetourHelper).GetField("_Native", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, inner);
        check(!RuntimeGuard.Verify(requireComplete: false) && !RuntimeGuard.Snapshot.Healthy,
            "native platform backing-field tampering fails closed");
    }

    private static byte[] Read(IntPtr address, int count)
    {
        var result = new byte[count];
        Marshal.Copy(address, result, 0, count);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Replacement(VirtualFixture instance) => 77;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int AlternateReplacement(AlternateFixture instance) => 77;

    private class AlternateFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual int Value() => 1;
    }

    private interface IVirtualFixture { int Value(); }
    private sealed class VirtualFixture : IVirtualFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Value() => 1;
    }
}
