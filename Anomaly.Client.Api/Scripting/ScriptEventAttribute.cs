using System;

namespace Anomaly.Client.Api.Scripting;

/// <summary>Explicitly approves a host event for bounded, queued script subscriptions.</summary>
/// <remarks>The event's declaring category and exact C# name are used unless an alias is supplied.</remarks>
[AttributeUsage(AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
public sealed class ScriptEventAttribute : Attribute
{
    public ScriptEventAttribute(string name = null) => Name = name;

    /// <summary>An optional alias. Without an alias the exact C# event name is used.</summary>
    public string Name { get; }

    /// <summary>
    /// Retains only the latest pending observation for each subscription generation.
    /// Intermediate samples may be replaced before dispatch; false preserves every
    /// arrival in the bounded reliable queue and retires the script on overflow.
    /// </summary>
    /// <remarks>This is per subscription, not per player or object. Removal invalidates its pending sample.</remarks>
    public bool CoalesceLatest { get; set; }
}
