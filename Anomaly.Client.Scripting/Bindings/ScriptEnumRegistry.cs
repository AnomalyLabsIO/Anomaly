using System.Globalization;
using System.Reflection;
using Anomaly.Client.Scripting.Catalog;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Bindings;

/// <summary>Enum constants from the selected game assembly and explicitly selected protocol enum types.</summary>
internal sealed class ScriptEnumRegistry
{
    private const int MaxEnums = 1024;
    private const int MaxMembers = 32768;
    private const int MaxMembersPerEnum = 2048;
    private static readonly HashSet<string> ReservedGlobals = new(StringComparer.Ordinal)
    {
        "_G", "_VERSION", "assert", "error", "select", "type", "tonumber", "tostring", "ipairs", "pairs", "next",
        "pcall", "xpcall", "math", "table", "string", "Enums", "Events", "Messages", "Game", "Output", "io", "os", "package",
        "debug", "require", "load", "loadfile", "dofile", "coroutine", "collectgarbage", "rawget", "rawset",
        "getmetatable", "setmetatable", "print"
    };
    private readonly object _sync = new();
    private State _state = new(null, new(), new(StringComparer.Ordinal), Array.Empty<ScriptEnumCatalog>());

    internal void Discover(Assembly trustedAssembly, params Type[] additionalEnums)
    {
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        lock (_sync)
        {
            if (_state.Assembly == trustedAssembly) return;
            if (_state.Assembly != null) throw new InvalidOperationException("The enum assembly has already been selected.");
            // Fail the entire batch on incomplete metadata. Never publish a partially loaded catalog.
            if (additionalEnums.Any(type => !type.IsEnum || !type.IsVisible))
                throw new InvalidOperationException("Additional enum contracts must be public enums.");
            var types = trustedAssembly.GetTypes().Where(type => type.Assembly == trustedAssembly &&
                type.IsVisible && type.IsEnum && !type.ContainsGenericParameters)
                .Concat(additionalEnums).Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
            if (types.Length > MaxEnums) throw new InvalidOperationException("The game enum catalog exceeds its type limit.");
            var shortCounts = types.GroupBy(type => type.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var policies = new Dictionary<Type, Policy>();
            var nodes = new Dictionary<string, Policy>(StringComparer.Ordinal);
            var catalogs = new List<ScriptEnumCatalog>();
            var count = 0;
            foreach (var type in types)
            {
                var canonical = type.FullName!;
                if (canonical.Length > ScriptBindingRegistry.MaxStringBytes)
                    throw new InvalidOperationException("An enum identity exceeds its length limit.");
                var alias = shortCounts[type.Name] == 1 && ScriptNameRules.IsValidIdentifier(type.Name) ? type.Name : null;
                var globalName = type.IsPublic && alias != null && !ReservedGlobals.Contains(alias) ? alias : null;
                var policy = new Policy(type, alias, globalName);
                count += policy.Catalog.Members.Count;
                if (count > MaxMembers) throw new InvalidOperationException("The game enum catalog exceeds its member limit.");
                policies.Add(type, policy);
                nodes.Add(canonical, policy);
                catalogs.Add(policy.Catalog);
            }
            // Canonical CLR identities always win, even if another type has a matching short name.
            foreach (var policy in policies.Values)
                if (policy.Catalog.Alias is { } alias && !nodes.TryAdd(alias, policy) && nodes[alias] != policy)
                    throw new InvalidOperationException("An enum alias conflicts with a canonical identity.");
            _state = new State(trustedAssembly, policies, nodes, catalogs.AsReadOnly());
        }
    }

    internal bool Contains(Type type) => _state.Policies.ContainsKey(type);
    internal IReadOnlyList<ScriptEnumCatalog> GetCatalog() => _state.Catalogs;
    internal bool IsGlobalName(string name) => name == "Enums" || _state.Catalogs.Any(value => value.GlobalName == name);

    internal object Parse(Type type, string text) => GetPolicy(type).Parse(text);
    internal string Format(Type type, object value) => GetPolicy(type).Format(value);

    private Policy GetPolicy(Type type) => _state.Policies.TryGetValue(type, out var policy) ? policy :
        throw new ScriptRuntimeException("The enum type has not been approved by this runtime.");

    internal void InstallGlobals(Script owner)
    {
        using var memoryScope = owner.MemoryBudget?.EnterScope();
        if (_state.Policies.Count == 0) return;
        if (!owner.Globals.Get("Enums").IsNil() || _state.Catalogs.Any(value =>
            value.GlobalName != null && !owner.Globals.Get(value.GlobalName).IsNil()))
            throw new InvalidOperationException("An enum global is already installed.");
        owner.Globals.Set("Enums", CreateNode(owner, null));
        foreach (var policy in _state.Policies.Values)
            if (policy.Catalog.GlobalName is { } name) owner.Globals.Set(name, CreateNode(owner, policy));
    }

    private DynValue CreateNode(Script owner, Policy? policy)
    {
        var node = MemoryBudget.Allocate(384, () => new Descriptor(this, owner, policy));
        return UserData.Create(node, node);
    }

    private sealed record State(Assembly? Assembly, Dictionary<Type, Policy> Policies,
        Dictionary<string, Policy> Nodes, IReadOnlyList<ScriptEnumCatalog> Catalogs);

    private sealed class Policy
    {
        private readonly Type _type;
        private readonly Dictionary<string, Constant> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, Constant> _values = new();
        private readonly Constant[] _flags;
        internal ScriptEnumCatalog Catalog { get; }
        internal IReadOnlyDictionary<string, string> Members { get; }

        internal Policy(Type type, string? alias, string? globalName)
        {
            _type = type;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            if (fields.Length > MaxMembersPerEnum)
                throw new InvalidOperationException("An enum exceeds its member limit.");
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            var catalogMembers = new List<ScriptEnumMemberCatalog>();
            foreach (var field in fields)
            {
                if (field.Name.Length > 128) throw new InvalidOperationException("An enum member exceeds its name limit.");
                var raw = field.GetRawConstantValue()!;
                var bits = Bits(raw);
                if (!_values.TryGetValue(bits, out var constant))
                {
                    constant = new Constant(field.Name, Enum.ToObject(type, raw), bits);
                    _values.Add(bits, constant);
                }
                if (!_names.TryAdd(field.Name, constant))
                    throw new InvalidOperationException("Enum member names must be unambiguous ignoring case.");
                members.Add(field.Name, constant.Name);
                catalogMembers.Add(new ScriptEnumMemberCatalog(field.Name, constant.Name,
                    Convert.ToString(raw, CultureInfo.InvariantCulture)!));
            }
            Members = members;
            Catalog = new ScriptEnumCatalog(type.FullName!, alias, globalName,
                type.IsDefined(typeof(FlagsAttribute), false), catalogMembers);
            _flags = _values.Values.Where(value => value.Bits != 0).OrderByDescending(value => value.Bits).ToArray();
        }

        internal object Parse(string text)
        {
            // Input is bounded before any token or reflection allocation. The scratch owner
            // covers the worst-case token copies; returned boxes have their own reservation.
            CheckText(text);
            var scratch = MemoryBudget.Allocate(512L + 16L * text.Length, () => new object());
            try
            {
                var remaining = text.AsSpan().Trim();
                if (Catalog.IsFlags && remaining.SequenceEqual("0"))
                    return MemoryBudget.Allocate(32, () => Enum.ToObject(_type, 0));
                ulong bits = 0;
                var count = 0;
                while (true)
                {
                    if (++count > 64) throw InvalidValue();
                    var separator = remaining.IndexOf(',');
                    if (separator >= 0 && !Catalog.IsFlags) throw InvalidValue();
                    var token = (separator < 0 ? remaining : remaining[..separator]).Trim();
                    if (!_names.TryGetValue(token.ToString(), out var constant)) throw InvalidValue();
                    bits |= constant.Bits;
                    if (separator < 0) break;
                    remaining = remaining[(separator + 1)..];
                }
                if (_values.TryGetValue(bits, out var named)) return named.Value;
                return MemoryBudget.Allocate(32, () => Enum.ToObject(_type, bits));
            }
            finally { GC.KeepAlive(scratch); }
        }

        internal string Format(object value)
        {
            if (value.GetType() != _type) throw InvalidValue();
            // Convert's enum path boxes the underlying scalar.
            var scratch = MemoryBudget.Allocate(96, () => new object());
            try
            {
                var bits = Bits(value);
                if (_values.TryGetValue(bits, out var named)) return named.Name;
                if (!Catalog.IsFlags) throw InvalidValue();
                if (bits == 0) return "0";
                var parts = MemoryBudget.Allocate(1024 + 2L * ScriptBindingRegistry.MaxStringBytes,
                    () => new List<string>(64));
                ulong covered = 0;
                var length = 0;
                // Include only declared masks wholly contained in this value. Overlapping
                // composite masks are valid too; a greedy subtraction would reject them.
                foreach (var flag in _flags)
                {
                    if ((bits & flag.Bits) != flag.Bits || (covered | flag.Bits) == covered) continue;
                    covered |= flag.Bits;
                    length += flag.Name.Length + (parts.Count == 0 ? 0 : 2);
                    if (length > ScriptBindingRegistry.MaxStringBytes) throw InvalidValue();
                    parts.Add(flag.Name);
                }
                if (covered != bits) throw InvalidValue();
                parts.Reverse();
                var text = MemoryBudget.AllocateString(length, () => string.Join(", ", parts));
                CheckText(text);
                return text;
            }
            finally { GC.KeepAlive(scratch); }
        }

        private static ulong Bits(object value)
        {
            var code = Convert.GetTypeCode(value);
            return code is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                ? unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture))
                : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }

