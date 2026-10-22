using System.Reflection;
using System.Text;
using MelonLoader;

namespace Anomaly.Bootstrap;

internal sealed class AdmissionInventoryEntry
{
    internal string Id { get; }
    internal string Version { get; }

    internal AdmissionInventoryEntry(string id, string version)
    {
        Id = id;
        Version = version;
    }
}

internal sealed class AdmissionInventorySnapshot
{
    internal IReadOnlyList<AdmissionInventoryEntry> Entries { get; }
    internal long Generation { get; }

    internal AdmissionInventorySnapshot(IEnumerable<AdmissionInventoryEntry> entries, long generation)
    {
        Entries = Array.AsReadOnly(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
        Generation = generation;
    }
}

// Deliberately independent of MelonLoader so lifetime/collision behavior is testable
// without running the loader or a game's static constructors.
internal sealed class AdmissionInventoryStore
{
    private readonly Dictionary<object, AdmissionInventoryEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, object> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, AdmissionInventoryEntry> _runtimeIdentities = new(ReferenceEqualityComparer.Instance);
    private Dictionary<object, AdmissionRegistryObservation> _divergentRegistry = new(ReferenceEqualityComparer.Instance);
    private long _generation;
    private string? _failure;

    internal long Generation => _generation;
    internal bool IsValid => _failure == null;
    internal bool TryGetEntry(object origin, out AdmissionInventoryEntry entry) => _entries.TryGetValue(origin, out entry!);

    internal bool ObserveRuntimeIdentity(object origin, string id, string version)
    {
        if (!_entries.TryGetValue(origin, out var original))
        {
            Observe(origin, id, version);
            original = _entries[origin];
        }
        var previous = _runtimeIdentities.TryGetValue(origin, out var last) ? last : original;
        if (previous.Id != id || previous.Version != version)
            _generation++;
        _runtimeIdentities[origin] = new AdmissionInventoryEntry(id, version);
        return original.Id == id && original.Version == version;
    }

    internal void ObserveDivergentRegistry(IReadOnlyDictionary<object, AdmissionRegistryObservation> current)
    {
        if (_divergentRegistry.Count == current.Count && current.All(pair =>
                _divergentRegistry.TryGetValue(pair.Key, out var previous) && previous == pair.Value))
            return;
        _divergentRegistry = new Dictionary<object, AdmissionRegistryObservation>(current, ReferenceEqualityComparer.Instance);
        _generation++;
    }

    internal void Observe(object origin, string id, string version)
    {
        try
        {
            ValidateText(id, 128);
            ValidateText(version, 48);
        }
        catch (Exception)
        {
            Fail("Loaded client mod metadata cannot be represented in the admission inventory.");
        }
        if (_entries.TryGetValue(origin, out var previous))
        {
            if (!StringComparer.Ordinal.Equals(previous.Id, id) || !StringComparer.Ordinal.Equals(previous.Version, version))
                Fail("Previously observed client mod metadata changed.");
            return;
        }
        if (_owners.ContainsKey(id))
            Fail("More than one loaded client component claims the same mod ID.");
        _owners.Add(id, origin);
        _entries.Add(origin, new AdmissionInventoryEntry(id, version));
        _generation++;
    }

    internal void CancelUnloadedReservation(object origin)
    {
        if (!_entries.Remove(origin, out var entry))
            return;
        _owners.Remove(entry.Id);
        _runtimeIdentities.Remove(origin);
        _generation++;
    }

    internal void Invalidate(string reason)
    {
        if (_failure == null)
        {
            _failure = reason;
            _generation++;
        }
    }

    internal AdmissionInventorySnapshot Capture()
    {
        if (_failure != null)
            throw new InvalidOperationException(_failure);
        return new AdmissionInventorySnapshot(_entries.Values, _generation);
    }

    private void Fail(string reason)
    {
        Invalidate(reason);
        throw new InvalidOperationException(reason);
    }

    private static void ValidateText(string text, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl) || new UTF8Encoding(false, true).GetByteCount(text) > maxBytes)
            throw new InvalidOperationException("Loaded client mod metadata is invalid or exceeds its wire bound.");
    }
}

