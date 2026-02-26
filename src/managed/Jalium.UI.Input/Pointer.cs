namespace Jalium.UI.Input;

/// <summary>
/// Specifies the type of pointer device.
/// </summary>
public enum PointerDeviceType
{
    /// <summary>
    /// A touch pointer.
    /// </summary>
    Touch,

    /// <summary>
    /// A pen/stylus pointer.
    /// </summary>
    Pen,

    /// <summary>
    /// A mouse pointer.
    /// </summary>
    Mouse
}

/// <summary>
/// Specifies pointer update kinds.
/// </summary>
public enum PointerUpdateKind
{
    /// <summary>
    /// Other or unknown update.
    /// </summary>
    Other,

    /// <summary>
    /// Left button pressed.
    /// </summary>
    LeftButtonPressed,

    /// <summary>
    /// Left button released.
    /// </summary>
    LeftButtonReleased,

    /// <summary>
    /// Right button pressed.
    /// </summary>
    RightButtonPressed,

    /// <summary>
    /// Right button released.
    /// </summary>
    RightButtonReleased,

    /// <summary>
    /// Middle button pressed.
    /// </summary>
    MiddleButtonPressed,

    /// <summary>
    /// Middle button released.
    /// </summary>
    MiddleButtonReleased,

    /// <summary>
    /// XButton1 pressed.
    /// </summary>
    XButton1Pressed,

    /// <summary>
    /// XButton1 released.
    /// </summary>
    XButton1Released,

    /// <summary>
    /// XButton2 pressed.
    /// </summary>
    XButton2Pressed,

    /// <summary>
    /// XButton2 released.
    /// </summary>
    XButton2Released
}

/// <summary>
/// Represents a pointer input point.
/// </summary>
public sealed class PointerPoint
{
    /// <summary>
    /// Gets the unique ID of the pointer.
    /// </summary>
    public uint PointerId { get; }

    /// <summary>
    /// Gets the position of the pointer.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the type of pointer device.
    /// </summary>
    public PointerDeviceType PointerDeviceType { get; }

    /// <summary>
    /// Gets a value indicating whether the pointer is in contact.
    /// </summary>
    public bool IsInContact { get; }

    /// <summary>
    /// Gets the pointer properties.
    /// </summary>
    public PointerPointProperties Properties { get; }

    /// <summary>
    /// Gets the timestamp of this point.
    /// </summary>
    public ulong Timestamp { get; }

    /// <summary>
    /// Gets the frame ID.
    /// </summary>
    public uint FrameId { get; }

    /// <summary>
    /// Initializes a new instance of the PointerPoint class.
    /// </summary>
    public PointerPoint(
        uint pointerId,
        Point position,
        PointerDeviceType deviceType,
        bool isInContact,
        PointerPointProperties properties,
        ulong timestamp,
        uint frameId = 0)
    {
        PointerId = pointerId;
        Position = position;
        PointerDeviceType = deviceType;
        IsInContact = isInContact;
        Properties = properties;
        Timestamp = timestamp;
        FrameId = frameId;
    }

    /// <summary>
    /// Gets the position relative to the specified element.
    /// </summary>
    public Point GetPosition(UIElement? relativeTo)
    {
        // In a real implementation, this would transform coordinates
        return Position;
    }
}

/// <summary>
/// Properties associated with a pointer point.
/// </summary>
public sealed class PointerPointProperties
{
    /// <summary>
    /// Gets the pressure of the pointer (0.0 - 1.0).
    /// </summary>
    public float Pressure { get; init; } = 1.0f;

    /// <summary>
    /// Gets a value indicating whether the left button is pressed.
    /// </summary>
    public bool IsLeftButtonPressed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the right button is pressed.
    /// </summary>
    public bool IsRightButtonPressed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the middle button is pressed.
    /// </summary>
    public bool IsMiddleButtonPressed { get; init; }

    /// <summary>
    /// Gets a value indicating whether XButton1 is pressed.
    /// </summary>
    public bool IsXButton1Pressed { get; init; }

    /// <summary>
    /// Gets a value indicating whether XButton2 is pressed.
    /// </summary>
    public bool IsXButton2Pressed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the barrel button is pressed (for pens).
    /// </summary>
    public bool IsBarrelButtonPressed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the eraser is active (for pens).
    /// </summary>
    public bool IsEraser { get; init; }

