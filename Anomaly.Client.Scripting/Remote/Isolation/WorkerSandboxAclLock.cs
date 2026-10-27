using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Anomaly.Client.Scripting.Remote.Isolation;

// Every Anomaly process for this user, including other interactive sessions, serializes whole-DACL read/modify/write
// operations, including recovery. A dead owner abandons the kernel mutex rather than
// leaving a stale lock file that another process might incorrectly steal.
[SupportedOSPlatform("windows")]
internal sealed class WorkerSandboxAclLock : IDisposable
{
    private readonly SafeFileHandle _mutex;
    private bool _acquired;

    internal WorkerSandboxAclLock()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Worker ACL coordination requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Worker ACL coordination requires a user SID.");
        var sddl = "O:" + user.Value + "D:P(A;;GA;;;" + user.Value + ")(A;;GA;;;SY)";
        if (!WorkerSandboxNative.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new WorkerSandboxNative.SecurityAttributes
            {
                Length = Marshal.SizeOf<WorkerSandboxNative.SecurityAttributes>(), SecurityDescriptor = descriptor
            };
            _mutex = WorkerSandboxNative.CreateMutex(ref attributes, false, "Global\\Anomaly.WorkerIsolation.Acl." + user.Value);
            if (_mutex.IsInvalid) { _mutex.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        finally { WorkerSandboxNative.LocalFree(descriptor); }
        try
        {
            var error = WorkerSandboxNative.GetSecurityInfo(_mutex, 6, 1, out var owner, out _, out _, out _, out var security);
            if (error != 0) throw new Win32Exception(checked((int)error));
            try
            {
                if (owner == IntPtr.Zero || !new SecurityIdentifier(owner).Equals(user))
                    throw new InvalidOperationException("The worker ACL mutex belongs to a different user.");
            }
            finally { WorkerSandboxNative.LocalFree(security); }
            var wait = WorkerSandboxNative.WaitForSingleObject(_mutex, 15000);
            // WAIT_ABANDONED grants ownership too; recovery still checks each journal's
            // exclusive lease and actual worker liveness before removing owned state.
            if (wait != 0 && wait != 0x80)
            {
                if (wait == 0x102) throw new TimeoutException("Timed out acquiring worker ACL coordination.");
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            _acquired = true;
        }
        catch { _mutex.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (!_acquired) return;
        _acquired = false;
        try
        {
            if (!WorkerSandboxNative.ReleaseMutex(_mutex)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { _mutex.Dispose(); }
    }
}
