namespace Jalium.UI.Input;

/// <summary>
/// Represents a single touch input contact (one finger / stylus tip).
/// </summary>
public sealed class TouchDevice
{
    private Point _position;
    private Point _previousPosition;
    private bool _isActive;
    private UIElement? _capturedElement;
    private StylusPointCollection? _lastStylusPoints;
    private Rect _lastContactRect = Rect.Empty;
    private TouchAction _lastAction = TouchAction.Move;
    private int _lastTimestamp;

    /// <summary>Gets the unique identifier for this touch device.</summary>
    public int Id { get; }

    /// <summary>Gets the element that this touch device is targeting.</summary>
    public UIElement? Target { get; private set; }

    /// <summary>Gets the current position of the touch.</summary>
    public Point Position => _position;

    /// <summary>Gets the previous position of the touch.</summary>
    public Point PreviousPosition => _previousPosition;

    /// <summary>Indicates whether this contact is currently active.</summary>
    public bool IsActive => _isActive;

    /// <summary>The element that has captured this contact (or null).</summary>
    public UIElement? Captured => _capturedElement;

    /// <summary>The direct hit-test target (before capture redirection).</summary>
    public UIElement? DirectlyOver { get; set; }

    /// <summary>Last contact patch reported by the platform (DIPs in <see cref="Target"/>'s client space).</summary>
    public Rect ContactRect => _lastContactRect;

    /// <summary>Timestamp (Environment.TickCount) of the most recent update.</summary>
    public int LastTimestamp => _lastTimestamp;

    public TouchDevice(int id, UIElement? target)
    {
        Id = id;
        Target = target;
        _isActive = true;
    }

    public void UpdatePosition(Point newPosition)
    {
        _previousPosition = _position;
        _position = newPosition;
    }

    /// <summary>Replaces this device's <see cref="Target"/> (used when capture changes).</summary>
    internal void RetargetTo(UIElement? target)
    {
        Target = target;
    }

    /// <summary>
    /// Records the latest high-frequency packet frame from the platform pointer source.
    /// </summary>
    internal void RecordFrame(StylusPointCollection? stylusPoints, Rect contactRect, TouchAction action, int timestamp)
    {
        _lastStylusPoints = stylusPoints;
        _lastContactRect = contactRect;
        _lastAction = action;
        _lastTimestamp = timestamp;
    }

    /// <summary>
    /// Captures this touch device to the specified element. Returns true on success.
    /// </summary>
    public bool Capture(UIElement? element)
    {
        _capturedElement = element;
        return true;
    }

    /// <summary>Gets the touch point relative to <paramref name="relativeTo"/>.</summary>
    public TouchPoint GetTouchPoint(UIElement? relativeTo)
    {
        Point position = TransformPoint(_position, relativeTo);
        Rect bounds = TransformRect(_lastContactRect, position, relativeTo);
        return new TouchPoint(this, position, bounds, _lastAction);
    }

    /// <summary>Gets intermediate touch points captured between the previous report and this one.</summary>
    public TouchPointCollection GetIntermediateTouchPoints(UIElement? relativeTo)
    {
        if (_lastStylusPoints == null || _lastStylusPoints.Count == 0)
        {
            return new TouchPointCollection { GetTouchPoint(relativeTo) };
        }

        var collection = new TouchPointCollection();
        int lastIndex = _lastStylusPoints.Count - 1;
        for (int i = 0; i < _lastStylusPoints.Count; i++)
        {
            StylusPoint sp = _lastStylusPoints[i];
            Point raw = new(sp.X, sp.Y);
            Point pt = TransformPoint(raw, relativeTo);
            Rect bounds = TransformRect(_lastContactRect, pt, relativeTo);
            TouchAction action = (i == lastIndex) ? _lastAction : TouchAction.Move;
            collection.Add(new TouchPoint(this, pt, bounds, action));
        }
        return collection;
    }

    public void Deactivate()
    {
        _isActive = false;
        _capturedElement = null;
    }

