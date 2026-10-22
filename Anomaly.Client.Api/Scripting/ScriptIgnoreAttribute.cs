using System;

namespace Anomaly.Client.Api.Scripting;

/// <summary>Explicitly excludes a type or member, overriding script visibility annotations.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property |
    AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Constructor,
    AllowMultiple = false, Inherited = false)]
public sealed class ScriptIgnoreAttribute : Attribute
{
}
