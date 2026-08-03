using System;

namespace Anomaly.Client.Api.Modding;

/// <summary>Lifecycle contract for modules owned by the Anomaly runtime.</summary>
public interface IAnomalyMod : IDisposable
{
    void Initialize(IModContext context);
    void OnEnable();
    void OnDisable();
}
