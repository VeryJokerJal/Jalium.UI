using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Jalium.UI.Input;

/// <summary>Represents one independently tracked touch contact.</summary>
public abstract class TouchDevice : InputDevice, IManipulator
{
    private static readonly object ActiveDeviceGate = new();
    private static readonly List<TouchDevice> ActiveDeviceList = [];

    private readonly int _deviceId;
    private bool _isActive;
    private IInputElement? _rawDirectlyOver;
    private IInputElement? _directlyOver;
    private IInputElement? _captured;
    private CaptureMode _captureMode;
    private PresentationSource? _activeSource;
    private Point _position;
    private Point _previousPosition;
    private StylusPointCollection? _lastStylusPoints;
    private Rect _lastContactRect = Rect.Empty;
    private TouchAction _lastAction = TouchAction.Move;
    private int _lastTimestamp;

    protected TouchDevice(int deviceId)
    {
        _deviceId = deviceId;
    }

    public int Id => _deviceId;
    public bool IsActive => _isActive;
    public IInputElement? DirectlyOver => _directlyOver;
    public IInputElement? Captured => _captured;
    public CaptureMode CaptureMode => _captureMode;
    public Point Position => _position;
    public Point PreviousPosition => _previousPosition;
    public Rect ContactRect => _lastContactRect;
    public int LastTimestamp => _lastTimestamp;

    public sealed override IInputElement? Target => _directlyOver;
    public sealed override PresentationSource? ActiveSource => _activeSource;

    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler? Updated;