    /// <summary>
    /// Gets a value indicating whether the primary button is pressed.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Gets a value indicating whether the input is inverted.
    /// </summary>
    public bool IsInverted { get; init; }

    /// <summary>
    /// Gets the contact area rectangle.
    /// </summary>
    public Rect ContactRect { get; init; }

    /// <summary>
    /// Gets the raw contact area rectangle.
    /// </summary>
    public Rect ContactRectRaw { get; init; }

    /// <summary>
    /// Gets the X tilt in degrees (-90 to 90).
    /// </summary>
    public float XTilt { get; init; }

    /// <summary>
    /// Gets the Y tilt in degrees (-90 to 90).
    /// </summary>
    public float YTilt { get; init; }

    /// <summary>
    /// Gets the twist/rotation angle in degrees (0 to 359).
    /// </summary>
    public float Twist { get; init; }

    /// <summary>
    /// Gets the mouse wheel delta.
    /// </summary>
    public int MouseWheelDelta { get; init; }

    /// <summary>
    /// Gets the update kind.
    /// </summary>
    public PointerUpdateKind PointerUpdateKind { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pointer has pressure info.
    /// </summary>
    public bool HasPressure => Pressure != 1.0f;

    /// <summary>
    /// Gets a value indicating whether the pointer has tilt info.
    /// </summary>
    public bool HasTilt => XTilt != 0 || YTilt != 0;
}

/// <summary>
/// Event arguments for pointer events.
/// </summary>
public class PointerEventArgs : InputEventArgs
{
    /// <summary>
    /// Gets the current pointer point.
    /// </summary>
    public PointerPoint Pointer { get; }

    /// <summary>
    /// Gets the key modifiers active during the event.
    /// </summary>
    public ModifierKeys KeyModifiers { get; }

    /// <summary>
    /// Gets or sets a value indicating whether pointer promotion should be canceled.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Initializes a new instance of the PointerEventArgs class.
    /// </summary>
    public PointerEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(timestamp)
    {
        Pointer = pointer;
        KeyModifiers = modifiers;
    }

    /// <summary>
    /// Gets the current point relative to the specified element.
    /// </summary>
    public PointerPoint GetCurrentPoint(UIElement? relativeTo)
    {
        if (relativeTo == null)
            return Pointer;

        // Transform position to relative coordinates
        var relativePosition = Pointer.GetPosition(relativeTo);
        return new PointerPoint(
            Pointer.PointerId,
            relativePosition,
            Pointer.PointerDeviceType,
            Pointer.IsInContact,
            Pointer.Properties,
            Pointer.Timestamp,
            Pointer.FrameId);
    }

    /// <summary>
    /// Gets intermediate points since the last event.
    /// </summary>
    public IList<PointerPoint> GetIntermediatePoints(UIElement? relativeTo)
    {
        // In a real implementation, this would return all intermediate points
        return new List<PointerPoint> { GetCurrentPoint(relativeTo) };
    }
}

/// <summary>
/// Event arguments for pointer down events.
/// </summary>
public class PointerDownEventArgs : PointerEventArgs
{
    /// <summary>
    /// Gets a value indicating whether the pointer was pressed during this event.
    /// </summary>
    public bool IsPressed => true;

    /// <summary>
    /// Initializes a new instance of the PointerDownEventArgs class.
    /// </summary>
    public PointerDownEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer up events.
/// </summary>
public class PointerUpEventArgs : PointerEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerUpEventArgs class.
    /// </summary>
    public PointerUpEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer move events.
/// </summary>
public class PointerMoveEventArgs : PointerEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerMoveEventArgs class.
    /// </summary>
    public PointerMoveEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer cancel events.
/// </summary>
public class PointerCancelEventArgs : PointerEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerCancelEventArgs class.
    /// </summary>
    public PointerCancelEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer pressed events (legacy alias for PointerDown).
/// </summary>
public sealed class PointerPressedEventArgs : PointerDownEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerPressedEventArgs class.
    /// </summary>
    public PointerPressedEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer released events (legacy alias for PointerUp).
/// </summary>
public sealed class PointerReleasedEventArgs : PointerUpEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerReleasedEventArgs class.
    /// </summary>
    public PointerReleasedEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer moved events (legacy alias for PointerMove).
/// </summary>
public sealed class PointerMovedEventArgs : PointerMoveEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerMovedEventArgs class.
    /// </summary>
    public PointerMovedEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer wheel changed events.
/// </summary>
public sealed class PointerWheelChangedEventArgs : PointerEventArgs
{
    /// <summary>
    /// Gets the mouse wheel delta.
    /// </summary>
    public int Delta => Pointer.Properties.MouseWheelDelta;

