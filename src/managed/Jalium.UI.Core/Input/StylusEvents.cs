using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for stylus input events.
/// </summary>
public class StylusEventArgs : InputEventArgs
{
    public StylusEventArgs(StylusDevice stylusDevice, int timestamp) : base(stylusDevice, timestamp)
    {
        StylusDevice = stylusDevice ?? throw new ArgumentNullException(nameof(stylusDevice));
    }

    public StylusDevice StylusDevice { get; }
    public bool InAir => StylusDevice?.InAir ?? false;
    public bool Inverted => StylusDevice?.Inverted ?? false;

    /// <summary>
    /// Gets or sets a value indicating whether downstream pointer promotion should be canceled.
    /// </summary>
    public bool Cancel { get; set; }

    public Point GetPosition(IInputElement? relativeTo)
        => StylusDevice.GetPosition(relativeTo);

    public StylusPointCollection GetStylusPoints(IInputElement? relativeTo)
        => StylusDevice?.GetStylusPoints(relativeTo) ?? new StylusPointCollection();

    public StylusPointCollection GetStylusPoints(
        IInputElement? relativeTo,
        StylusPointDescription subsetToReformatTo)
        => StylusDevice.GetStylusPoints(relativeTo, subsetToReformatTo);

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is StylusEventHandler stylusHandler)
        {
            stylusHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

public sealed class StylusDownEventArgs : StylusEventArgs
{
    public StylusDownEventArgs(StylusDevice stylusDevice, int timestamp) : base(stylusDevice, timestamp) { }

    public int TapCount { get; init; } = 1;

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is StylusDownEventHandler stylusHandler)
        {
            stylusHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

public sealed class StylusButtonEventArgs : StylusEventArgs
{
    public StylusButtonEventArgs(StylusDevice stylusDevice, int timestamp, StylusButton stylusButton) : base(stylusDevice, timestamp)
    {
        StylusButton = stylusButton;
    }

    public StylusButton StylusButton { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is StylusButtonEventHandler stylusHandler)
        {
            stylusHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

public sealed class StylusSystemGestureEventArgs : StylusEventArgs
{
    public StylusSystemGestureEventArgs(StylusDevice stylusDevice, int timestamp, SystemGesture systemGesture) : base(stylusDevice, timestamp)
    {
        SystemGesture = systemGesture;
    }

    public SystemGesture SystemGesture { get; }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is StylusSystemGestureEventHandler stylusHandler)
        {
            stylusHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

public enum SystemGesture
{
    None = 0,
    Tap = 16,
    DoubleTap = 17,
    RightTap = 18,
    Drag = 19,
    RightDrag = 20,
    HoldEnter = 21,
    HoldLeave = 22,
    HoverEnter = 23,
    HoverLeave = 24,
    Flick = 31,
    TwoFingerTap = 4352,
}

/// <summary>
/// Provides data for stylus routed events.
/// </summary>
public static class Stylus
{
    public static readonly RoutedEvent PreviewStylusDownEvent = Register(nameof(PreviewStylusDownEvent), RoutingStrategy.Tunnel, typeof(StylusDownEventHandler));
    public static readonly RoutedEvent StylusDownEvent = Register(nameof(StylusDownEvent), RoutingStrategy.Bubble, typeof(StylusDownEventHandler));
    public static readonly RoutedEvent PreviewStylusMoveEvent = Register(nameof(PreviewStylusMoveEvent), RoutingStrategy.Tunnel, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusMoveEvent = Register(nameof(StylusMoveEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));
    public static readonly RoutedEvent PreviewStylusUpEvent = Register(nameof(PreviewStylusUpEvent), RoutingStrategy.Tunnel, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusUpEvent = Register(nameof(StylusUpEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));
    public static readonly RoutedEvent PreviewStylusInAirMoveEvent = Register(nameof(PreviewStylusInAirMoveEvent), RoutingStrategy.Tunnel, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusInAirMoveEvent = Register(nameof(StylusInAirMoveEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusEnterEvent = Register(nameof(StylusEnterEvent), RoutingStrategy.Direct, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusLeaveEvent = Register(nameof(StylusLeaveEvent), RoutingStrategy.Direct, typeof(StylusEventHandler));
    public static readonly RoutedEvent PreviewStylusInRangeEvent = Register(nameof(PreviewStylusInRangeEvent), RoutingStrategy.Tunnel, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusInRangeEvent = Register(nameof(StylusInRangeEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));
    public static readonly RoutedEvent PreviewStylusOutOfRangeEvent = Register(nameof(PreviewStylusOutOfRangeEvent), RoutingStrategy.Tunnel, typeof(StylusEventHandler));
    public static readonly RoutedEvent StylusOutOfRangeEvent = Register(nameof(StylusOutOfRangeEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));
    public static readonly RoutedEvent PreviewStylusButtonDownEvent = Register(nameof(PreviewStylusButtonDownEvent), RoutingStrategy.Tunnel, typeof(StylusButtonEventHandler));
    public static readonly RoutedEvent StylusButtonDownEvent = Register(nameof(StylusButtonDownEvent), RoutingStrategy.Bubble, typeof(StylusButtonEventHandler));
    public static readonly RoutedEvent PreviewStylusButtonUpEvent = Register(nameof(PreviewStylusButtonUpEvent), RoutingStrategy.Tunnel, typeof(StylusButtonEventHandler));
    public static readonly RoutedEvent StylusButtonUpEvent = Register(nameof(StylusButtonUpEvent), RoutingStrategy.Bubble, typeof(StylusButtonEventHandler));
    public static readonly RoutedEvent PreviewStylusSystemGestureEvent = Register(nameof(PreviewStylusSystemGestureEvent), RoutingStrategy.Tunnel, typeof(StylusSystemGestureEventHandler));
    public static readonly RoutedEvent StylusSystemGestureEvent = Register(nameof(StylusSystemGestureEvent), RoutingStrategy.Bubble, typeof(StylusSystemGestureEventHandler));
    public static readonly RoutedEvent GotStylusCaptureEvent = Register(nameof(GotStylusCaptureEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));
    public static readonly RoutedEvent LostStylusCaptureEvent = Register(nameof(LostStylusCaptureEvent), RoutingStrategy.Bubble, typeof(StylusEventHandler));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsFlicksEnabledProperty =
        DependencyProperty.RegisterAttached("IsFlicksEnabled", typeof(bool), typeof(Stylus), new PropertyMetadata(true));
    public static readonly DependencyProperty IsPressAndHoldEnabledProperty =
        DependencyProperty.RegisterAttached("IsPressAndHoldEnabled", typeof(bool), typeof(Stylus), new PropertyMetadata(true));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsTapFeedbackEnabledProperty =
        DependencyProperty.RegisterAttached("IsTapFeedbackEnabled", typeof(bool), typeof(Stylus), new PropertyMetadata(true));
    public static readonly DependencyProperty IsTouchFeedbackEnabledProperty =
        DependencyProperty.RegisterAttached("IsTouchFeedbackEnabled", typeof(bool), typeof(Stylus), new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetIsFlicksEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsFlicksEnabledProperty) is true;
    }
    public static void SetIsFlicksEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsFlicksEnabledProperty, value);
    }
    public static bool GetIsPressAndHoldEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsPressAndHoldEnabledProperty) is true;
    }
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsPressAndHoldEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsPressAndHoldEnabledProperty, value);
    }
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetIsTapFeedbackEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsTapFeedbackEnabledProperty) is true;
    }
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsTapFeedbackEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsTapFeedbackEnabledProperty, value);
    }
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetIsTouchFeedbackEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsTouchFeedbackEnabledProperty) is true;
    }
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsTouchFeedbackEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsTouchFeedbackEnabledProperty, value);
    }

    public static StylusDevice? CurrentStylusDevice => Tablet.CurrentStylusDevice;

    /// <summary>Gets the input element that currently owns stylus capture.</summary>
    public static IInputElement? Captured => CurrentStylusDevice?.Captured;

    /// <summary>Gets the input element currently under the stylus.</summary>
    public static IInputElement? DirectlyOver => CurrentStylusDevice?.DirectlyOver;

    public static bool Capture(IInputElement? element) => Capture(element, CaptureMode.Element);

    public static bool Capture(IInputElement? element, CaptureMode captureMode)
    {
        if (!Enum.IsDefined(captureMode))
            throw new System.ComponentModel.InvalidEnumArgumentException(nameof(captureMode), (int)captureMode, typeof(CaptureMode));
        return CurrentStylusDevice?.Capture(element, captureMode) == true;
    }

    public static void Synchronize() => CurrentStylusDevice?.Synchronize();

    public static void AddGotStylusCaptureHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, GotStylusCaptureEvent, handler);
    public static void RemoveGotStylusCaptureHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, GotStylusCaptureEvent, handler);
    public static void AddLostStylusCaptureHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, LostStylusCaptureEvent, handler);
    public static void RemoveLostStylusCaptureHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, LostStylusCaptureEvent, handler);
    public static void AddPreviewStylusButtonDownHandler(DependencyObject element, StylusButtonEventHandler handler) => AddHandler(element, PreviewStylusButtonDownEvent, handler);
    public static void RemovePreviewStylusButtonDownHandler(DependencyObject element, StylusButtonEventHandler handler) => RemoveHandler(element, PreviewStylusButtonDownEvent, handler);
    public static void AddPreviewStylusButtonUpHandler(DependencyObject element, StylusButtonEventHandler handler) => AddHandler(element, PreviewStylusButtonUpEvent, handler);
    public static void RemovePreviewStylusButtonUpHandler(DependencyObject element, StylusButtonEventHandler handler) => RemoveHandler(element, PreviewStylusButtonUpEvent, handler);
    public static void AddPreviewStylusDownHandler(DependencyObject element, StylusDownEventHandler handler) => AddHandler(element, PreviewStylusDownEvent, handler);
    public static void RemovePreviewStylusDownHandler(DependencyObject element, StylusDownEventHandler handler) => RemoveHandler(element, PreviewStylusDownEvent, handler);
    public static void AddPreviewStylusInAirMoveHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, PreviewStylusInAirMoveEvent, handler);
    public static void RemovePreviewStylusInAirMoveHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, PreviewStylusInAirMoveEvent, handler);
    public static void AddPreviewStylusInRangeHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, PreviewStylusInRangeEvent, handler);
    public static void RemovePreviewStylusInRangeHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, PreviewStylusInRangeEvent, handler);
    public static void AddPreviewStylusMoveHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, PreviewStylusMoveEvent, handler);
    public static void RemovePreviewStylusMoveHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, PreviewStylusMoveEvent, handler);
    public static void AddPreviewStylusOutOfRangeHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, PreviewStylusOutOfRangeEvent, handler);
    public static void RemovePreviewStylusOutOfRangeHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, PreviewStylusOutOfRangeEvent, handler);
    public static void AddPreviewStylusSystemGestureHandler(DependencyObject element, StylusSystemGestureEventHandler handler) => AddHandler(element, PreviewStylusSystemGestureEvent, handler);
    public static void RemovePreviewStylusSystemGestureHandler(DependencyObject element, StylusSystemGestureEventHandler handler) => RemoveHandler(element, PreviewStylusSystemGestureEvent, handler);
    public static void AddPreviewStylusUpHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, PreviewStylusUpEvent, handler);
    public static void RemovePreviewStylusUpHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, PreviewStylusUpEvent, handler);
    public static void AddStylusButtonDownHandler(DependencyObject element, StylusButtonEventHandler handler) => AddHandler(element, StylusButtonDownEvent, handler);
    public static void RemoveStylusButtonDownHandler(DependencyObject element, StylusButtonEventHandler handler) => RemoveHandler(element, StylusButtonDownEvent, handler);
    public static void AddStylusButtonUpHandler(DependencyObject element, StylusButtonEventHandler handler) => AddHandler(element, StylusButtonUpEvent, handler);
    public static void RemoveStylusButtonUpHandler(DependencyObject element, StylusButtonEventHandler handler) => RemoveHandler(element, StylusButtonUpEvent, handler);
    public static void AddStylusDownHandler(DependencyObject element, StylusDownEventHandler handler) => AddHandler(element, StylusDownEvent, handler);
    public static void RemoveStylusDownHandler(DependencyObject element, StylusDownEventHandler handler) => RemoveHandler(element, StylusDownEvent, handler);
    public static void AddStylusEnterHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusEnterEvent, handler);
    public static void RemoveStylusEnterHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusEnterEvent, handler);
    public static void AddStylusInAirMoveHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusInAirMoveEvent, handler);
    public static void RemoveStylusInAirMoveHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusInAirMoveEvent, handler);
    public static void AddStylusInRangeHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusInRangeEvent, handler);
    public static void RemoveStylusInRangeHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusInRangeEvent, handler);
    public static void AddStylusLeaveHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusLeaveEvent, handler);
    public static void RemoveStylusLeaveHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusLeaveEvent, handler);
    public static void AddStylusMoveHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusMoveEvent, handler);
    public static void RemoveStylusMoveHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusMoveEvent, handler);
    public static void AddStylusOutOfRangeHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusOutOfRangeEvent, handler);
    public static void RemoveStylusOutOfRangeHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusOutOfRangeEvent, handler);
    public static void AddStylusSystemGestureHandler(DependencyObject element, StylusSystemGestureEventHandler handler) => AddHandler(element, StylusSystemGestureEvent, handler);
    public static void RemoveStylusSystemGestureHandler(DependencyObject element, StylusSystemGestureEventHandler handler) => RemoveHandler(element, StylusSystemGestureEvent, handler);
    public static void AddStylusUpHandler(DependencyObject element, StylusEventHandler handler) => AddHandler(element, StylusUpEvent, handler);
    public static void RemoveStylusUpHandler(DependencyObject element, StylusEventHandler handler) => RemoveHandler(element, StylusUpEvent, handler);

    private static void AddHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not IInputElement inputElement)
            throw new ArgumentException("The element must implement IInputElement.", nameof(element));
        inputElement.AddHandler(routedEvent, handler);
    }

    private static void RemoveHandler(DependencyObject element, RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not IInputElement inputElement)
            throw new ArgumentException("The element must implement IInputElement.", nameof(element));
        inputElement.RemoveHandler(routedEvent, handler);
    }

    private static RoutedEvent Register(string fieldName, RoutingStrategy strategy, Type handlerType) =>
        EventManager.RegisterRoutedEvent(
            fieldName.EndsWith("Event", StringComparison.Ordinal) ? fieldName[..^5] : fieldName,
            strategy,
            handlerType,
            typeof(Stylus));
}

public delegate void StylusEventHandler(object sender, StylusEventArgs e);
public delegate void StylusDownEventHandler(object sender, StylusDownEventArgs e);
public delegate void StylusButtonEventHandler(object sender, StylusButtonEventArgs e);
public delegate void StylusSystemGestureEventHandler(object sender, StylusSystemGestureEventArgs e);