internal readonly record struct AdmissionRegistryObservation(bool Present, bool Registered, string Id, string Version);

internal sealed class AdmissionOriginIndex
{
    private readonly Dictionary<Type, object> _declared = new();
    private readonly Dictionary<object, object> _fallback = new(ReferenceEqualityComparer.Instance);

    internal bool Declare(Type type, object origin) => _declared.TryAdd(type, origin);

    internal object Resolve(Type type, object instance, out bool declared)
    {
        if (_declared.TryGetValue(type, out var origin))
        {
            declared = true;
            return origin;
        }
        declared = false;
        if (!_fallback.TryGetValue(instance, out origin))
            _fallback.Add(instance, origin = new object());
        return origin;
    }
}

// Engine-inert cursor: a partial or superseded registry view never replaces the
// last complete divergence view. Lifetime identities remain in the store.
internal sealed class AdmissionRegistryReconciliation
{
    private readonly Dictionary<object, AdmissionInventoryEntry> _registered;
    private readonly HashSet<object> _live;
    private readonly object[] _items;
    private readonly Dictionary<object, AdmissionRegistryObservation> _divergent = new(ReferenceEqualityComparer.Instance);
    private int _index;
    private int _observed;
    private object? _pendingItem;

    internal AdmissionRegistryReconciliation(long revision,
        IEnumerable<KeyValuePair<object, AdmissionInventoryEntry>> registered, IEnumerable<object> live)
    {
        Revision = revision;
        _registered = new Dictionary<object, AdmissionInventoryEntry>(registered, ReferenceEqualityComparer.Instance);
        _live = new HashSet<object>(live, ReferenceEqualityComparer.Instance);
        var items = new HashSet<object>(_registered.Keys, ReferenceEqualityComparer.Instance);
        items.UnionWith(_live);
        _items = items.ToArray();
    }

    internal long Revision { get; }
    internal bool IsComplete => _observed == _items.Length;
    internal int ItemCount => _items.Length;

    internal bool TryTake(out object item)
    {
        if (_pendingItem != null)
            throw new InvalidOperationException("The previous registry item has not been observed.");
        if (_index == _items.Length) { item = null!; return false; }
        item = _pendingItem = _items[_index++];
        return true;
    }

    internal bool Observe(object item, bool registered, AdmissionInventoryEntry current)
    {
        if (!ReferenceEquals(item, _pendingItem))
            throw new InvalidOperationException("The registry observation does not match the current cursor item.");
        bool present = _live.Contains(item);
        bool known = _registered.TryGetValue(item, out var original);
        bool divergent = known
            ? !present || !registered || current.Id != original!.Id || current.Version != original.Version
            : present;
        if (divergent)
            _divergent[item] = new AdmissionRegistryObservation(present, registered, current.Id, current.Version);
        _pendingItem = null;
        _observed++;
        return divergent;
    }

    internal bool TryPublish(long currentRevision, AdmissionInventoryStore store)
    {
        if (!IsComplete || Revision != currentRevision) return false;
        store.ObserveDivergentRegistry(_divergent);
        return true;
    }
}

// Access is serialized by the ledger. Reading a completed admission view needs
// only latched state; it does not invoke loader metadata or rebuild the inventory.
internal sealed class AdmissionInventoryPublication
{
    private AdmissionInventorySnapshot? _snapshot;
    private long _registryRevision = -1;

    internal void Publish(AdmissionInventorySnapshot snapshot, long registryRevision)
    { _snapshot = snapshot; _registryRevision = registryRevision; }
    internal void Clear() { _snapshot = null; _registryRevision = -1; }
    internal bool IsCurrent(long generation, long registryRevision) => _snapshot != null &&
        _snapshot.Generation == generation && _registryRevision == registryRevision;

    internal AdmissionInventorySnapshot Capture(AdmissionInventoryStore store, long registryRevision,
        bool reconciliationInProgress, int pendingReservations)
    {
        if (pendingReservations != 0)
            throw new InvalidOperationException("A managed module load is still in progress.");
        if (!store.IsValid || reconciliationInProgress || !IsCurrent(store.Generation, registryRevision))
            throw new InvalidOperationException("A complete current main-thread inventory reconciliation is required before admission.");
        return _snapshot!;
    }
}

