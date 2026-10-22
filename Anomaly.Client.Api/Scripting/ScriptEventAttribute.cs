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
}
