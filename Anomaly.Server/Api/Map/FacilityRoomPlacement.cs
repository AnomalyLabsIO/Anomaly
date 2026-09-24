using System;
using System.Threading;
using MapGeneration;
using UnityEngine;

namespace Anomaly.Server.Api.Map;

/// <summary>A selected facility room whose final placement can be edited during LayoutFinalizing.</summary>
/// <remarks>
/// Descriptors contain no live room objects. Setters are available only on the event's server
/// thread until all handlers return. Later handlers see earlier edits; the last assignment wins.
/// </remarks>
public sealed class FacilityRoomPlacement
{
    private readonly int _authoringThreadId;
    private bool _active = true;
    private Vector3 _position;
    private float _rotationY;
    private bool _isRemoved;

    internal FacilityRoomPlacement(FacilityZone zone, Vector2Int coords, RoomName name,
        RoomShape shape, Vector3 position, float yaw, int authoringThreadId)
    {
        ValidatePosition(position);
        ValidateFinite(yaw, nameof(yaw));
        if (authoringThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(authoringThreadId));
        Zone = zone;
        Coords = coords;
        Name = name;
        Shape = shape;
        OriginalPosition = _position = position;
        OriginalRotationY = _rotationY = yaw;
        _authoringThreadId = authoringThreadId;
    }

    /// <summary>The room's atlas zone, unchanged by final placement edits.</summary>
    public FacilityZone Zone { get; }
    /// <summary>The room's stable atlas coordinate, unchanged by moving the room.</summary>
    public Vector2Int Coords { get; }
    /// <summary>The selected vanilla room type.</summary>
    public RoomName Name { get; }
    /// <summary>The selected room's shape.</summary>
    public RoomShape Shape { get; }
    /// <summary>The world position at entry to LayoutFinalizing, including earlier recipe edits.</summary>
    public Vector3 OriginalPosition { get; }
    /// <summary>The absolute world Y rotation at entry to LayoutFinalizing, in degrees.</summary>
    public float OriginalRotationY { get; }

    /// <summary>The final absolute world position. All components must be finite.</summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            RequireAuthoring();
            ValidatePosition(value);
            _position = value;
        }
    }

    /// <summary>The final absolute world Y rotation in degrees. Must be finite.</summary>
    public float RotationY
    {
        get => _rotationY;
        set
        {
            RequireAuthoring();
            ValidateFinite(value, nameof(value));
            _rotationY = value;
        }
    }

    /// <summary>Whether to omit this room and its vanilla contents from the final facility.</summary>
    /// <remarks>A later handler can set this back to false before the event ends.</remarks>
    public bool IsRemoved
    {
        get => _isRemoved;
        set
        {
            RequireAuthoring();
            _isRemoved = value;
        }
    }

    internal void Seal() => _active = false;

    private void RequireAuthoring()
    {
        if (Thread.CurrentThread.ManagedThreadId != _authoringThreadId)
            throw new InvalidOperationException("Facility placement edits must run on the LayoutFinalizing event's server thread.");
        if (!_active)
            throw new InvalidOperationException("Facility placement edits are allowed only while LayoutFinalizing handlers are running.");
    }

    private static void ValidatePosition(Vector3 position)
    {
        ValidateFinite(position.x, nameof(position));
        ValidateFinite(position.y, nameof(position));
        ValidateFinite(position.z, nameof(position));
    }

    private static void ValidateFinite(float value, string parameter)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(parameter, "Facility placement values must be finite.");
    }
}