internal static class AdmissionInventoryLedger
{
    private static readonly object Sync = new();
    private static readonly AdmissionInventoryStore Store = new();
    private static readonly HashSet<Assembly> Assemblies = new(ReferenceEqualityComparer.Instance);
    private static readonly AdmissionOriginIndex MelonOrigins = new();
    private static readonly Dictionary<Type, MelonBase> DeclaredInstances = new();
    private static readonly Dictionary<MelonBase, AdmissionInventoryEntry> Registered = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, ModuleReservation> Reservations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AdmissionInventoryPublication Publication = new();
    private static bool _installed;
    private static int _pendingReservations;
    private static long _registryRevision;
    private static long _nextHeartbeat;
    private static AdmissionRegistryReconciliation? _heartbeat;

    internal static bool HasPendingHeartbeat
    {
        get { lock (Sync) return _installed && (_heartbeat != null || Environment.TickCount64 >= _nextHeartbeat); }
    }

    internal static void Checkpoint() => ReconcileCheckpoint();

    internal static void Install()
    {
        lock (Sync)
        {
            if (_installed)
                return;
            _installed = true;
            // AssemblyLoad fires before the loader can construct a Melon from it.
            // Raw custom attribute data does not construct user-defined attributes.
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            MelonBase.OnMelonInitializing.Subscribe(ObserveMelon, int.MinValue);
            MelonBase.OnMelonRegistered.Subscribe(OnRegistered, int.MinValue);
            MelonBase.OnMelonUnregistered.Subscribe(OnUnregistered, int.MinValue);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                ObserveAssembly(assembly);
            foreach (var melon in MelonBase.RegisteredMelons)
                OnRegistered(melon);
        }
        ReconcileCheckpoint();
    }

    internal static ModuleReservation ReserveModule(string id, string version, string path)
    {
        lock (Sync)
        {
            EnsureInstalled();
            string fullPath = Path.GetFullPath(path);
            if (Reservations.ContainsKey(fullPath))
                throw new InvalidOperationException("A managed module already has a lifetime reservation for this path.");
            var reservation = new ModuleReservation(fullPath);
            Store.Observe(reservation, id, version);
            Reservations.Add(fullPath, reservation);
            _pendingReservations++;
            return reservation;
        }
    }

    internal static AdmissionInventorySnapshot CaptureAdmission()
    {
        if (ProtectedMethodCatalog.IsMainThread)
            ReconcileCheckpoint();
        lock (Sync)
        {
            EnsureInstalled();
            return Publication.Capture(Store, _registryRevision, _heartbeat != null, _pendingReservations);
        }
    }

    internal static bool VerifyGeneration(long generation)
    {
        lock (Sync)
        {
            // Called on transport worker threads. Reconciliation belongs to the
            // main-thread freeze/heartbeat; packet sends only check latched state.
            return _installed && Store.IsValid && Store.Generation == generation && _pendingReservations == 0;
        }
    }

    private static void EnsureInstalled()
    {
        if (!_installed)
            throw new InvalidOperationException("The Bootstrap lifetime inventory is not installed.");
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        lock (Sync)
            ObserveAssembly(args.LoadedAssembly, observedDuringLoad: true);
    }

    private static void ObserveAssembly(Assembly assembly, bool observedDuringLoad = false)
    {
        if (!Assemblies.Add(assembly) || assembly.IsDynamic)
            return;
        try
        {
            if (!string.IsNullOrEmpty(assembly.Location) && Reservations.TryGetValue(Path.GetFullPath(assembly.Location), out var reservation))
                reservation.Bind(assembly);

            if (!assembly.GetReferencedAssemblies().Any(reference => reference.Name == "MelonLoader"))
                return;
            foreach (var info in AdmissionMetadataReader.Read(assembly))
            {
                if (!observedDuringLoad)
                    RuntimeGuard.ReportCoverageGap("A mod assembly was already loaded before its immutable metadata was observed.");
                var origin = new object();
                if (!MelonOrigins.Declare(info.Type, origin))
                {
                    Store.Invalidate("Conflicting original MelonInfo declarations prevent an unambiguous inventory.");
                    throw new InvalidDataException("More than one MelonInfo declaration names the same mod type.");
                }
                Store.Observe(origin, info.Id, info.Version);
            }
        }
        catch (InvalidAdmissionMetadataException exception)
        {
            Store.Invalidate("A loaded MelonInfo declaration cannot identify an unambiguous admission source: " + exception.Message);
            RuntimeGuard.ReportCoverageGap("Invalid loaded MelonInfo declaration: " + exception.Message);
        }
        catch (Exception exception)
        {
            RuntimeGuard.ReportCoverageGap("Could not capture loaded mod metadata: " + exception.GetType().Name);
        }
    }

