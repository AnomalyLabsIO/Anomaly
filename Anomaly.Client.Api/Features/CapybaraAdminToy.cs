using Il2CppAdminToys;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed CapybaraToy state.</summary>
[ScriptType]
public class CapybaraAdminToy : AdminToy
{
    internal CapybaraAdminToy(CapybaraToy toy) : base(toy) => CapybaraBase = toy;

    public CapybaraToy CapybaraBase { get; }
    public bool CollisionsEnabled => CapybaraBase.CollisionsEnabled;
}
