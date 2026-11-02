using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Anomaly.Client.Api.Scripting;
using Anomaly.Client.Scripting.Bindings;
using Anomaly.Client.Scripting.Events;
using Anomaly.Client.Scripting.Output;
using Anomaly.Client.Scripting.Remote.Protocol;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Remote;

/// <summary>The production policy cutover, not a second reflection binder.</summary>
internal static class RemoteHostValues
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    internal static IReadOnlyList<ScriptTypePolicy> ApprovedPolicies(ScriptRuntime runtime)
    {
        var policies = runtime.Bindings.GetPolicies();
        foreach (var policy in policies)
        {
            var type = policy.Type;
            if (type.Assembly != typeof(ScriptTypeAttribute).Assembly && type != typeof(ScriptGameContext) &&
                type != typeof(ScriptPosition) && type != typeof(ScriptOutput))
                throw new InvalidDataException("Remote catalogs accept production script types only.");
            foreach (var member in policy.Members.Values)
            {
                if (member.AllowWrite || (member.Method != null && !ApprovedMethod(type, member.Method)))
                    throw new InvalidDataException("Remote catalogs accept read-only gameplay policies only.");
            }
        }
        if (runtime.EventCatalog is { } catalog)
            foreach (var source in catalog.Categories.Values.SelectMany(category => category.Values))
                if (source.EventInfo.DeclaringType?.Assembly != typeof(ScriptTypeAttribute).Assembly)
                    throw new InvalidDataException("Remote catalogs accept production event sources only.");
        return policies;
    }

    private static bool ApprovedMethod(Type type, MethodInfo method)
    {
        // No API wrapper methods are admitted. The root's nine queries and the
        // output capability are the only shipped callable object operations.
        if (method.DeclaringType != type) return false;
        if (type == typeof(ScriptGameContext))
            return method.Name is nameof(ScriptGameContext.GetPlayer) or nameof(ScriptGameContext.GetPlayerAt) or
                nameof(ScriptGameContext.GetCurrentItem) or nameof(ScriptGameContext.GetRoom) or
                nameof(ScriptGameContext.GetFirearmState) or nameof(ScriptGameContext.GetHealth) or
                nameof(ScriptGameContext.GetHumeShield) or nameof(ScriptGameContext.GetStamina) or
                nameof(ScriptGameContext.GetPosition);
        return type == typeof(ScriptOutput) && method.Name is nameof(ScriptOutput.Log) or
            nameof(ScriptOutput.ShowText) or nameof(ScriptOutput.ClearText);
    }

    internal static WireValue ExportCatalog(ScriptRuntime runtime)
    {
        using var scope = runtime.EnterAllocationScope();
        var policies = ApprovedPolicies(runtime);
        var types = new WireValue[policies.Count];
        for (var index = 0; index < policies.Count; index++)
        {
            var policy = policies[index];
            var members = new WireValue[policy.Members.Count];
            var memberIndex = 0;
            foreach (var member in policy.Members.Values.OrderBy(member => member.Name, StringComparer.Ordinal))
                members[memberIndex++] = Record(("name", WireValue.String(member.Name)),
                    ("method", WireValue.Flag(member.Method != null)), ("argc", WireValue.Numeric(member.Parameters.Length)));
            types[index] = Record(("name", WireValue.String(policy.Name)), ("members", WireValue.Array(members)));
        }
        var enums = new Dictionary<string, WireValue>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, WireValue>(StringComparer.Ordinal);
        var globals = new Dictionary<string, WireValue>(StringComparer.Ordinal);
        foreach (var enumeration in runtime.Bindings.Enums.GetCatalog())
        {
            var members = new Dictionary<string, WireValue>(StringComparer.Ordinal);
            foreach (var member in enumeration.Members) members.Add(member.Name, WireValue.String(member.Value));
            enums.Add(enumeration.LuaName, WireValue.Object(string.Empty, members));
            if (enumeration.Alias is { } alias) aliases.Add(alias, WireValue.String(enumeration.LuaName));
            if (enumeration.GlobalName is { } global) globals.Add(global, WireValue.String(enumeration.LuaName));
        }
        var events = new List<WireValue>();
        if (runtime.EventCatalog is { } catalog)
            foreach (var source in catalog.Categories.Values.SelectMany(category => category.Values)
                .OrderBy(source => source.Category, StringComparer.Ordinal).ThenBy(source => source.Name, StringComparer.Ordinal))
                events.Add(Record(("category", WireValue.String(source.Category)), ("name", WireValue.String(source.Name)),
                    ("coalesce", WireValue.Flag(source.CoalesceLatest)), ("arity", WireValue.Numeric(source.Parameters.Length))));
        return Record(("types", WireValue.Array(types)), ("enums", WireValue.Object(string.Empty, enums)),
            ("enumAliases", WireValue.Object(string.Empty, aliases)), ("enumGlobals", WireValue.Object(string.Empty, globals)),
            ("events", WireValue.Array(events)));
    }

    internal static WireValue Record(params (string Key, WireValue Value)[] fields)
    {
        var values = new Dictionary<string, WireValue>(fields.Length, StringComparer.Ordinal);
        foreach (var field in fields) values.Add(field.Key, field.Value);
        return WireValue.Object(string.Empty, values);
    }

    internal static WireValue Frozen(object? value) => value switch
    {
        null => WireValue.Null,
        bool boolean => WireValue.Flag(boolean),
        double number when double.IsFinite(number) => WireValue.Numeric(number),
        string text => WireValue.String(CheckString(text)),
        ScriptFrozenEventValue frozen => WireValue.Object(frozen.Name,
            frozen.Fields.ToDictionary(pair => pair.Key, pair => Frozen(pair.Value), StringComparer.Ordinal)),
        _ => throw new InvalidDataException("Invalid frozen event value.")
    };

    internal static string CheckString(string value)
    {
        if (value.Length > ScriptBindingRegistry.MaxStringBytes || Utf8.GetByteCount(value) > ScriptBindingRegistry.MaxStringBytes)
            throw new InvalidDataException("Host strings exceed their UTF-8 byte limit.");
        return value;
    }

    internal static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
        type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
        type == typeof(ulong) || type == typeof(float) || type == typeof(double);

    internal static void CheckNumber(double number, Type type)
    {
        if (!double.IsFinite(number) || (type == typeof(float) && !float.IsFinite((float)number)) ||
            (type != typeof(float) && type != typeof(double) && (Math.Truncate(number) != number || Math.Abs(number) > 9007199254740991d)))
            throw new ScriptRuntimeException("The host value is not an approved finite scalar.");
    }

    internal static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: { } inner }) exception = inner;
        return exception;
    }

    internal static void Rethrow(Exception exception) => ExceptionDispatchInfo.Capture(exception).Throw();

    internal static void Shape(WireValue value, params string[] fields)
    {
        if (value.Kind != WireKind.Object || value.Text.Length != 0 || value.Fields.Count != fields.Length)
            throw new InvalidDataException("Invalid host request shape.");
        foreach (var field in fields)
            if (!value.Fields.ContainsKey(field)) throw new InvalidDataException("Unknown or missing host request field.");
    }

    // Dispatch can also be used by trusted host tools; do not rely on the pipe
    // decoder alone for bounds on the parent-side capability boundary.
    internal static void CheckRequest(WireValue request)
    {
        var nodes = 0;
        var bytes = 22;
        CheckNode(request, 0, ref nodes, ref bytes);
    }

    private static void CheckNode(WireValue value, int depth, ref int nodes, ref int bytes)
    {
        if (value == null || depth > 16 || ++nodes > RemoteProtocol.MaxWorkerNodes)
            throw new InvalidDataException("Host request graph limit exceeded.");
        AddBytes(1, ref bytes);
        switch (value.Kind)
        {
            case WireKind.Null: return;
            case WireKind.Boolean: AddBytes(1, ref bytes); return;
            case WireKind.Number:
                if (!double.IsFinite(value.Number)) throw new InvalidDataException("Non-finite host request number.");
                AddBytes(8, ref bytes); return;
            case WireKind.String: AddText(value.Text, 4096, ref bytes); return;
            case WireKind.Reference:
                if (value.Handle <= 0 || value.Depth is < 0 or > 8) throw new InvalidDataException("Invalid host reference.");
                AddText(value.Text, 128, ref bytes); AddBytes(12, ref bytes); return;
            case WireKind.Object:
                if (value.Fields.Count > 256) throw new InvalidDataException("Host request record limit exceeded.");
                AddText(value.Text, 128, ref bytes); AddBytes(4, ref bytes);
                foreach (var pair in value.Fields)
                {
                    AddText(pair.Key, 128, ref bytes);
                    CheckNode(pair.Value, depth + 1, ref nodes, ref bytes);
                }
                return;
            case WireKind.Array:
                if (value.Items.Count > 256) throw new InvalidDataException("Host request array limit exceeded.");
                AddBytes(4, ref bytes);
                foreach (var item in value.Items) CheckNode(item, depth + 1, ref nodes, ref bytes);
                return;
            default: throw new InvalidDataException("Unknown host request kind.");
        }
    }

    private static void AddText(string text, int maximum, ref int bytes)
    {
        if (text.Length > maximum) throw new InvalidDataException("Host request text limit exceeded.");
        var count = Utf8.GetByteCount(text);
        if (count > maximum) throw new InvalidDataException("Host request text limit exceeded.");
        AddBytes(4 + count, ref bytes);
    }

    private static void AddBytes(int count, ref int bytes)
    {
        if (count > RemoteProtocol.MaxWorkerFrameBytes - bytes) throw new InvalidDataException("Host request byte limit exceeded.");
        bytes += count;
    }
}