    /// <summary>
    /// Transforms <paramref name="source"/> — which is stored in window-root
    /// client coordinates (the same space as <c>MouseEventArgs.Position</c>) —
    /// into the local coordinate space of <paramref name="relativeTo"/>.
    /// </summary>
    /// <remarks>
    /// This mirrors <see cref="MouseEventArgs.GetPosition"/> exactly: walk up
    /// from <paramref name="relativeTo"/> collecting the ancestor chain, then
    /// descend from the root undoing each step's VisualBounds offset and
    /// RenderTransform. An earlier implementation used
    /// <c>Target.TransformToVisual(relativeTo).Transform(source)</c>, but
    /// <c>source</c> is window-local rather than Target-local, so the
    /// transform composed in the wrong direction and the InkCanvas drew
    /// strokes offset by the canvas's top-left position.
    /// </remarks>
    private static Point TransformPoint(Point source, UIElement? relativeTo)
    {
        if (relativeTo == null) return source;

        var chain = new List<Visual>();
        Visual? current = relativeTo;
        while (current != null)
        {
            chain.Add(current);
            current = current.VisualParent;
        }

        var p = source;
        for (int i = chain.Count - 2; i >= 0; i--)
        {
            var child = chain[i];

            if (child is FrameworkElement fe)
            {
                p = new Point(p.X - fe.VisualBounds.X, p.Y - fe.VisualBounds.Y);
            }

            if (child is UIElement ui && ui.RenderTransform is { } rt
                && rt.Value.TryInvert(out var inv))
            {
                var origin = ui.RenderTransformOrigin;
                var size = ui.RenderSize;
                var ox = origin.X * size.Width;
                var oy = origin.Y * size.Height;
                var translated = new Point(p.X - ox, p.Y - oy);
                var inverted = inv.Transform(translated);
                p = new Point(inverted.X + ox, inverted.Y + oy);
            }
        }

        return p;
    }

    private static Rect TransformRect(Rect rect, Point center, UIElement? relativeTo)
    {
        if (rect.IsEmpty || relativeTo == null)
        {
            return rect;
        }
        // Re-center the contact patch around the transformed point so the
        // size carries over while the position follows the contact.
        return new Rect(center.X - rect.Width / 2, center.Y - rect.Height / 2, rect.Width, rect.Height);
    }
}

/// <summary>
/// Represents a single touch point sample.
/// </summary>
public sealed class TouchPoint
{
    public TouchDevice TouchDevice { get; }
    public Point Position { get; }
    public Rect Bounds { get; }
    public TouchAction Action { get; }
    public Size Size => Bounds.IsEmpty ? Size.Empty : Bounds.Size;

    public TouchPoint(TouchDevice touchDevice, Point position, Rect bounds, TouchAction action)
    {
        TouchDevice = touchDevice;
        Position = position;
        Bounds = bounds;
        Action = action;
    }
}

/// <summary>Collection of touch points.</summary>
public sealed class TouchPointCollection : List<TouchPoint>
{
}

/// <summary>Specifies the action that caused a touch event.</summary>
public enum TouchAction
{
    /// <summary>A touch point was pressed.</summary>
    Down,
    /// <summary>A touch point was moved.</summary>
    Move,
    /// <summary>A touch point was released.</summary>
    Up,
    /// <summary>A touch point was canceled by the system.</summary>
    Cancel
}

/// <summary>Describes the static capabilities of the system's touch digitizer.</summary>
public sealed class TouchCapabilities
{
    /// <summary>True if a touch digitizer is present.</summary>
    public bool TouchPresent { get; init; }
    /// <summary>Maximum number of simultaneous contacts the device supports.</summary>
    public int Contacts { get; init; }
}

/// <summary>
/// Event arguments for touch routed events.
/// </summary>
public sealed class TouchEventArgs : InputEventArgs
{
    /// <summary>The touch device that raised this event.</summary>
    public TouchDevice TouchDevice { get; }

    /// <summary>Gets or sets whether downstream pointer promotion should be cancelled.</summary>
    public bool Cancel { get; set; }

    public TouchEventArgs(TouchDevice touchDevice, int timestamp)
        : base(timestamp)
    {
        TouchDevice = touchDevice;
    }

    /// <summary>Gets the touch point relative to the specified element.</summary>
    public TouchPoint GetTouchPoint(UIElement? relativeTo) => TouchDevice.GetTouchPoint(relativeTo);

    /// <summary>Gets intermediate touch points captured between the previous report and this one.</summary>
    public TouchPointCollection GetIntermediateTouchPoints(UIElement? relativeTo) => TouchDevice.GetIntermediateTouchPoints(relativeTo);

    /// <inheritdoc />
    internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is TouchEventHandler touchHandler)
        {
            touchHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>Event handler delegate for touch events.</summary>
public delegate void TouchEventHandler(object sender, TouchEventArgs e);
