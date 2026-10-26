namespace Anomaly.ScriptSandboxPrototype;

internal static class HostContractChecks
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        var limits = new SandboxLimits();
        var first = new SandboxedObjectApi(limits);
        var second = new SandboxedObjectApi(limits);
        var handle = first.Spawn("demo/cube");
        var other = second.Spawn("demo/panel");
        MustReject(() => second.SetPosition(handle, 1, 2, 3));
        Require(second.Snapshot.Single().Handle == other && second.Snapshot.Single().X == 0, "foreign mutation changed second scope");
        passed.Add("real foreign scope handle rejected without mutation");

        first.SetPosition(handle, 1, 2, 3);
        var before = first.Snapshot.Single();
        MustReject(() => first.SetPosition(handle, double.NaN, 0, 0));
        MustReject(() => first.SetPosition(handle, 0, double.PositiveInfinity, 0));
        MustReject(() => first.SetPosition(handle, limits.MaxCoordinate + 1, 0, 0));
        MustReject(() => first.SetLabel(handle, new string('é', limits.MaxLabelLength)));
        Require(first.Snapshot.Single() == before, "invalid argument caused partial mutation");
        passed.Add("finite bounds and UTF-8 limits reject atomically");

        first.Destroy(handle);
        MustReject(() => first.Destroy(handle));
        MustReject(() => first.SetPosition(handle, 0, 0, 0));
        passed.Add("destroyed handle cannot be reused");
        var priorGenerationHandle = first.Spawn("demo/cube");
        first.Revoke();
        MustReject(() => first.SetPosition(priorGenerationHandle, 0, 0, 0));
        MustReject(() => first.Spawn("demo/cube"));
        Require(first.ObjectCount == 0, "revocation retained objects");
        passed.Add("revoked scope clears objects and rejects stale callbacks");

        var objectLimited = new SandboxedObjectApi(limits with { MaxObjects = 1 });
        objectLimited.Spawn("demo/cube");
        MustReject(() => objectLimited.Spawn("demo/cube"));
        Require(objectLimited.ObjectCount == 1, "object limit partially spawned");
        var callLimited = new SandboxedObjectApi(limits with { MaxCommands = 2 });
        MustReject(() => callLimited.Destroy("forged"));
        MustReject(() => callLimited.Destroy("forged"));
        MustReject(() => callLimited.Spawn("demo/cube"));
        Require(callLimited.ObjectCount == 0 && callLimited.Calls == 3, "invalid calls bypassed host-call budget");
        passed.Add("object and attempted host-call budgets enforced");

        Require(SourcePolicy.Reject(new string(' ', limits.MaxSourceBytes), limits) == null, "exact source boundary rejected");
        Require(SourcePolicy.Reject(new string(' ', limits.MaxSourceBytes + 1), limits) != null, "source over-limit accepted");
        Require(SourcePolicy.Reject("MoonSharp_dump_b64::AA==", limits) != null, "binary dump accepted");
        passed.Add("source byte boundary and dump rejection enforced");
        return passed;
    }

    private static void MustReject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("host contract check expected a rejection");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
