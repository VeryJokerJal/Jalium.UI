using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Data;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class InkCanvasTouchTests
{
    [Fact]
    public void TouchDown_InInkMode_ShouldStartDrawing()
    {
        var canvas = CreateInkCanvas();

        RaiseTouchDown(canvas, pointerId: 1, new Point(50, 50));

        // An active touch stroke session should exist.
        var sessions = GetActiveTouchStrokes(canvas);
        Assert.Single(sessions);
    }

    [Fact]
    public void TouchMove_InInkMode_ShouldAddPoints()
    {
        var canvas = CreateInkCanvas();

        RaiseTouchDown(canvas, pointerId: 1, new Point(10, 10));
        RaiseTouchMove(canvas, pointerId: 1, new Point(30, 30));
        RaiseTouchMove(canvas, pointerId: 1, new Point(60, 60));

        var sessions = GetActiveTouchStrokes(canvas);
        Assert.Single(sessions);

        // The stroke should have 3 points (down + 2 moves).
        var stroke = sessions.Values.First();
        Assert.True(GetStrokePointCount(stroke) >= 3);
    }

    [Fact]
    public void TouchUp_InInkMode_ShouldCommitStroke()
    {
        var canvas = CreateInkCanvas();

        RaiseTouchDown(canvas, pointerId: 1, new Point(10, 10));
        RaiseTouchMove(canvas, pointerId: 1, new Point(30, 30));
        RaiseTouchUp(canvas, pointerId: 1, new Point(50, 50));

        // The stroke should be committed to the Strokes collection.
        Assert.Single(canvas.Strokes);

        // Active sessions should be empty.
        var sessions = GetActiveTouchStrokes(canvas);
        Assert.Empty(sessions);
    }

    [Fact]
    public void StrokeCollected_ShouldFireOnTouchUp()
    {
        var canvas = CreateInkCanvas();
        Stroke? collectedStroke = null;
        canvas.StrokeCollected += (_, e) => collectedStroke = e.Stroke;

        RaiseTouchDown(canvas, pointerId: 1, new Point(10, 10));
        RaiseTouchMove(canvas, pointerId: 1, new Point(30, 30));
        RaiseTouchUp(canvas, pointerId: 1, new Point(50, 50));

        Assert.NotNull(collectedStroke);
    }

    [Fact]
    public void MultiTouch_ShouldDrawMultipleStrokesSimultaneously()
    {
        var canvas = CreateInkCanvas();

        // Finger 1 starts
        RaiseTouchDown(canvas, pointerId: 1, new Point(10, 10));
        // Finger 2 starts
        RaiseTouchDown(canvas, pointerId: 2, new Point(100, 100));

        var sessions = GetActiveTouchStrokes(canvas);
        Assert.Equal(2, sessions.Count);

        // Both fingers move
        RaiseTouchMove(canvas, pointerId: 1, new Point(30, 30));
        RaiseTouchMove(canvas, pointerId: 2, new Point(120, 120));

        // Finger 1 lifts
        RaiseTouchUp(canvas, pointerId: 1, new Point(50, 50));

        Assert.Single(canvas.Strokes);
        sessions = GetActiveTouchStrokes(canvas);
        Assert.Single(sessions); // Finger 2 still active

        // Finger 2 lifts
        RaiseTouchUp(canvas, pointerId: 2, new Point(150, 150));

        Assert.Equal(2, canvas.Strokes.Count);
        sessions = GetActiveTouchStrokes(canvas);
        Assert.Empty(sessions);
    }

    [Fact]
    public void TouchInEraseMode_ShouldEraseStrokes()
    {
        var canvas = CreateInkCanvas();

        // Pre-populate a stroke.
        var points = new StylusPointCollection();
        points.Add(new StylusPoint(50, 50));
        points.Add(new StylusPoint(60, 60));
        var stroke = new Stroke(points, new DrawingAttributes());
        canvas.Strokes.Add(stroke);
        Assert.Single(canvas.Strokes);

        canvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        canvas.EraserDiameter = 20;

        RaiseTouchDown(canvas, pointerId: 1, new Point(55, 55));

        // Stroke should be erased.
        Assert.Empty(canvas.Strokes);
    }

    [Fact]
    public void EditingModeChange_ShouldCancelActiveTouchStrokes()
    {
        var canvas = CreateInkCanvas();

        RaiseTouchDown(canvas, pointerId: 1, new Point(10, 10));
        RaiseTouchMove(canvas, pointerId: 1, new Point(30, 30));

        var sessions = GetActiveTouchStrokes(canvas);
        Assert.NotEmpty(sessions);

        // Changing editing mode should clear active sessions.
        canvas.EditingMode = InkCanvasEditingMode.EraseByStroke;

        sessions = GetActiveTouchStrokes(canvas);
        Assert.Empty(sessions);
    }

    [Fact]
    public void TouchEvent_ShouldBeMarkedAsHandled()
    {
        var canvas = CreateInkCanvas();

        var args = CreateTouchEventArgs(pointerId: 1, new Point(10, 10), TouchAction.Down);
        args.RoutedEvent = UIElement.TouchDownEvent;
        canvas.RaiseEvent(args);

        Assert.True(args.Handled);
    }

    #region Helpers

    private static InkCanvas CreateInkCanvas()
    {
        var canvas = new InkCanvas();
        canvas.Arrange(new Rect(0, 0, 400, 400));
        return canvas;
    }

    private static void RaiseTouchDown(InkCanvas canvas, int pointerId, Point position)
    {
        var device = Touch.RegisterTouchPoint(pointerId, position, canvas);
        var args = new TouchEventArgs(device, 0) { RoutedEvent = UIElement.TouchDownEvent };
        canvas.RaiseEvent(args);
    }

    private static void RaiseTouchMove(InkCanvas canvas, int pointerId, Point position)
    {
        Touch.UpdateTouchPoint(pointerId, position);
        var device = Touch.GetDevice(pointerId)
            ?? Touch.RegisterTouchPoint(pointerId, position, canvas);
        var args = new TouchEventArgs(device, 0) { RoutedEvent = UIElement.TouchMoveEvent };
        canvas.RaiseEvent(args);
    }

    private static void RaiseTouchUp(InkCanvas canvas, int pointerId, Point position)
    {
        Touch.UpdateTouchPoint(pointerId, position);
        var device = Touch.GetDevice(pointerId)
            ?? Touch.RegisterTouchPoint(pointerId, position, canvas);
        var args = new TouchEventArgs(device, 0) { RoutedEvent = UIElement.TouchUpEvent };
        canvas.RaiseEvent(args);
        Touch.UnregisterTouchPoint(pointerId);
    }

    private static TouchEventArgs CreateTouchEventArgs(int pointerId, Point position, TouchAction action)
    {
        var device = Touch.RegisterTouchPoint(pointerId, position, null);
        return new TouchEventArgs(device, 0);
    }

    private static Dictionary<int, object> GetActiveTouchStrokes(InkCanvas canvas)
    {
        var field = typeof(InkCanvas).GetField("_activeTouchStrokes",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var dict = field!.GetValue(canvas) as System.Collections.IDictionary;
        Assert.NotNull(dict);

        var result = new Dictionary<int, object>();
        foreach (System.Collections.DictionaryEntry entry in dict!)
        {
            result[(int)entry.Key] = entry.Value!;
        }
        return result;
    }

    private static int GetStrokePointCount(object touchStrokeSession)
    {
        var pointsProp = touchStrokeSession.GetType().GetProperty("Points",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert.NotNull(pointsProp);
        var points = pointsProp!.GetValue(touchStrokeSession) as System.Collections.ICollection;
        Assert.NotNull(points);
        return points!.Count;
    }

    #endregion
}
