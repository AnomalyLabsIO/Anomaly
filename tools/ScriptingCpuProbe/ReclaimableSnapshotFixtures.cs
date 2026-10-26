using Anomaly.Client.Api.Scripting;

namespace Anomaly.ScriptingCpuProbe.SnapshotFixtures;

// Trusted fixed regression metadata. This namespace is the sole event discovery
// scope used by the worker's nested-snapshot ownership fixture.
[ScriptType]
public sealed class SnapshotPayload
{
    public SnapshotPayload(SnapshotPayload? next, string text) { Next = next; Text = text; }
    [ScriptVisible] public SnapshotPayload? Next { get; }
    [ScriptVisible] public string Text { get; }
}

public static class SnapshotEvents
{
    private static Action<SnapshotPayload>? _published;
    public static int Removes { get; private set; }

    [ScriptEvent]
    public static event Action<SnapshotPayload> Published
    {
        add => _published += value;
        remove { _published -= value; Removes++; }
    }

    public static void Publish(SnapshotPayload value) => _published?.Invoke(value);
    public static void Reset() { _published = null; Removes = 0; }
}
