using System;

namespace Anomaly.Client.Api.Internal;

/// <summary>Orders durable writes before scheduler cancellation during teardown.</summary>
internal static class RuntimeTeardown
{
    internal static void FlushBeforeCancel(Action flush, Action cancelScheduledWork)
    {
        if (flush == null) throw new ArgumentNullException(nameof(flush));
        if (cancelScheduledWork == null) throw new ArgumentNullException(nameof(cancelScheduledWork));

        try
        {
            flush();
        }
        finally
        {
            cancelScheduledWork();
        }
    }
}
