using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Scripting.Tests.EventFixtures.Valid
{
    [ScriptType]
    public class Payload
    {
        private string _text = "original";
        public static int GetterCalls;
        [ScriptVisible] public string Text { get { Interlocked.Increment(ref GetterCalls); return _text; } set => _text = value; }
        [ScriptVisible] public string Second { get; set; } = "";
        [ScriptVisible] public string Third { get; set; } = "";
        [ScriptVisible] public string Fourth { get; set; } = "";
        [ScriptVisible] public string Fifth { get; set; } = "";
        [ScriptVisible] public Payload? Next { get; set; }
        [ScriptVisible] public Payload? Branch { get; set; }
    }
    public sealed class DerivedPayload : Payload { }

    public static class SignalsEvents
    {
        private static Action<Payload>? _changed;
        private static Action? _ping;
        public static int Adds;
        public static int Removes;
        public static bool ThrowOnAdd;
        public static bool ThrowOnRemove;
        public static Action? DuringAdd;
        [ScriptEvent]
        public static event Action<Payload> Changed
        {
            add { Adds++; _changed += value; DuringAdd?.Invoke(); if (ThrowOnAdd) throw new InvalidOperationException("add failure"); }
            remove { Removes++; if (ThrowOnRemove) throw new InvalidOperationException("remove failure"); _changed -= value; }
        }
        [ScriptEvent("Empty")]
        public static event Action Ping { add => _ping += value; remove => _ping -= value; }
        public static event Action<Payload>? Hidden;
        [ScriptEvent, ScriptIgnore] public static event Action? Ignored;
        public static void Publish(Payload value) => _changed?.Invoke(value);
        public static void PublishEmpty() => _ping?.Invoke();
        public static void Reset()
        {
            _changed = null; _ping = null; Hidden = null; Ignored = null;
            Adds = Removes = Payload.GetterCalls = 0;
            ThrowOnAdd = ThrowOnRemove = false; DuringAdd = null;
        }
        public static void PublishHidden(Payload value) { Hidden?.Invoke(value); Ignored?.Invoke(); }
    }

    [ScriptIgnore]
    public static class IgnoredEvents
    {
        [ScriptEvent] public static event Action? Hidden;
        public static void Publish() => Hidden?.Invoke();
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.BadSignature
{
    public static class UnsafeEvents
    {
        [ScriptEvent] public static event Action<object, object>? Changed;
        public static void Publish() => Changed?.Invoke(new object(), new object());
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.BadReturn
{
    public static class UnsafeEvents
    {
        [ScriptEvent] public static event Func<int>? Changed;
        public static int Publish() => Changed?.Invoke() ?? 0;
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.BadArgument
{
    public static class UnsafeEvents
    {
        [ScriptEvent] public static event Action<object>? Changed;
        public static void Publish() => Changed?.Invoke(new object());
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.Writable
{
    [ScriptType] public sealed class Payload { [ScriptVisible(AllowWrite = true)] public int Value { get; set; } }
    public static class UnsafeEvents
    {
        [ScriptEvent] public static event Action<Payload>? Changed;
        public static void Publish() => Changed?.Invoke(new Payload());
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.Callable
{
    [ScriptType] public sealed class Payload { [ScriptVisible] public int Read() => 1; }
    public static class UnsafeEvents
    {
        [ScriptEvent] public static event Action<Payload>? Changed;
        public static void Publish() => Changed?.Invoke(new Payload());
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.Keyword
{
    public static class UnsafeEvents
    {
        [ScriptEvent("return")] public static event Action? Changed;
        public static void Publish() => Changed?.Invoke();
    }
}

namespace Anomaly.Client.Scripting.Tests.EventFixtures.Duplicate
{
    public static class UnsafeEvents
    {
        [ScriptEvent("Same")] public static event Action? First;
        [ScriptEvent("Same")] public static event Action? Second;
        public static void Publish() { First?.Invoke(); Second?.Invoke(); }
    }
}
