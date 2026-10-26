using System.Runtime.InteropServices;
using System.Text;

namespace Anomaly.ScriptSandboxPrototype;

internal sealed class LuauRuntime : IScriptRuntime
{
    private static readonly string[] Outcomes = { "Completed", "ScriptError", "CpuLimit", "MemoryLimit", "SourceRejected", "NativeMemoryFailure" };

    public RuntimeExecution Execute(string source, SandboxedObjectApi api, SandboxLimits limits)
    {
        var rejection = SourcePolicy.Reject(source, limits);
        if (rejection is not null)
            return new RuntimeExecution("SourceRejected", rejection);

        if (Native.AbiVersion() != 1)
            throw new InvalidOperationException("The Luau prototype native ABI does not match this adapter.");

        // Only primitives cross this bridge. The callback never invokes Lua,
        // raises native errors, or allows a managed exception to escape.
        HostCallback callback = (IntPtr _, int operation, IntPtr first, nuint firstLength,
            IntPtr second, nuint secondLength, double x, double y, double z,
            IntPtr output, nuint capacity, out nuint outputLength) =>
        {
            outputLength = 0;
            try
            {
                if (firstLength > 4096 || secondLength > 4096)
                    throw new InvalidOperationException("Native host string byte limit exceeded.");
                var a = Marshal.PtrToStringUTF8(first, checked((int)firstLength)) ?? string.Empty;
                var b = Marshal.PtrToStringUTF8(second, checked((int)secondLength)) ?? string.Empty;
                var result = string.Empty;
                switch (operation)
                {
                    case 1: result = api.Spawn(a); break;
                    case 2: api.SetPosition(a, x, y, z); break;
                    case 3: api.SetLabel(a, b); break;
                    case 4: api.Destroy(a); break;
                    default: throw new InvalidOperationException("Unknown object command.");
                }
                WriteResponse(output, capacity, result, out outputLength);
                return 0;
            }
            catch (Exception error)
            {
                // Error responses are fixed-size and do not include arbitrary
                // script data or managed stack traces. Native raises afterwards.
                try { WriteResponse(output, capacity, error.Message, out outputLength); }
                catch { outputLength = 0; }
                return 1;
            }
        };

        var utf8 = Encoding.UTF8.GetBytes(source);
        var message = new byte[2048];
        int outcome;
        ulong peak;
        int resumes;
        try
        {
            outcome = Native.Execute(utf8, (nuint)utf8.Length, checked((nuint)limits.MaxSourceBytes),
                callback, IntPtr.Zero, checked((ulong)limits.VmMemoryLimitBytes), checked((ulong)limits.InstructionSlice),
                limits.MaxResumes, limits.ExecutionTimeoutMs, limits.MaxCommands, out peak, out resumes,
                message, (nuint)message.Length);
        }
        finally
        {
            GC.KeepAlive(callback);
        }
        var end = Array.IndexOf(message, (byte)0);
        var text = Encoding.UTF8.GetString(message, 0, end < 0 ? message.Length : end);
        if ((uint)outcome >= Outcomes.Length)
            throw new InvalidOperationException($"Native Luau returned unknown outcome {outcome}.");
        return new RuntimeExecution(Outcomes[outcome], text, checked((long)peak), resumes);
    }

    private static void WriteResponse(IntPtr output, nuint capacity, string value, out nuint length)
    {
        // Bound bytes before allocation, and reserve a native terminator slot.
        var text = value.Length > 200 ? value[..200] : value;
        var bytes = Encoding.UTF8.GetBytes(text);
        var count = Math.Min(bytes.Length, checked((int)capacity) - 1);
        if (count < 0) throw new InvalidOperationException("Invalid response buffer.");
        Marshal.Copy(bytes, 0, output, count);
        Marshal.WriteByte(output, count, 0);
        length = (nuint)count;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HostCallback(IntPtr context, int operation, IntPtr first, nuint firstLength,
        IntPtr second, nuint secondLength, double x, double y, double z,
        IntPtr output, nuint capacity, out nuint outputLength);

    private static class Native
    {
        [DllImport("luau_sandbox", EntryPoint = "al_abi_version", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint AbiVersion();

        [DllImport("luau_sandbox", EntryPoint = "al_execute", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Execute(byte[] source, nuint sourceSize, nuint maxSourceSize,
            HostCallback callback, IntPtr context, ulong memoryLimit, ulong instructionSlice,
            int maxResumes, int timeoutMs, int maxCommands, out ulong peak, out int resumes,
            [Out] byte[] message, nuint messageCapacity);
    }
}
