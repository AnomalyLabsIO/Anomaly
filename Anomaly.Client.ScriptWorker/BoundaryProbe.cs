using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Anomaly.Client.Scripting.Remote.Protocol;

namespace Anomaly.Client.ScriptWorker;

internal static class BoundaryProbe
{
    private const int ChunkBytes = 1024 * 1024;

    internal static WireValue Run(WireValue data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Worker containment requires Windows.");
        var mode = data.Required("mode").RequireString(16);
        if (mode == "Memory")
        {
            Wire.Shape(data, "mode");
            return ProbeMemory();
        }
        if (mode != "Boundary") throw new InvalidDataException("Unknown qualification probe.");
        Wire.Shape(data, "mode", "sentinel", "endpoint", "executable");
        var sentinel = data.Required("sentinel").RequireString(1024);
        var executable = data.Required("executable").RequireString(1024);
        var endpoint = data.Required("endpoint").RequireString(128);
        if (!Path.IsPathFullyQualified(sentinel) || !Path.IsPathFullyQualified(executable) ||
            !IPEndPoint.TryParse(endpoint, out var address) || !IPAddress.IsLoopback(address.Address) || address.Port == 0)
            throw new InvalidDataException("Invalid qualification fixture.");
        var readDenied = false;
        try { using var file = new FileStream(sentinel, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); file.ReadByte(); }
        catch (UnauthorizedAccessException) { readDenied = true; }
        var writeDenied = false;
        try { using var file = new FileStream(sentinel, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }
        catch (UnauthorizedAccessException) { writeDenied = true; }
        var networkDenied = false;
        var networkTimedOut = false;
        var networkUnavailable = false;
        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            socket.ConnectAsync(address, timeout.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AccessDenied) { networkDenied = true; }
        // Timeout alone is not proof of denial. The parent must establish that
        // this same endpoint remained reachable and accepted no worker connection.
        catch (OperationCanceledException) { networkTimedOut = true; }
        catch (TimeoutException) { networkTimedOut = true; }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut) { networkTimedOut = true; }
        // LPAC may deny Winsock provider/catalog initialization before connect.
        // Report this separately; the parent still requires its token-authority
        // proof, zero network capabilities and reachable-endpoint controls.
        catch (SocketException exception) when (exception.NativeErrorCode == 10107) { networkUnavailable = true; }
        catch (SocketException exception)
        {
            throw new InvalidOperationException("Network isolation probe failed: Winsock " + exception.NativeErrorCode + " (" + exception.SocketErrorCode + ").", exception);
        }
        var childDenied = false;
        try
        {
            var start = new ProcessStartInfo(executable, "--version") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Probe child did not start.");
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 5 or 1816) { childDenied = true; }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Child-process isolation probe failed: Win32 " + exception.NativeErrorCode + ".", exception);
        }
        return Wire.Record(("fileDenied", WireValue.Flag(readDenied && writeDenied)),
            ("networkDenied", WireValue.Flag(networkDenied)), ("networkTimedOut", WireValue.Flag(networkTimedOut)),
            ("networkUnavailable", WireValue.Flag(networkUnavailable)), ("childDenied", WireValue.Flag(childDenied)));
    }

    private static WireValue ProbeMemory()
    {
        // Bypasses CLR accounting deliberately: the parent must prove the actual job ceiling.
        var allocations = new IntPtr[512];
        var count = 0;
        var denied = false;
        try
        {
            for (; count < allocations.Length; count++)
            {
                var pointer = VirtualAlloc(IntPtr.Zero, (UIntPtr)ChunkBytes, 0x3000, 0x04);
                if (pointer == IntPtr.Zero) { denied = true; break; }
                allocations[count] = pointer;
                Marshal.WriteByte(pointer, 0x5a);
            }
            return Wire.Record(("memoryDenied", WireValue.Flag(denied)), ("allocatedBytes", WireValue.Numeric((long)count * ChunkBytes)));
        }
        finally
        {
            for (var i = 0; i < count; i++)
                if (!VirtualFree(allocations[i], UIntPtr.Zero, 0x8000)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);
}
