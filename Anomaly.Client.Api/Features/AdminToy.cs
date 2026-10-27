using System.Collections.ObjectModel;
using Il2CppAdminToys;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class AdminToy
{
    private static readonly Dictionary<AdminToyBase, AdminToy> _toys = new();

    protected AdminToy(AdminToyBase toyBase)
    {
        Base = toyBase;
    }

    public AdminToyBase Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null;

    public T As<T>() where T : AdminToy => this as T;
    public bool Is<T>() where T : AdminToy => this is T;

    public static bool TryGet(AdminToyBase toyBase, out AdminToy toy)
    {
        toy = Get(toyBase);
        return toy != null;
    }

    [ScriptVisible]
    public string CommandName => Base.CommandName;

    public Vector3 Position => Base.Position;

    public Quaternion Rotation => Base.Rotation;

    public Vector3 Scale => Base.Scale;

    [ScriptVisible]
    public byte MovementSmoothing => Base.MovementSmoothing;

    [ScriptVisible]
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

        ScriptHostAccess.ChargeRegistryEntries(_toys.Count);
        ScriptHostAccess.ChargeWrapperCreation();
        toy = CreateWrapper(toyBase);
        _toys[toyBase] = toy;
        return toy;
    }

    private static AdminToy CreateWrapper(AdminToyBase toyBase)
    {
        if (toyBase.TryCast<PrimitiveObjectToy>() is { } primitiveObjectToy)
            return new PrimitiveAdminToy(primitiveObjectToy);

        if (toyBase.TryCast<LightSourceToy>() is { } lightSourceToy)
            return new LightAdminToy(lightSourceToy);

        if (toyBase.TryCast<SpeakerToy>() is { } speakerToy)
            return new SpeakerAdminToy(speakerToy);

        if (toyBase.TryCast<ShootingTarget>() is { } shootingTarget)
            return new ShootingTargetAdminToy(shootingTarget);

        if (toyBase.TryCast<TextToy>() is { } text)
            return new TextAdminToy(text);
        if (toyBase.TryCast<InvisibleInteractableToy>() is { } interactable)
            return new InteractableAdminToy(interactable);
        if (toyBase.TryCast<Scp079CameraToy>() is { } camera)
            return new CameraAdminToy(camera);
        if (toyBase.TryCast<WaypointToy>() is { } waypoint)
            return new WaypointAdminToy(waypoint);
        if (toyBase.TryCast<CapybaraToy>() is { } capybara)
            return new CapybaraAdminToy(capybara);

        return new AdminToy(toyBase);
    }

    internal static bool Unregister(AdminToyBase toyBase) =>
        toyBase != null && _toys.Remove(toyBase);

    internal static void ClearAll() => _toys.Clear();

    public override string ToString() => $"AdminToy({CommandName})";
}
