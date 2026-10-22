using System;

namespace Anomaly.Client.Api.Scripting;

/// <summary>Names a reference type whose explicitly selected members may be bound to a script.</summary>
/// <remarks>This annotation does not register the type or grant a script any capabilities.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScriptTypeAttribute : Attribute
{
    public ScriptTypeAttribute(string name = null)
    {
        if (name != null && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An explicit script type name cannot be empty.", nameof(name));
        Name = name;
    }

    /// <summary>An optional alias. Without an alias the exact C# type name is used.</summary>
    public string Name { get; }
}
