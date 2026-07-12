using Jalium.UI.Input;

namespace Jalium.UI;

public partial class UIElement
{
    // ─────────────────────────────────────────────────────────────
    //  Touch capture & over tracking.
    //  Multiple touch contacts can exist simultaneously. Each contact is
    //  tracked per-pointer-id rather than as a single static slot like the
    //  mouse, so a static dictionary keyed by id holds capture and each
    //  UIElement holds three lazily-allocated TouchDevice lists for the
    //  contacts it currently owns or covers.
    // ─────────────────────────────────────────────────────────────

    private readonly struct CaptureRecord
    {
        public CaptureRecord(UIElement element, TouchDevice device) { Element = element; Device = device; }
        public UIElement Element { get; }
        public TouchDevice Device { get; }
    }

    private static readonly Dictionary<int, CaptureRecord> s_touchCaptures = new();

    private List<TouchDevice>? _touchesOver;
    private List<TouchDevice>? _touchesDirectlyOver;
    private List<TouchDevice>? _touchesCaptured;

    // ── CLR event wrappers ──

    /// <summary>Occurs when a touch contact enters the bounds of this element.</summary>
    public event TouchEventHandler TouchEnter
    {
        add => AddHandler(TouchEnterEvent, value);
        remove => RemoveHandler(TouchEnterEvent, value);
    }

    /// <summary>Occurs when a touch contact leaves the bounds of this element.</summary>
    public event TouchEventHandler TouchLeave
    {
        add => AddHandler(TouchLeaveEvent, value);
        remove => RemoveHandler(TouchLeaveEvent, value);
    }

    /// <summary>Occurs when this element acquires capture of a touch contact.</summary>
    public event TouchEventHandler GotTouchCapture
    {
        add => AddHandler(GotTouchCaptureEvent, value);
        remove => RemoveHandler(GotTouchCaptureEvent, value);
    }

    /// <summary>Occurs when a captured touch contact is released from this element.</summary>
    public event TouchEventHandler LostTouchCapture
    {
        add => AddHandler(LostTouchCaptureEvent, value);
        remove => RemoveHandler(LostTouchCaptureEvent, value);
    }

    // ── Public capture/query API ──

    /// <summary>Gets the touch contacts captured to this element.</summary>
    public IEnumerable<TouchDevice> TouchesCaptured => _touchesCaptured ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Gets the touch contacts whose primary hit target is this element.</summary>
    public IEnumerable<TouchDevice> TouchesDirectlyOver => _touchesDirectlyOver ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Gets the touch contacts currently over this element or any of its descendants.</summary>
    public IEnumerable<TouchDevice> TouchesOver => _touchesOver ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Gets the touch contacts captured to this element or any of its descendants.</summary>
    public IEnumerable<TouchDevice> TouchesCapturedWithin
    {
        get
        {
            foreach (var pair in s_touchCaptures)
            {
                if (IsSelfOrDescendant(pair.Value.Element, this))
                    yield return pair.Value.Device;
            }
        }
    }

    /// <summary>True if any touch contact is captured to this element.</summary>
    public bool AreAnyTouchesCaptured => _touchesCaptured is { Count: > 0 };

    /// <summary>True if any touch contact is captured to this element or any of its descendants.</summary>
    public bool AreAnyTouchesCapturedWithin
    {
        get
        {
            foreach (var pair in s_touchCaptures)
            {
                if (IsSelfOrDescendant(pair.Value.Element, this))
                    return true;
            }
            return false;
        }
    }

    /// <summary>True if any touch contact is directly over this element.</summary>
    public bool AreAnyTouchesDirectlyOver => _touchesDirectlyOver is { Count: > 0 };

