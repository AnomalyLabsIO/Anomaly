using System;

namespace Anomaly.Client.Api.Scripting;

/// <summary>Bounds audited API work performed on behalf of a remote host operation.</summary>
/// <remarks>This is not a native allocator or a deadline for a single trusted native call.</remarks>
internal static class ScriptHostAccess
{
    internal const int MaximumNativeStringLength = 4096;
    internal const int MaximumHierarchySteps = 64;
    internal const int MaximumRegistryEntries = 512;
    internal const int MaximumWrapperCreations = 4;

    [ThreadStatic] private static int _depth;
    [ThreadStatic] private static int _hierarchySteps;
    [ThreadStatic] private static int _registryEntries;
    [ThreadStatic] private static int _wrapperCreations;
    [ThreadStatic] private static bool _failed;

    internal static bool IsActive => _depth != 0;

    // Nested capture/query scopes share the operation's counters and terminal failure.
    // A value-type lease avoids allocating for every remote property read.
    internal static Scope Enter()
    {
        if (_depth == 0)
        {
            _hierarchySteps = 0;
            _registryEntries = 0;
            _wrapperCreations = 0;
            _failed = false;
        }
        else
            ThrowIfFailed();
        return new Scope(++_depth, Environment.CurrentManagedThreadId);
    }

    internal static void CheckNativeStringLength(int length)
    {
        if (!IsActive) return;
        ThrowIfFailed();
        if (length < 0 || length > MaximumNativeStringLength)
            Fail("Native string length exceeds the remote host limit.");
    }

    internal static void ChargeHierarchyStep() => Charge(ref _hierarchySteps, 1, MaximumHierarchySteps);
    internal static void ChargeRegistryEntries(int count = 1) => Charge(ref _registryEntries, count, MaximumRegistryEntries);
    internal static void ChargeWrapperCreation() => Charge(ref _wrapperCreations, 1, MaximumWrapperCreations);

    internal static void ThrowIfFailed()
    {
        if (IsActive && _failed)
            throw new ScriptHostAccessException("The remote host operation has exhausted its native work budget.");
    }

    private static void Charge(ref int used, int count, int maximum)
    {
        if (!IsActive) return;
        ThrowIfFailed();
        if (count < 0 || count > maximum - used)
            Fail("The remote host operation exceeds its native work budget.");
        used += count;
    }

    private static void Fail(string message)
    {
        _failed = true;
        throw new ScriptHostAccessException(message);
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly int _level;
        private readonly int _thread;

        internal Scope(int level, int thread)
        {
            _level = level;
            _thread = thread;
        }

        public void Dispose()
        {
            if (_thread != Environment.CurrentManagedThreadId || _level != _depth)
                throw new InvalidOperationException("Native host access scopes must be disposed once in thread-local stack order.");
            --_depth;
        }
    }
}

internal sealed class ScriptHostAccessException : Exception
{
    internal ScriptHostAccessException(string message) : base(message) { }
}