    public abstract TouchPoint GetTouchPoint(IInputElement? relativeTo);
    public abstract TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo);

    public bool Capture(IInputElement? element) => Capture(element, CaptureMode.Element);

    public bool Capture(IInputElement? element, CaptureMode captureMode)
    {
        if (!Enum.IsDefined(captureMode))
            throw new InvalidEnumArgumentException(nameof(captureMode), (int)captureMode, typeof(CaptureMode));
        if (element is null || captureMode == CaptureMode.None)
        {
            element = null;
            captureMode = CaptureMode.None;
        }
        else if (!element.IsEnabled || element is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
        {
            return false;
        }

        if (ReferenceEquals(_captured, element) && _captureMode == captureMode)
            return true;

        IInputElement? previous = _captured;
        _captured = element;
        _captureMode = captureMode;
        _directlyOver = ResolveDirectlyOver(_rawDirectlyOver);
        OnCapture(element, captureMode);

        if (previous is not null)
        {
            previous.RaiseEvent(new TouchEventArgs(this, Environment.TickCount)
            {
                RoutedEvent = UIElement.LostTouchCaptureEvent,
                Source = previous,
            });
        }
        if (element is not null)
        {
            element.RaiseEvent(new TouchEventArgs(this, Environment.TickCount)
            {
                RoutedEvent = UIElement.GotTouchCaptureEvent,
                Source = element,
            });
        }
        Synchronize();
        return true;
    }

    protected virtual void OnCapture(IInputElement? element, CaptureMode captureMode)
    {
    }

    protected void Activate()
    {
        if (_isActive)
            throw new InvalidOperationException("The touch device is already active.");
        lock (ActiveDeviceGate)
            ActiveDeviceList.Add(this);
        _isActive = true;
        Activated?.Invoke(this, EventArgs.Empty);
    }

    protected void Deactivate()
    {
        if (!_isActive)
            throw new InvalidOperationException("The touch device is not active.");
        Capture(null);
        lock (ActiveDeviceGate)
            ActiveDeviceList.Remove(this);
        _isActive = false;
        Deactivated?.Invoke(this, EventArgs.Empty);
    }

    protected bool ReportDown() => Report(TouchAction.Down, UIElement.PreviewTouchDownEvent, UIElement.TouchDownEvent);
    protected bool ReportMove() => Report(TouchAction.Move, UIElement.PreviewTouchMoveEvent, UIElement.TouchMoveEvent);
    protected bool ReportUp() => Report(TouchAction.Up, UIElement.PreviewTouchUpEvent, UIElement.TouchUpEvent);

    protected void SetActiveSource(PresentationSource? activeSource) => _activeSource = activeSource;

    public void Synchronize()
    {
        if (_activeSource?.IsDisposed == true)
            return;
        OnUpdated();
        ReportFrame(_lastTimestamp == 0 ? Environment.TickCount : _lastTimestamp);
    }

    protected virtual void OnManipulationStarted()
    {
    }

    protected virtual void OnManipulationEnded(bool cancel)
    {
        if (_captured is not null)
            Capture(null);
    }

    Point IManipulator.GetPosition(IInputElement? relativeTo) => GetTouchPoint(relativeTo).Position;
    void IManipulator.ManipulationEnded(bool cancel) => OnManipulationEnded(cancel);

    public void UpdatePosition(Point newPosition)
    {
        _previousPosition = _position;
        _position = newPosition;
    }

    internal void RetargetTo(UIElement? target) => SetDirectlyOver(target);
    internal void SetDirectlyOver(IInputElement? directlyOver)
    {
        _rawDirectlyOver = directlyOver;
        _directlyOver = ResolveDirectlyOver(directlyOver);
    }

    internal void RecordFrame(StylusPointCollection? stylusPoints, Rect contactRect, TouchAction action, int timestamp)
    {
        _lastStylusPoints = stylusPoints?.Clone();
        _lastContactRect = contactRect;
        _lastAction = action;
        _lastTimestamp = timestamp;
        OnUpdated();
        ReportFrame(timestamp);
    }

    internal void ActivateForManager()
    {
        if (!_isActive)
            Activate();
    }

    internal void DeactivateForManager()
    {
        if (_isActive)
            Deactivate();
    }

    protected Point CurrentPosition => _position;
    protected StylusPointCollection? LastStylusPoints => _lastStylusPoints;
    protected Rect LastContactRect => _lastContactRect;
    protected TouchAction LastAction => _lastAction;

    internal static TouchPointCollection GetTouchPoints(IInputElement? relativeTo)
    {
        TouchDevice[] devices;
        lock (ActiveDeviceGate)
            devices = ActiveDeviceList.ToArray();
        TouchPointCollection points = new();
        foreach (TouchDevice device in devices)
            points.Add(device.GetTouchPoint(relativeTo));
        return points;
    }

    internal static TouchPoint? GetPrimaryTouchPoint(IInputElement? relativeTo)
    {
        lock (ActiveDeviceGate)
            return ActiveDeviceList.Count == 0 ? null : ActiveDeviceList[0].GetTouchPoint(relativeTo);
    }

    internal static event Action<int>? FrameUpdated;

    private bool Report(TouchAction action, RoutedEvent previewEvent, RoutedEvent bubbleEvent)
    {
        _lastAction = action;
        IInputElement? target = _captured ?? _directlyOver;
        bool handled = false;
        if (target is not null)
        {
            TouchEventArgs preview = new(this, _lastTimestamp == 0 ? Environment.TickCount : _lastTimestamp)
            {
                RoutedEvent = previewEvent,
            };
            target.RaiseEvent(preview);
            handled = preview.Handled;
            if (!preview.Handled)
            {
                TouchEventArgs bubble = new(this, preview.Timestamp) { RoutedEvent = bubbleEvent };
                target.RaiseEvent(bubble);
                handled |= bubble.Handled;
            }
        }
        OnUpdated();
        ReportFrame(_lastTimestamp == 0 ? Environment.TickCount : _lastTimestamp);
        return handled;
    }

    private void OnUpdated() => Updated?.Invoke(this, EventArgs.Empty);
    private static void ReportFrame(int timestamp) => FrameUpdated?.Invoke(timestamp);

    private IInputElement? ResolveDirectlyOver(IInputElement? rawDirectlyOver)
    {
        if (_captured is null || _captureMode == CaptureMode.None)
            return rawDirectlyOver;
        if (_captureMode == CaptureMode.Element)
            return _captured;
        return IsWithinCapturedSubtree(rawDirectlyOver, _captured) ? rawDirectlyOver : _captured;
    }

    private static bool IsWithinCapturedSubtree(IInputElement? candidate, IInputElement captured)
    {
        if (ReferenceEquals(candidate, captured))
            return true;
        if (candidate is not Visual visual || captured is not Visual capturedVisual)
            return false;
        Visual? current = visual.VisualParent;
        while (current is not null)
        {
            if (ReferenceEquals(current, capturedVisual))
                return true;
            current = current.VisualParent;
        }
        return false;
    }
}

