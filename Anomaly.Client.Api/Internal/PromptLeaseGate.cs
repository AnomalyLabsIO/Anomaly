using System;

namespace Anomaly.Client.Api.Internal;

internal readonly struct PromptLease
{
    internal PromptLease(string owner, long generation)
    {
        Owner = owner;
        Generation = generation;
    }

    internal string Owner { get; }
    internal long Generation { get; }
}

/// <summary>Pure owner/generation gate for persistent UI prompt callbacks.</summary>
internal sealed class PromptLeaseGate
{
    private readonly object _sync = new();
    private string _owner;
    private long _generation;

    internal PromptLease Begin(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A prompt owner is required.", nameof(owner));
        lock (_sync)
        {
            AdvanceGeneration();
            _owner = owner;
            return new PromptLease(owner, _generation);
        }
    }

    internal bool IsCurrent(PromptLease lease)
    {
        lock (_sync)
            return IsCurrentLocked(lease);
    }

    internal bool TryConsume(PromptLease lease)
    {
        lock (_sync)
        {
            if (!IsCurrentLocked(lease))
                return false;
            _owner = null;
            AdvanceGeneration();
            return true;
        }
    }

    internal bool Invalidate(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return false;
        lock (_sync)
        {
            if (!string.Equals(_owner, owner, StringComparison.Ordinal))
                return false;
            _owner = null;
            AdvanceGeneration();
            return true;
        }
    }

    internal void InvalidateAll()
    {
        lock (_sync)
        {
            _owner = null;
            AdvanceGeneration();
        }
    }

    private bool IsCurrentLocked(PromptLease lease) =>
        lease.Generation == _generation &&
        string.Equals(lease.Owner, _owner, StringComparison.Ordinal);

    private void AdvanceGeneration()
    {
        if (_generation == long.MaxValue)
            throw new InvalidOperationException("Prompt lease generation is exhausted.");
        _generation++;
    }
}
