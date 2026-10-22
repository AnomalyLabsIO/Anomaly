using System.Reflection;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Catalog;

namespace Anomaly.Client.Scripting.Events;

/// <summary>One trusted discovery scope and one shared CLR bridge per approved event.</summary>
internal sealed class ScriptEventCatalog : IDisposable
{
    private const int MaxEvents = 256;
    private readonly Dictionary<string, IReadOnlyDictionary<string, ScriptEventSource>> _categories;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    private ScriptEventCatalog(Dictionary<string, IReadOnlyDictionary<string, ScriptEventSource>> categories)
        => _categories = categories;

    internal static ScriptEventCatalog DiscoverApi(ScriptBindingRegistry bindings)
        => Discover(bindings, typeof(ScriptTypeAttribute).Assembly, "Anomaly.Client.Api.Events.Handlers");

    internal static ScriptEventCatalog Discover(ScriptBindingRegistry bindings, Assembly trustedAssembly, string handlerNamespace)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        if (string.IsNullOrWhiteSpace(handlerNamespace)) throw new ArgumentException("An exact event namespace is required.");
        var categories = new Dictionary<string, IReadOnlyDictionary<string, ScriptEventSource>>(StringComparer.Ordinal);
        var count = 0;
        foreach (var type in trustedAssembly.GetTypes().Where(type => type.Assembly == trustedAssembly &&
            type.Namespace == handlerNamespace && type.IsVisible && type.IsClass && type.IsAbstract && type.IsSealed &&
            !type.IsGenericType && !type.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false))
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var events = new Dictionary<string, ScriptEventSource>(StringComparer.Ordinal);
            var category = type.Name.EndsWith("Events", StringComparison.Ordinal) ? type.Name[..^6] : type.Name;
            foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                var annotation = eventInfo.GetCustomAttribute<ScriptEventAttribute>(inherit: false);
                if (annotation == null || eventInfo.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false)) continue;
                if (++count > MaxEvents) throw new InvalidOperationException("The approved event surface exceeds its event limit.");
                ScriptNameRules.ValidateIdentifier(category);
                var name = annotation.Name ?? eventInfo.Name;
                ScriptNameRules.ValidateIdentifier(name);
                var handler = eventInfo.EventHandlerType;
                var invoke = handler?.GetMethod("Invoke");
                var add = eventInfo.AddMethod;
                var remove = eventInfo.RemoveMethod;
                var parameters = invoke?.GetParameters();
                if (handler == null || handler.ContainsGenericParameters || invoke == null || invoke.ReturnType != typeof(void) ||
                    parameters == null || parameters.Length > 1 || parameters.Any(parameter => parameter.IsOut ||
                        parameter.ParameterType.IsByRef || parameter.ParameterType.IsPointer || parameter.IsOptional ||
                        parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false)) ||
                    add == null || remove == null || !add.IsPublic || !remove.IsPublic || !add.IsStatic || !remove.IsStatic)
                    throw new InvalidOperationException("An approved event must have public static accessors and a void zero/one-argument delegate.");
                ScriptEventSnapshotPlan? plan = null;
                if (parameters.Length == 1)
                {
                    // Validation is atomic for this entire annotated value graph.
                    bindings.RegisterType(parameters[0].ParameterType);
                    plan = ScriptEventSnapshotPlan.Create(parameters[0].ParameterType, bindings);
                }
                if (!events.TryAdd(name, new ScriptEventSource(category, name, eventInfo, parameters, plan)))
                    throw new InvalidOperationException("Approved event aliases must be unique within their category.");
            }
            if (events.Count > 0 && !categories.TryAdd(category, events))
                throw new InvalidOperationException("Approved event categories must be unique.");
        }
        return new ScriptEventCatalog(categories);
    }

    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, ScriptEventSource>> Categories => _categories;
    internal bool IsDisposed => _disposed;

    internal IReadOnlyList<ScriptEventCatalogEntry> GetCatalog() => _categories.Values.SelectMany(value => value.Values)
        .OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal)
        .Select(value => new ScriptEventCatalogEntry(value.Category, value.Name, value.EventInfo.DeclaringType!.FullName!,
            value.EventInfo.Name, value.Parameters.Select(parameter => new ScriptParameterCatalog(
                parameter.Name ?? "argument", parameter.ParameterType.FullName ?? parameter.ParameterType.Name))))
        .ToArray();

    internal bool Owns(ScriptEventSource source) => _categories.TryGetValue(source.Category, out var category) &&
        category.TryGetValue(source.Name, out var found) && ReferenceEquals(source, found);

    internal void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Event subscriptions must be accessed on their owner thread.");
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptEventCatalog));
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Event subscriptions must be disposed on their owner thread.");
        if (_disposed) return;
        _disposed = true;
        Exception? failure = null;
        foreach (var source in _categories.Values.SelectMany(value => value.Values))
        {
            try { source.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
        }
        if (failure != null) throw new InvalidOperationException("An event bridge could not be detached.", failure);
    }

}
