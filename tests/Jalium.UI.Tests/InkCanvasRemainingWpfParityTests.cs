using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class InkCanvasRemainingWpfParityTests
{
    [Fact]
    public void CanonicalInkAndInkCanvasContractsUseWpfNamespacesAndAccessibility()
    {
        Assert.Equal("Jalium.UI.Ink", typeof(Stroke).Namespace);
        Assert.Equal("Jalium.UI.Ink", typeof(StrokeCollection).Namespace);
        Assert.Equal("Jalium.UI.Ink", typeof(DrawingAttributes).Namespace);
        Assert.Equal("Jalium.UI.Ink", typeof(StylusShape).Namespace);
        Assert.Equal("Jalium.UI.Ink", typeof(ApplicationGesture).Namespace);
        Assert.Equal("Jalium.UI.Input.StylusPlugIns", typeof(DynamicRenderer).Namespace);

        PropertyInfo? dynamicRenderer = typeof(InkCanvas).GetProperty(
            "DynamicRenderer",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(dynamicRenderer);
        Assert.Equal(typeof(DynamicRenderer), dynamicRenderer!.PropertyType);
        Assert.True(dynamicRenderer.GetMethod!.IsFamily);
        Assert.True(dynamicRenderer.SetMethod!.IsFamily);

        PropertyInfo? inkPresenter = typeof(InkCanvas).GetProperty(
            "InkPresenter",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(inkPresenter);
        Assert.Equal(typeof(InkPresenter), inkPresenter!.PropertyType);
        Assert.True(inkPresenter.GetMethod!.IsFamily);

        Assert.Contains(typeof(IAddChild), typeof(InkCanvas).GetInterfaces());
        var publicProperties = new Dictionary<string, Type>
        {
            [nameof(InkCanvas.ActiveEditingMode)] = typeof(InkCanvasEditingMode),
            [nameof(InkCanvas.Children)] = typeof(UIElementCollection),
            [nameof(InkCanvas.DefaultStylusPointDescription)] = typeof(StylusPointDescription),
            [nameof(InkCanvas.EditingModeInverted)] = typeof(InkCanvasEditingMode),
            [nameof(InkCanvas.EraserShape)] = typeof(StylusShape),
            [nameof(InkCanvas.IsGestureRecognizerAvailable)] = typeof(bool),
            [nameof(InkCanvas.MoveEnabled)] = typeof(bool),
            [nameof(InkCanvas.PreferredPasteFormats)] = typeof(IEnumerable<InkCanvasClipboardFormat>),
            [nameof(InkCanvas.ResizeEnabled)] = typeof(bool),
            [nameof(InkCanvas.UseCustomCursor)] = typeof(bool),
        };
        foreach ((string name, Type type) in publicProperties)
            Assert.Equal(type, typeof(InkCanvas).GetProperty(name)!.PropertyType);

        foreach (string fieldName in new[]
        {
            nameof(InkCanvas.ActiveEditingModeProperty),
            nameof(InkCanvas.BottomProperty),
            nameof(InkCanvas.EditingModeInvertedProperty),
            nameof(InkCanvas.LeftProperty),
            nameof(InkCanvas.RightProperty),
            nameof(InkCanvas.TopProperty),
        })
        {
            Assert.Equal(
                typeof(DependencyProperty),
                typeof(InkCanvas).GetField(fieldName)!.FieldType);
        }

        Assert.Equal(
            typeof(RoutedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.ActiveEditingModeChanged))!.EventHandlerType);
        Assert.Equal(
            typeof(DrawingAttributesReplacedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.DefaultDrawingAttributesReplaced))!.EventHandlerType);
        Assert.Equal(
            typeof(RoutedEventHandler),
            typeof(InkCanvas).GetEvent(nameof(InkCanvas.EditingModeInvertedChanged))!.EventHandlerType);
        Assert.Equal(
            typeof(RoutedEvent),
            typeof(InkCanvas).GetField(nameof(InkCanvas.ActiveEditingModeChangedEvent))!.FieldType);
        Assert.Equal(
            typeof(RoutedEvent),
            typeof(InkCanvas).GetField(nameof(InkCanvas.EditingModeInvertedChangedEvent))!.FieldType);

        Assert.Equal(typeof(StrokeCollection), typeof(InkCanvas).GetMethod(
            nameof(InkCanvas.GetSelectedStrokes),
            Type.EmptyTypes)!.ReturnType);
        Assert.NotNull(typeof(InkCanvas).GetMethod(
            nameof(InkCanvas.Select),
            [typeof(StrokeCollection), typeof(IEnumerable<UIElement>)]));

        var gestureConstructor = typeof(InkCanvasGestureEventArgs).GetConstructor(
            [typeof(StrokeCollection), typeof(IEnumerable<GestureRecognitionResult>)]);
        Assert.NotNull(gestureConstructor);
        Assert.Equal(
            typeof(ReadOnlyCollection<GestureRecognitionResult>),
            typeof(InkCanvasGestureEventArgs).GetMethod(
                nameof(InkCanvasGestureEventArgs.GetGestureRecognitionResults))!.ReturnType);
    }

    [Fact]
    public void DefaultsAttachedPositioningAndDrawingAttributeReplacementAreFunctional()
    {
        var canvas = new InkCanvas();

        Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
        Assert.Equal(InkCanvasEditingMode.EraseByStroke, canvas.EditingModeInverted);
        Assert.Equal(InkCanvasEditingMode.Ink, canvas.ActiveEditingMode);
        Assert.True(canvas.MoveEnabled);
        Assert.True(canvas.ResizeEnabled);
        Assert.False(canvas.UseCustomCursor);
        Assert.IsType<RectangleStylusShape>(canvas.EraserShape);
        Assert.Equal(8.0, canvas.EraserShape.Width);
        Assert.Equal(8.0, canvas.EraserShape.Height);
        Assert.Equal(
            new[] { InkCanvasClipboardFormat.InkSerializedFormat },
            canvas.PreferredPasteFormats);

        var child = new Border { Width = 20, Height = 10 };
        InkCanvas.SetLeft(child, 30);
        InkCanvas.SetTop(child, 40);
        canvas.Children.Add(child);
        canvas.Measure(new Size(200, 200));
        canvas.Arrange(new Rect(0, 0, 200, 200));

        Assert.Same(canvas, child.VisualParent?.VisualParent);
        Assert.Equal(30.0, child.VisualBounds.X);
        Assert.Equal(40.0, child.VisualBounds.Y);
        Assert.Equal(20.0, child.RenderSize.Width);
        Assert.Equal(10.0, child.RenderSize.Height);

        DrawingAttributes previous = canvas.DefaultDrawingAttributes;
        var replacement = new DrawingAttributes { Width = 7, Height = 9 };
        DrawingAttributesReplacedEventArgs? received = null;
        canvas.DefaultDrawingAttributesReplaced += (_, args) => received = args;

        canvas.DefaultDrawingAttributes = replacement;

        Assert.NotNull(received);
        Assert.Same(previous, received!.PreviousDrawingAttributes);
        Assert.Same(replacement, received.NewDrawingAttributes);
        Assert.Throws<ArgumentNullException>(() => canvas.DefaultDrawingAttributes = null!);
    }

    [Fact]
    public void StylusInversionUpdatesActiveEditingModeAndRaisesRoutedEvent()
    {
        var canvas = new InkCanvas
        {
            EditingMode = InkCanvasEditingMode.Ink,
            EditingModeInverted = InkCanvasEditingMode.EraseByPoint,
        };
        var changes = new List<InkCanvasEditingMode>();
        canvas.ActiveEditingModeChanged += (_, args) =>
        {
            Assert.Same(InkCanvas.ActiveEditingModeChangedEvent, args.RoutedEvent);
            changes.Add(canvas.ActiveEditingMode);
        };

        var device = new PointerStylusDevice(8101);
        device.UpdateState(
            new Point(10, 10),
            pressureFactor: 0.5f,
            inAir: false,
            inverted: true,
            inRange: true,
            barrelPressed: false,
            eraserPressed: true,
            directlyOver: canvas);
        canvas.RaiseEvent(new StylusDownEventArgs(device, 0)
        {
            RoutedEvent = UIElement.PreviewStylusDownEvent,
        });

        Assert.Equal(InkCanvasEditingMode.EraseByPoint, canvas.ActiveEditingMode);

        device.UpdateState(
            new Point(10, 10),
            pressureFactor: 0.5f,
            inAir: true,
            inverted: false,
            inRange: false,
            barrelPressed: false,
            eraserPressed: false,
            directlyOver: canvas);
        canvas.RaiseEvent(new StylusEventArgs(device, 1)
        {
            RoutedEvent = UIElement.PreviewStylusOutOfRangeEvent,
        });

        Assert.Equal(InkCanvasEditingMode.Ink, canvas.ActiveEditingMode);
        Assert.Equal(
            new[] { InkCanvasEditingMode.EraseByPoint, InkCanvasEditingMode.Ink },
            changes);
    }

    [Fact]
    public void GestureConfigurationAndCollectedStrokePipelineUseRealSampledPoints()
    {
        var canvas = CreateCanvas();
        canvas.EditingMode = InkCanvasEditingMode.GestureOnly;
        canvas.SetEnabledGestures([ApplicationGesture.Right]);
        InkCanvasGestureEventArgs? gesture = null;
        canvas.Gesture += (_, args) => gesture = args;

        RaiseTouchStroke(canvas, 8201, new Point(10, 20), new Point(80, 20));

        Assert.NotNull(gesture);
        Assert.Equal(
            ApplicationGesture.Right,
            Assert.Single(gesture!.GetGestureRecognitionResults()).ApplicationGesture);
        Assert.Empty(canvas.Strokes);

        canvas.EditingMode = InkCanvasEditingMode.InkAndGesture;
        canvas.Gesture += (_, args) => args.Cancel = true;
        var collected = 0;
        canvas.StrokeCollected += (_, _) => collected++;
        RaiseTouchStroke(canvas, 8202, new Point(20, 30), new Point(100, 30));

        Assert.Single(canvas.Strokes);
        Assert.Equal(1, collected);

        Assert.Throws<ArgumentException>(() =>
            canvas.SetEnabledGestures(Array.Empty<ApplicationGesture>()));
        Assert.Throws<ArgumentException>(() =>
            canvas.SetEnabledGestures([ApplicationGesture.Right, ApplicationGesture.Right]));
        Assert.Throws<ArgumentException>(() =>
            canvas.SetEnabledGestures([ApplicationGesture.AllGestures, ApplicationGesture.Right]));
    }

    [Fact]
    public void SelectionHitTestingClipboardAndMovePipelineEditActualStrokes()
    {
        var source = CreateCanvas();
        source.EditingMode = InkCanvasEditingMode.Select;
        Stroke stroke = CreateStroke(10, 10, 30, 30);
        source.Strokes.Add(stroke);
        source.Select(new StrokeCollection { stroke });

        Rect bounds = source.GetSelectionBounds();
        Assert.Equal(InkCanvasSelectionHitResult.Selection, source.HitTestSelection(
            new Point(bounds.X + bounds.Width * 0.5, bounds.Y + bounds.Height * 0.5)));
        Assert.Equal(InkCanvasSelectionHitResult.TopLeft, source.HitTestSelection(
            new Point(bounds.Left - 5, bounds.Top - 5)));

        source.CopySelection();
        Assert.True(source.CanPaste());

        var target = CreateCanvas();
        target.Paste(new Point(100, 120));

        Stroke pasted = Assert.Single(target.Strokes);
        Rect pastedBounds = pasted.GetBounds();
        Assert.Equal(100.0, pastedBounds.X, 6);
        Assert.Equal(120.0, pastedBounds.Y, 6);
        Assert.Same(pasted, Assert.Single(target.GetSelectedStrokes()));

        var moved = 0;
        target.SelectionMoved += (_, _) => moved++;
        Rect oldBounds = target.GetSelectionBounds();
        Point center = new(
            oldBounds.X + oldBounds.Width * 0.5,
            oldBounds.Y + oldBounds.Height * 0.5);
        target.RaiseEvent(CreateMouseDown(center));
        target.RaiseEvent(CreateMouseMove(new Point(center.X + 15, center.Y + 5)));
        target.RaiseEvent(CreateMouseUp(new Point(center.X + 15, center.Y + 5)));

        Rect movedBounds = target.GetSelectionBounds();
        Assert.Equal(oldBounds.X + 15, movedBounds.X, 6);
        Assert.Equal(oldBounds.Y + 5, movedBounds.Y, 6);
        Assert.Equal(1, moved);

        source.CutSelection();
        Assert.Empty(source.Strokes);
        Assert.Empty(source.GetSelectedStrokes());
    }

    [Fact]
    public void ClipboardFormatsAreValidatedDeduplicatedAndSnapshotted()
    {
        var canvas = new InkCanvas();
        var formats = new List<InkCanvasClipboardFormat>
        {
            InkCanvasClipboardFormat.Text,
            InkCanvasClipboardFormat.Text,
            InkCanvasClipboardFormat.Xaml,
        };

        canvas.PreferredPasteFormats = formats;
        formats.Clear();

        Assert.Equal(
            new[] { InkCanvasClipboardFormat.Text, InkCanvasClipboardFormat.Xaml },
            canvas.PreferredPasteFormats);
        Assert.Throws<ArgumentNullException>(() => canvas.PreferredPasteFormats = null!);
        Assert.Throws<ArgumentException>(() => canvas.PreferredPasteFormats =
            [(InkCanvasClipboardFormat)99]);
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

    private static void RaiseTouchStroke(InkCanvas canvas, int pointerId, Point start, Point end)
    {
        var device = Touch.RegisterTouchPoint(pointerId, start, canvas);
        canvas.RaiseEvent(new TouchEventArgs(device, 0) { RoutedEvent = UIElement.TouchDownEvent });
        Touch.UpdateTouchPoint(pointerId, end);
        canvas.RaiseEvent(new TouchEventArgs(device, 1) { RoutedEvent = UIElement.TouchMoveEvent });
        canvas.RaiseEvent(new TouchEventArgs(device, 2) { RoutedEvent = UIElement.TouchUpEvent });
        Touch.UnregisterTouchPoint(pointerId);
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position) => new(
        UIElement.MouseDownEvent,
        position,
        MouseButton.Left,
        MouseButtonState.Pressed,
        clickCount: 1,
        leftButton: MouseButtonState.Pressed,
        middleButton: MouseButtonState.Released,
        rightButton: MouseButtonState.Released,
        xButton1: MouseButtonState.Released,
        xButton2: MouseButtonState.Released,
        modifiers: ModifierKeys.None,
        timestamp: 0);

    private static MouseEventArgs CreateMouseMove(Point position) => new(
        UIElement.MouseMoveEvent,
        position,
        MouseButtonState.Pressed,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        ModifierKeys.None,
        timestamp: 1);

    private static MouseButtonEventArgs CreateMouseUp(Point position) => new(
        UIElement.MouseUpEvent,
        position,
        MouseButton.Left,
        MouseButtonState.Released,
        clickCount: 1,
        leftButton: MouseButtonState.Released,
        middleButton: MouseButtonState.Released,
        rightButton: MouseButtonState.Released,
        xButton1: MouseButtonState.Released,
        xButton2: MouseButtonState.Released,
        modifiers: ModifierKeys.None,
        timestamp: 2);
}
