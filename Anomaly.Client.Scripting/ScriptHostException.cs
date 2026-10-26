using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Anomaly.Client.Scripting;

internal static class ScriptHostException
{
    // Reflection wrapping must not turn a fatal CLR failure into a recoverable
    // Lua error or a successful event-publication result.
    internal static void RethrowFatal(Exception exception)
    {
        while (exception is TargetInvocationException or TypeInitializationException && exception.InnerException != null)
            exception = exception.InnerException;
        if (exception is OutOfMemoryException or StackOverflowException)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