        private static void CheckText(string text)
        {
            if (text.Length > ScriptBindingRegistry.MaxStringBytes ||
                System.Text.Encoding.UTF8.GetByteCount(text) > ScriptBindingRegistry.MaxStringBytes) throw InvalidValue();
        }

        private static ScriptRuntimeException InvalidValue() => new("The value is not a declared enum name or valid named flags combination.");
        private sealed record Constant(string Name, object Value, ulong Bits);
    }

    private sealed class Descriptor : IUserDataDescriptor
    {
        private readonly ScriptEnumRegistry _registry;
        private readonly Script _owner;
        private readonly Policy? _policy;
        internal Descriptor(ScriptEnumRegistry registry, Script owner, Policy? policy)
        { _registry = registry; _owner = owner; _policy = policy; }
        public string Name => _policy?.Catalog.ClrType ?? "Enums";
        public Type Type => typeof(Descriptor);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            using var memoryScope = script.MemoryBudget?.EnterScope();
            if (index.Type != DataType.String || index.String.Length > ScriptBindingRegistry.MaxStringBytes) return DynValue.Nil;
            if (_policy != null) return _policy.Members.TryGetValue(index.String, out var value) ? DynValue.NewString(value) : DynValue.Nil;
            return _registry._state.Nodes.TryGetValue(index.String, out var policy) ? _registry.CreateNode(script, policy) : DynValue.Nil;
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        { Check(script, obj); throw new ScriptRuntimeException("Enum constants are read-only."); }
        public string AsString(object obj) { Check(_owner, obj); return Name; }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        private void Check(Script script, object obj)
        {
            script.ExecutionBudget?.Check();
            script.MemoryBudget?.Check();
            if (!ReferenceEquals(script, _owner) || !ReferenceEquals(obj, this))
                throw new ScriptRuntimeException("The enum registry belongs to another script.");
        }
    }
}
