using System.Runtime.InteropServices;

namespace Anomaly.Client.Diagnostics;

internal static class NativeMethods
{
    // SEH filter
    internal delegate int UnhandledExceptionFilterDelegate(IntPtr exceptionPointers);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr SetUnhandledExceptionFilter(UnhandledExceptionFilterDelegate filter);

    // Minidump
    [DllImport("dbghelp.dll", SetLastError = true)]
    internal static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        IntPtr hFile,
        uint dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // Constants
    internal const uint MiniDumpNormal = 0x00000000;
    internal const uint MiniDumpWithThreadInfo = 0x00001000;
    internal const uint MiniDumpWithFullMemory = 0x00000002;

    internal const int EXCEPTION_EXECUTE_HANDLER = 1;

    // File creation constants
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint CREATE_ALWAYS = 2;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    internal static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    /// <summary>
    /// Creates a minidump file at the specified path.
    /// Safe to call from any thread. Uses MiniDumpNormal by default (compact, just stacks).
    /// </summary>
    internal static bool WriteMiniDump(string dumpPath, uint dumpType = MiniDumpNormal, IntPtr exceptionPointers = default)
    {
        if (!DiagnosticPrivacyPolicy.MiniDumpsEnabled)
            return false;

        IntPtr fileHandle = CreateFileW(
            dumpPath,
            GENERIC_WRITE,
            0, // no sharing
            IntPtr.Zero,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (fileHandle == INVALID_HANDLE_VALUE)
            return false;

        try
        {
            return MiniDumpWriteDump(
                GetCurrentProcess(),
                GetCurrentProcessId(),
                fileHandle,
                dumpType,
                exceptionPointers,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        finally
        {
            CloseHandle(fileHandle);
        }
    }
}
