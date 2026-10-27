using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Events;

/// <summary>Publication ownership without requiring an interpreter in the publishing process.</summary>
internal interface IScriptEventPublicationOwner
{
    bool IsUnavailable { get; }
    bool RequiresNativeScope { get; }
    bool TryReservePublication(ScriptEventRegistration registration, out MemoryBudget.AllocationReservation reservation);
    void Publish(ScriptEventRegistration registration, object?[] arguments, object allocationOwner);
    void RequestRetirement(string reason = "The event publisher violated its owner-thread contract; session retired.", string? eventName = null);
    void EnsureOwner(Script script);
}
