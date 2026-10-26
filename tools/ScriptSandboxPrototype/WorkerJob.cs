using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Anomaly.ScriptSandboxPrototype;

/// <summary>Safety for the experiment, not a filesystem/network sandbox or a VM limit.</summary>
internal sealed class WorkerJob : IDisposable
{
    private IntPtr _handle;

    public WorkerJob(Process process, long memoryLimitBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This prototype's process memory safety uses Windows Job Objects.");
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var information = new ExtendedLimitInformation
            {
                BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x100 | 0x2000 },
                ProcessMemoryLimit = (UIntPtr)(ulong)memoryLimitBytes
            };
            if (!SetInformationJobObject(_handle, 9, ref information, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!AssignProcessToJobObject(_handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
