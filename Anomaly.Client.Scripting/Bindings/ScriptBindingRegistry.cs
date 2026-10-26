using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Catalog;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Bindings;

/// <summary>Instance-owned, explicit binding policies. No global MoonSharp descriptors are registered.</summary>
internal sealed class ScriptBindingRegistry
{
    internal const int MaxStringBytes = 4096;
    internal const int MaxObjectDepth = 8;
    private const int MaxPolicyDepth = 16;
    private const int MaxTypes = 256;
    private const double MaxExactInteger = 9007199254740991d;
    private readonly object _sync = new();
    private readonly Dictionary<Type, ScriptTypePolicy> _types = new();
    internal ScriptEnumRegistry Enums { get; } = new();
    internal bool HasGameContext => _types.ContainsKey(typeof(ScriptGameContext));

    internal void RegisterType<T>() => RegisterType(typeof(T));

    internal void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_sync)
        {
            var pending = new Dictionary<Type, ScriptTypePolicy>();
            BuildPolicy(type, pending, new HashSet<Type>(), 0);
            Commit(pending);
        }
    }

    internal void RegisterEventArgument(Type type)
    {
        lock (_sync)
        {
            var pending = new Dictionary<Type, ScriptTypePolicy>();
            ValidateValueType(type, pending, new HashSet<Type>(), 0);
            Commit(pending);
        }
    }

    /// <summary>Scans only an already-loaded assembly explicitly selected by trusted host code.</summary>
    internal void AutoDiscoverScriptTypes(Assembly trustedAssembly)
    {
        ArgumentNullException.ThrowIfNull(trustedAssembly);
        var candidates = trustedAssembly.GetTypes()
            .Where(type => type.IsDefined(typeof(ScriptTypeAttribute), inherit: false) &&
                !type.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false))
            .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        lock (_sync)
        {
            var pending = new Dictionary<Type, ScriptTypePolicy>();
            foreach (var type in candidates) BuildPolicy(type, pending, new HashSet<Type>(), 0);
            Commit(pending);
        }
    }

    internal ScriptTypePolicy GetTypePolicy(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_sync)
            return _types.TryGetValue(type, out var policy) ? policy :
                throw new InvalidOperationException("The exact host type is not registered.");
    }

    internal ScriptApiCatalog GetCatalog()
    {
        lock (_sync) return new ScriptApiCatalog(_types.Values.Select(value => value.Catalog), enums: Enums.GetCatalog());
    }

    private void Commit(Dictionary<Type, ScriptTypePolicy> pending)
    {
        if (_types.Values.Concat(pending.Values).GroupBy(value => value.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Script type aliases must be unique throughout the binding graph.");
        foreach (var pair in pending) _types.Add(pair.Key, pair.Value);
    }

    internal void SetGlobal(Script script, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(script);
        ValidateName(name);
        script.ExecutionBudget?.Check();
        script.Globals.Set(name, ToScriptValue(script, value));
        script.ExecutionBudget?.Check();
    }

    internal DynValue ToScriptValue(Script script, object? value) => ToScriptValue(script, value, null, 0);

    internal DynValue ToScriptValue(Script script, object? value, Type? declaredType, int depth)
    {
        using var memoryScope = script.MemoryBudget?.EnterScope();
        script.ExecutionBudget?.Check();
        if (value == null) return DynValue.Nil;
        var type = value.GetType();
        declaredType = Nullable.GetUnderlyingType(declaredType ?? type) ?? declaredType;
        if (declaredType != null && !IsApprovedValue(type, declaredType))
            throw new ScriptRuntimeException("The host value is outside the approved declared type.");
        if (type == typeof(string)) return DynValue.NewString(CheckString((string)value));
        if (type == typeof(bool)) return DynValue.NewBoolean((bool)value);
        if (type.IsEnum) return DynValue.NewString(Enums.Format(type, value));
        if (IsNumeric(type))
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            CheckNumber(number, type);
            return DynValue.NewNumber(number);
        }
        if (depth > MaxObjectDepth)
            throw new ScriptRuntimeException("Host object access exceeded its depth limit.");
        ScriptTypePolicy policy;
        lock (_sync)
        {
            if (!_types.TryGetValue(declaredType ?? type, out policy!))
                throw new ScriptRuntimeException("The host value's exact type has not been approved.");
        }
        var descriptor = MemoryBudget.Allocate(384, () => new ScriptBoundUserDataDescriptor(this, policy, script, depth));
        return UserData.Create(value, descriptor);
    }

    internal object? FromScriptValue(Script script, DynValue value, Type type)
    {
        using var memoryScope = script.MemoryBudget?.EnterScope();
        script.MemoryBudget?.Check();
        script.ExecutionBudget?.Check();
        var nullable = Nullable.GetUnderlyingType(type);
        if (value.IsNil() && (nullable != null || type == typeof(string) || !type.IsValueType)) return null;
        type = nullable ?? type;
        if (type.IsEnum)
        {
            if (value.Type != DataType.String) throw InvalidArgument();
            return Enums.Parse(type, value.String);
        }
        if (type == typeof(string))
        {
            if (value.Type != DataType.String) throw InvalidArgument();
            var text = CheckString(value.String);
            return MemoryBudget.AllocateString(text.Length, () => new string(text.AsSpan()));
        }
        if (type == typeof(bool))
        {
            if (value.Type != DataType.Boolean) throw InvalidArgument();
            return MemoryBudget.Allocate(32, () => (object)value.Boolean);
        }
        if (IsNumeric(type))
        {
            if (value.Type != DataType.Number) throw InvalidArgument();
            CheckNumber(value.Number, type);
            // ChangeType boxes its input as well as its scalar output.
            try { return MemoryBudget.Allocate(64, () => Convert.ChangeType(value.Number, type, CultureInfo.InvariantCulture)); }
            catch (OverflowException) { throw InvalidArgument(); }
        }
        if (value.Type == DataType.UserData && value.UserData.Descriptor is ScriptBoundUserDataDescriptor descriptor)
            return descriptor.GetArgumentObject(this, script, value.UserData.Object, type);
        throw InvalidArgument();
    }

    internal static object? Invoke(Script script, MethodInfo method, object target, object?[]? arguments)
    {
        using var memoryScope = script.MemoryBudget?.EnterScope();
        script.ExecutionBudget?.Check();
        // Account for bounded reflection/exception bookkeeping before entering CLR.
        // This allowance cannot intercept allocations performed by the approved
        // method itself; such methods still require a trusted, bounded implementation.
        var scratch = MemoryBudget.Allocate(2048L + 64L * (arguments?.Length ?? 0), () => new object());
        var resultType = Nullable.GetUnderlyingType(method.ReturnType) ?? method.ReturnType;
        using var resultMemory = method.ReturnType.IsValueType && method.ReturnType != typeof(void)
            ? MemoryBudget.Reserve(IsNumeric(resultType) || resultType == typeof(bool) || resultType.IsEnum ? 32 : 256) : default;
        object? result;
        try
        {
            result = method.Invoke(target, arguments);
            if (result != null && method.ReturnType.IsValueType) resultMemory.Bind(result);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ScriptHostException.RethrowFatal(exception);
            Exception inner = exception.InnerException;
            while (inner is TargetInvocationException nested && nested.InnerException != null)
                inner = nested.InnerException;
            if (inner is ScriptExecutionAbortedException or OutOfMemoryException or StackOverflowException)
                ExceptionDispatchInfo.Capture(inner).Throw();
            script.MemoryBudget?.Check();
            script.ExecutionBudget?.Check();
            throw MemoryBudget.Allocate(512, () => new ScriptRuntimeException("The approved host operation failed."));
        }
        finally { GC.KeepAlive(scratch); }
        script.MemoryBudget?.Check();
        script.ExecutionBudget?.Check();
        return result;
    }

    internal string DescribeSurface() => GetCatalog().DescribeSurface();

    // A registered subclass can be projected through a declared base policy, never
    // through its larger runtime surface. Unknown subclasses still fail closed.
    internal bool IsApprovedValue(Type actual, Type declared)
    {
        if (actual == declared) return true;
        lock (_sync) return declared.IsAssignableFrom(actual) && _types.ContainsKey(actual) && _types.ContainsKey(declared);
    }

    private void BuildPolicy(Type type, Dictionary<Type, ScriptTypePolicy> pending, HashSet<Type> building, int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            if (!Enums.Contains(type)) throw new InvalidOperationException("The enum type is outside the approved game assembly.");
            return;
        }
        if (_types.ContainsKey(type) || pending.ContainsKey(type) || building.Contains(type)) return;
        if (depth > MaxPolicyDepth || _types.Count + pending.Count + building.Count >= MaxTypes)
            throw new InvalidOperationException("The annotated binding graph exceeds its policy limit.");
        var annotation = type.GetCustomAttribute<ScriptTypeAttribute>(inherit: false);
        if ((!type.IsClass && !type.IsValueType) || type.IsGenericType || typeof(Delegate).IsAssignableFrom(type) ||
            annotation == null || type.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false))
            throw new InvalidOperationException($"Type '{type.FullName}' is not an explicitly approved script type.");
        var name = annotation.Name ?? type.Name;
        ValidateName(name);
        if (_types.Values.Concat(pending.Values).Any(value => value.Name == name && value.Type != type))
            throw new InvalidOperationException("Script type aliases must be unique.");

        building.Add(type);
        var members = new Dictionary<string, ScriptMemberPolicy>(StringComparer.Ordinal);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        // Each concrete type still needs its own ScriptType. Shared members are
        // inherited only from explicitly annotated, non-ignored base types.
        var declaringTypes = new HashSet<Type>();
        for (Type? declaring = type; declaring != null &&
            declaring.IsDefined(typeof(ScriptTypeAttribute), inherit: false) &&
            !declaring.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false); declaring = declaring.BaseType)
            declaringTypes.Add(declaring);
        // Reflect through the concrete type so an override cannot fall back to a
        // visible base declaration after its own attribute is removed or ignored.
        foreach (var property in type.GetProperties(flags).Where(property => declaringTypes.Contains(property.DeclaringType!)))
        {
            var visible = property.GetCustomAttribute<ScriptVisibleAttribute>(inherit: false);
            if (visible == null || property.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false)) continue;
            var getter = property.GetMethod;
            if (getter?.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false) == true) continue;
            if (getter == null || !getter.IsPublic || getter.IsStatic || property.GetIndexParameters().Length != 0)
                throw new InvalidOperationException("Only public instance properties without index parameters may be exposed.");
            ValidateValueType(property.PropertyType, pending, building, depth + 1);
            var allowWrite = visible.AllowWrite;
            if (allowWrite)
            {
                var setter = property.SetMethod;
                if (setter?.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false) == true) allowWrite = false;
                else if (setter == null || !setter.IsPublic || setter.IsStatic)
                    throw new InvalidOperationException("An explicitly writable property needs a public instance setter.");
            }
            AddMember(members, new ScriptMemberPolicy(visible.Name ?? property.Name, property, allowWrite));
        }
        foreach (var method in type.GetMethods(flags).Where(method => declaringTypes.Contains(method.DeclaringType!)))
        {
            var visible = method.GetCustomAttribute<ScriptVisibleAttribute>(inherit: false);
            if (visible == null || method.IsDefined(typeof(ScriptIgnoreAttribute), inherit: false)) continue;
            if (method.IsStatic || method.IsSpecialName || method.IsGenericMethod || method.Name == nameof(GetType) || visible.AllowWrite)
                throw new InvalidOperationException("Static, generic, special, or reflection methods cannot be exposed.");
            ValidateValueType(method.ReturnType, pending, building, depth + 1, allowVoid: true);
            var parameters = method.GetParameters();
            if (parameters.Length > 16) throw new InvalidOperationException("Exposed methods may have at most 16 parameters.");
            foreach (var parameter in parameters)
            {
                if (parameter.IsOut || parameter.ParameterType.IsByRef || parameter.IsOptional ||
                    parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false))
                    throw new InvalidOperationException("Ref, out, optional, and variadic parameters are not supported.");
                ValidateValueType(parameter.ParameterType, pending, building, depth + 1);
            }
            AddMember(members, new ScriptMemberPolicy(visible.Name ?? method.Name, method));
        }
        building.Remove(type);
        pending.Add(type, new ScriptTypePolicy(type, name, members));
    }

    private void ValidateValueType(Type type, Dictionary<Type, ScriptTypePolicy> pending, HashSet<Type> building, int depth, bool allowVoid = false)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if ((allowVoid && type == typeof(void)) || type == typeof(string) || type == typeof(bool) || IsNumeric(type)) return;
        BuildPolicy(type, pending, building, depth);
    }

    private static void AddMember(Dictionary<string, ScriptMemberPolicy> members, ScriptMemberPolicy member)
    {
        ValidateName(member.Name);
        if (!members.TryAdd(member.Name, member))
            throw new InvalidOperationException("Script member aliases and overloads must be unique.");
    }

    private static void ValidateName(string name) => ScriptNameRules.ValidateIdentifier(name);
    private static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
        type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
        type == typeof(ulong) || type == typeof(float) || type == typeof(double);

    private static void CheckNumber(double value, Type type)
    {
        if (!double.IsFinite(value) || (type == typeof(float) && !float.IsFinite((float)value)) ||
            (type != typeof(double) && type != typeof(float) && (Math.Truncate(value) != value || Math.Abs(value) > MaxExactInteger)))
            throw InvalidArgument();
    }

    private static string CheckString(string value)
    {
        if (value.Length > MaxStringBytes || Encoding.UTF8.GetByteCount(value) > MaxStringBytes)
            throw new ScriptRuntimeException("Host strings exceed their UTF-8 byte limit.");
        return value;
    }

    private static ScriptRuntimeException InvalidArgument() => new("The host value does not match the approved scalar or reference type.");
}