    /// <summary>True if any touch contact is over this element or any of its descendants.</summary>
    public bool AreAnyTouchesOver
    {
        get
        {
            if (_touchesOver is { Count: > 0 })
            {
                return true;
            }

            for (var index = 0; index < VisualChildrenCount; index++)
            {
                if (GetVisualChild(index) is UIElement child && child.AreAnyTouchesOver)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Captures the specified touch contact to this element. Any prior capture for that
    /// contact is released, raising a paired LostTouchCapture / GotTouchCapture.
    /// </summary>
    public bool CaptureTouch(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!IsEnabled || Visibility != Visibility.Visible)
            return false;

        if (s_touchCaptures.TryGetValue(touchDevice.Id, out var previous))
        {
            if (ReferenceEquals(previous.Element, this))
                return true;
            previous.Element.RemoveCapturedTouchInternal(touchDevice);
        }

        s_touchCaptures[touchDevice.Id] = new CaptureRecord(this, touchDevice);
        AddCapturedTouchInternal(touchDevice);
        touchDevice.Capture(this);
        return true;
    }

    /// <summary>Releases a previously captured touch contact from this element.</summary>
    public bool ReleaseTouchCapture(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!s_touchCaptures.TryGetValue(touchDevice.Id, out var record) || !ReferenceEquals(record.Element, this))
            return false;
        s_touchCaptures.Remove(touchDevice.Id);
        RemoveCapturedTouchInternal(touchDevice);
        touchDevice.Capture(null);
        return true;
    }

    /// <summary>Releases all touch contacts captured by this element.</summary>
    public void ReleaseAllTouchCaptures()
    {
        if (_touchesCaptured == null || _touchesCaptured.Count == 0)
            return;
        // Snapshot to allow mutation while raising events.
        var devices = _touchesCaptured.ToArray();
        foreach (var device in devices)
        {
            ReleaseTouchCapture(device);
        }
    }

    /// <summary>Returns the element that has captured the specified touch contact, or null.</summary>
    public static UIElement? GetTouchCapture(int touchId)
    {
        return s_touchCaptures.TryGetValue(touchId, out var record) ? record.Element : null;
    }

    /// <summary>Forces release of all touch captures. Invoked on window deactivation / capture loss.</summary>
    internal static void ForceReleaseAllTouchCaptures()
    {
        if (s_touchCaptures.Count == 0) return;
        var snapshot = s_touchCaptures.ToArray();
        s_touchCaptures.Clear();
        foreach (var pair in snapshot)
        {
            CaptureRecord record = pair.Value;
            record.Element.RemoveCapturedTouchInternal(record.Device);
            record.Device.Capture(null);
        }
    }

    // ── Internal hooks used by the input dispatcher ──

    internal void AddCapturedTouchInternal(TouchDevice device)
    {
        (_touchesCaptured ??= new List<TouchDevice>(1)).Add(device);
        UpdateTouchDependencyState();
    }

    internal void RemoveCapturedTouchInternal(TouchDevice device)
    {
        _touchesCaptured?.Remove(device);
        UpdateTouchDependencyState();
    }

    internal void AddDirectlyOverTouchInternal(TouchDevice device)
    {
        (_touchesDirectlyOver ??= new List<TouchDevice>(1)).Add(device);
        UpdateTouchDependencyState();
    }

    internal void RemoveDirectlyOverTouchInternal(TouchDevice device)
    {
        _touchesDirectlyOver?.Remove(device);
        UpdateTouchDependencyState();
    }

    internal void AddOverTouchInternal(TouchDevice device)
    {
        (_touchesOver ??= new List<TouchDevice>(1)).Add(device);
        UpdateTouchDependencyState();
    }

    internal void RemoveOverTouchInternal(TouchDevice device)
    {
        _touchesOver?.Remove(device);
        UpdateTouchDependencyState();
    }

    internal void RaiseGotTouchCapture(TouchDevice device)
    {
        var args = new TouchEventArgs(device, Environment.TickCount) { RoutedEvent = GotTouchCaptureEvent };
        RaiseEvent(args);
    }

    internal void RaiseLostTouchCapture(TouchDevice device)
    {
        var args = new TouchEventArgs(device, Environment.TickCount) { RoutedEvent = LostTouchCaptureEvent };
        RaiseEvent(args);
    }

    private static bool IsSelfOrDescendant(UIElement candidate, UIElement reference)
    {
        Visual? current = candidate;
        while (current != null)
        {
            if (ReferenceEquals(current, reference)) return true;
            current = current.VisualParent;
        }
        return false;
    }
}
