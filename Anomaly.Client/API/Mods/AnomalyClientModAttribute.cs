using System;

namespace Anomaly.Client.API.Mods;

/// <summary>
/// Marks a class implementing <see cref="IAnomalyClientMod"/> for auto-discovery
/// by <see cref="AnomalyClientMods.Discover"/>. The marked class must have a
/// parameterless constructor.
/// </summary>
/// <remarks>
/// Auto-discovery is a convenience; mods that need more control (e.g. MelonMods
/// that want to register their existing instance) should call
/// <see cref="AnomalyClientMods.Register"/> directly during startup.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AnomalyClientModAttribute : Attribute
{
}
