using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides data for stylus-related events.
/// </summary>
public class StylusEventArgs : InputEventArgs
{
    public StylusEventArgs(StylusDevice stylusDevice, int timestamp) : base(timestamp)
    {
        StylusDevice = stylusDevice;
    }

    public StylusDevice StylusDevice { get; }
    public bool InAir => StylusDevice?.InAir ?? false;
    public bool Inverted => StylusDevice?.Inverted ?? false;

    /// <summary>
    /// Gets or sets a value indicating whether downstream pointer promotion should be canceled.
    /// </summary>
    public bool Cancel { get; set; }

    public StylusPointCollection GetStylusPoints(UIElement? relativeTo)
        => StylusDevice?.GetStylusPoints(relativeTo) ?? new StylusPointCollection();

    protected internal override void InvokeEventHandler(Delegate handler, object target)
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

    protected internal override void InvokeEventHandler(Delegate handler, object target)
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

    protected internal override void InvokeEventHandler(Delegate handler, object target)
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

    protected internal override void InvokeEventHandler(Delegate handler, object target)
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
    public static readonly RoutedEvent StylusDownEvent = UIElement.StylusDownEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusMoveEvent = UIElement.StylusMoveEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusUpEvent = UIElement.StylusUpEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent PreviewStylusDownEvent = UIElement.PreviewStylusDownEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent PreviewStylusMoveEvent = UIElement.PreviewStylusMoveEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent PreviewStylusUpEvent = UIElement.PreviewStylusUpEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusInAirMoveEvent = UIElement.StylusInAirMoveEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusEnterEvent = UIElement.StylusEnterEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusLeaveEvent = UIElement.StylusLeaveEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusInRangeEvent = UIElement.StylusInRangeEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusOutOfRangeEvent = UIElement.StylusOutOfRangeEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusSystemGestureEvent = UIElement.StylusSystemGestureEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusButtonDownEvent = UIElement.StylusButtonDownEvent.AddOwner(typeof(UIElement));
    public static readonly RoutedEvent StylusButtonUpEvent = UIElement.StylusButtonUpEvent.AddOwner(typeof(UIElement));

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
    public static bool GetIsFlicksEnabled(DependencyObject element) => element.GetValue(IsFlicksEnabledProperty) is true;
    public static void SetIsFlicksEnabled(DependencyObject element, bool value) => element.SetValue(IsFlicksEnabledProperty, value);
    public static bool GetIsPressAndHoldEnabled(DependencyObject element) => element.GetValue(IsPressAndHoldEnabledProperty) is true;
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsPressAndHoldEnabled(DependencyObject element, bool value) => element.SetValue(IsPressAndHoldEnabledProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetIsTapFeedbackEnabled(DependencyObject element) => element.GetValue(IsTapFeedbackEnabledProperty) is true;
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsTapFeedbackEnabled(DependencyObject element, bool value) => element.SetValue(IsTapFeedbackEnabledProperty, value);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static bool GetIsTouchFeedbackEnabled(DependencyObject element) => element.GetValue(IsTouchFeedbackEnabledProperty) is true;
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static void SetIsTouchFeedbackEnabled(DependencyObject element, bool value) => element.SetValue(IsTouchFeedbackEnabledProperty, value);

    public static StylusDevice? CurrentStylusDevice => Tablet.CurrentStylusDevice;
}

public delegate void StylusEventHandler(object sender, StylusEventArgs e);
public delegate void StylusDownEventHandler(object sender, StylusDownEventArgs e);
public delegate void StylusButtonEventHandler(object sender, StylusButtonEventArgs e);
public delegate void StylusSystemGestureEventHandler(object sender, StylusSystemGestureEventArgs e);
