using System.Reflection;
using System.Reflection.Emit;
using Anomaly.Bootstrap;
using HarmonyLib;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class PatchPerformanceDiagnosticsTests
{
    private static readonly MethodInfo Target = Method(nameof(TargetFixture));
    private static readonly MethodInfo OtherTarget = Method(nameof(OtherTargetFixture));
    private static readonly MethodInfo Patch = Method(nameof(PatchFixture));
    private static readonly MethodInfo OtherPatch = Method(nameof(OtherPatchFixture));
    private static readonly (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] Empty = Array.Empty<(MethodInfo, HarmonyPatchType, string, int)>();

    [Fact]
    public void Duplicate_requires_exact_active_method_kind_target_and_owner_and_warns_once()
    {
        var tracker = new PatchPerformanceTracker();
        var one = Entries("owner");
        tracker.ObserveCompletedOperation(Target, Empty, one, true, 0);
        tracker.ObserveCompletedOperation(Target, one,
            one.Concat(Entries("other-owner")).Concat(Entries("owner", HarmonyPatchType.Postfix))
                .Concat(Entries("owner", method: OtherPatch)).ToArray(), true, 1);
        tracker.ObserveCompletedOperation(OtherTarget, Empty, one, true, 2);
        Assert.False(tracker.TryDequeueWarning(out _));

        var duplicate = one.Concat(Entries("owner", index: 1)).ToArray();
        tracker.ObserveCompletedOperation(Target, one, duplicate, true, 3);
        Assert.True(tracker.TryDequeueWarning(out string warning));
        Assert.Contains("2 active registrations", warning);
        Assert.Contains("owner 'owner'", warning);
        Assert.Contains(nameof(TargetFixture), warning);
        Assert.Contains(nameof(PatchFixture), warning);
        tracker.ObserveCompletedOperation(Target, duplicate, duplicate, true, 4);
        tracker.ObserveCompletedOperation(Target, duplicate, Empty, false, 5);
        tracker.ObserveCompletedOperation(Target, Empty, duplicate, true, 6);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Three_observed_installations_after_removals_warn_but_one_replacement_does_not()
    {
        var tracker = new PatchPerformanceTracker();
        var patch = Entries("owner");
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 0);
        tracker.ObserveCompletedOperation(Target, patch, Empty, false, 100);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 200);
        Assert.False(tracker.TryDequeueWarning(out _));
        tracker.ObserveCompletedOperation(Target, patch, Empty, false, 300);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 400);
        Assert.True(tracker.TryDequeueWarning(out string warning));
        Assert.Contains("installed 3 times in 5 minutes after observed removals", warning);
        Assert.Contains("may be intentional", warning);
    }

    [Fact]
    public void Churn_uses_rolling_clock_and_warning_suppression_window()
    {
        var tracker = new PatchPerformanceTracker();
        InstallCycle(tracker, "owner", 0);
        InstallCycle(tracker, "owner", 200000);
        InstallCycle(tracker, "owner", 400000);
        Assert.False(tracker.TryDequeueWarning(out _));
        InstallCycle(tracker, "owner", 400100);
        Assert.True(tracker.TryDequeueWarning(out _));
        InstallCycle(tracker, "owner", 400200);
        Assert.False(tracker.TryDequeueWarning(out _));
        InstallCycle(tracker, "owner", 700100);
        InstallCycle(tracker, "owner", 700200);
        InstallCycle(tracker, "owner", 700300);
        Assert.True(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Nested_or_reordered_before_snapshots_do_not_count_same_install_twice()
    {
        var tracker = new PatchPerformanceTracker();
        var patch = Entries("owner");
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 0);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 1);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 2);
        Assert.False(tracker.TryDequeueWarning(out _));
        tracker.ObserveCompletedOperation(Target, patch, patch, true, 3);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Missing_short_lived_removal_is_safely_undercounted()
    {
        var tracker = new PatchPerformanceTracker();
        var patch = Entries("owner");
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 0);
        // Another install has completed before the removal finalizer observes
        // the target; the current authoritative metadata is still present.
        tracker.ObserveCompletedOperation(Target, patch, patch, false, 1);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 2);
        tracker.ObserveCompletedOperation(Target, patch, patch, false, 3);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, 4);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Unsuccessful_install_does_not_increment_churn_or_warn_about_duplicates()
    {
        var tracker = new PatchPerformanceTracker();
        var duplicate = Entries("owner").Concat(Entries("owner", index: 1)).ToArray();
        tracker.ObserveCompletedOperation(Target, Empty, duplicate, false, 0);
        Assert.False(tracker.TryDequeueWarning(out _));
        tracker.ObserveCompletedOperation(Target, duplicate, Empty, false, 1);
        InstallCycle(tracker, "owner", 2);
        InstallCycle(tracker, "owner", 4);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Owner_and_kind_have_independent_churn_history()
    {
        var tracker = new PatchPerformanceTracker();
        InstallCycle(tracker, "owner", 0);
        InstallCycle(tracker, "OWNER", 10);
        InstallCycle(tracker, "owner", 20, HarmonyPatchType.Postfix);
        Assert.False(tracker.TryDequeueWarning(out _));
        InstallCycle(tracker, "owner", 30);
        InstallCycle(tracker, "owner", 40);
        Assert.True(tracker.TryDequeueWarning(out string warning));
        Assert.Contains("owner 'owner'", warning);
        Assert.Contains("Prefix", warning);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Same_named_patch_methods_from_distinct_loaded_modules_are_not_duplicates()
    {
        var tracker = new PatchPerformanceTracker();
        var first = DynamicPatch("DiagnosticPatchFixture.First");
        var second = DynamicPatch("DiagnosticPatchFixture.Second");
        var firstEntry = Entries("owner", method: first);
        tracker.ObserveCompletedOperation(Target, Empty, firstEntry, true, 0);
        tracker.ObserveCompletedOperation(Target, firstEntry,
            firstEntry.Concat(Entries("owner", method: second)).ToArray(), true, 1);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Concurrent_observers_do_not_invent_reinstall_cycles_or_duplicate_warnings()
    {
        var tracker = new PatchPerformanceTracker();
        var patch = Entries("owner");
        Parallel.For(0, 64, i => tracker.ObserveCompletedOperation(Target, Empty, patch, true, i));
        Assert.False(tracker.TryDequeueWarning(out _));
        var duplicate = patch.Concat(Entries("owner", index: 1)).ToArray();
        Parallel.For(0, 64, i => tracker.ObserveCompletedOperation(Target, patch, duplicate, true, i + 100));
        Assert.True(tracker.TryDequeueWarning(out string warning));
        Assert.Contains("active registrations", warning);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Clock_rollback_cannot_manufacture_rapid_reinstall_warning()
    {
        var tracker = new PatchPerformanceTracker();
        InstallCycle(tracker, "owner", 1000);
        InstallCycle(tracker, "owner", 2000);
        InstallCycle(tracker, "owner", 500);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Capacity_and_queue_are_bounded_and_report_suppression()
    {
        var tracker = new PatchPerformanceTracker();
        for (int i = 0; i < PatchPerformanceTracker.MaximumTrackedTuples + 10; i++)
        {
            var one = Entries("owner-" + i);
            var duplicate = one.Concat(Entries("owner-" + i, index: 1)).ToArray();
            tracker.ObserveCompletedOperation(Target, Empty, duplicate, true, i);
        }
        Assert.Equal(PatchPerformanceTracker.MaximumTrackedTuples, tracker.TrackedTupleCount);
        Assert.Equal(PatchPerformanceTracker.MaximumQueuedWarnings, tracker.PendingWarningCount);
        for (int i = 0; i < PatchPerformanceTracker.MaximumQueuedWarnings; i++)
            Assert.True(tracker.TryDequeueWarning(out _));
        Assert.True(tracker.TryDequeueWarning(out string summary));
        Assert.Contains("additional warnings suppressed", summary);
        Assert.Contains("Tracking capacity reached", summary);
        Assert.False(tracker.TryDequeueWarning(out _));
        tracker.ObserveCompletedOperation(Target, Empty, Entries("later"), true, 5000);
        Assert.False(tracker.TryDequeueWarning(out _));
    }

    [Fact]
    public void Queue_captures_detached_text_and_sanitizes_owner_controls()
    {
        var tracker = new PatchPerformanceTracker();
        var duplicate = Entries("owner\r\nforged").Concat(Entries("owner\r\nforged", index: 1)).ToArray();
        tracker.ObserveCompletedOperation(Target, Empty, duplicate, true, 0);
        duplicate[0] = (OtherPatch, HarmonyPatchType.Postfix, "changed", 2);
        Assert.True(tracker.TryDequeueWarning(out string warning));
        Assert.DoesNotContain('\r', warning);
        Assert.DoesNotContain('\n', warning);
        Assert.Contains("owner  forged", warning);
        Assert.DoesNotContain("changed", warning);
        Assert.True(warning.Length < 1500);
    }

    [Fact]
    public void Diagnostic_observations_and_capacity_do_not_change_guard_audit()
    {
        var audit = new GuardAudit(false);
        audit.Ready();
        GuardSnapshot before = audit.Snapshot;
        var tracker = new PatchPerformanceTracker();
        InstallCycle(tracker, "owner", 0);
        InstallCycle(tracker, "owner", 2);
        InstallCycle(tracker, "owner", 4);
        Assert.True(tracker.TryDequeueWarning(out _));
        GuardSnapshot after = audit.Snapshot;
        Assert.Equal(before.Flags, after.Flags);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(before.Healthy, after.Healthy);
        Assert.Equal(before.MutationInProgress, after.MutationInProgress);
    }

    [Fact]
    public void Installed_processor_owner_and_class_completion_fields_match_the_supported_contract()
    {
        // Metadata inspection only: constructing Harmony runs old MonoMod hooks,
        // which require the separate net6 probe rather than this net8 testhost.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        var directOwner = typeof(PatchProcessor).GetField("instance", flags);
        var classOwner = typeof(PatchClassProcessor).GetField("instance", flags);
        Assert.NotNull(directOwner);
        Assert.NotNull(classOwner);
        Assert.Equal(typeof(HarmonyLib.Harmony), directOwner!.FieldType);
        Assert.Equal(typeof(HarmonyLib.Harmony), classOwner!.FieldType);
        Assert.False(directOwner.IsStatic);
        Assert.False(classOwner.IsStatic);

        var classOperation = typeof(PatchClassProcessor).GetMethod("ProcessPatchJob", flags);
        Assert.NotNull(classOperation);
        var jobType = Assert.Single(classOperation!.GetParameters()).ParameterType;
        var replacement = jobType.GetField("replacement", flags | BindingFlags.Public);
        Assert.NotNull(replacement);
        Assert.Equal(typeof(MethodInfo), replacement!.FieldType);
    }

    private static void InstallCycle(PatchPerformanceTracker tracker, string owner, long now,
        HarmonyPatchType kind = HarmonyPatchType.Prefix)
    {
        var patch = Entries(owner, kind);
        tracker.ObserveCompletedOperation(Target, Empty, patch, true, now);
        tracker.ObserveCompletedOperation(Target, patch, Empty, false, now + 1);
    }

    private static (MethodInfo Method, HarmonyPatchType Kind, string Owner, int Index)[] Entries(string owner,
        HarmonyPatchType kind = HarmonyPatchType.Prefix, MethodInfo? method = null, int index = 0) =>
        new[] { (method ?? Patch, kind, owner, index) };

    private static MethodInfo Method(string name) => typeof(PatchPerformanceDiagnosticsTests)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static MethodInfo DynamicPatch(string assemblyName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("patch-module").DefineType("Fixture.Patch", TypeAttributes.Public);
        var method = type.DefineMethod("Prefix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Prefix")!;
    }

    private static void TargetFixture() { }
    private static void OtherTargetFixture() { }
    private static void PatchFixture() { }
    private static void OtherPatchFixture() { }
}
