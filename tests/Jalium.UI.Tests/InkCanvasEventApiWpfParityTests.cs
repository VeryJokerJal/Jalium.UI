using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Ink;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class InkCanvasEventApiWpfParityTests
{
    [Fact]
    public void EventAndEventArgsTypesMatchWpfSurface()
    {
        Assert.Equal(
            typeof(InkCanvasStrokeCollectedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.StrokeCollected))!.EventHandlerType);
        Assert.Equal(
            typeof(InkCanvasStrokeErasingEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.StrokeErasing))!.EventHandlerType);
        Assert.Equal(
            typeof(InkCanvasStrokesReplacedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.StrokesReplaced))!.EventHandlerType);
        Assert.Equal(
            typeof(InkCanvasSelectionChangingEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.SelectionChanging))!.EventHandlerType);
        Assert.Equal(
            typeof(InkCanvasSelectionEditingEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.SelectionMoving))!.EventHandlerType);
        Assert.Equal(
            typeof(InkCanvasSelectionEditingEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.SelectionResizing))!.EventHandlerType);
        Assert.Equal(
            typeof(InkCanvasGestureEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.Gesture))!.EventHandlerType);
        Assert.Equal(
            typeof(RoutedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.StrokeErased))!.EventHandlerType);
        Assert.Equal(
            typeof(RoutedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.EditingModeChanged))!.EventHandlerType);

        Assert.Equal(typeof(RoutedEventArgs), typeof(InkCanvasStrokeCollectedEventArgs).BaseType);
        Assert.Equal(typeof(CancelEventArgs), typeof(InkCanvasStrokeErasingEventArgs).BaseType);
        Assert.Equal(typeof(CancelEventArgs), typeof(InkCanvasSelectionChangingEventArgs).BaseType);
        Assert.Equal(typeof(CancelEventArgs), typeof(InkCanvasSelectionEditingEventArgs).BaseType);
        Assert.Equal(typeof(EventArgs), typeof(InkCanvasStrokesReplacedEventArgs).BaseType);
        Assert.False(typeof(InkCanvasStrokeCollectedEventArgs).IsSealed);
        Assert.False(typeof(InkCanvasStrokeErasingEventArgs).IsSealed);
        Assert.False(typeof(InkCanvasSelectionChangingEventArgs).IsSealed);
        Assert.False(typeof(InkCanvasSelectionEditingEventArgs).IsSealed);
        Assert.False(typeof(InkCanvasStrokesReplacedEventArgs).IsSealed);
    }

    [Fact]
    public void RoutedEventFieldsMatchWpfMetadata()
    {
        AssertRoutedEvent(
            InkCanvas.StrokeCollectedEvent,
            nameof(InkCanvas.StrokeCollected),
            typeof(InkCanvasStrokeCollectedEventHandler));
        AssertRoutedEvent(
            InkCanvas.GestureEvent,
            nameof(InkCanvas.Gesture),
            typeof(InkCanvasGestureEventHandler));
        AssertRoutedEvent(
            InkCanvas.StrokeErasedEvent,
            nameof(InkCanvas.StrokeErased),
            typeof(RoutedEventHandler));
        AssertRoutedEvent(
            InkCanvas.EditingModeChangedEvent,
            nameof(InkCanvas.EditingModeChanged),
            typeof(RoutedEventHandler));
    }

    [Fact]
    public void TouchCommitRaisesTypedStrokeCollectedRoutedEvent()
    {
        const int pointerId = 7101;
        var canvas = CreateCanvas();
        InkCanvasStrokeCollectedEventArgs? received = null;
        canvas.StrokeCollected += (_, args) => received = args;

        RaiseTouch(canvas, pointerId, new Point(10, 10), UIElement.TouchDownEvent);
        Touch.UpdateTouchPoint(pointerId, new Point(30, 30));
        RaiseTouch(canvas, pointerId, new Point(30, 30), UIElement.TouchMoveEvent);
        Touch.UpdateTouchPoint(pointerId, new Point(50, 50));
        RaiseTouch(canvas, pointerId, new Point(50, 50), UIElement.TouchUpEvent);
        Touch.UnregisterTouchPoint(pointerId);

        Assert.NotNull(received);
        Assert.Same(InkCanvas.StrokeCollectedEvent, received!.RoutedEvent);
        Assert.Same(canvas.Strokes.Single(), received.Stroke);
    }

    [Fact]
    public void ErasePathHonorsCancellationAndRaisesStrokeErasedAfterRemoval()
    {
        var canvas = CreateCanvas();
        var stroke = CreateStroke(50, 50, 60, 60);
        canvas.Strokes.Add(stroke);
        canvas.EditingMode = InkCanvasEditingMode.EraseByStroke;

        InkCanvasStrokeErasingEventHandler cancel = (_, args) => args.Cancel = true;
        var erasedCount = 0;
        canvas.StrokeErasing += cancel;
        canvas.StrokeErased += (_, args) =>
        {
            Assert.Same(InkCanvas.StrokeErasedEvent, args.RoutedEvent);
            erasedCount++;
        };

        RaiseTouchSequence(canvas, pointerId: 7102, new Point(55, 55));

        Assert.Contains(stroke, canvas.Strokes);
        Assert.Equal(0, erasedCount);

        canvas.StrokeErasing -= cancel;
        RaiseTouchSequence(canvas, pointerId: 7103, new Point(55, 55));

        Assert.DoesNotContain(stroke, canvas.Strokes);
        Assert.Equal(1, erasedCount);
    }

    [Fact]
    public void ReplacingStrokesRaisesPayloadWithBothCollectionInstances()
    {
        var canvas = new InkCanvas();
        var previous = canvas.Strokes;
        var replacement = new StrokeCollection { CreateStroke(0, 0, 10, 10) };
        InkCanvasStrokesReplacedEventArgs? received = null;
        canvas.StrokesReplaced += (_, args) => received = args;

        canvas.Strokes = replacement;

        Assert.NotNull(received);
        Assert.Same(replacement, received!.NewStrokes);
        Assert.Same(previous, received.PreviousStrokes);
    }

    [Fact]
    public void ProgrammaticSelectionCanBeRewrittenOrCanceledBySelectionChanging()
    {
        var canvas = new InkCanvas();
        var first = CreateStroke(0, 0, 10, 10);
        var second = CreateStroke(20, 20, 30, 30);
        canvas.Strokes.Add(first);
        canvas.Strokes.Add(second);
        var changedCount = 0;
        canvas.SelectionChanged += (_, _) => changedCount++;

        InkCanvasSelectionChangingEventHandler rewrite = (_, args) =>
            args.SetSelectedStrokes(new StrokeCollection { second });
        canvas.SelectionChanging += rewrite;

        canvas.Select(new StrokeCollection { first });

        Assert.Equal(InkCanvasEditingMode.Select, canvas.EditingMode);
        Assert.Same(second, Assert.Single(canvas.GetSelectedStrokes()));
        Assert.Equal(1, changedCount);

        var snapshot = canvas.GetSelectedStrokes();
        snapshot.Clear();
        Assert.Same(second, Assert.Single(canvas.GetSelectedStrokes()));

        canvas.SelectionChanging -= rewrite;
        canvas.SelectionChanging += (_, args) => args.Cancel = true;

        canvas.Select(new StrokeCollection { first });

        Assert.Same(second, Assert.Single(canvas.GetSelectedStrokes()));
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void SelectionEditingRaisersAreVirtualAndDeliverMutableCancelArgs()
    {
        var moving = typeof(InkCanvas).GetMethod(
            "OnSelectionMoving",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var resizing = typeof(InkCanvas).GetMethod(
            "OnSelectionResizing",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(moving);
        Assert.NotNull(resizing);
        Assert.True(moving!.IsVirtual);
        Assert.True(resizing!.IsVirtual);

        var canvas = new EventProbeInkCanvas();
        canvas.SelectionMoving += (_, args) =>
        {
            args.NewRectangle = new Rect(5, 6, 70, 80);
            args.Cancel = true;
        };

        var eventArgs = canvas.RaiseSelectionMoving(
            new Rect(0, 0, 10, 20),
            new Rect(1, 2, 30, 40));

        Assert.True(eventArgs.Cancel);
        Assert.Equal(new Rect(0, 0, 10, 20), eventArgs.OldRectangle);
        Assert.Equal(new Rect(5, 6, 70, 80), eventArgs.NewRectangle);
    }

    [Fact]
    public void GestureArgsValidateInputsExposeReadOnlyResultsAndRouteTypedEvent()
    {
        var stroke = CreateStroke(0, 0, 10, 10);
        var strokes = new StrokeCollection { stroke };
        var result = new GestureRecognitionResult(
            RecognitionConfidence.Strong,
            ApplicationGesture.Check);

        Assert.Throws<ArgumentException>(() =>
            new InkCanvasGestureEventArgs(new StrokeCollection(), new[] { result }));
        Assert.Throws<ArgumentException>(() =>
            new InkCanvasGestureEventArgs(strokes, Array.Empty<GestureRecognitionResult>()));

        var args = new InkCanvasGestureEventArgs(strokes, new[] { result });
        var canvas = new EventProbeInkCanvas();
        InkCanvasGestureEventArgs? received = null;
        canvas.Gesture += (_, eventArgs) => received = eventArgs;

        canvas.RaiseGesture(args);

        Assert.Same(args, received);
        Assert.Same(InkCanvas.GestureEvent, args.RoutedEvent);
        Assert.Same(result, Assert.Single(args.GetGestureRecognitionResults()));
        Assert.IsAssignableFrom<IReadOnlyList<GestureRecognitionResult>>(
            args.GestureRecognitionResults);
    }

    private static InkCanvas CreateCanvas()
    {
        var canvas = new InkCanvas();
        canvas.Arrange(new Rect(0, 0, 400, 400));
        return canvas;
    }

    private static Stroke CreateStroke(double x1, double y1, double x2, double y2)
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(x1, y1),
            new StylusPoint(x2, y2),
        };
        return new Stroke(points, new DrawingAttributes());
    }

    private static void RaiseTouchSequence(InkCanvas canvas, int pointerId, Point position)
    {
        RaiseTouch(canvas, pointerId, position, UIElement.TouchDownEvent);
        RaiseTouch(canvas, pointerId, position, UIElement.TouchUpEvent);
        Touch.UnregisterTouchPoint(pointerId);
    }

    private static void RaiseTouch(
        InkCanvas canvas,
        int pointerId,
        Point position,
        RoutedEvent routedEvent)
    {
        var device = Touch.GetDevice(pointerId)
            ?? Touch.RegisterTouchPoint(pointerId, position, canvas);
        var args = new TouchEventArgs(device, 0) { RoutedEvent = routedEvent };
        canvas.RaiseEvent(args);
    }

    private static void AssertRoutedEvent(
        RoutedEvent routedEvent,
        string name,
        Type handlerType)
    {
        Assert.Equal(name, routedEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, routedEvent.RoutingStrategy);
        Assert.Equal(handlerType, routedEvent.HandlerType);
        Assert.Equal(typeof(InkCanvas), routedEvent.OwnerType);
    }

    private sealed class EventProbeInkCanvas : InkCanvas
    {
        public InkCanvasSelectionEditingEventArgs RaiseSelectionMoving(
            Rect oldRectangle,
            Rect newRectangle)
        {
            var args = new InkCanvasSelectionEditingEventArgs(oldRectangle, newRectangle);
            OnSelectionMoving(args);
            return args;
        }

        public void RaiseGesture(InkCanvasGestureEventArgs args) => OnGesture(args);
    }
}
