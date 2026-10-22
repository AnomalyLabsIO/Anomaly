using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>Read-only observations made after the game's SCP-079 RPCs are processed.</summary>
public static class Scp079Events
{
    /// <summary>
    /// Fires for the first known or changed power value while the SCP-079 is
    /// locally controlled or spectated. This is not a per-frame prediction event.
    /// </summary>
    public static event AnomalyEventHandler<Scp079AuxiliaryPowerChangedEventArgs> AuxiliaryPowerChanged;

    /// <summary>Fires for the first known or changed total experience for a role life.</summary>
    public static event AnomalyEventHandler<Scp079ExperienceChangedEventArgs> ExperienceChanged;

    /// <summary>Fires for the first known or changed one-based tier for a role life.</summary>
    public static event AnomalyEventHandler<Scp079TierChangedEventArgs> TierChanged;

    internal static void OnAuxiliaryPowerChanged(Scp079AuxiliaryPowerChangedEventArgs args) => EventInvoker.Invoke(AuxiliaryPowerChanged, args);
    internal static void OnExperienceChanged(Scp079ExperienceChangedEventArgs args) => EventInvoker.Invoke(ExperienceChanged, args);
    internal static void OnTierChanged(Scp079TierChangedEventArgs args) => EventInvoker.Invoke(TierChanged, args);
}