    private static AdmissionInventoryEntry CurrentEntry(MelonBase melon)
    {
        string id = AdmissionMetadataReader.IsCoreType(melon.GetType()) ? "anomaly.client" : string.IsNullOrWhiteSpace(melon.ID) ? melon.Info?.Name ?? string.Empty : melon.ID;
        return new AdmissionInventoryEntry(id, melon.Info?.Version ?? string.Empty);
    }

    private static void ObserveMelon(MelonBase melon) => ObserveMelon(melon, null);

    private static void ObserveMelon(MelonBase melon, AdmissionInventoryEntry? observedEntry)
    {
        if (melon is not MelonMod)
            return;
        lock (Sync)
        {
            ObserveAssembly(melon.GetType().Assembly);
            object origin = MelonOrigins.Resolve(melon.GetType(), melon, out bool declared);
            if (!declared)
            {
                // A custom loader with no declarative identity cannot silently
                // establish a clean inventory after arbitrary constructor code.
                RuntimeGuard.ReportCoverageGap("A loaded MelonMod has no pre-construction identity.");
            }
            else if (DeclaredInstances.TryGetValue(melon.GetType(), out var previous) && !ReferenceEquals(previous, melon))
            {
                Store.Invalidate("Distinct MelonMod instances claim the same declared mod identity.");
                Divergence("Duplicate runtime instances claim one declared mod identity.");
                return;
            }
            else if (declared)
                DeclaredInstances[melon.GetType()] = melon;
            try
            {
                var entry = observedEntry ?? CurrentEntry(melon);
                if (!Store.ObserveRuntimeIdentity(origin, entry.Id, entry.Version))
                    Divergence("A MelonMod identity differs from its original observed metadata.");
            }
            catch (Exception)
            {
                Divergence("A MelonMod identity differs from its loaded metadata.");
            }
        }
    }

    private static void OnRegistered(MelonBase melon)
    {
        if (melon is not MelonMod)
            return;
        lock (Sync)
        {
            ObserveMelon(melon);
            Registered[melon] = CurrentEntry(melon);
            _registryRevision++;
        }
    }

    private static void OnUnregistered(MelonBase melon)
    {
        if (melon is not MelonMod) return;
        lock (Sync)
        {
            ObserveMelon(melon);
            Registered.Remove(melon);
            _registryRevision++;
            // Code cannot be unloaded from the default context. Keep its claim.
        }
    }

    // Called as one fair scheduler source. A registry snapshot, one mod check,
    // or final publication is one unit; no lock or live enumerator spans frames.
    internal static bool ProcessOneHeartbeatItem()
    {
        if (!ProtectedMethodCatalog.IsMainThread) return false;
        AdmissionRegistryReconciliation? cycle;
        lock (Sync)
        {
            if (!_installed) return false;
            if (!Store.IsValid)
            {
                _heartbeat = null;
                Publication.Clear();
                return false;
            }
            if (_heartbeat != null && _heartbeat.Revision != _registryRevision)
                _heartbeat = null;
            cycle = _heartbeat;
            if (cycle == null && Environment.TickCount64 < _nextHeartbeat) return false;
        }
        if (cycle == null)
        {
            StartHeartbeat();
            return true;
        }

        if (cycle.TryTake(out object item))
        {
            var melon = (MelonBase)item;
            AdmissionInventoryEntry current = CurrentEntry(melon);
            bool registered = melon.Registered;
            lock (Sync)
            {
                if (!ReferenceEquals(_heartbeat, cycle) || cycle.Revision != _registryRevision)
                {
                    _heartbeat = null;
                    return true;
                }
                // Identity changes advance the store generation immediately on
                // discovery; publication of the full registry view waits below.
                ObserveMelon(melon, current);
                if (cycle.Observe(melon, registered, current))
                    Divergence("The loader registry changed outside an observed lifecycle transition.");
            }
            return true;
        }

        lock (Sync)
        {
            if (!ReferenceEquals(_heartbeat, cycle) || !cycle.TryPublish(_registryRevision, Store))
            {
                _heartbeat = null;
                return true;
            }
            Publication.Publish(Store.Capture(), _registryRevision);
            _heartbeat = null;
            _nextHeartbeat = Environment.TickCount64 + 1000;
        }
        return true;
    }

