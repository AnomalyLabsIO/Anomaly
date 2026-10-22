using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Bindings;

/// <summary>Binds immutable policy and one host value to exactly one script.</summary>
internal sealed class ScriptBoundUserDataDescriptor : IUserDataDescriptor
{
    private readonly ScriptBindingRegistry _registry;
    private readonly ScriptTypePolicy _policy;
    private readonly Script _owner;
    private readonly int _depth;

    internal ScriptBoundUserDataDescriptor(ScriptBindingRegistry registry, ScriptTypePolicy policy, Script owner, int depth)
    {
        _registry = registry;
        _policy = policy;
        _owner = owner;
        _depth = depth;
    }

    public string Name => _policy.Name;
    public Type Type => _policy.Type;

    public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
    {
        CheckOwner(script, obj);
        if (index.Type != DataType.String || !_policy.Members.TryGetValue(index.String, out var member)) return DynValue.Nil;
        if (member.Property != null)
        {
            var value = ScriptBindingRegistry.Invoke(script, member.Property.GetMethod!, obj, null);
            return _registry.ToScriptValue(script, value, member.ReturnType, _depth + 1);
        }
        return DynValue.NewCallback((context, arguments) =>
        {
            var caller = context.GetScript();
            CheckOwner(caller, obj);
            var offset = arguments.IsMethodCall ? 1 : 0;
            if (offset != 0 && (arguments[0].Type != DataType.UserData ||
                !ReferenceEquals(arguments[0].UserData.Descriptor, this) || !ReferenceEquals(arguments[0].UserData.Object, obj)))
                throw new ScriptRuntimeException("The method receiver does not belong to this binding.");
            if (arguments.Count != member.Parameters.Length + offset)
                throw new ScriptRuntimeException("The host operation received an incorrect argument count.");
            var converted = new object?[member.Parameters.Length];
            for (var i = 0; i < converted.Length; i++)
                converted[i] = _registry.FromScriptValue(caller, arguments[i + offset], member.Parameters[i].ParameterType);
            var result = ScriptBindingRegistry.Invoke(caller, member.Method!, obj, converted);
            return member.ReturnType == typeof(void) ? DynValue.Nil :
                _registry.ToScriptValue(caller, result, member.ReturnType, _depth + 1);
        }, _policy.Name + "." + member.Name);
    }

    public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
    {
        CheckOwner(script, obj);
        if (index.Type != DataType.String || !_policy.Members.TryGetValue(index.String, out var member) ||
            member.Property == null || !member.AllowWrite)
            throw new ScriptRuntimeException("The host member is not approved for writes.");
        var converted = _registry.FromScriptValue(script, value, member.ReturnType);
        ScriptBindingRegistry.Invoke(script, member.Property.SetMethod!, obj, new[] { converted });
        return true;
    }

    public string AsString(object obj)
    {
        _owner.ExecutionBudget?.Check();
        return "[script " + _policy.Name + "]";
    }

    public DynValue MetaIndex(Script script, object obj, string metaname)
    {
        CheckOwner(script, obj);
        return null!;
    }

    public bool IsTypeCompatible(Type type, object obj) => type == _policy.Type && obj != null && obj.GetType() == type;

    internal object GetArgumentObject(ScriptBindingRegistry registry, Script script, object obj, Type expectedType)
    {
        CheckOwner(script, obj);
        if (!ReferenceEquals(registry, _registry) || expectedType != _policy.Type)
            throw new ScriptRuntimeException("The host reference is outside this binding's approved type graph.");
        return obj;
    }

    private void CheckOwner(Script script, object obj)
    {
        script.ExecutionBudget?.Check();
        if (!ReferenceEquals(script, _owner) || obj == null || obj.GetType() != _policy.Type)
            throw new ScriptRuntimeException("The host object belongs to another script or type scope.");
    }
}
