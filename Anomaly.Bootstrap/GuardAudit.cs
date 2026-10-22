namespace Anomaly.Bootstrap;

[Flags]
internal enum GuardAuditFlags : ushort
{
    None = 0,
    ExternalHarmonyModification = 1,
    ExternalDetourModification = 2,
    CoverageIncomplete = 4,
    InventoryDivergence = 8,
    BlockedAttemptObserved = 16
}

internal sealed class GuardSnapshot
{
    internal GuardSnapshot(ushort flags, long generation, bool healthy, bool allowExternalModifications, int mutationInProgress = 0)
    {
        Flags = flags;
        Generation = generation;
        Healthy = healthy;
        AllowExternalModifications = allowExternalModifications;
        MutationInProgress = mutationInProgress;
    }

    public ushort Flags { get; }
    public long Generation { get; }
    public bool Healthy { get; }
    public bool AllowExternalModifications { get; }
    public int MutationInProgress { get; }
}

// Flags and generations are process-lifetime evidence. Removing a patch or reconnecting
// never clears them, and snapshots cannot expose mutable monitor state to consumers.
internal sealed class GuardAudit
{
    private readonly object _sync = new();
    private readonly bool _allowExternal;
    private GuardAuditFlags _flags;
    private long _generation;
    private bool _healthy;
    private bool _permanentlyFailed;
    private string? _fatalReason;
    private int _mutations;
    private readonly HashSet<string> _observations = new(StringComparer.Ordinal);
    private readonly Queue<string> _diagnostics = new();

    internal GuardAudit(bool allowExternal) => _allowExternal = allowExternal;

    internal GuardSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new GuardSnapshot((ushort)_flags, _generation, _healthy, _allowExternal, _mutations);
        }
    }

    internal string[] Diagnostics { get { lock (_sync) return _diagnostics.ToArray(); } }
    internal string? FatalReason { get { lock (_sync) return _fatalReason; } }

    internal void BeginMutation()
    {
        lock (_sync) _mutations++;
    }

    internal void EndMutation(GuardAuditFlags flags, string reason)
    {
        lock (_sync)
        {
            // Evidence is committed before the transient barrier opens, under the
            // same lock used to freeze admission snapshots on network threads.
            if (flags != GuardAuditFlags.None)
                Record(flags, reason, occurrence: true);
            if (_mutations <= 0)
                Record(GuardAuditFlags.CoverageIncomplete, "Guard mutation accounting changed.", fatal: true);
            else
                _mutations--;
        }
    }

    internal void Ready()
    {
        lock (_sync)
        {
            _healthy = !_permanentlyFailed;
            _generation++;
        }
    }

    internal void Record(GuardAuditFlags flags, string reason, bool fatal = false, bool occurrence = false)
    {
        reason ??= "Unspecified guard observation";
        if (reason.Length > 2048)
            reason = reason.Substring(0, 2048);
        lock (_sync)
        {
            bool risky = (flags & ~GuardAuditFlags.BlockedAttemptObserved) != 0;
            bool changed = (risky && ((_flags & flags) != flags || occurrence)) || (fatal && _healthy);
            _flags |= flags;
            if (fatal)
            {
                _fatalReason ??= reason;
                _healthy = false;
                _permanentlyFailed = true;
            }
            // Bounded deduplication prevents hostile patch loops growing monitor memory.
            if (_observations.Count < 1024 && _observations.Add(reason))
            {
                if (_diagnostics.Count == 128)
                    _diagnostics.Dequeue();
                _diagnostics.Enqueue(DateTimeOffset.UtcNow.ToString("O") + " " + reason);
                changed |= risky;
            }
            if (changed)
                _generation++;
        }
    }
}
