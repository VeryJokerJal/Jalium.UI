using Jalium.UI;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

public class RealTimeStylusTests
{
    [Fact]
    public void StylusPlugInCollection_Lifecycle_ShouldFollowCollectionOperations()
    {
        var element = new TestElement();
        var lifecycle = new List<string>();
        var plugInA = new LifecyclePlugIn("A", lifecycle);
        var plugInB = new LifecyclePlugIn("B", lifecycle);
        var plugInC = new LifecyclePlugIn("C", lifecycle);

        element.GetStylusPlugIns(createIfMissing: true)!.Add(plugInA);
        element.GetStylusPlugIns(createIfMissing: true)!.Add(plugInB);
        element.GetStylusPlugIns(createIfMissing: true)![1] = plugInC;
        element.GetStylusPlugIns(createIfMissing: true)!.Remove(plugInA);
        element.GetStylusPlugIns(createIfMissing: true)!.Clear();

        Assert.Equal(
            new[]
            {
                "A:added",
                "B:added",
                "B:removed",
                "C:added",
                "A:removed",
                "C:removed"
            },
            lifecycle);
        Assert.Null(plugInA.Element);
        Assert.Null(plugInB.Element);
        Assert.Null(plugInC.Element);
    }

    [Fact]
    public void Process_SetStylusPoints_ShouldFlowToLaterPlugInsAndResult()
    {
        var root = new TestElement();
        var child = new TestElement();
        root.AddChild(child);

        var expected = new StylusPointCollection(new[] { new StylusPoint(10, 20, 0.9f) });
        var rewrite = new RewriteStylusPointsPlugIn(expected);
        var capture = new CaptureStylusPointsPlugIn();

        root.GetStylusPlugIns(createIfMissing: true)!.Add(rewrite);
        child.GetStylusPlugIns(createIfMissing: true)!.Add(capture);

        using var rts = new RealTimeStylus(root);
        RealTimeStylusProcessResult result = rts.Process(
            pointerId: 7,
            target: child,
            action: StylusInputAction.Move,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(1, 2, 0.4f) }),
            timestamp: 10,
            inAir: false,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        StylusPointCollection captured = Assert.IsType<StylusPointCollection>(capture.LastPoints);
        Assert.Single(captured);
        Assert.Equal(10, captured[0].X);
        Assert.Equal(20, captured[0].Y);
        Assert.Equal(0.9f, captured[0].PressureFactor);

