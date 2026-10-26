using System.Text;

namespace Anomaly.ScriptSandboxPrototype;

/// <summary>
/// In-memory stand-in for a trusted Unity command sink. No Unity, filesystem,
/// networking, arbitrary property assignment, CLR objects, or callbacks escape.
/// A future client adapter must validate/apply successful batches on its game thread.
/// </summary>
internal sealed class SandboxedObjectApi
{
    private static readonly HashSet<string> ApprovedAssets = new(StringComparer.Ordinal)
    {
        "demo/cube", "demo/panel"
    };

    private readonly SandboxLimits _limits;
    private readonly string _owner = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, ObjectSnapshot> _objects = new(StringComparer.Ordinal);
    private bool _revoked;
    private int _nextId;

    public SandboxedObjectApi(SandboxLimits limits) => _limits = limits;

    public int Calls { get; private set; }
    public int Violations { get; private set; }
    public int ObjectCount => _objects.Count;
    public IReadOnlyList<ObjectSnapshot> Snapshot => _objects.Values.ToArray();

    public string Spawn(string asset)
    {
        Enter();
        if (asset == null || !ApprovedAssets.Contains(asset))
            throw Reject("asset is not in the approved catalog");
        if (_objects.Count >= _limits.MaxObjects)
            throw Reject("object budget exceeded");

        var handle = _owner + ":" + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _objects.Add(handle, new ObjectSnapshot(handle, asset, 0, 0, 0, ""));
        return handle;
    }

    public void SetPosition(string handle, double x, double y, double z)
    {
        Enter();
        var value = Resolve(handle);
        if (!FiniteBounded(x) || !FiniteBounded(y) || !FiniteBounded(z))
            throw Reject("position must contain finite coordinates within the world bounds");
        _objects[handle] = value with { X = x, Y = y, Z = z };
    }

    public void SetLabel(string handle, string text)
    {
        Enter();
        var value = Resolve(handle);
        if (text == null || text.Length > _limits.MaxLabelLength || Encoding.UTF8.GetByteCount(text) > _limits.MaxLabelLength)
            throw Reject("label exceeds its UTF-8 byte budget");
        _objects[handle] = value with { Label = text };
    }

    public void Destroy(string handle)
    {
        Enter();
        Resolve(handle);
        _objects.Remove(handle);
    }

    public void Revoke()
    {
        _revoked = true;
        _objects.Clear();
    }

    private void Enter()
    {
        if (_revoked)
            throw Reject("object scope has been revoked");
        if (++Calls > _limits.MaxCommands)
            throw Reject("host-call budget exceeded");
    }

    private ObjectSnapshot Resolve(string handle)
    {
        if (handle == null || !_objects.TryGetValue(handle, out var value))
            throw Reject("unknown, foreign, or stale object handle");
        return value;
    }

    private bool FiniteBounded(double value) => double.IsFinite(value) && Math.Abs(value) <= _limits.MaxCoordinate;

    private InvalidOperationException Reject(string message)
    {
        Violations++;
        return new InvalidOperationException(message);
    }
}

internal sealed record ObjectSnapshot(string Handle, string Asset, double X, double Y, double Z, string Label);
