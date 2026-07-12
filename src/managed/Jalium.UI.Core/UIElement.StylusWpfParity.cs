using Jalium.UI.Input;

namespace Jalium.UI;

public partial class UIElement
{
    public static readonly RoutedEvent GotStylusCaptureEvent =
        Stylus.GotStylusCaptureEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent LostStylusCaptureEvent =
        Stylus.LostStylusCaptureEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusButtonDownEvent =
        Stylus.PreviewStylusButtonDownEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusButtonUpEvent =
        Stylus.PreviewStylusButtonUpEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusInAirMoveEvent =
        Stylus.PreviewStylusInAirMoveEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusInRangeEvent =
        Stylus.PreviewStylusInRangeEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusOutOfRangeEvent =
        Stylus.PreviewStylusOutOfRangeEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusSystemGestureEvent =
        Stylus.PreviewStylusSystemGestureEvent.AddOwner(typeof(UIElement));

    public event StylusEventHandler GotStylusCapture
    {
        add => AddHandler(GotStylusCaptureEvent, value);
        remove => RemoveHandler(GotStylusCaptureEvent, value);
    }

    public event StylusEventHandler LostStylusCapture
    {
        add => AddHandler(LostStylusCaptureEvent, value);
        remove => RemoveHandler(LostStylusCaptureEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonDown
    {
        add => AddHandler(PreviewStylusButtonDownEvent, value);
        remove => RemoveHandler(PreviewStylusButtonDownEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonUp
    {
        add => AddHandler(PreviewStylusButtonUpEvent, value);
        remove => RemoveHandler(PreviewStylusButtonUpEvent, value);
    }

    public event StylusEventHandler PreviewStylusInAirMove
    {
        add => AddHandler(PreviewStylusInAirMoveEvent, value);
        remove => RemoveHandler(PreviewStylusInAirMoveEvent, value);
    }

    public event StylusEventHandler PreviewStylusInRange
    {
        add => AddHandler(PreviewStylusInRangeEvent, value);
        remove => RemoveHandler(PreviewStylusInRangeEvent, value);
    }

    public event StylusEventHandler PreviewStylusOutOfRange
    {
        add => AddHandler(PreviewStylusOutOfRangeEvent, value);
        remove => RemoveHandler(PreviewStylusOutOfRangeEvent, value);
    }

    public event StylusSystemGestureEventHandler PreviewStylusSystemGesture
    {
        add => AddHandler(PreviewStylusSystemGestureEvent, value);
        remove => RemoveHandler(PreviewStylusSystemGestureEvent, value);
    }

    protected virtual void OnGotStylusCapture(StylusEventArgs e)
    {
    }

    protected virtual void OnLostStylusCapture(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusInAirMove(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusInRange(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusOutOfRange(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusSystemGesture(StylusSystemGestureEventArgs e)
    {
    }
}
