using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Anomaly.Client.Scripting.Remote.Isolation;

[SupportedOSPlatform("windows")]
internal static class WorkerSandboxTokenVerifier
{
    // Class 46 is not queryable through the user-mode token APIs on every Windows
    // version that supports LPAC creation. Check the actual child's access semantics
    // instead, before its initial thread runs; never infer LPAC from requested flags.
    internal static void VerifyLeastPrivilege(SafeFileHandle token, IntPtr packageSid)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Worker token verification requires Windows.");
        if (!WorkerSandboxNative.DuplicateTokenEx(token, 0x8, IntPtr.Zero, 2, 2, out var impersonation))
            throw Failure("DuplicateTokenEx for suspended worker access verification");
        using (impersonation)
        {
            var userInformation = Marshal.AllocHGlobal(4096);
            try
            {
                if (!WorkerSandboxNative.GetTokenInformation(token, 1, userInformation, 4096, out _))
                    throw Failure("GetTokenInformation(TokenUser) for suspended worker access verification");
                var user = new SecurityIdentifier(Marshal.ReadIntPtr(userInformation)).Value;
                // All descriptors grant the ordinary user access; only package
                // authority differs. Own-package and restricted-package controls
                // prevent a blanket denial or broken duplication from passing.
                CheckAccess(impersonation, user, new SecurityIdentifier(packageSid).Value, true);
                CheckAccess(impersonation, user, "S-1-15-2-1", false);
                CheckAccess(impersonation, user, "S-1-15-2-2", true);
            }
            finally { Marshal.FreeHGlobal(userInformation); }
        }
    }

    private static void CheckAccess(SafeFileHandle token, string user, string package, bool expected)
    {
        var sddl = "O:" + user + "G:" + user + "D:P(A;;0x1;;;" + user + ")(A;;0x1;;;" + package + ")";
        if (!WorkerSandboxNative.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw Failure("Creating worker package-authority access descriptor");
        var privileges = IntPtr.Zero;
        try
        {
            privileges = Marshal.AllocHGlobal(1024);
            uint privilegeLength = 1024;
            var mapping = new WorkerSandboxNative.GenericMapping { Read = 1, Write = 2, Execute = 4, All = 7 };
            if (!WorkerSandboxNative.AccessCheck(descriptor, token, 1, ref mapping, privileges, ref privilegeLength,
                    out var granted, out var allowed))
                throw Failure("AccessCheck for worker package authority " + package);
            if (allowed != expected || (allowed && (granted & 1) != 1))
                throw new InvalidOperationException("The suspended worker did not prove least-privileged application-package access semantics (" + package + ").");
        }
        finally
        {
            if (privileges != IntPtr.Zero) Marshal.FreeHGlobal(privileges);
            WorkerSandboxNative.LocalFree(descriptor);
        }
    }

    private static Win32Exception Failure(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, operation + " failed (Win32 " + error + ").");
    }
}
