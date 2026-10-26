using System.Text;

namespace Anomaly.ScriptSandboxPrototype;

internal sealed record ProbeCase(
    string Name,
    string Source,
    string[] MoonSharpOutcomes,
    string[] LuauOutcomes,
    int? ExpectedObjects = null,
    bool ExpectPolicyViolation = false,
    string Note = "");

internal static class ProbeCases
{
    public static IReadOnlyList<ProbeCase> Create(string sentinelPath, string loopbackUrl)
    {
        var sentinel = LuaString(sentinelPath);
        var loopback = LuaString(loopbackUrl);
        string[] completed = { "Completed" };
        string[] scriptError = { "ScriptError" };
        string[] sourceRejected = { "SourceRejected" };
        string[] cpuLimit = { "CpuLimit" };
        string[] moonWatchdog = { "ProcessTimeout", "CpuLimit" };

        return new List<ProbeCase>
        {
            new("happy_owned_object", @"
local cube = objects.spawn('demo/cube')
local panel = objects.spawn('demo/panel')
assert(type(cube) == 'string' and type(panel) == 'string' and cube ~= panel)
objects.set_position(cube, 1, 2, 3)
objects.set_label(cube, 'client object')
objects.destroy(panel)", completed, completed, ExpectedObjects: 1,
                Note: "Primitive values and opaque handles exercise every approved host operation."),

            new("bounded_arithmetic_work", @"
local sum = 0
for i = 1, 5000 do sum = sum + i % 17 end
assert(sum == 39987)", completed, completed, ExpectedObjects: 0,
                Note: "A bounded common-Lua arithmetic workload checks useful execution within the VM budget; cold startup is not a game performance benchmark."),

            new("objects_spawn_then_error", @"
local h = objects.spawn('demo/cube')
objects.set_position(h, 1, 2, 3)
objects.set_label(h, 'must be discarded')
error('intentional staged failure')", scriptError, scriptError, ExpectedObjects: 0,
                Note: "One object exists before failure; the worker must revoke staged state before returning a failed execution."),

            new("forged_handle", "objects.set_position('forged:1', 0, 0, 0)",
                scriptError, scriptError, ExpectedObjects: 0, ExpectPolicyViolation: true,
                Note: "An unhandled capability violation discards the script's staged objects."),

            new("destroyed_handle", @"
local h = objects.spawn('demo/cube')
objects.destroy(h)
assert(not pcall(function() objects.set_position(h, 0, 0, 0) end))
assert(not pcall(function() objects.set_label(h, 'stale') end))
assert(not pcall(function() objects.destroy(h) end))", completed, completed,
                ExpectedObjects: 0, ExpectPolicyViolation: true,
                Note: "Destroyed-handle reuse is covered here; real foreign scopes and revoked generations require host self-tests."),

            new("finite_bounded_coordinates", @"
local h = objects.spawn('demo/cube')
objects.set_position(h, 10, 20, 30)
assert(not pcall(function() objects.set_position(h, 0/0, 0, 0) end))
assert(not pcall(function() objects.set_position(h, 0, 1/0, 0) end))
assert(not pcall(function() objects.set_position(h, 0, 0, -1/0) end))
assert(not pcall(function() objects.set_position(h, 1001, 0, 0) end))
assert(not pcall(function() objects.set_position(h, 0, -1001, 0) end))
objects.set_position(h, 1000, -1000, 0)", completed, completed,
                ExpectedObjects: 1, ExpectPolicyViolation: true,
                Note: "NaN, infinities and out-of-range coordinates must fail before mutation; inclusive boundary coordinates are valid."),

            new("utf8_label_budget", @"
local h = objects.spawn('demo/panel')
objects.set_label(h, string.rep('x', 128))
assert(not pcall(function() objects.set_label(h, string.rep('x', 129)) end))
assert(not pcall(function() objects.set_label(h, string.rep('é', 65)) end))
objects.set_label(h, 'bounded')", completed, completed,
                ExpectedObjects: 1, ExpectPolicyViolation: true,
                Note: "The multibyte label is under 128 characters but over the 128-byte UTF-8 budget."),

            new("asset_catalog_allowlist", @"
assert(not pcall(function() objects.spawn('https://example.invalid/payload') end))
assert(not pcall(function() objects.spawn('../../unapproved') end))
assert(not pcall(function() objects.spawn('demo/unknown') end))", completed, completed,
                ExpectedObjects: 0, ExpectPolicyViolation: true,
                Note: "Asset strings are exact catalog keys; this API never interprets them as URLs or filesystem paths."),

            new("object_quota", @"
for i = 1, 16 do objects.spawn('demo/cube') end
assert(not pcall(function() objects.spawn('demo/cube') end))", completed, completed,
                ExpectedObjects: 16, ExpectPolicyViolation: true,
                Note: "The rejected seventeenth spawn must not add an object."),

            new("attempted_host_call_quota", @"
local h = objects.spawn('demo/cube')
for i = 1, 140 do
    assert(not pcall(function() objects.set_label('forged', 'x') end))
end
assert(not pcall(function() objects.set_label(h, 'budget exhausted') end))", completed, completed,
                ExpectedObjects: 1, ExpectPolicyViolation: true,
                Note: "Invalid-handle calls consume the same host-call budget as successful calls, even through pcall."),

            new("strict_primitive_arguments", @"
local h = objects.spawn('demo/cube')
assert(not pcall(function() objects.spawn({}) end))
assert(not pcall(function() objects.set_position(h, '1', 2, 3) end))
assert(not pcall(function() objects.set_position(h, false, 2, 3) end))
assert(not pcall(function() objects.set_label(h, {}) end))
assert(not pcall(function() objects.destroy(h, 'extra') end))
assert(not pcall(function() objects.destroy() end))
objects.set_position(h, 1, 2, 3)", completed, completed, ExpectedObjects: 1,
                Note: "No automatic CLR/userdata conversion, numeric-string coercion, or ignored extra arguments."),

            new("prevalidation_attempt_quota", @"
for i = 1, 140 do assert(not pcall(function() objects.spawn({}) end)) end
assert(not pcall(function() objects.spawn('demo/cube') end))", completed, completed,
                ExpectedObjects: 0,
                Note: "Wrong-type calls consume the adapter's attempt budget before shared API validation."),

            new("callback_string_byte_limit", @"
local h = objects.spawn('demo/cube')
assert(not pcall(function() objects.set_label(h, string.rep('x', 4097)) end))
objects.set_label(h, 'small')", completed, completed,
                ExpectedObjects: 1,
                Note: "Both adapters reject oversized callback strings before host lookup or native-to-managed marshaling."),

            new("dangerous_globals_absent", @"
local forbidden = {
    'io', 'os', 'package', 'require', 'dofile', 'loadfile', 'load', 'loadstring',
    'debug', 'coroutine', 'clr', 'luanet', 'import', 'System', 'CS',
    'UnityEngine', 'UserData', 'http', 'socket', 'ffi'
}
for _, name in ipairs(forbidden) do assert(_G[name] == nil, name .. ' is exposed') end
assert(objects.file == nil and objects.http == nil and objects.process == nil)
assert(string.dump == nil)", completed, completed, ExpectedObjects: 0,
                Note: "Surface assertions complement the actual sentinel/HTTP operations below; they alone are not an escape proof."),

            new("actual_sentinel_read", $@"
local ok = pcall(function()
    local f = assert(io.open({sentinel}, 'rb'))
    local value = f:read('*a')
    f:close()
    return value
end)
assert(not ok, 'sentinel read unexpectedly succeeded')", completed, completed, ExpectedObjects: 0,
                Note: "Attempts an actual read of the task-owned sentinel; rejection is asserted in Lua."),

            new("actual_sentinel_write", $@"
local ok = pcall(function()
    local f = assert(io.open({sentinel}, 'wb'))
    f:write('UNEXPECTED_SCRIPT_WRITE')
    f:close()
end)
assert(not ok, 'sentinel write unexpectedly succeeded')", completed, completed, ExpectedObjects: 0,
                Note: "Only the task-owned sentinel is targeted. The parent independently checks its exact bytes after worker exit."),

            new("actual_sentinel_delete", $@"
assert(not pcall(function() return os.remove({sentinel}) end), 'os.remove is callable')
assert(not pcall(function() return System.IO.File.Delete({sentinel}) end), 'CLR deletion is callable')", completed, completed,
                ExpectedObjects: 0,
                Note: "Only the task-owned sentinel is targeted. The parent checks existence after success, crash or watchdog kill."),

            new("actual_process_start", @"
assert(not pcall(function()
    return os.execute('cmd.exe /d /c exit 0')
end), 'os.execute is callable')
assert(not pcall(function()
    return luanet.import_type('System.Diagnostics.Process').Start('cmd.exe', '/d /c exit 0')
end), 'CLR process launch is callable')", completed, completed, ExpectedObjects: 0,
                Note: "Both attempts use only a harmless immediately exiting command; no shell capability should be exposed."),

            new("actual_loopback_http", $@"
assert(not pcall(function() return require('socket.http').request({loopback}) end))
assert(not pcall(function() return http.get({loopback}) end))
assert(not pcall(function()
    return UnityEngine.Networking.UnityWebRequest.Get({loopback}):SendWebRequest()
end))
assert(not pcall(function()
    return luanet.import_type('System.Net.WebClient')().DownloadString({loopback})
end))", completed, completed, ExpectedObjects: 0,
                Note: "Every actual request target is the task-owned loopback witness; the parent independently requires zero accepted requests."),

            new("actual_clr_and_module_loaders", $@"
assert(not pcall(function() return require('System.IO') end))
assert(not pcall(function() return loadfile({sentinel}) end))
assert(not pcall(function() return dofile({sentinel}) end))
assert(not pcall(function() return load('return 1') end))
assert(not pcall(function() return loadstring('return 1') end))
assert(not pcall(function()
    return luanet.import_type('System.IO.File').ReadAllText({sentinel})
end))
assert(not pcall(function() return System.IO.File.ReadAllText({sentinel}) end))
assert(not pcall(function() return clr.System.IO.File.ReadAllText({sentinel}) end))", completed, completed,
                ExpectedObjects: 0,
                Note: "Actual CLR/file/module/compiler entry points must be unreachable, including a loader path naming the sentinel."),

            new("infinite_loop", "while true do end", cpuLimit, cpuLimit, ExpectedObjects: 0,
                Note: "Ordinary VM bytecode must stop within the runtime's own CPU budget."),

            new("pcall_inner_infinite_loop", "pcall(function() while true do end end)",
                moonWatchdog, cpuLimit, ExpectedObjects: 0,
                Note: "pcall must not convert the host's runtime budget into an unlimited retry or unbounded call."),

            new("pcall_swallowed_host_errors", @"
while true do
    pcall(function() objects.set_label('forged', 'retry') end)
end", moonWatchdog, cpuLimit, ExpectedObjects: 0,
                Note: "A script can catch policy exceptions; CPU termination must remain controlled outside those exceptions."),

            new("reentrant_string_callback_loop", @"
while true do
    pcall(function()
        string.gsub('a', '.', function() while true do end end)
    end)
end", moonWatchdog, cpuLimit, ExpectedObjects: 0,
                Note: "A standard-library callback re-enters Lua; a parent kill demonstrates lack of runtime preemption rather than a successful VM limit."),

            new("single_allocation_above_vm_quota", @"
local value = string.rep('x', 8 * 1024 * 1024)
assert(#value == 8 * 1024 * 1024)", completed, new[] { "MemoryLimit" }, ExpectedObjects: 0,
                Note: "MoonSharp completion demonstrates the missing per-VM heap limit; Luau must reject this under the 4 MiB allocator quota."),

            new("pcall_swallowed_memory_errors", @"
while true do
    pcall(function()
        local value = string.rep('x', 8 * 1024 * 1024)
        assert(#value == 8 * 1024 * 1024)
    end)
end", new[] { "CpuLimit", "ProcessTimeout", "ProcessMemoryLimit", "WorkerMemoryFailure", "WorkerFailure" },
                new[] { "MemoryLimit" }, ExpectedObjects: 0,
                Note: "Luau allocation denial remains terminal even if pcall catches its Lua error; MoonSharp lacks the per-VM allocation boundary."),

            new("growing_string_table", @"
local values = {}
local seed = string.rep('x', 256 * 1024)
for i = 1, 1000000 do values[i] = seed .. tostring(i) end
assert(false, 'growth unexpectedly finished')", new[] { "ProcessMemoryLimit", "CpuLimit", "WorkerMemoryFailure", "WorkerFailure" },
                new[] { "MemoryLimit" }, ExpectedObjects: 0,
                Note: "Incremental growth avoids a single enormous process allocation. A MoonSharp process kill/crash is external containment, not a VM heap guarantee."),

            new("raw_bytecode_rejected", "\u001bLua\0\u0001\u0002", sourceRejected, sourceRejected,
                ExpectedObjects: 0, Note: "Only reviewed source text may enter either compiler/interpreter."),

            new("moonsharp_dump_rejected", "MoonSharp_dump_b64::AA==", sourceRejected, sourceRejected,
                ExpectedObjects: 0, Note: "MoonSharp LoadString also accepts encoded binary dumps; the shared source gate rejects that input for both runtimes."),

            new("source_byte_limit", "--" + new string('x', 64 * 1024), sourceRejected, sourceRejected,
                ExpectedObjects: 0, Note: "The package source-byte gate runs before compilation or VM execution."),

            new("malformed_source", "local = function( end", scriptError, scriptError,
                ExpectedObjects: 0, Note: "Compiler/parser errors must be reported without activating any staged objects."),

            new("deeply_nested_parser_error", "return " + new string('(', 256) + "1" + new string(')', 255),
                scriptError, scriptError, ExpectedObjects: 0,
                Note: "A bounded nested expression with a missing closing token exercises parsing before runtime instruction hooks; the parent watchdog covers that phase.")
        };
    }

    private static string LuaString(string value)
    {
        var result = new StringBuilder("\"");
        foreach (byte item in Encoding.UTF8.GetBytes(value))
        {
            // Three decimal digits avoid consuming a following digit as part of an escape.
            if (item >= 32 && item <= 126 && item != (byte)'"' && item != (byte)'\\')
                result.Append((char)item);
            else
                result.Append('\\').Append(item.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        }
        return result.Append('"').ToString();
    }
}
