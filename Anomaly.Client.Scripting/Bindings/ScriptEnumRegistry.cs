using System.Globalization;
using System.Reflection;
using Anomaly.Client.Scripting.Catalog;
using Anomaly.Client.Scripting.Remote.Protocol;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Bindings;

/// <summary>Enum constants from the selected game assembly and explicitly selected protocol enum types.</summary>
internal sealed class ScriptEnumRegistry
{
    private const int MaxEnums = 1024;
    private const int MaxMembers = 32768;
    private const int MaxMembersPerEnum = 2048;
    private readonly object _sync = new();
    private State _state = new(null, new(), new(StringComparer.Ordinal), new(), new(), Array.Empty<ScriptEnumCatalog>());

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
            var names = types.Select(type => (Type: type, Canonical: EnumNameRules.Clean(type.FullName!),
                Relative: EnumNameRules.Clean(type.Namespace == null ? type.FullName! : type.FullName![(type.Namespace.Length + 1)..])))
                .ToArray();
            if (names.Any(value => string.IsNullOrEmpty(value.Canonical) || value.Canonical.Length > ScriptBindingRegistry.MaxStringBytes) ||
                names.GroupBy(value => value.Canonical, StringComparer.Ordinal).Any(group => group.Count() != 1))
                throw new InvalidOperationException("Clean Lua enum identities must be unique and bounded.");
            // Plan the complete batch before publishing any name. Short names cannot hide
            // a qualified enum, another short enum, or an enum namespace branch.
            var aliases = names.ToDictionary(value => value.Type, value => EnumNameRules.IsPath(value.Relative) &&
                (value.Relative == value.Canonical || !EnumNameRules.Conflicts(value.Relative, value.Canonical)) &&
                !names.Any(other => other.Type != value.Type &&
                    (EnumNameRules.Conflicts(value.Relative, other.Canonical) || EnumNameRules.Conflicts(value.Relative, other.Relative)))
                    ? value.Relative : null);
            var policies = new Dictionary<Type, Policy>();
            var nodes = new Dictionary<string, EnumPathNode<Policy>>(StringComparer.Ordinal);
            var registryTree = new EnumPathNode<Policy>();
            var globalTree = new EnumPathNode<Policy>();
            var catalogs = new List<ScriptEnumCatalog>();
            var count = 0;
            foreach (var value in names)
            {
                var alias = aliases[value.Type];
                var globalName = alias != null && !EnumNameRules.ReservedRoots.Contains(EnumNameRules.Root(alias)) ? alias :
                    EnumNameRules.IsPath(value.Canonical) && !EnumNameRules.ReservedRoots.Contains(EnumNameRules.Root(value.Canonical))
                        ? value.Canonical : null;
                var policy = new Policy(value.Type, alias, globalName);
                count += policy.Catalog.Members.Count;
                if (count > MaxMembers) throw new InvalidOperationException("The game enum catalog exceeds its member limit.");
                policies.Add(value.Type, policy);
                nodes.Add(value.Canonical, EnumNameRules.IsPath(value.Canonical)
                    ? registryTree.Add(value.Canonical, policy) : new EnumPathNode<Policy>(policy));
                catalogs.Add(policy.Catalog);
            }
            foreach (var policy in policies.Values)
            {
                if (policy.Catalog.Alias is { } alias)
                {
                    var node = registryTree.Add(alias, policy);
                    if (!nodes.TryAdd(alias, node) && nodes[alias].Value != policy)
                        throw new InvalidOperationException("An enum alias conflicts with a qualified Lua name.");
                }
                if (policy.Catalog.GlobalName is { } global) globalTree.Add(global, policy);
            }
            _state = new State(trustedAssembly, policies, nodes, registryTree, globalTree, catalogs.AsReadOnly());
        }
    }

    internal bool Contains(Type type) => _state.Policies.ContainsKey(type);
    internal IReadOnlyList<ScriptEnumCatalog> GetCatalog() => _state.Catalogs;
    internal bool IsGlobalName(string name) => name == "Enums" || _state.GlobalTree.Children.ContainsKey(name);

    internal object Parse(Type type, string text) => GetPolicy(type).Parse(text);
    internal string Format(Type type, object value) => GetPolicy(type).Format(value);

    private Policy GetPolicy(Type type) => _state.Policies.TryGetValue(type, out var policy) ? policy :
        throw new ScriptRuntimeException("The enum type has not been approved by this runtime.");

    internal void InstallGlobals(Script owner)
    {
        using var memoryScope = owner.MemoryBudget?.EnterScope();
        if (_state.Policies.Count == 0) return;
        if (!owner.Globals.Get("Enums").IsNil() || _state.GlobalTree.Children.Keys.Any(name => !owner.Globals.Get(name).IsNil()))
            throw new InvalidOperationException("An enum global is already installed.");
        var pending = MemoryBudget.Allocate(128L + (_state.GlobalTree.Children.Count + 1L) * 64L,
            () => new Dictionary<string, DynValue>(_state.GlobalTree.Children.Count + 1, StringComparer.Ordinal));
        pending.Add("Enums", CreateNode(owner, _state.RegistryTree, "Enums", registryRoot: true));
        foreach (var root in _state.GlobalTree.Children)
            pending.Add(root.Key, CreateNode(owner, root.Value, root.Key));
        foreach (var entry in pending) owner.Globals.Set(entry.Key, entry.Value);
    }

    private DynValue CreateNode(Script owner, EnumPathNode<Policy> node, string name, bool registryRoot = false)
    {
        var descriptor = MemoryBudget.Allocate(384, () => new Descriptor(this, owner, node, name, registryRoot));
        return UserData.Create(descriptor, descriptor);
    }

    private sealed record State(Assembly? Assembly, Dictionary<Type, Policy> Policies,
        Dictionary<string, EnumPathNode<Policy>> Nodes, EnumPathNode<Policy> RegistryTree, EnumPathNode<Policy> GlobalTree,
        IReadOnlyList<ScriptEnumCatalog> Catalogs);

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
        private readonly EnumPathNode<Policy> _node;
        private readonly bool _registryRoot;
        public string Name { get; }
        internal Descriptor(ScriptEnumRegistry registry, Script owner, EnumPathNode<Policy> node, string name, bool registryRoot)
        { _registry = registry; _owner = owner; _node = node; Name = node.Value?.Catalog.LuaName ?? name; _registryRoot = registryRoot; }
        public Type Type => typeof(Descriptor);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            using var memoryScope = script.MemoryBudget?.EnterScope();
            if (index.Type != DataType.String || index.String.Length > ScriptBindingRegistry.MaxStringBytes) return DynValue.Nil;
            if (_node.Value is { } policy)
                return policy.Members.TryGetValue(index.String, out var value) ? DynValue.NewString(value) : DynValue.Nil;
            if (_registryRoot && _registry._state.Nodes.TryGetValue(index.String, out var exact))
                return _registry.CreateNode(script, exact, exact.Value!.Catalog.LuaName);
            return _node.Children.TryGetValue(index.String, out var child)
                ? _registry.CreateNode(script, child, Name + "." + index.String) : DynValue.Nil;
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
