using System.ComponentModel;
using System.Runtime.InteropServices;
using MelonLoader;

// Compile the same dependency-free dialog into Client so its missing-Bootstrap
// rejection can still warn without resolving the absent plugin assembly.
#if ANOMALY_CLIENT
namespace Anomaly.Client.Internal.Bootstrap;
#else
namespace Anomaly.Bootstrap;
#endif

internal static class BlockingFailureDialog
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbTaskModal = 0x00002000;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTopmost = 0x00040000;
    private const uint GwOwner = 4;

    private static readonly BlockingFailureAction Failure = new(ShowWarning,
        () => Environment.Exit(78),
        exception => MelonLogger.Error("Could not display the Bootstrap warning; closing the game: " + exception));

    internal static bool IsActive => Failure.IsActive;

    // Call only from an early loader/main-thread lifecycle boundary after guard
    // operations unwind. The modal loop blocks frame progression, not all threads.
    internal static void ShowAndExit(string reason) => Failure.Run(reason);

    private static void ShowWarning(string reason)
    {
        // The Windows runtime also reports Windows while running under Wine/Proton;
        // user32 is supplied by the OS/compatibility layer, not a bundled UI library.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Bootstrap warning requires the Windows game runtime.");

        IntPtr owner = FindOwner();
        uint flags = MbOk | MbIconWarning | MbSetForeground | MbTopmost;
        if (owner == IntPtr.Zero) flags |= MbTaskModal;
        string detail = reason.Length > 4096 ? reason.Substring(0, 4096) : reason;
        string message = "Anomaly cannot safely continue.\r\n\r\n" + detail +
            "\r\n\r\nGameplay is paused. Select OK or close this warning to close the game." +
            "\r\n\r\nCheck MelonLoader/Latest.log for details before restarting.";
        if (MessageBoxW(owner, message, "Anomaly - game must close", flags) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "MessageBoxW could not create the Bootstrap warning.");
    }

    private static IntPtr FindOwner()
    {
        // Never use an unchecked foreground HWND: it may belong to another app.
        IntPtr console = GetConsoleWindow();
        IntPtr active = GetActiveWindow();
        if (active != IntPtr.Zero && active != console && IsWindowVisible(active))
            return active;

        IntPtr owner = IntPtr.Zero;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (window == console || !IsWindowVisible(window) || GetWindow(window, GwOwner) != IntPtr.Zero)
                return true;
            owner = window;
            return false;
        }, IntPtr.Zero);
        return owner;
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowCallback callback, IntPtr parameter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();
}

internal sealed class BlockingFailureAction
{
    private readonly Action<string> _showWarning;
    private readonly Action _completeAction;
    private readonly Action<Exception> _reportError;
    private int _active;

    internal BlockingFailureAction(Action<string> showWarning, Action completeAction, Action<Exception> reportError)
    {
        _showWarning = showWarning;
        _completeAction = completeAction;
        _reportError = reportError;
    }

    internal bool IsActive => Volatile.Read(ref _active) != 0;

    internal void Run(string message)
    {
        // MessageBox pumps window messages. Reentrant callbacks and repeated
        // failures must not open another warning or complete the action early.
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) return;
        try
        {
            _showWarning(message);
        }
        catch (Exception exception)
        {
            try { _reportError(exception); }
            catch { /* Logging failure must not prevent the required terminal action. */ }
        }
        finally
        {
            // OK, Escape, the close button, and failure to create a dialog all
            // finish the same action. There is no continue/cancel path.
            _completeAction();
        }
    }
}