/// <summary>Touch device populated by the platform pointer pipeline.</summary>
internal sealed class PointerTouchDevice : TouchDevice
{
    internal PointerTouchDevice(int id, UIElement? target)
        : base(id)
    {
        SetDirectlyOver(target);
        ActivateForManager();
    }

    public override TouchPoint GetTouchPoint(IInputElement? relativeTo)
    {
        Point position = InputCoordinateHelper.FromRoot(CurrentPosition, relativeTo);
        Rect bounds = TransformRect(LastContactRect, position, relativeTo);
        return new TouchPoint(this, position, bounds, LastAction);
    }

    public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)
    {
        StylusPointCollection? packets = LastStylusPoints;
        if (packets is null || packets.Count == 0)
            return new TouchPointCollection { GetTouchPoint(relativeTo) };

        TouchPointCollection points = new();
        for (int index = 0; index < packets.Count; index++)
        {
            Point position = InputCoordinateHelper.FromRoot(packets[index].ToPoint(), relativeTo);
            Rect bounds = TransformRect(LastContactRect, position, relativeTo);
            TouchAction action = index == packets.Count - 1 ? LastAction : TouchAction.Move;
            points.Add(new TouchPoint(this, position, bounds, action));
        }
        return points;
    }

    private static Rect TransformRect(Rect rect, Point center, IInputElement? relativeTo)
    {
        if (rect.IsEmpty || relativeTo is null)
            return rect;
        return new Rect(center.X - rect.Width / 2, center.Y - rect.Height / 2, rect.Width, rect.Height);
    }
}

/// <summary>Represents one reported position and contact area for a touch device.</summary>
public class TouchPoint : IEquatable<TouchPoint>
{
    public TouchPoint(TouchDevice touchDevice, Point position, Rect bounds, TouchAction action)
    {
        TouchDevice = touchDevice ?? throw new ArgumentNullException(nameof(touchDevice));
        Position = position;
        Bounds = bounds;
        Action = action;
    }

    public TouchDevice TouchDevice { get; }
    public Point Position { get; }
    public Rect Bounds { get; }
    public Size Size => Bounds.Size;
    public TouchAction Action { get; }

    bool IEquatable<TouchPoint>.Equals(TouchPoint? other)
        => other is not null &&
           ReferenceEquals(other.TouchDevice, TouchDevice) &&
           other.Position == Position &&
           other.Bounds == Bounds &&
           other.Action == Action;
}

/// <summary>Collection of touch points.</summary>
public class TouchPointCollection : Collection<TouchPoint>
{
}

public enum TouchAction
{
    Down,
    Move,
    Up,
    Cancel,
}

public sealed class TouchCapabilities
{
    public bool TouchPresent { get; init; }
    public int Contacts { get; init; }
}

/// <summary>Event data for touch routed events.</summary>
public sealed class TouchEventArgs : InputEventArgs
{
    public TouchEventArgs(TouchDevice touchDevice, int timestamp)
        : base(touchDevice, timestamp)
    {
        TouchDevice = touchDevice ?? throw new ArgumentNullException(nameof(touchDevice));
    }

    public TouchDevice TouchDevice { get; }
    public bool Cancel { get; set; }
    public TouchPoint GetTouchPoint(IInputElement? relativeTo) => TouchDevice.GetTouchPoint(relativeTo);
    public TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo) => TouchDevice.GetIntermediateTouchPoints(relativeTo);

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is TouchEventHandler touchHandler)
            touchHandler(target, this);
        else
            base.InvokeEventHandler(handler, target);
    }
}

public delegate void TouchEventHandler(object sender, TouchEventArgs e);
