using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.Platforms;

namespace Anomaly.Bootstrap;

// Keep CLR fixup precodes intact. Detouring only their jump can also leave newly
// compiled callers using the original body, bypassing the guard's interceptor.
// Resolve a genuine, pinned CLR method and detour its compiled implementation.
internal sealed class ClrEntryStubPreserver : IDetourNativePlatform
{
    private readonly IDetourNativePlatform _inner;
    private static ClrEntryStubPreserver? _installed;

    private ClrEntryStubPreserver(IDetourNativePlatform inner) => _inner = inner;

    internal static void Install()
    {
        if (_installed != null) throw new InvalidOperationException("CLR entry stub preservation is already installed.");
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || Environment.Version.Major != 6)
            throw new NotSupportedException("CLR entry stub preservation requires the supported Windows x64 .NET 6 runtime.");
        var platform = new ClrEntryStubPreserver(DetourHelper.Native
            ?? throw new InvalidOperationException("The loader native detour platform is unavailable."));
        DetourHelper.Native = platform;
        _installed = platform;
    }

    internal static bool IsInstalled => _installed != null && ReferenceEquals(DetourHelper.Native, _installed);
    internal static bool IsExpectedPlatform(IDetourNativePlatform platform) => _installed != null && ReferenceEquals(platform, _installed);

    internal static bool IsFixupPrecode(ReadOnlySpan<byte> bytes) => bytes.Length >= 8 &&
        ((bytes[0] == 0xE9 && bytes[5] == 0x5F) ||
         (bytes[0] == 0xE8 && bytes[5] == 0x5E));

    // The supported CoreCLR 6 x64 ordinary IL MethodDesc has an eight-byte base,
    // an optional non-vtable slot, then its native code slot. Other classifications
    // or MethodImpl layouts must not be guessed from arbitrary native memory.
    internal static int NativeCodeSlotOffset(ushort flags) =>
        (flags & 7) == 0 && (flags & 0x20) != 0 && (flags & 0x10) == 0
            ? 8 + ((flags & 8) != 0 ? 8 : 0) : -1;

    public NativeDetourData Create(IntPtr from, IntPtr to, byte? type)
    {
        byte[]? entry = Read(from, 16);
        IntPtr source = entry != null && IsFixupPrecode(entry) ? ResolveBody(from, entry) : from;
        NativeDetourData data = _inner.Create(source, to, type);
        if (data.Method != source || data.Target != to)
            throw new InvalidOperationException("The native detour platform changed the resolved source or destination.");
        return data;
    }

    private static IntPtr ResolveBody(IntPtr source, byte[] entry)
    {
        // A fabricated precode must never redirect an authorized raw source onto
        // a different protected method. Match its MethodDesc and an independently
        // resolved entrypoint against the loader's already-pinned managed identity.
        IntPtr chunkAddress = new(checked(source.ToInt64() + 8L + entry[7] * 8L));
        byte[] chunk = Read(chunkAddress, 8) ?? throw new NotSupportedException("CLR precode metadata is unreadable.");
        long methodDesc = checked(BitConverter.ToInt64(chunk) + entry[6] * 8L);
        if (DetourHelper.Runtime is not DetourRuntimeILPlatform runtime)
            throw new NotSupportedException("The supported loader CLR runtime adapter is unavailable.");
        var pin = runtime.GetPins().FirstOrDefault(candidate => candidate.Handle.Value.ToInt64() == methodDesc);
        IntPtr canonical = pin.Method == null ? IntPtr.Zero : pin.Handle.GetFunctionPointer();
        // GetFunctionPointer is not the only entrypoint for a method. MonoMod
        // follows CLR precodes and can return another CLR-owned precode in the
        // same method's entry chain. Resolve from the independently pinned method,
        // never by following an arbitrary caller-supplied source jump.
        IntPtr resolved = pin.Method == null || canonical == source ? canonical : runtime.GetNativeStart(pin.Method);
        if (pin.Method == null || (canonical != source && resolved != source))
            throw new NotSupportedException($"CLR precode does not match a pinned managed entrypoint. " +
                $"source=0x{source.ToInt64():X} descriptor=0x{methodDesc:X} canonical=0x{canonical.ToInt64():X} resolved=0x{resolved.ToInt64():X} " +
                $"pinned={pin.Method != null} method={pin.Method?.DeclaringType?.FullName}::{pin.Method} " +
                $"entry={Convert.ToHexString(entry)}");

        // Pinning normally already prepared this method. Preparation is bounded
        // and precedes all native writes, including for a cold fixup entrypoint.
        RuntimeHelpers.PrepareMethod(pin.Handle);
        byte[] descriptor = Read(pin.Handle.Value, 8) ?? throw new NotSupportedException("Pinned CLR method metadata is unreadable.");
        int offset = NativeCodeSlotOffset(BitConverter.ToUInt16(descriptor, 6));
        if (offset < 0) throw new NotSupportedException("Unsupported CLR method descriptor layout; refusing to overwrite its precode.");
        byte[] slot = Read(new IntPtr(checked(methodDesc + offset)), 8)
            ?? throw new NotSupportedException("Pinned CLR native code slot is unreadable.");
        long address = BitConverter.ToInt64(slot);
        IntPtr body = new(address);
        byte[]? bodyEntry = address > 0 && (address & 1) == 0 && body != source ? Read(body, 8) : null;
        if (bodyEntry == null || IsFixupPrecode(bodyEntry))
            throw new NotSupportedException("Pinned CLR method has no confirmed compiled implementation.");
        return body;
    }

    public void Apply(NativeDetourData detour)
    {
        byte[]? entry = Read(detour.Method, 8);
        if (entry != null && IsFixupPrecode(entry))
            throw new InvalidOperationException("Native detour data targets a CLR entry stub instead of its compiled implementation.");
        _inner.Apply(detour);
    }

    private static byte[]? Read(IntPtr address, int count)
    {
        if (address == IntPtr.Zero) return null;
        var bytes = new byte[count];
        // An unmapped raw source must not cause an access violation in the adapter.
        return ReadProcessMemory(new IntPtr(-1), address, bytes, (UIntPtr)bytes.Length, out UIntPtr read) &&
            read.ToUInt64() == (ulong)bytes.Length ? bytes : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr read);

    public void Free(NativeDetourData detour) => _inner.Free(detour);
    public void Copy(IntPtr source, IntPtr destination, byte type) => _inner.Copy(source, destination, type);
    public void MakeWritable(IntPtr source, uint size) => _inner.MakeWritable(source, size);
    public void MakeExecutable(IntPtr source, uint size) => _inner.MakeExecutable(source, size);
    public void MakeReadWriteExecutable(IntPtr source, uint size) => _inner.MakeReadWriteExecutable(source, size);
    public void FlushICache(IntPtr source, uint size) => _inner.FlushICache(source, size);
    public IntPtr MemAlloc(uint size) => _inner.MemAlloc(size);
    public void MemFree(IntPtr pointer) => _inner.MemFree(pointer);
}
