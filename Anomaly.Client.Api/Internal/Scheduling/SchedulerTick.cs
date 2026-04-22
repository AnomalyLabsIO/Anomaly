using Anomaly.Client.Api.Scheduling;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Scheduling;

/// <summary>
/// Internal driver that advances <see cref="Scheduler"/> once per frame.
/// Called from <c>ClientRuntime.Update</c>. Uses <see cref="Time.unscaledDeltaTime"/>
/// so the scheduler is unaffected by in-game pause or time scaling.
/// </summary>
internal static class SchedulerTick
{
    internal static void Update()
    {
        Scheduler.Tick(Time.unscaledDeltaTime);
    }
}
