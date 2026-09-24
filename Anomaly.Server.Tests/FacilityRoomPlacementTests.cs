using System.Threading;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Map;
using MapGeneration;
using UnityEngine;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class FacilityRoomPlacementTests
{
    [Fact]
    public void SealingRejectsEveryMutationAndKeepsFinalValuesReadable()
    {
        var room = CreateRoom();
        room.Position = new Vector3(30f, 100f, 45f);
        room.RotationY = 180f;
        room.IsRemoved = true;
        room.Seal();
        room.Seal();

        Assert.Throws<InvalidOperationException>(() => room.Position = new Vector3(0f, 0f, 0f));
        Assert.Throws<InvalidOperationException>(() => room.RotationY = 0f);
        Assert.Throws<InvalidOperationException>(() => room.IsRemoved = false);
        Assert.Equal(new Vector3(30f, 100f, 45f), room.Position);
        Assert.Equal(180f, room.RotationY);
        Assert.True(room.IsRemoved);
        Assert.Equal(new Vector3(15f, 100f, 30f), room.OriginalPosition);
        Assert.Equal(90f, room.OriginalRotationY);
    }

    [Fact]
    public void OtherThreadCannotMutateAnActiveDescriptor()
    {
        var room = CreateRoom();
        var failures = new Exception[3];
        var worker = new Thread(() =>
        {
            failures[0] = Record.Exception(() => room.Position = new Vector3(0f, 0f, 0f));
            failures[1] = Record.Exception(() => room.RotationY = 0f);
            failures[2] = Record.Exception(() => room.IsRemoved = true);
        });
        worker.IsBackground = true;
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Placement worker did not complete.");

        foreach (var failure in failures) Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(room.OriginalPosition, room.Position);
        Assert.Equal(room.OriginalRotationY, room.RotationY);
        Assert.False(room.IsRemoved);
        room.IsRemoved = true;
        Assert.True(room.IsRemoved);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.NaN, 0f)]
    [InlineData(0f, 0f, float.NaN)]
    [InlineData(float.PositiveInfinity, 0f, 0f)]
    [InlineData(0f, float.NegativeInfinity, 0f)]
    [InlineData(0f, 0f, float.PositiveInfinity)]
    public void InvalidPositionDoesNotReplaceAnEarlierValidEdit(float x, float y, float z)
    {
        var room = CreateRoom();
        var validPosition = new Vector3(60f, 100f, 30f);
        room.Position = validPosition;
        Assert.Throws<ArgumentOutOfRangeException>(() => room.Position = new Vector3(x, y, z));
        Assert.Equal(validPosition, room.Position);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidRotationDoesNotReplaceAnEarlierValidEdit(float value)
    {
        var room = CreateRoom();
        room.RotationY = 450f;
        Assert.Throws<ArgumentOutOfRangeException>(() => room.RotationY = value);
        Assert.Equal(450f, room.RotationY);
    }

    [Fact]
    public void EventCollectionCopiesMembershipButSharesGuardedDescriptors()
    {
        var room = CreateRoom();
        var source = new List<FacilityRoomPlacement> { room };
        var args = new FacilityLayoutFinalizingEventArgs(42, 12345, source);
        source.Clear();

        Assert.Equal(42UL, args.GenerationId);
        Assert.Equal(12345, args.Seed);
        Assert.Same(room, Assert.Single(args.Rooms));
        Assert.Throws<NotSupportedException>(() => ((IList<FacilityRoomPlacement>)args.Rooms).Clear());
        room.IsRemoved = true;
        Assert.True(args.Rooms[0].IsRemoved);
        room.Seal();
        Assert.Throws<InvalidOperationException>(() => args.Rooms[0].IsRemoved = false);
    }

    [Fact]
    public void EventCollectionRejectsMissingRooms()
    {
        Assert.Throws<ArgumentNullException>(() => new FacilityLayoutFinalizingEventArgs(1, 1, null));
        Assert.Throws<ArgumentException>(() =>
            new FacilityLayoutFinalizingEventArgs(1, 1, new FacilityRoomPlacement[] { null }));
    }

    private static FacilityRoomPlacement CreateRoom() => new(FacilityZone.LightContainment,
        new Vector2Int(1, 2), RoomName.LczArmory, RoomShape.Straight,
        new Vector3(15f, 100f, 30f), 90f, Thread.CurrentThread.ManagedThreadId);
}