        StylusPointCollection finalPoints = result.RawStylusInput.GetStylusPoints();
        Assert.Single(finalPoints);
        Assert.Equal(10, finalPoints[0].X);
        Assert.Equal(20, finalPoints[0].Y);
        Assert.Equal(0.9f, finalPoints[0].PressureFactor);
    }

    [Fact]
    public void QueueProcessedCallbacks_ShouldRunInFifoOrder()
    {
        var root = new TestElement();
        var processed = new List<string>();
        root.GetStylusPlugIns(createIfMissing: true)!.Add(new ProcessedTrackingPlugIn("A", processed));
        root.GetStylusPlugIns(createIfMissing: true)!.Add(new ProcessedTrackingPlugIn("B", processed));

        using var rts = new RealTimeStylus(root);
        RealTimeStylusProcessResult result = rts.Process(
            pointerId: 8,
            target: root,
            action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(3, 4, 0.5f) }),
            timestamp: 11,
            inAir: false,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.Empty(processed);

        rts.QueueProcessedCallbacks(result);
        root.Dispatcher.ProcessQueue();

        Assert.Equal(new[] { "A", "B" }, processed);
    }

    [Fact]
    public void Process_WhenPlugInThrows_ShouldCancelCurrentSession()
    {
        var root = new TestElement();
        root.GetStylusPlugIns(createIfMissing: true)!.Add(new ThrowingPlugIn());

        using var rts = new RealTimeStylus(root);
        RealTimeStylusProcessResult result = rts.Process(
            pointerId: 9,
            target: root,
            action: StylusInputAction.Move,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(5, 6, 0.5f) }),
            timestamp: 12,
            inAir: false,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.True(result.Canceled);
        Assert.True(result.SessionEnded);
    }

    [Fact]
    public void Process_ShouldInvokePlugInsFromAncestorToTarget()
    {
        var root = new TestElement();
        var parent = new TestElement();
        var child = new TestElement();
        root.AddChild(parent);
        parent.AddChild(child);

        var order = new List<string>();
        root.GetStylusPlugIns(createIfMissing: true)!.Add(new OrderPlugIn("root", order));
        parent.GetStylusPlugIns(createIfMissing: true)!.Add(new OrderPlugIn("parent", order));
        child.GetStylusPlugIns(createIfMissing: true)!.Add(new OrderPlugIn("child", order));

        using var rts = new RealTimeStylus(root);
        _ = rts.Process(
            pointerId: 10,
            target: child,
            action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(7, 8, 0.5f) }),
            timestamp: 13,
            inAir: false,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.Equal(new[] { "root", "parent", "child" }, order);
    }

    [Fact]
    public void Process_ShouldTrackRangeAndTargetTransitions()
    {
        var root = new TestElement();
        var childA = new TestElement();
        var childB = new TestElement();
        root.AddChild(childA);
        root.AddChild(childB);

        using var rts = new RealTimeStylus(root);

        RealTimeStylusProcessResult first = rts.Process(
            pointerId: 11,
            target: childA,
            action: StylusInputAction.InAirMove,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(1, 1, 0.5f) }),
            timestamp: 20,
            inAir: true,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.True(first.EnteredRange);
        Assert.True(first.EnteredElement);
        Assert.False(first.LeftElement);

        RealTimeStylusProcessResult second = rts.Process(
            pointerId: 11,
            target: childA,
            action: StylusInputAction.InAirMove,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(2, 2, 0.5f) }),
            timestamp: 21,
            inAir: true,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.False(second.EnteredRange);
        Assert.False(second.EnteredElement);
        Assert.False(second.LeftElement);

        RealTimeStylusProcessResult third = rts.Process(
            pointerId: 11,
            target: childB,
            action: StylusInputAction.InAirMove,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(3, 3, 0.5f) }),
            timestamp: 22,
            inAir: true,
            inRange: true,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.True(third.EnteredElement);
        Assert.True(third.LeftElement);
        Assert.Same(childA, third.PreviousTarget);

        RealTimeStylusProcessResult fourth = rts.Process(
            pointerId: 11,
            target: childB,
            action: StylusInputAction.InAirMove,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(4, 4, 0.5f) }),
            timestamp: 23,
            inAir: true,
            inRange: false,
            barrelButtonPressed: false,
            eraserPressed: false,
            inverted: false,
            pointerCanceled: false);

        Assert.True(fourth.ExitedRange);
        Assert.True(fourth.SessionEnded);
    }

    private sealed class TestElement : FrameworkElement
    {
        public void AddChild(UIElement child)
        {
            AddVisualChild(child);
        }
    }

    private sealed class LifecyclePlugIn(string name, List<string> lifecycle) : StylusPlugIn
    {
        protected override void OnAdded() => lifecycle.Add($"{name}:added");
        protected override void OnRemoved() => lifecycle.Add($"{name}:removed");
    }

    private sealed class RewriteStylusPointsPlugIn(StylusPointCollection replacement) : StylusPlugIn
    {
        private readonly StylusPointCollection _replacement = new(replacement);

        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            rawStylusInput.SetStylusPoints(_replacement);
        }
    }

    private sealed class CaptureStylusPointsPlugIn : StylusPlugIn
    {
        public StylusPointCollection? LastPoints { get; private set; }

        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            LastPoints = rawStylusInput.GetStylusPoints();
        }
    }

    private sealed class ProcessedTrackingPlugIn(string name, List<string> processed) : StylusPlugIn
    {
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            rawStylusInput.NotifyWhenProcessed(rawStylusInput);
        }

        protected override void OnStylusDownProcessed(object callbackData, bool targetVerified)
        {
            processed.Add(name);
        }
    }

    private sealed class ThrowingPlugIn : StylusPlugIn
    {
        protected override void OnStylusMove(RawStylusInput rawStylusInput)
        {
            throw new InvalidOperationException("Injected failure.");
        }
    }

    private sealed class OrderPlugIn(string name, List<string> order) : StylusPlugIn
    {
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            order.Add(name);
        }
    }
}
