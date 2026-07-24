using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public class AndroidTouchInputQueueTests
{
    [Fact]
    public void RepeatedMovesForPointer_KeepOnlyLatestPacket()
    {
        var queue = new AndroidTouchInputQueue();

        queue.Enqueue(Touch(pointerId: 7, action: 2, x: 10, eventTimeMillis: 100));
        queue.Enqueue(Touch(pointerId: 7, action: 2, x: 20, eventTimeMillis: 108));

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out AndroidTouchInput packet));
        Assert.Equal(7, packet.PointerId);
        Assert.Equal(20, packet.X);
        Assert.Equal(108, packet.EventTimeMillis);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void StateTransitions_AreOrderedBarriersForMoveCoalescing()
    {
        var queue = new AndroidTouchInputQueue();

        queue.Enqueue(Touch(pointerId: 1, action: 0, eventTimeMillis: 1));
        queue.Enqueue(Touch(pointerId: 1, action: 2, x: 10, eventTimeMillis: 2));
        queue.Enqueue(Touch(pointerId: 1, action: 2, x: 20, eventTimeMillis: 3));
        queue.Enqueue(Touch(pointerId: 2, action: 0, eventTimeMillis: 4));
        queue.Enqueue(Touch(pointerId: 1, action: 2, x: 30, eventTimeMillis: 5));
        queue.Enqueue(Touch(pointerId: 2, action: 2, x: 40, eventTimeMillis: 6));
        queue.Enqueue(Touch(pointerId: 1, action: 2, x: 35, eventTimeMillis: 7));
        queue.Enqueue(Touch(pointerId: 1, action: 1, eventTimeMillis: 8));
        queue.Enqueue(Touch(pointerId: 2, action: 2, x: 50, eventTimeMillis: 9));

        AndroidTouchInput[] packets = Drain(queue);

        Assert.Collection(
            packets,
            packet => AssertPacket(packet, pointerId: 1, action: 0, x: 0, time: 1),
            packet => AssertPacket(packet, pointerId: 1, action: 2, x: 20, time: 3),
            packet => AssertPacket(packet, pointerId: 2, action: 0, x: 0, time: 4),
            packet => AssertPacket(packet, pointerId: 1, action: 2, x: 35, time: 7),
            packet => AssertPacket(packet, pointerId: 2, action: 2, x: 40, time: 6),
            packet => AssertPacket(packet, pointerId: 1, action: 1, x: 0, time: 8),
            packet => AssertPacket(packet, pointerId: 2, action: 2, x: 50, time: 9));
    }

    [Fact]
    public void Cancel_PreservesLatestMoveBeforeIt_AndClearDropsAllPackets()
    {
        var queue = new AndroidTouchInputQueue();

        queue.Enqueue(Touch(pointerId: 3, action: 2, x: 12, eventTimeMillis: 10));
        queue.Enqueue(Touch(pointerId: 3, action: 2, x: 18, eventTimeMillis: 11));
        queue.Enqueue(Touch(pointerId: 3, action: 3, x: 18, eventTimeMillis: 12));

        Assert.True(queue.TryDequeue(out AndroidTouchInput move));
        AssertPacket(move, pointerId: 3, action: 2, x: 18, time: 11);
        Assert.True(queue.TryDequeue(out AndroidTouchInput cancel));
        AssertPacket(cancel, pointerId: 3, action: 3, x: 18, time: 12);

        queue.Enqueue(Touch(pointerId: 4, action: 0, eventTimeMillis: 13));
        queue.Enqueue(Touch(pointerId: 4, action: 2, x: 30, eventTimeMillis: 14));
        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }

    private static AndroidTouchInput Touch(
        int pointerId,
        int action,
        float x = 0,
        long eventTimeMillis = 0)
        => new(
            pointerId,
            x,
            0,
            1,
            action,
            AndroidActivityBridge.PointerTypeTouch,
            0,
            eventTimeMillis);

    private static AndroidTouchInput[] Drain(AndroidTouchInputQueue queue)
    {
        var packets = new List<AndroidTouchInput>();
        while (queue.TryDequeue(out AndroidTouchInput packet))
            packets.Add(packet);
        return packets.ToArray();
    }

    private static void AssertPacket(
        AndroidTouchInput packet,
        int pointerId,
        int action,
        float x,
        long time)
    {
        Assert.Equal(pointerId, packet.PointerId);
        Assert.Equal(action, packet.Action);
        Assert.Equal(x, packet.X);
        Assert.Equal(time, packet.EventTimeMillis);
    }
}
