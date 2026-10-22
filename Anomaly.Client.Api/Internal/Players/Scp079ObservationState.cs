namespace Anomaly.Client.Api.Internal.Players;

/// <summary>Receipts belong to one role life; unknown values are distinct from zero.</summary>
internal sealed class Scp079ObservationState
{
    private readonly int _lifeIdentifier;
    private int _experienceRevision;

    internal Scp079ObservationState(int lifeIdentifier)
    {
        _lifeIdentifier = lifeIdentifier;
    }

    internal bool IsInvalidated { get; private set; }
    internal float? AuxiliaryPower { get; private set; }
    internal int? Experience { get; private set; }
    internal int? Tier { get; private set; }

    internal bool TryObserveAuxiliaryPower(int lifeIdentifier, float value, out float? previous)
    {
        previous = AuxiliaryPower;
        if (IsInvalidated || lifeIdentifier != _lifeIdentifier)
            return false;

        AuxiliaryPower = value;
        return true;
    }

    internal bool TryObserveExperience(int lifeIdentifier, int experience, int tier,
        out int? previousExperience, out int? previousTier)
    {
        previousExperience = Experience;
        previousTier = Tier;
        if (IsInvalidated || lifeIdentifier != _lifeIdentifier)
            return false;

        if (Experience != experience || Tier != tier)
            _experienceRevision++;
        Experience = experience;
        Tier = tier;
        return true;
    }

    /// <summary>Stops the second notification when the first subscriber changes this observation or its role life.</summary>
    internal void DispatchExperienceChanges(Func<bool> isCurrentLife, Action experienceChanged, Action tierChanged)
    {
        int revision = _experienceRevision;
        if (IsInvalidated || !isCurrentLife())
            return;
        experienceChanged?.Invoke();
        if (IsInvalidated || revision != _experienceRevision || !isCurrentLife())
            return;
        tierChanged?.Invoke();
    }

    internal void InvalidateAuxiliaryPower() => AuxiliaryPower = null;

    internal void Invalidate()
    {
        IsInvalidated = true;
        _experienceRevision++;
        AuxiliaryPower = null;
        Experience = null;
        Tier = null;
    }
}
