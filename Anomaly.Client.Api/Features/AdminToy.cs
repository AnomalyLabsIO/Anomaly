using System.Collections.ObjectModel;
using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

public class AdminToy
{
    private static readonly Dictionary<AdminToyBase, AdminToy> _toys = new();

    protected AdminToy(AdminToyBase toyBase)
    {
        Base = toyBase;
    }

    public AdminToyBase Base { get; }

    public string CommandName => Base.CommandName;

    public Vector3 Position => Base.Position;

    public Quaternion Rotation => Base.Rotation;

    public Vector3 Scale => Base.Scale;

    public byte MovementSmoothing => Base.MovementSmoothing;

    public bool IsStatic => Base.IsStatic;

    public GameObject GameObject => Base.gameObject;

    public Transform Transform => Base.CachedTransform;

    public static ReadOnlyCollection<AdminToy> List
    {
        get
        {
            List<AdminToy> toys = new();
            foreach (AdminToyBase toyBase in UnityEngine.Object.FindObjectsOfType<AdminToyBase>())
            {
                AdminToy toy = Get(toyBase);
                if (toy != null)
                    toys.Add(toy);
            }

            return toys.AsReadOnly();
        }
    }

    public static AdminToy Get(AdminToyBase toyBase)
    {
        if (toyBase == null)
            return null;

        if (_toys.TryGetValue(toyBase, out AdminToy toy))
            return toy;

        toy = CreateWrapper(toyBase);
        _toys[toyBase] = toy;
        return toy;
    }

    private static AdminToy CreateWrapper(AdminToyBase toyBase)
    {
        if (toyBase is PrimitiveObjectToy primitiveObjectToy)
            return new PrimitiveAdminToy(primitiveObjectToy);

        if (toyBase is LightSourceToy lightSourceToy)
            return new LightAdminToy(lightSourceToy);

        if (toyBase is SpeakerToy speakerToy)
            return new SpeakerAdminToy(speakerToy);

        if (toyBase is ShootingTarget shootingTarget)
            return new ShootingTargetAdminToy(shootingTarget);

        return new AdminToy(toyBase);
    }

    internal static bool Unregister(AdminToyBase toyBase) =>
        toyBase != null && _toys.Remove(toyBase);

    internal static void ClearAll() => _toys.Clear();

    public override string ToString() => $"AdminToy({CommandName})";
}
