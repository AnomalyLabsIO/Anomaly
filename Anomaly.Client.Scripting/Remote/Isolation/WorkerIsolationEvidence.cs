using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Anomaly.Client.Scripting.Remote.Isolation;

internal sealed class WorkerIsolationEvidence
{
    private readonly SafeFileHandle _job;
    private long _peak;

    internal WorkerIsolationEvidence(SafeFileHandle job)
    {
        _job = job;
    }

    public bool AppContainer => true;
    public bool LessPrivilegedAppContainer => true;
    public bool LoopbackExempt => false;
    public int NetworkCapabilities => 0;
    public long ProcessMemoryLimitBytes => WorkerSandboxNative.MemoryLimit;
    public long JobMemoryLimitBytes => WorkerSandboxNative.MemoryLimit;
    public int CpuRatePercent => 10;
    public int ActiveProcessLimit => 1;
    public long PeakProcessMemoryBytes
    {
        get
        {
            if (_job.IsClosed) return Interlocked.Read(ref _peak);
            try
            {
                if (!WorkerSandboxNative.QueryInformationJobObject(_job, 9, out WorkerSandboxNative.ExtendedLimitInformation limits,
                        (uint)Marshal.SizeOf<WorkerSandboxNative.ExtendedLimitInformation>(), IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var peak = checked((long)limits.PeakProcessMemoryUsed.ToUInt64());
                long previous;
                do { previous = Interlocked.Read(ref _peak); }
                while (peak > previous && Interlocked.CompareExchange(ref _peak, peak, previous) != previous);
                return Math.Max(peak, previous);
            }
            catch (ObjectDisposedException) { return Interlocked.Read(ref _peak); }
        }
    }
}
