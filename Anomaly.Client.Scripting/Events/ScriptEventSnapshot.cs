using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Anomaly.Client.Scripting.Bindings;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Events;

/// <summary>Copies approved event data before the publisher can mutate or reuse it.</summary>
internal sealed class ScriptEventSnapshotPlan
{
    internal const int MaxDepth = 8;
    internal const int MaxNodes = 64;
    internal const int MaxPayloadBytes = 16 * 1024;
    private readonly Type _type;
    private readonly string? _name;
    private SnapshotMember[] _members = Array.Empty<SnapshotMember>();

    private ScriptEventSnapshotPlan(Type type, string? name)
    {
        _type = type;
        _name = name;
    }

    internal static ScriptEventSnapshotPlan Create(Type type, ScriptBindingRegistry bindings)
        => Build(type, bindings, new Dictionary<Type, ScriptEventSnapshotPlan>(), 0);

    private static ScriptEventSnapshotPlan Build(Type type, ScriptBindingRegistry bindings,
        Dictionary<Type, ScriptEventSnapshotPlan> plans, int depth)
    {
        if (depth > 16) throw new InvalidOperationException("Event argument metadata exceeds its depth limit.");
        if (plans.TryGetValue(type, out var existing)) return existing;
        if (IsScalar(type)) return new ScriptEventSnapshotPlan(type, null);
        var policy = bindings.GetTypePolicy(type);
        var plan = new ScriptEventSnapshotPlan(type, policy.Name);
        plans.Add(type, plan);
        var members = new List<SnapshotMember>();
        foreach (var member in policy.Members.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            if (member.Property == null || member.AllowWrite)
                throw new InvalidOperationException("Queued event arguments support approved read-only properties only.");
            members.Add(new SnapshotMember(member.Name, member.Property,
                Build(member.ReturnType, bindings, plans, depth + 1)));
        }
        plan._members = members.ToArray();
        return plan;
    }

    internal object? Capture(object? value)
        => Capture(value, new CaptureBudget(), new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance), 0);

    private object? Capture(object? value, CaptureBudget budget, HashSet<object> path, int depth)
    {
        budget.AddNode();
        if (depth > MaxDepth) throw new InvalidOperationException("Event argument snapshot exceeds its depth limit.");
        if (value == null)
        {
            if (_type.IsValueType) throw new InvalidOperationException("Event argument type does not match its declaration.");
            return null;
        }
        if (value.GetType() != _type)
            throw new InvalidOperationException("Polymorphic event arguments are not approved.");
        if (_type == typeof(string))
        {
            var text = (string)value;
            if (text.Length > ScriptBindingRegistry.MaxStringBytes)
                throw new InvalidOperationException("Event strings exceed their byte limit.");
            var bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes > ScriptBindingRegistry.MaxStringBytes)
                throw new InvalidOperationException("Event strings exceed their byte limit.");
            budget.AddBytes(bytes);
            return text;
        }
        if (_type == typeof(bool)) return value;
        if (IsNumeric(_type))
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number) || (_type == typeof(float) && !float.IsFinite((float)number)) ||
                (_type != typeof(double) && _type != typeof(float) && Math.Abs(number) > 9007199254740991d))
                throw new InvalidOperationException("Event numbers must be finite and exactly representable.");
            return number;
        }
        if (!path.Add(value)) throw new InvalidOperationException("Cyclic event argument values are not supported.");
        try
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var member in _members)
                fields.Add(member.Name, member.Plan.Capture(member.Property.GetValue(value), budget, path, depth + 1));
            return new ScriptFrozenEventValue(_name!, new ReadOnlyDictionary<string, object?>(fields));
        }
        finally { path.Remove(value); }
    }

    private static bool IsScalar(Type type) => type == typeof(string) || type == typeof(bool) || IsNumeric(type);
    private static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
        type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
        type == typeof(ulong) || type == typeof(float) || type == typeof(double);

    private sealed record SnapshotMember(string Name, PropertyInfo Property, ScriptEventSnapshotPlan Plan);
    private sealed class CaptureBudget
    {
        private int _nodes;
        private int _bytes;
        internal void AddNode()
        {
            if (++_nodes > MaxNodes) throw new InvalidOperationException("Event argument snapshot exceeds its node limit.");
            AddBytes(32);
        }
        internal void AddBytes(int count)
        {
            if (count > MaxPayloadBytes - _bytes)
                throw new InvalidOperationException("Event argument snapshot exceeds its payload limit.");
            _bytes += count;
        }
    }
}

internal sealed record ScriptFrozenEventValue(string Name, IReadOnlyDictionary<string, object?> Fields)
{
    internal static DynValue ToScriptValue(Script owner, object? value)
    {
        owner.ExecutionBudget?.Check();
        return value switch
        {
            null => DynValue.Nil,
            string text => DynValue.NewString(text),
            bool boolean => DynValue.NewBoolean(boolean),
            double number => DynValue.NewNumber(number),
            ScriptFrozenEventValue frozen => UserData.Create(frozen, new Descriptor(owner, frozen)),
            _ => throw new ScriptRuntimeException("Invalid frozen event argument.")
        };
    }

    private sealed class Descriptor : IUserDataDescriptor
    {
        private readonly Script _owner;
        private readonly ScriptFrozenEventValue _value;
        internal Descriptor(Script owner, ScriptFrozenEventValue value) { _owner = owner; _value = value; }
        public string Name => _value.Name;
        public Type Type => typeof(ScriptFrozenEventValue);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            return index.Type == DataType.String && _value.Fields.TryGetValue(index.String, out var value)
                ? ToScriptValue(script, value) : DynValue.Nil;
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        {
            Check(script, obj);
            throw new ScriptRuntimeException("Event argument snapshots are read-only.");
        }
        public string AsString(object obj) { Check(_owner, obj); return "[event " + Name + "]"; }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        private void Check(Script script, object obj)
        {
            script.ExecutionBudget?.Check();
            if (!ReferenceEquals(script, _owner) || !ReferenceEquals(obj, _value))
                throw new ScriptRuntimeException("The event snapshot belongs to another script.");
        }
    }
}
