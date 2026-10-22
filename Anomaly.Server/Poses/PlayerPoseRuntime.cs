using System;
using System.Runtime.CompilerServices;
using Anomaly.Server.Api.Features;
using Mirror;

namespace Anomaly.Server.Poses;

/// <summary>Shares native lifecycle observations and traffic capacity, never pose ownership, across authorities.</summary>
internal static class PlayerPoseRuntime
{
    internal static readonly PlayerPoseService Cosmetic = new PlayerPoseService(false);
    internal static readonly PlayerPoseService Combat = new PlayerPoseService(true);
    private static ConditionalWeakTable<NetworkConnectionToClient, PoseTrafficBudget> Budgets = new ConditionalWeakTable<NetworkConnectionToClient, PoseTrafficBudget>();
    internal static PoseTrafficBudget GetBudget(NetworkConnectionToClient connection)
    {
        return connection == null ? new PoseTrafficBudget() : Budgets.GetValue(connection, _ => new PoseTrafficBudget());
    }
    internal static void Init() { Combat.Init(); Cosmetic.Init(); }
    internal static void Disable()
    {
        try { Both(service => service.Disable()); }
        finally { Budgets = new ConditionalWeakTable<NetworkConnectionToClient, PoseTrafficBudget>(); }
    }
    private static void Both(Action<PlayerPoseService> action)
    {
        Exception first = null;
        try { action(Cosmetic); } catch (Exception exception) { first = exception; }
        try { action(Combat); }
        catch (Exception exception) { if (first != null) throw new AggregateException(first, exception); throw; }
        if (first != null) throw first;
    }
    internal static void RequireGameThread() => Cosmetic.RequireGameThread();
    internal static PlayerPoseScope CreateScope(string ownerId) => Cosmetic.CreateScope(ownerId);
    internal static void ModelInitialized(ReferenceHub hub) => Both(service => service.ModelInitialized(hub));
    internal static void RoleDelivered(uint target, uint recipient, sbyte role, int life)
    => Both(service => service.RoleDelivered(target, recipient, role, life));
    internal static void BeginRoleDelivery() { Cosmetic.BeginRoleDelivery(); Combat.BeginRoleDelivery(); }
    internal static void EndRoleDelivery() => Both(service => service.EndRoleDelivery());
    internal static void ObserveVisibility(ReferenceHub recipient, ReferenceHub target, bool visible)
    => Both(service => service.ObserveVisibility(recipient, target, visible));
    internal static void RootDiscontinuity(ReferenceHub hub) => Both(service => service.RootDiscontinuity(hub));
    internal static void RemovePlayer(ReferenceHub hub)
    {
        try { Both(service => service.RemovePlayer(hub)); }
        finally { if (hub?.connectionToClient != null) Budgets.Remove(hub.connectionToClient); }
    }
}
