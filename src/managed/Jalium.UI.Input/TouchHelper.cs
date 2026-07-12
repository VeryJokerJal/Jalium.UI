using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>
/// Aggregates attached properties and helper queries that control touch-related
/// behaviour on any <see cref="UIElement"/>: ripple feedback enable/disable,
/// per-element ripple brush override, and device-type sniffing for event args.
/// </summary>
public static class TouchHelper
{
    /// <summary>
    /// Identifies the <c>TouchHelper.IsRippleEnabled</c> attached property.
    /// Default: false. Controls (ButtonBase / ListBoxItem / MenuItem / Hyperlink etc.)
    /// override the default via <see cref="DependencyProperty.OverrideMetadata(Type, PropertyMetadata)"/> in
    /// their static constructors so any new instance opts-in automatically.
    /// </summary>
    public static readonly DependencyProperty IsRippleEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsRippleEnabled",
            typeof(bool),
            typeof(TouchHelper),
            new PropertyMetadata(false));

    public static bool GetIsRippleEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsRippleEnabledProperty) ?? false);
    }

    public static void SetIsRippleEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsRippleEnabledProperty, value);
    }

    /// <summary>
    /// Identifies the <c>TouchHelper.RippleBrush</c> attached property.
    /// When null, the ripple uses a semi-transparent overlay derived from
    /// the foreground brush (33% alpha).
    /// </summary>
    public static readonly DependencyProperty RippleBrushProperty =
        DependencyProperty.RegisterAttached(
            "RippleBrush",
            typeof(Brush),
            typeof(TouchHelper),
            new PropertyMetadata(null));

    public static Brush? GetRippleBrush(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(RippleBrushProperty) as Brush;
    }

    public static void SetRippleBrush(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RippleBrushProperty, value);
    }

    /// <summary>
    /// Identifies the <c>TouchHelper.IsTouchInteractive</c> attached property.
    /// Master switch: when false, all touch-driven behaviour on this element
    /// (ripple, long-press, native Touch handlers) is suppressed.
    /// Default: true.
    /// </summary>
    public static readonly DependencyProperty IsTouchInteractiveProperty =
        DependencyProperty.RegisterAttached(
            "IsTouchInteractive",
            typeof(bool),
            typeof(TouchHelper),
            new PropertyMetadata(true));

    public static bool GetIsTouchInteractive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsTouchInteractiveProperty) ?? true);
    }

    public static void SetIsTouchInteractive(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsTouchInteractiveProperty, value);
    }

    // ── Static device-type queries ──

    /// <summary>True when the event originated from a touch contact.</summary>
    public static bool IsFromTouch(RoutedEventArgs e) => GetDeviceType(e) == PointerDeviceType.Touch;

    /// <summary>True when the event originated from a pen / stylus.</summary>
    public static bool IsFromPen(RoutedEventArgs e) => GetDeviceType(e) == PointerDeviceType.Pen;

    /// <summary>True when the event originated from a mouse.</summary>
    public static bool IsFromMouse(RoutedEventArgs e) => GetDeviceType(e) == PointerDeviceType.Mouse;

    /// <summary>
    /// Returns the pointer device type for any input event args we can recognise.
    /// Falls back to <see cref="PointerDeviceType.Mouse"/> when no specific device info is present.
    /// </summary>
    public static PointerDeviceType GetDeviceType(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e)
        {
            case TouchEventArgs:
                return PointerDeviceType.Touch;
            case StylusEventArgs:
                return PointerDeviceType.Pen;
            case PointerEventArgs p:
                return p.Pointer.PointerDeviceType;
            default:
                return PointerDeviceType.Mouse;
        }
    }

    /// <summary>
    /// True when the supplied event likely came from a finger (touch) or a pen,
    /// i.e. a non-mouse source — useful for branching ripple / hit-area logic.
    /// </summary>
    public static bool IsTouchLike(RoutedEventArgs e)
    {
        var kind = GetDeviceType(e);
        return kind == PointerDeviceType.Touch || kind == PointerDeviceType.Pen;
    }
}