    private static void StartHeartbeat()
    {
        long revision;
        KeyValuePair<object, AdmissionInventoryEntry>[] registered;
        lock (Sync)
        {
            revision = _registryRevision;
            registered = Registered.Select(pair => new KeyValuePair<object, AdmissionInventoryEntry>(pair.Key, pair.Value)).ToArray();
        }
        // The AssemblyLoad subscription already captured every later assembly;
        // repeat assembly-wide reflection is unnecessary during reconciliation.
        object[] live = MelonBase.RegisteredMelons.Where(melon => melon is MelonMod).Cast<object>().ToArray();
        var cycle = new AdmissionRegistryReconciliation(revision, registered, live);
        lock (Sync)
        {
            if (revision == _registryRevision)
                _heartbeat = cycle;
        }
    }

    private static void ReconcileCheckpoint()
    {
        if (!ProtectedMethodCatalog.IsMainThread)
            throw new InvalidOperationException("Inventory reconciliation requires the Bootstrap main thread.");
        long revision;
        lock (Sync)
        {
            EnsureInstalled();
            if (!Store.IsValid)
                throw new InvalidOperationException("Loaded mod metadata prevents a valid admission inventory.");
            // Explicit admission observes every entry anew, even if a periodic
            // cursor checked an earlier entry on a preceding frame.
            _heartbeat = null;
            _nextHeartbeat = 0;
            revision = _registryRevision;
        }
        int restarts = 0;
        while (true)
        {
            ProcessOneHeartbeatItem();
            lock (Sync)
            {
                if (!Store.IsValid)
                    throw new InvalidOperationException("Loaded mod metadata prevents a valid admission inventory.");
                if (_heartbeat == null && Publication.IsCurrent(Store.Generation, _registryRevision))
                    return;
                if (revision != _registryRevision)
                {
                    revision = _registryRevision;
                    if (++restarts > 4)
                        throw new InvalidOperationException("The loader registry kept changing during admission reconciliation.");
                }
            }
        }
    }

    private static void Divergence(string reason)
    {
        RuntimeGuard.ReportInventoryDivergence(reason);
    }

    internal sealed class ModuleReservation : IDisposable
    {
        private readonly string _path;
        private Assembly? _assembly;
        internal bool Finished { get; private set; }

        internal ModuleReservation(string path) => _path = path;

        internal void Bind(Assembly assembly)
        {
            if (_assembly != null && !ReferenceEquals(_assembly, assembly))
            {
                Store.Invalidate("A module reservation cannot identify two different loaded assemblies.");
                Divergence("A managed module reservation matched more than one assembly.");
                return;
            }
            _assembly = assembly;
        }

        internal void Complete(Assembly assembly)
        {
            lock (Sync)
            {
                if (Finished || string.IsNullOrEmpty(assembly.Location) || !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(assembly.Location), _path))
                    throw new InvalidOperationException("The managed module completion does not match its load reservation.");
                Bind(assembly);
                Finished = true;
                _pendingReservations--;
            }
        }

        public void Dispose()
        {
            lock (Sync)
            {
                if (Finished)
                    return;
                Finished = true;
                _pendingReservations--;
                if (_assembly == null)
                {
                    Store.CancelUnloadedReservation(this);
                    Reservations.Remove(_path);
                }
                // Failed construction/initialization still leaves executable code
                // loaded. Its immutable reservation survives for the process life.
            }
        }
    }
}
