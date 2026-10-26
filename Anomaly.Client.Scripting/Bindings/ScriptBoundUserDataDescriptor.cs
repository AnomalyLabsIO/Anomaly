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
        using var memoryScope = script.MemoryBudget?.EnterScope();
        if (index.Type != DataType.String || !_policy.Members.TryGetValue(index.String, out var member)) return DynValue.Nil;
        if (member.Property != null)
        {
            var value = ScriptBindingRegistry.Invoke(script, member.Property.GetMethod!, obj, null);
            return _registry.ToScriptValue(script, value, member.ReturnType, _depth + 1);
        }
        var binding = MemoryBudget.Allocate(192, () => new BoundMethod(this, obj, member));
        var callback = MemoryBudget.Allocate(96,
            () => new Func<ScriptExecutionContext, CallbackArguments, DynValue>(binding.Invoke));
        var name = MemoryBudget.AllocateString(_policy.Name.Length + 1L + member.Name.Length,
            () => _policy.Name + "." + member.Name);
        return DynValue.NewCallback(callback, name);
    }

    private sealed class BoundMethod
    {
        private readonly ScriptBoundUserDataDescriptor _descriptor;
        private readonly object _target;
        private readonly ScriptMemberPolicy _member;
        internal BoundMethod(ScriptBoundUserDataDescriptor descriptor, object target, ScriptMemberPolicy member)
        { _descriptor = descriptor; _target = target; _member = member; }

        internal DynValue Invoke(ScriptExecutionContext context, CallbackArguments arguments)
        {
            var caller = context.GetScript();
            _descriptor.CheckOwner(caller, _target);
            using var memoryScope = caller.MemoryBudget?.EnterScope();
            var offset = arguments.IsMethodCall ? 1 : 0;
            if (offset != 0 && (arguments[0].Type != DataType.UserData ||
                !ReferenceEquals(arguments[0].UserData.Descriptor, _descriptor) || !ReferenceEquals(arguments[0].UserData.Object, _target)))
                throw new ScriptRuntimeException("The method receiver does not belong to this binding.");
            if (arguments.Count != _member.Parameters.Length + offset)
                throw new ScriptRuntimeException("The host operation received an incorrect argument count.");
            var converted = MemoryBudget.Allocate(32L + 8L * _member.Parameters.Length,
                () => new object?[_member.Parameters.Length]);
            for (var i = 0; i < converted.Length; i++)
                converted[i] = _descriptor._registry.FromScriptValue(caller, arguments[i + offset], _member.Parameters[i].ParameterType);
            var result = ScriptBindingRegistry.Invoke(caller, _member.Method!, _target, converted);
            return _member.ReturnType == typeof(void) ? DynValue.Nil :
                _descriptor._registry.ToScriptValue(caller, result, _member.ReturnType, _descriptor._depth + 1);
        }
    }

    public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
    {
        CheckOwner(script, obj);
        using var memoryScope = script.MemoryBudget?.EnterScope();
        if (index.Type != DataType.String || !_policy.Members.TryGetValue(index.String, out var member) ||
            member.Property == null || !member.AllowWrite)
            throw new ScriptRuntimeException("The host member is not approved for writes.");
        var converted = _registry.FromScriptValue(script, value, member.ReturnType);
        var arguments = MemoryBudget.Allocate(40, () => new[] { converted });
        ScriptBindingRegistry.Invoke(script, member.Property.SetMethod!, obj, arguments);
        return true;
    }

    public string AsString(object obj)
    {
        _owner.ExecutionBudget?.Check();
        using var memoryScope = _owner.MemoryBudget?.EnterScope();
        return MemoryBudget.AllocateString(9L + _policy.Name.Length, () => "[script " + _policy.Name + "]");
    }

    public DynValue MetaIndex(Script script, object obj, string metaname)
    {
        CheckOwner(script, obj);
        return null!;
    }

    public bool IsTypeCompatible(Type type, object obj) => type == _policy.Type && obj != null && _registry.IsApprovedValue(obj.GetType(), type);

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
        script.MemoryBudget?.Check();
        if (!ReferenceEquals(script, _owner) || obj == null || !_registry.IsApprovedValue(obj.GetType(), _policy.Type))
            throw new ScriptRuntimeException("The host object belongs to another script or type scope.");
    }
}
