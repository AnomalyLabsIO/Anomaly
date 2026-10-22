using System;

namespace Anomaly.Client.Api.Scripting;

/// <summary>Opts one declared method or property into an approved script type's binding.</summary>
/// <remarks>Properties expose only their public getter unless <see cref="AllowWrite"/> is explicitly enabled.</remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScriptVisibleAttribute : Attribute
{
    public ScriptVisibleAttribute(string name = null) => Name = name;

    /// <summary>An optional alias. Without an alias the exact C# member name is used.</summary>
    public string Name { get; }

    /// <summary>Allows an otherwise approved property's public setter. Defaults to false.</summary>
    public bool AllowWrite { get; set; }
}
