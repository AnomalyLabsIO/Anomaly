using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class PoseHarmonyCompatibilityTests
{
    private static int _enqueued;
    private static int _committed;

    [Fact]
    public void InstalledHarmonyReportsWhenAnotherPrefixSkipsTheNativeEnqueue()
    {
        var harmony = new Harmony("anomaly.tests.pose.originalguard");
        MethodInfo native = AccessTools.Method(typeof(PoseHarmonyCompatibilityTests), nameof(NativeEnqueue));
        _enqueued = 0; _committed = 0;
        try
        {
            harmony.Patch(native,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(PoseHarmonyCompatibilityTests), nameof(CompanionPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(PoseHarmonyCompatibilityTests), nameof(CommitPostfix))));
            NativeEnqueue(true);
            Assert.Equal(0, _enqueued);
            Assert.Equal(0, _committed);
            NativeEnqueue(false);
            Assert.Equal(1, _enqueued);
            Assert.Equal(1, _committed);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NativeEnqueue(bool skip) => _enqueued++;
    private static bool CompanionPrefix(bool skip) => !skip;
    private static void CommitPostfix(bool __runOriginal) { if (__runOriginal) _committed++; }
}
