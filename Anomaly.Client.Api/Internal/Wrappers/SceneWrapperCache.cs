using System.Collections.ObjectModel;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Internal.Wrappers;

/// <summary>Identity cache for scene objects, with explicit scene-reset and destroyed-object eviction.</summary>
internal sealed class SceneWrapperCache<TBase, TWrapper> where TBase : UnityEngine.Object where TWrapper : class
{
    private readonly Dictionary<TBase, TWrapper> _wrappers = new();
    private readonly Func<TBase, TWrapper> _create;

    internal SceneWrapperCache(Func<TBase, TWrapper> create) => _create = create;

    internal TWrapper Get(TBase value)
    {
        if (value == null)
            return null;
        if (!_wrappers.TryGetValue(value, out TWrapper wrapper))
        {
            ScriptHostAccess.ChargeRegistryEntries(_wrappers.Count);
            ScriptHostAccess.ChargeWrapperCreation();
            _wrappers[value] = wrapper = _create(value);
        }
        return wrapper;
    }

    internal ReadOnlyCollection<TWrapper> Snapshot(IEnumerable<TBase> values)
    {
        RemoveDestroyed();
        List<TWrapper> result = new();
        foreach (TBase value in values)
        {
            TWrapper wrapper = Get(value);
            if (wrapper != null)
                result.Add(wrapper);
        }
        return result.AsReadOnly();
    }

    internal bool Remove(TBase value, out TWrapper wrapper)
    {
        wrapper = null;
        return !ReferenceEquals(value, null) && _wrappers.Remove(value, out wrapper);
    }

    internal void RemoveDestroyed()
    {
        foreach (TBase value in _wrappers.Keys.Where(value => value == null).ToArray())
            _wrappers.Remove(value);
    }

    internal void Clear() => _wrappers.Clear();
}