    /// <summary>
    /// Initializes a new instance of the PointerWheelChangedEventArgs class.
    /// </summary>
    public PointerWheelChangedEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event arguments for pointer capture lost events.
/// </summary>
public sealed class PointerCaptureLostEventArgs : PointerEventArgs
{
    /// <summary>
    /// Initializes a new instance of the PointerCaptureLostEventArgs class.
    /// </summary>
    public PointerCaptureLostEventArgs(PointerPoint pointer, ModifierKeys modifiers, int timestamp)
        : base(pointer, modifiers, timestamp)
    {
    }
}

/// <summary>
/// Event handler delegates for pointer events.
/// </summary>
public delegate void PointerEventHandler(object sender, PointerEventArgs e);
public delegate void PointerDownEventHandler(object sender, PointerDownEventArgs e);
public delegate void PointerUpEventHandler(object sender, PointerUpEventArgs e);
public delegate void PointerMoveEventHandler(object sender, PointerMoveEventArgs e);
public delegate void PointerCancelEventHandler(object sender, PointerCancelEventArgs e);
public delegate void PointerPressedEventHandler(object sender, PointerPressedEventArgs e);
public delegate void PointerReleasedEventHandler(object sender, PointerReleasedEventArgs e);
public delegate void PointerMovedEventHandler(object sender, PointerMovedEventArgs e);
public delegate void PointerWheelChangedEventHandler(object sender, PointerWheelChangedEventArgs e);
public delegate void PointerCaptureLostEventHandler(object sender, PointerCaptureLostEventArgs e);

/// <summary>
/// Routed events for pointer input.
/// </summary>
public static class PointerEvents
{
    /// <summary>
    /// Identifies the PreviewPointerDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerDownEvent =
        UIElement.PreviewPointerDownEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerDown routed event.
    /// </summary>
    public static readonly RoutedEvent PointerDownEvent =
        UIElement.PointerDownEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerMoveEvent =
        UIElement.PreviewPointerMoveEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerMove routed event.
    /// </summary>
    public static readonly RoutedEvent PointerMoveEvent =
        UIElement.PointerMoveEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerUpEvent =
        UIElement.PreviewPointerUpEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerUp routed event.
    /// </summary>
    public static readonly RoutedEvent PointerUpEvent =
        UIElement.PointerUpEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerCancel routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerCancelEvent =
        UIElement.PreviewPointerCancelEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerCancel routed event.
    /// </summary>
    public static readonly RoutedEvent PointerCancelEvent =
        UIElement.PointerCancelEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerPressed routed event (legacy alias for PointerDown).
    /// </summary>
    public static readonly RoutedEvent PointerPressedEvent =
        UIElement.PointerPressedEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerMoved routed event (legacy alias for PointerMove).
    /// </summary>
    public static readonly RoutedEvent PointerMovedEvent =
        UIElement.PointerMovedEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerReleased routed event (legacy alias for PointerUp).
    /// </summary>
    public static readonly RoutedEvent PointerReleasedEvent =
        UIElement.PointerReleasedEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PointerEntered routed event.
    /// </summary>
    public static readonly RoutedEvent PointerEnteredEvent =
        EventManager.RegisterRoutedEvent("PointerEntered", RoutingStrategy.Direct,
            typeof(PointerEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerExited routed event.
    /// </summary>
    public static readonly RoutedEvent PointerExitedEvent =
        EventManager.RegisterRoutedEvent("PointerExited", RoutingStrategy.Direct,
            typeof(PointerEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerWheelChanged routed event.
    /// </summary>
    public static readonly RoutedEvent PointerWheelChangedEvent =
        EventManager.RegisterRoutedEvent("PointerWheelChanged", RoutingStrategy.Bubble,
            typeof(PointerWheelChangedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerCaptureLost routed event.
    /// </summary>
    public static readonly RoutedEvent PointerCaptureLostEvent =
        EventManager.RegisterRoutedEvent("PointerCaptureLost", RoutingStrategy.Direct,
            typeof(PointerCaptureLostEventHandler), typeof(UIElement));
}
