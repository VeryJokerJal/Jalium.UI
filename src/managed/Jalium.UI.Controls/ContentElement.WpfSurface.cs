using Jalium.UI.Input;

namespace Jalium.UI;

/// <summary>
/// WPF-compatible dependency-property and routed-input surface for nonvisual content.
/// </summary>
public partial class ContentElement
{
    private static readonly DependencyPropertyKey AreAnyTouchesCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesCaptured), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesCapturedWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesCapturedWithin), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesDirectlyOver), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesOver), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsFocused), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsKeyboardFocusWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsKeyboardFocusWithin), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsKeyboardFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsKeyboardFocused), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsMouseCaptureWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseCaptureWithin), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsMouseCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseCaptured), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsMouseDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseDirectlyOver), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsMouseOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseOver), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStylusCaptureWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusCaptureWithin), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStylusCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusCaptured), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStylusDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusDirectlyOver), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStylusOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusOver), typeof(bool), typeof(ContentElement), new PropertyMetadata(false));

    public static readonly DependencyProperty AllowDropProperty =
        DragDrop.AllowDropProperty.AddOwner(typeof(ContentElement), new PropertyMetadata(false));
    public static readonly DependencyProperty FocusableProperty =
        UIElement.FocusableProperty.AddOwner(typeof(ContentElement), new PropertyMetadata(false));
    public static readonly DependencyProperty IsEnabledProperty =
        UIElement.IsEnabledProperty.AddOwner(typeof(ContentElement), new PropertyMetadata(true));
    public static readonly DependencyProperty AreAnyTouchesCapturedProperty = AreAnyTouchesCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesCapturedWithinProperty = AreAnyTouchesCapturedWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesDirectlyOverProperty = AreAnyTouchesDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesOverProperty = AreAnyTouchesOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsFocusedProperty = IsFocusedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsKeyboardFocusWithinProperty = IsKeyboardFocusWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsKeyboardFocusedProperty = IsKeyboardFocusedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseCaptureWithinProperty = IsMouseCaptureWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseCapturedProperty = IsMouseCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseDirectlyOverProperty = IsMouseDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseOverProperty = IsMouseOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusCaptureWithinProperty = IsStylusCaptureWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusCapturedProperty = IsStylusCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusDirectlyOverProperty = IsStylusDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusOverProperty = IsStylusOverPropertyKey.DependencyProperty;

    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent = UIElement.PreviewGotKeyboardFocusEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent GotKeyboardFocusEvent = UIElement.GotKeyboardFocusEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent = UIElement.PreviewLostKeyboardFocusEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent LostKeyboardFocusEvent = UIElement.LostKeyboardFocusEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent GotFocusEvent = UIElement.GotFocusEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent LostFocusEvent = UIElement.LostFocusEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent GotMouseCaptureEvent = UIElement.GotMouseCaptureEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent LostMouseCaptureEvent = UIElement.LostMouseCaptureEvent.AddOwner(typeof(ContentElement));

    public static readonly RoutedEvent PreviewKeyDownEvent = UIElement.PreviewKeyDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent KeyDownEvent = UIElement.KeyDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewKeyUpEvent = UIElement.PreviewKeyUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent KeyUpEvent = UIElement.KeyUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewTextInputEvent = UIElement.PreviewTextInputEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent TextInputEvent = UIElement.TextInputEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseDownEvent = UIElement.PreviewMouseDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseDownEvent = UIElement.MouseDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseUpEvent = UIElement.PreviewMouseUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseUpEvent = UIElement.MouseUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseMoveEvent = UIElement.PreviewMouseMoveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseMoveEvent = UIElement.MouseMoveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseEnterEvent = UIElement.MouseEnterEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseLeaveEvent = UIElement.MouseLeaveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseWheelEvent = UIElement.PreviewMouseWheelEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseWheelEvent = UIElement.MouseWheelEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseLeftButtonDownEvent = UIElement.PreviewMouseLeftButtonDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseLeftButtonDownEvent = UIElement.MouseLeftButtonDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseLeftButtonUpEvent = UIElement.PreviewMouseLeftButtonUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseLeftButtonUpEvent = UIElement.MouseLeftButtonUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseRightButtonDownEvent = UIElement.PreviewMouseRightButtonDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseRightButtonDownEvent = UIElement.MouseRightButtonDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewMouseRightButtonUpEvent = UIElement.PreviewMouseRightButtonUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent MouseRightButtonUpEvent = UIElement.MouseRightButtonUpEvent.AddOwner(typeof(ContentElement));

    public static readonly RoutedEvent PreviewTouchDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchDown), RoutingStrategy.Tunnel, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent TouchDownEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchDown), RoutingStrategy.Bubble, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent PreviewTouchMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchMove), RoutingStrategy.Tunnel, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent TouchMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchMove), RoutingStrategy.Bubble, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent PreviewTouchUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchUp), RoutingStrategy.Tunnel, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent TouchUpEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchUp), RoutingStrategy.Bubble, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent TouchEnterEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchEnter), RoutingStrategy.Direct, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent TouchLeaveEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchLeave), RoutingStrategy.Direct, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent GotTouchCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(GotTouchCapture), RoutingStrategy.Bubble, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));
    public static readonly RoutedEvent LostTouchCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(LostTouchCapture), RoutingStrategy.Bubble, typeof(EventHandler<TouchEventArgs>), typeof(ContentElement));

    public static readonly RoutedEvent PreviewStylusDownEvent = UIElement.PreviewStylusDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusDownEvent = UIElement.StylusDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusMoveEvent = UIElement.PreviewStylusMoveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusMoveEvent = UIElement.StylusMoveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusUpEvent = UIElement.PreviewStylusUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusUpEvent = UIElement.StylusUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusInAirMoveEvent = UIElement.PreviewStylusInAirMoveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusInAirMoveEvent = UIElement.StylusInAirMoveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusEnterEvent = UIElement.StylusEnterEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusLeaveEvent = UIElement.StylusLeaveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusInRangeEvent = UIElement.PreviewStylusInRangeEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusInRangeEvent = UIElement.StylusInRangeEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusOutOfRangeEvent = UIElement.PreviewStylusOutOfRangeEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusOutOfRangeEvent = UIElement.StylusOutOfRangeEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusButtonDownEvent = UIElement.PreviewStylusButtonDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusButtonDownEvent = UIElement.StylusButtonDownEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusButtonUpEvent = UIElement.PreviewStylusButtonUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusButtonUpEvent = UIElement.StylusButtonUpEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewStylusSystemGestureEvent = UIElement.PreviewStylusSystemGestureEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent StylusSystemGestureEvent = UIElement.StylusSystemGestureEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent GotStylusCaptureEvent = UIElement.GotStylusCaptureEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent LostStylusCaptureEvent = UIElement.LostStylusCaptureEvent.AddOwner(typeof(ContentElement));

    public static readonly RoutedEvent PreviewDragEnterEvent = UIElement.PreviewDragEnterEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent DragEnterEvent = UIElement.DragEnterEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewDragOverEvent = UIElement.PreviewDragOverEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent DragOverEvent = UIElement.DragOverEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewDragLeaveEvent = UIElement.PreviewDragLeaveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent DragLeaveEvent = UIElement.DragLeaveEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewDropEvent = UIElement.PreviewDropEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent DropEvent = UIElement.DropEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent GiveFeedbackEvent = UIElement.GiveFeedbackEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewGiveFeedbackEvent = UIElement.PreviewGiveFeedbackEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent QueryContinueDragEvent = UIElement.QueryContinueDragEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent PreviewQueryContinueDragEvent = UIElement.PreviewQueryContinueDragEvent.AddOwner(typeof(ContentElement));
    public static readonly RoutedEvent QueryCursorEvent = UIElement.QueryCursorEvent.AddOwner(typeof(ContentElement));

    static ContentElement()
    {
        RegisterClassHandler(PreviewKeyDownEvent, new KeyEventHandler((s, e) => ((ContentElement)s).OnPreviewKeyDown(e)));
        RegisterClassHandler(KeyDownEvent, new KeyEventHandler((s, e) => ((ContentElement)s).OnKeyDown(e)));
        RegisterClassHandler(PreviewKeyUpEvent, new KeyEventHandler((s, e) => ((ContentElement)s).OnPreviewKeyUp(e)));
        RegisterClassHandler(KeyUpEvent, new KeyEventHandler((s, e) => ((ContentElement)s).OnKeyUp(e)));
        RegisterClassHandler(PreviewTextInputEvent, new TextCompositionEventHandler((s, e) => ((ContentElement)s).OnPreviewTextInput(e)));
        RegisterClassHandler(TextInputEvent, new TextCompositionEventHandler((s, e) => ((ContentElement)s).OnTextInput(e)));
        RegisterClassHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDownThunk));
        RegisterClassHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownThunk));
        RegisterClassHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUpThunk));
        RegisterClassHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpThunk));
        RegisterClassHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnPreviewMouseLeftButtonDown(e)));
        RegisterClassHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnMouseLeftButtonDown(e)));
        RegisterClassHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnPreviewMouseLeftButtonUp(e)));
        RegisterClassHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnMouseLeftButtonUp(e)));
        RegisterClassHandler(PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnPreviewMouseRightButtonDown(e)));
        RegisterClassHandler(MouseRightButtonDownEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnMouseRightButtonDown(e)));
        RegisterClassHandler(PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnPreviewMouseRightButtonUp(e)));
        RegisterClassHandler(MouseRightButtonUpEvent, new MouseButtonEventHandler((s, e) => ((ContentElement)s).OnMouseRightButtonUp(e)));
        RegisterClassHandler(PreviewMouseMoveEvent, new MouseEventHandler((s, e) => ((ContentElement)s).OnPreviewMouseMove(e)));
        RegisterClassHandler(MouseMoveEvent, new MouseEventHandler((s, e) => ((ContentElement)s).OnMouseMove(e)));
        RegisterClassHandler(MouseEnterEvent, new MouseEventHandler((s, e) => ((ContentElement)s).OnMouseEnter(e)));
        RegisterClassHandler(MouseLeaveEvent, new MouseEventHandler((s, e) => ((ContentElement)s).OnMouseLeave(e)));
        RegisterClassHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler((s, e) => ((ContentElement)s).OnPreviewMouseWheel(e)));
        RegisterClassHandler(MouseWheelEvent, new MouseWheelEventHandler((s, e) => ((ContentElement)s).OnMouseWheel(e)));
        RegisterClassHandler(GotMouseCaptureEvent, new MouseEventHandler((s, e) => ((ContentElement)s).OnGotMouseCapture(e)));
        RegisterClassHandler(LostMouseCaptureEvent, new MouseEventHandler((s, e) => ((ContentElement)s).OnLostMouseCapture(e)));

        RegisterClassHandler(PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnPreviewTouchDown(e)));
        RegisterClassHandler(TouchDownEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnTouchDown(e)));
        RegisterClassHandler(PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnPreviewTouchMove(e)));
        RegisterClassHandler(TouchMoveEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnTouchMove(e)));
        RegisterClassHandler(PreviewTouchUpEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnPreviewTouchUp(e)));
        RegisterClassHandler(TouchUpEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnTouchUp(e)));
        RegisterClassHandler(TouchEnterEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnTouchEnter(e)));
        RegisterClassHandler(TouchLeaveEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnTouchLeave(e)));
        RegisterClassHandler(GotTouchCaptureEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnGotTouchCapture(e)));
        RegisterClassHandler(LostTouchCaptureEvent, new EventHandler<TouchEventArgs>((s, e) => ((ContentElement)s!).OnLostTouchCapture(e)));

        RegisterClassHandler(PreviewStylusDownEvent, new StylusDownEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusDown(e)));
        RegisterClassHandler(StylusDownEvent, new StylusDownEventHandler((s, e) => ((ContentElement)s).OnStylusDown(e)));
        RegisterClassHandler(PreviewStylusMoveEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusMove(e)));
        RegisterClassHandler(StylusMoveEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusMove(e)));
        RegisterClassHandler(PreviewStylusUpEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusUp(e)));
        RegisterClassHandler(StylusUpEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusUp(e)));
        RegisterClassHandler(PreviewStylusInAirMoveEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusInAirMove(e)));
        RegisterClassHandler(StylusInAirMoveEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusInAirMove(e)));
        RegisterClassHandler(StylusEnterEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusEnter(e)));
        RegisterClassHandler(StylusLeaveEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusLeave(e)));
        RegisterClassHandler(PreviewStylusInRangeEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusInRange(e)));
        RegisterClassHandler(StylusInRangeEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusInRange(e)));
        RegisterClassHandler(PreviewStylusOutOfRangeEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusOutOfRange(e)));
        RegisterClassHandler(StylusOutOfRangeEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnStylusOutOfRange(e)));
        RegisterClassHandler(PreviewStylusButtonDownEvent, new StylusButtonEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusButtonDown(e)));
        RegisterClassHandler(StylusButtonDownEvent, new StylusButtonEventHandler((s, e) => ((ContentElement)s).OnStylusButtonDown(e)));
        RegisterClassHandler(PreviewStylusButtonUpEvent, new StylusButtonEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusButtonUp(e)));
        RegisterClassHandler(StylusButtonUpEvent, new StylusButtonEventHandler((s, e) => ((ContentElement)s).OnStylusButtonUp(e)));
        RegisterClassHandler(PreviewStylusSystemGestureEvent, new StylusSystemGestureEventHandler((s, e) => ((ContentElement)s).OnPreviewStylusSystemGesture(e)));
        RegisterClassHandler(StylusSystemGestureEvent, new StylusSystemGestureEventHandler((s, e) => ((ContentElement)s).OnStylusSystemGesture(e)));
        RegisterClassHandler(GotStylusCaptureEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnGotStylusCapture(e)));
        RegisterClassHandler(LostStylusCaptureEvent, new StylusEventHandler((s, e) => ((ContentElement)s).OnLostStylusCapture(e)));

        RegisterClassHandler(PreviewDragEnterEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnPreviewDragEnter(e)));
        RegisterClassHandler(DragEnterEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnDragEnter(e)));
        RegisterClassHandler(PreviewDragOverEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnPreviewDragOver(e)));
        RegisterClassHandler(DragOverEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnDragOver(e)));
        RegisterClassHandler(PreviewDragLeaveEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnPreviewDragLeave(e)));
        RegisterClassHandler(DragLeaveEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnDragLeave(e)));
        RegisterClassHandler(PreviewDropEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnPreviewDrop(e)));
        RegisterClassHandler(DropEvent, new DragEventHandler((s, e) => ((ContentElement)s).OnDrop(e)));
        RegisterClassHandler(PreviewGiveFeedbackEvent, new GiveFeedbackEventHandler((s, e) => ((ContentElement)s).OnPreviewGiveFeedback(e)));
        RegisterClassHandler(GiveFeedbackEvent, new GiveFeedbackEventHandler((s, e) => ((ContentElement)s).OnGiveFeedback(e)));
        RegisterClassHandler(PreviewQueryContinueDragEvent, new QueryContinueDragEventHandler((s, e) => ((ContentElement)s).OnPreviewQueryContinueDrag(e)));
        RegisterClassHandler(QueryContinueDragEvent, new QueryContinueDragEventHandler((s, e) => ((ContentElement)s).OnQueryContinueDrag(e)));
        RegisterClassHandler(QueryCursorEvent, new QueryCursorEventHandler((s, e) => ((ContentElement)s).OnQueryCursor(e)));
        RegisterClassHandler(PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => ((ContentElement)s).OnPreviewGotKeyboardFocus(e)));
        RegisterClassHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => ((ContentElement)s).OnGotKeyboardFocus(e)));
        RegisterClassHandler(PreviewLostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => ((ContentElement)s).OnPreviewLostKeyboardFocus(e)));
        RegisterClassHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => ((ContentElement)s).OnLostKeyboardFocus(e)));
        RegisterClassHandler(GotFocusEvent, new RoutedEventHandler((s, e) => ((ContentElement)s).OnGotFocus(e)));
        RegisterClassHandler(LostFocusEvent, new RoutedEventHandler((s, e) => ((ContentElement)s).OnLostFocus(e)));
    }

    private static void RegisterClassHandler(RoutedEvent routedEvent, Delegate handler) =>
        EventManager.RegisterClassHandler(typeof(ContentElement), routedEvent, handler);

    private static void OnPreviewMouseDownThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (ContentElement)sender;
        element.OnPreviewMouseDown(e);
        if (!e.Handled) ReRaiseButtonEvent(element, e, PreviewMouseLeftButtonDownEvent, PreviewMouseRightButtonDownEvent);
    }

    private static void OnMouseDownThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (ContentElement)sender;
        element.OnMouseDown(e);
        if (!e.Handled) ReRaiseButtonEvent(element, e, MouseLeftButtonDownEvent, MouseRightButtonDownEvent);
    }

    private static void OnPreviewMouseUpThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (ContentElement)sender;
        element.OnPreviewMouseUp(e);
        if (!e.Handled) ReRaiseButtonEvent(element, e, PreviewMouseLeftButtonUpEvent, PreviewMouseRightButtonUpEvent);
    }

    private static void OnMouseUpThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (ContentElement)sender;
        element.OnMouseUp(e);
        if (!e.Handled) ReRaiseButtonEvent(element, e, MouseLeftButtonUpEvent, MouseRightButtonUpEvent);
    }

    private static void ReRaiseButtonEvent(ContentElement element, MouseButtonEventArgs source, RoutedEvent leftEvent, RoutedEvent rightEvent)
    {
        RoutedEvent? routedEvent = source.ChangedButton switch
        {
            MouseButton.Left => leftEvent,
            MouseButton.Right => rightEvent,
            _ => null,
        };
        if (routedEvent is null) return;

        var args = new MouseButtonEventArgs(
            routedEvent,
            source.GetPosition(null),
            source.ChangedButton,
            source.ButtonState,
            source.ClickCount,
            source.LeftButton,
            source.MiddleButton,
            source.RightButton,
            source.XButton1,
            source.XButton2,
            source.KeyboardModifiers,
            source.Timestamp)
        {
            Source = source.Source,
        };
        args.SetOriginalSource(source.OriginalSource);
        element.RaiseEvent(args);
        if (args.Handled) source.Handled = true;
    }

    public event KeyboardFocusChangedEventHandler PreviewGotKeyboardFocus { add => AddHandler(PreviewGotKeyboardFocusEvent, value); remove => RemoveHandler(PreviewGotKeyboardFocusEvent, value); }
    public event KeyboardFocusChangedEventHandler GotKeyboardFocus { add => AddHandler(GotKeyboardFocusEvent, value); remove => RemoveHandler(GotKeyboardFocusEvent, value); }
    public event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus { add => AddHandler(PreviewLostKeyboardFocusEvent, value); remove => RemoveHandler(PreviewLostKeyboardFocusEvent, value); }
    public event KeyboardFocusChangedEventHandler LostKeyboardFocus { add => AddHandler(LostKeyboardFocusEvent, value); remove => RemoveHandler(LostKeyboardFocusEvent, value); }
    public event RoutedEventHandler GotFocus { add => AddHandler(GotFocusEvent, value); remove => RemoveHandler(GotFocusEvent, value); }
    public event RoutedEventHandler LostFocus { add => AddHandler(LostFocusEvent, value); remove => RemoveHandler(LostFocusEvent, value); }
    public event MouseEventHandler GotMouseCapture { add => AddHandler(GotMouseCaptureEvent, value); remove => RemoveHandler(GotMouseCaptureEvent, value); }
    public event MouseEventHandler LostMouseCapture { add => AddHandler(LostMouseCaptureEvent, value); remove => RemoveHandler(LostMouseCaptureEvent, value); }

    public event KeyEventHandler PreviewKeyDown { add => AddHandler(PreviewKeyDownEvent, value); remove => RemoveHandler(PreviewKeyDownEvent, value); }
    public event KeyEventHandler KeyDown { add => AddHandler(KeyDownEvent, value); remove => RemoveHandler(KeyDownEvent, value); }
    public event KeyEventHandler PreviewKeyUp { add => AddHandler(PreviewKeyUpEvent, value); remove => RemoveHandler(PreviewKeyUpEvent, value); }
    public event KeyEventHandler KeyUp { add => AddHandler(KeyUpEvent, value); remove => RemoveHandler(KeyUpEvent, value); }
    public event TextCompositionEventHandler PreviewTextInput { add => AddHandler(PreviewTextInputEvent, value); remove => RemoveHandler(PreviewTextInputEvent, value); }
    public event TextCompositionEventHandler TextInput { add => AddHandler(TextInputEvent, value); remove => RemoveHandler(TextInputEvent, value); }
    public event MouseButtonEventHandler PreviewMouseDown { add => AddHandler(PreviewMouseDownEvent, value); remove => RemoveHandler(PreviewMouseDownEvent, value); }
    public event MouseButtonEventHandler MouseDown { add => AddHandler(MouseDownEvent, value); remove => RemoveHandler(MouseDownEvent, value); }
    public event MouseButtonEventHandler PreviewMouseUp { add => AddHandler(PreviewMouseUpEvent, value); remove => RemoveHandler(PreviewMouseUpEvent, value); }
    public event MouseButtonEventHandler MouseUp { add => AddHandler(MouseUpEvent, value); remove => RemoveHandler(MouseUpEvent, value); }
    public event MouseEventHandler PreviewMouseMove { add => AddHandler(PreviewMouseMoveEvent, value); remove => RemoveHandler(PreviewMouseMoveEvent, value); }
    public event MouseEventHandler MouseMove { add => AddHandler(MouseMoveEvent, value); remove => RemoveHandler(MouseMoveEvent, value); }
    public event MouseEventHandler MouseEnter { add => AddHandler(MouseEnterEvent, value); remove => RemoveHandler(MouseEnterEvent, value); }
    public event MouseEventHandler MouseLeave { add => AddHandler(MouseLeaveEvent, value); remove => RemoveHandler(MouseLeaveEvent, value); }
    public event MouseWheelEventHandler PreviewMouseWheel { add => AddHandler(PreviewMouseWheelEvent, value); remove => RemoveHandler(PreviewMouseWheelEvent, value); }
    public event MouseWheelEventHandler MouseWheel { add => AddHandler(MouseWheelEvent, value); remove => RemoveHandler(MouseWheelEvent, value); }
    public event MouseButtonEventHandler PreviewMouseLeftButtonDown { add => AddHandler(PreviewMouseLeftButtonDownEvent, value); remove => RemoveHandler(PreviewMouseLeftButtonDownEvent, value); }
    public event MouseButtonEventHandler MouseLeftButtonDown { add => AddHandler(MouseLeftButtonDownEvent, value); remove => RemoveHandler(MouseLeftButtonDownEvent, value); }
    public event MouseButtonEventHandler PreviewMouseLeftButtonUp { add => AddHandler(PreviewMouseLeftButtonUpEvent, value); remove => RemoveHandler(PreviewMouseLeftButtonUpEvent, value); }
    public event MouseButtonEventHandler MouseLeftButtonUp { add => AddHandler(MouseLeftButtonUpEvent, value); remove => RemoveHandler(MouseLeftButtonUpEvent, value); }
    public event MouseButtonEventHandler PreviewMouseRightButtonDown { add => AddHandler(PreviewMouseRightButtonDownEvent, value); remove => RemoveHandler(PreviewMouseRightButtonDownEvent, value); }
    public event MouseButtonEventHandler MouseRightButtonDown { add => AddHandler(MouseRightButtonDownEvent, value); remove => RemoveHandler(MouseRightButtonDownEvent, value); }
    public event MouseButtonEventHandler PreviewMouseRightButtonUp { add => AddHandler(PreviewMouseRightButtonUpEvent, value); remove => RemoveHandler(PreviewMouseRightButtonUpEvent, value); }
    public event MouseButtonEventHandler MouseRightButtonUp { add => AddHandler(MouseRightButtonUpEvent, value); remove => RemoveHandler(MouseRightButtonUpEvent, value); }

    public event EventHandler<TouchEventArgs> PreviewTouchDown { add => AddHandler(PreviewTouchDownEvent, value); remove => RemoveHandler(PreviewTouchDownEvent, value); }
    public event EventHandler<TouchEventArgs> TouchDown { add => AddHandler(TouchDownEvent, value); remove => RemoveHandler(TouchDownEvent, value); }
    public event EventHandler<TouchEventArgs> PreviewTouchMove { add => AddHandler(PreviewTouchMoveEvent, value); remove => RemoveHandler(PreviewTouchMoveEvent, value); }
    public event EventHandler<TouchEventArgs> TouchMove { add => AddHandler(TouchMoveEvent, value); remove => RemoveHandler(TouchMoveEvent, value); }
    public event EventHandler<TouchEventArgs> PreviewTouchUp { add => AddHandler(PreviewTouchUpEvent, value); remove => RemoveHandler(PreviewTouchUpEvent, value); }
    public event EventHandler<TouchEventArgs> TouchUp { add => AddHandler(TouchUpEvent, value); remove => RemoveHandler(TouchUpEvent, value); }
    public event EventHandler<TouchEventArgs> TouchEnter { add => AddHandler(TouchEnterEvent, value); remove => RemoveHandler(TouchEnterEvent, value); }
    public event EventHandler<TouchEventArgs> TouchLeave { add => AddHandler(TouchLeaveEvent, value); remove => RemoveHandler(TouchLeaveEvent, value); }
    public event EventHandler<TouchEventArgs> GotTouchCapture { add => AddHandler(GotTouchCaptureEvent, value); remove => RemoveHandler(GotTouchCaptureEvent, value); }
    public event EventHandler<TouchEventArgs> LostTouchCapture { add => AddHandler(LostTouchCaptureEvent, value); remove => RemoveHandler(LostTouchCaptureEvent, value); }

    public event StylusDownEventHandler PreviewStylusDown { add => AddHandler(PreviewStylusDownEvent, value); remove => RemoveHandler(PreviewStylusDownEvent, value); }
    public event StylusDownEventHandler StylusDown { add => AddHandler(StylusDownEvent, value); remove => RemoveHandler(StylusDownEvent, value); }
    public event StylusEventHandler PreviewStylusMove { add => AddHandler(PreviewStylusMoveEvent, value); remove => RemoveHandler(PreviewStylusMoveEvent, value); }
    public event StylusEventHandler StylusMove { add => AddHandler(StylusMoveEvent, value); remove => RemoveHandler(StylusMoveEvent, value); }
    public event StylusEventHandler PreviewStylusUp { add => AddHandler(PreviewStylusUpEvent, value); remove => RemoveHandler(PreviewStylusUpEvent, value); }
    public event StylusEventHandler StylusUp { add => AddHandler(StylusUpEvent, value); remove => RemoveHandler(StylusUpEvent, value); }
    public event StylusEventHandler PreviewStylusInAirMove { add => AddHandler(PreviewStylusInAirMoveEvent, value); remove => RemoveHandler(PreviewStylusInAirMoveEvent, value); }
    public event StylusEventHandler StylusInAirMove { add => AddHandler(StylusInAirMoveEvent, value); remove => RemoveHandler(StylusInAirMoveEvent, value); }
    public event StylusEventHandler StylusEnter { add => AddHandler(StylusEnterEvent, value); remove => RemoveHandler(StylusEnterEvent, value); }
    public event StylusEventHandler StylusLeave { add => AddHandler(StylusLeaveEvent, value); remove => RemoveHandler(StylusLeaveEvent, value); }
    public event StylusEventHandler PreviewStylusInRange { add => AddHandler(PreviewStylusInRangeEvent, value); remove => RemoveHandler(PreviewStylusInRangeEvent, value); }
    public event StylusEventHandler StylusInRange { add => AddHandler(StylusInRangeEvent, value); remove => RemoveHandler(StylusInRangeEvent, value); }
    public event StylusEventHandler PreviewStylusOutOfRange { add => AddHandler(PreviewStylusOutOfRangeEvent, value); remove => RemoveHandler(PreviewStylusOutOfRangeEvent, value); }
    public event StylusEventHandler StylusOutOfRange { add => AddHandler(StylusOutOfRangeEvent, value); remove => RemoveHandler(StylusOutOfRangeEvent, value); }
    public event StylusButtonEventHandler PreviewStylusButtonDown { add => AddHandler(PreviewStylusButtonDownEvent, value); remove => RemoveHandler(PreviewStylusButtonDownEvent, value); }
    public event StylusButtonEventHandler StylusButtonDown { add => AddHandler(StylusButtonDownEvent, value); remove => RemoveHandler(StylusButtonDownEvent, value); }
    public event StylusButtonEventHandler PreviewStylusButtonUp { add => AddHandler(PreviewStylusButtonUpEvent, value); remove => RemoveHandler(PreviewStylusButtonUpEvent, value); }
    public event StylusButtonEventHandler StylusButtonUp { add => AddHandler(StylusButtonUpEvent, value); remove => RemoveHandler(StylusButtonUpEvent, value); }
    public event StylusSystemGestureEventHandler PreviewStylusSystemGesture { add => AddHandler(PreviewStylusSystemGestureEvent, value); remove => RemoveHandler(PreviewStylusSystemGestureEvent, value); }
    public event StylusSystemGestureEventHandler StylusSystemGesture { add => AddHandler(StylusSystemGestureEvent, value); remove => RemoveHandler(StylusSystemGestureEvent, value); }
    public event StylusEventHandler GotStylusCapture { add => AddHandler(GotStylusCaptureEvent, value); remove => RemoveHandler(GotStylusCaptureEvent, value); }
    public event StylusEventHandler LostStylusCapture { add => AddHandler(LostStylusCaptureEvent, value); remove => RemoveHandler(LostStylusCaptureEvent, value); }

    public event DragEventHandler PreviewDragEnter { add => AddHandler(PreviewDragEnterEvent, value); remove => RemoveHandler(PreviewDragEnterEvent, value); }
    public event DragEventHandler DragEnter { add => AddHandler(DragEnterEvent, value); remove => RemoveHandler(DragEnterEvent, value); }
    public event DragEventHandler PreviewDragOver { add => AddHandler(PreviewDragOverEvent, value); remove => RemoveHandler(PreviewDragOverEvent, value); }
    public event DragEventHandler DragOver { add => AddHandler(DragOverEvent, value); remove => RemoveHandler(DragOverEvent, value); }
    public event DragEventHandler PreviewDragLeave { add => AddHandler(PreviewDragLeaveEvent, value); remove => RemoveHandler(PreviewDragLeaveEvent, value); }
    public event DragEventHandler DragLeave { add => AddHandler(DragLeaveEvent, value); remove => RemoveHandler(DragLeaveEvent, value); }
    public event DragEventHandler PreviewDrop { add => AddHandler(PreviewDropEvent, value); remove => RemoveHandler(PreviewDropEvent, value); }
    public event DragEventHandler Drop { add => AddHandler(DropEvent, value); remove => RemoveHandler(DropEvent, value); }
    public event GiveFeedbackEventHandler PreviewGiveFeedback { add => AddHandler(PreviewGiveFeedbackEvent, value); remove => RemoveHandler(PreviewGiveFeedbackEvent, value); }
    public event GiveFeedbackEventHandler GiveFeedback { add => AddHandler(GiveFeedbackEvent, value); remove => RemoveHandler(GiveFeedbackEvent, value); }
    public event QueryContinueDragEventHandler PreviewQueryContinueDrag { add => AddHandler(PreviewQueryContinueDragEvent, value); remove => RemoveHandler(PreviewQueryContinueDragEvent, value); }
    public event QueryContinueDragEventHandler QueryContinueDrag { add => AddHandler(QueryContinueDragEvent, value); remove => RemoveHandler(QueryContinueDragEvent, value); }
    public event QueryCursorEventHandler QueryCursor { add => AddHandler(QueryCursorEvent, value); remove => RemoveHandler(QueryCursorEvent, value); }

    public event DependencyPropertyChangedEventHandler? FocusableChanged;
    public event DependencyPropertyChangedEventHandler? IsEnabledChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusDirectlyOverChanged;

    protected internal virtual void OnPreviewKeyDown(KeyEventArgs e) { }
    protected internal virtual void OnKeyDown(KeyEventArgs e) { }
    protected internal virtual void OnPreviewKeyUp(KeyEventArgs e) { }
    protected internal virtual void OnKeyUp(KeyEventArgs e) { }
    protected internal virtual void OnPreviewTextInput(TextCompositionEventArgs e) { }
    protected internal virtual void OnTextInput(TextCompositionEventArgs e) { }
    protected internal virtual void OnPreviewMouseDown(MouseButtonEventArgs e) { }
    protected internal virtual void OnMouseDown(MouseButtonEventArgs e) { }
    protected internal virtual void OnPreviewMouseUp(MouseButtonEventArgs e) { }
    protected internal virtual void OnMouseUp(MouseButtonEventArgs e) { }
    protected internal virtual void OnPreviewMouseMove(MouseEventArgs e) { }
    protected internal virtual void OnMouseMove(MouseEventArgs e) { }
    protected internal virtual void OnMouseEnter(MouseEventArgs e) { }
    protected internal virtual void OnMouseLeave(MouseEventArgs e) { }
    protected internal virtual void OnPreviewMouseWheel(MouseWheelEventArgs e) { }
    protected internal virtual void OnMouseWheel(MouseWheelEventArgs e) { }
    protected internal virtual void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e) { }
    protected internal virtual void OnMouseLeftButtonDown(MouseButtonEventArgs e) { }
    protected internal virtual void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e) { }
    protected internal virtual void OnMouseLeftButtonUp(MouseButtonEventArgs e) { }
    protected internal virtual void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e) { }
    protected internal virtual void OnMouseRightButtonDown(MouseButtonEventArgs e) { }
    protected internal virtual void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e) { }
    protected internal virtual void OnMouseRightButtonUp(MouseButtonEventArgs e) { }
    protected internal virtual void OnGotMouseCapture(MouseEventArgs e) { }
    protected internal virtual void OnLostMouseCapture(MouseEventArgs e) { }

    protected internal virtual void OnPreviewTouchDown(TouchEventArgs e) { }
    protected internal virtual void OnTouchDown(TouchEventArgs e) { }
    protected internal virtual void OnPreviewTouchMove(TouchEventArgs e) { }
    protected internal virtual void OnTouchMove(TouchEventArgs e) { }
    protected internal virtual void OnPreviewTouchUp(TouchEventArgs e) { }
    protected internal virtual void OnTouchUp(TouchEventArgs e) { }
    protected internal virtual void OnTouchEnter(TouchEventArgs e) { }
    protected internal virtual void OnTouchLeave(TouchEventArgs e) { }
    protected internal virtual void OnGotTouchCapture(TouchEventArgs e) { }
    protected internal virtual void OnLostTouchCapture(TouchEventArgs e) { }

    protected internal virtual void OnPreviewStylusDown(StylusDownEventArgs e) { }
    protected internal virtual void OnStylusDown(StylusDownEventArgs e) { }
    protected internal virtual void OnPreviewStylusMove(StylusEventArgs e) { }
    protected internal virtual void OnStylusMove(StylusEventArgs e) { }
    protected internal virtual void OnPreviewStylusUp(StylusEventArgs e) { }
    protected internal virtual void OnStylusUp(StylusEventArgs e) { }
    protected internal virtual void OnPreviewStylusInAirMove(StylusEventArgs e) { }
    protected internal virtual void OnStylusInAirMove(StylusEventArgs e) { }
    protected internal virtual void OnStylusEnter(StylusEventArgs e) { }
    protected internal virtual void OnStylusLeave(StylusEventArgs e) { }
    protected internal virtual void OnPreviewStylusInRange(StylusEventArgs e) { }
    protected internal virtual void OnStylusInRange(StylusEventArgs e) { }
    protected internal virtual void OnPreviewStylusOutOfRange(StylusEventArgs e) { }
    protected internal virtual void OnStylusOutOfRange(StylusEventArgs e) { }
    protected internal virtual void OnPreviewStylusButtonDown(StylusButtonEventArgs e) { }
    protected internal virtual void OnStylusButtonDown(StylusButtonEventArgs e) { }
    protected internal virtual void OnPreviewStylusButtonUp(StylusButtonEventArgs e) { }
    protected internal virtual void OnStylusButtonUp(StylusButtonEventArgs e) { }
    protected internal virtual void OnPreviewStylusSystemGesture(StylusSystemGestureEventArgs e) { }
    protected internal virtual void OnStylusSystemGesture(StylusSystemGestureEventArgs e) { }
    protected internal virtual void OnGotStylusCapture(StylusEventArgs e) { }
    protected internal virtual void OnLostStylusCapture(StylusEventArgs e) { }

    protected internal virtual void OnPreviewDragEnter(DragEventArgs e) { }
    protected internal virtual void OnDragEnter(DragEventArgs e) { }
    protected internal virtual void OnPreviewDragOver(DragEventArgs e) { }
    protected internal virtual void OnDragOver(DragEventArgs e) { }
    protected internal virtual void OnPreviewDragLeave(DragEventArgs e) { }
    protected internal virtual void OnDragLeave(DragEventArgs e) { }
    protected internal virtual void OnPreviewDrop(DragEventArgs e) { }
    protected internal virtual void OnDrop(DragEventArgs e) { }
    protected internal virtual void OnPreviewGiveFeedback(GiveFeedbackEventArgs e) { }
    protected internal virtual void OnGiveFeedback(GiveFeedbackEventArgs e) { }
    protected internal virtual void OnPreviewQueryContinueDrag(QueryContinueDragEventArgs e) { }
    protected internal virtual void OnQueryContinueDrag(QueryContinueDragEventArgs e) { }
    protected internal virtual void OnQueryCursor(QueryCursorEventArgs e) { }
    protected internal virtual void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { }
    protected internal virtual void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { }
    protected internal virtual void OnPreviewLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { }
    protected internal virtual void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { }
    protected virtual void OnGotFocus(RoutedEventArgs e) { }
    protected virtual void OnLostFocus(RoutedEventArgs e) { }

    protected virtual void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsMouseCaptureWithinChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsMouseCapturedChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsMouseDirectlyOverChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsStylusCaptureWithinChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsStylusCapturedChanged(DependencyPropertyChangedEventArgs e) { }
    protected virtual void OnIsStylusDirectlyOverChanged(DependencyPropertyChangedEventArgs e) { }
}
