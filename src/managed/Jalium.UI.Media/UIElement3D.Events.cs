using Jalium.UI.Input;

namespace Jalium.UI;

public abstract partial class UIElement3D
{
    private static RoutedEvent Own(RoutedEvent routedEvent) => routedEvent.AddOwner(typeof(UIElement3D));

    public static readonly RoutedEvent PreviewKeyDownEvent = Own(UIElement.PreviewKeyDownEvent);
    public static readonly RoutedEvent KeyDownEvent = Own(UIElement.KeyDownEvent);
    public static readonly RoutedEvent PreviewKeyUpEvent = Own(UIElement.PreviewKeyUpEvent);
    public static readonly RoutedEvent KeyUpEvent = Own(UIElement.KeyUpEvent);
    public static readonly RoutedEvent PreviewTextInputEvent = Own(UIElement.PreviewTextInputEvent);
    public static readonly RoutedEvent TextInputEvent = Own(UIElement.TextInputEvent);

    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent = Own(UIElement.PreviewGotKeyboardFocusEvent);
    public static readonly RoutedEvent GotKeyboardFocusEvent = Own(UIElement.GotKeyboardFocusEvent);
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent = Own(UIElement.PreviewLostKeyboardFocusEvent);
    public static readonly RoutedEvent LostKeyboardFocusEvent = Own(UIElement.LostKeyboardFocusEvent);
    public static readonly RoutedEvent GotFocusEvent = Own(UIElement.GotFocusEvent);
    public static readonly RoutedEvent LostFocusEvent = Own(UIElement.LostFocusEvent);

    public static readonly RoutedEvent PreviewMouseDownEvent = Own(UIElement.PreviewMouseDownEvent);
    public static readonly RoutedEvent MouseDownEvent = Own(UIElement.MouseDownEvent);
    public static readonly RoutedEvent PreviewMouseUpEvent = Own(UIElement.PreviewMouseUpEvent);
    public static readonly RoutedEvent MouseUpEvent = Own(UIElement.MouseUpEvent);
    public static readonly RoutedEvent PreviewMouseMoveEvent = Own(UIElement.PreviewMouseMoveEvent);
    public static readonly RoutedEvent MouseMoveEvent = Own(UIElement.MouseMoveEvent);
    public static readonly RoutedEvent MouseEnterEvent = Own(UIElement.MouseEnterEvent);
    public static readonly RoutedEvent MouseLeaveEvent = Own(UIElement.MouseLeaveEvent);
    public static readonly RoutedEvent PreviewMouseWheelEvent = Own(UIElement.PreviewMouseWheelEvent);
    public static readonly RoutedEvent MouseWheelEvent = Own(UIElement.MouseWheelEvent);
    public static readonly RoutedEvent PreviewMouseLeftButtonDownEvent = Own(UIElement.PreviewMouseLeftButtonDownEvent);
    public static readonly RoutedEvent MouseLeftButtonDownEvent = Own(UIElement.MouseLeftButtonDownEvent);
    public static readonly RoutedEvent PreviewMouseLeftButtonUpEvent = Own(UIElement.PreviewMouseLeftButtonUpEvent);
    public static readonly RoutedEvent MouseLeftButtonUpEvent = Own(UIElement.MouseLeftButtonUpEvent);
    public static readonly RoutedEvent PreviewMouseRightButtonDownEvent = Own(UIElement.PreviewMouseRightButtonDownEvent);
    public static readonly RoutedEvent MouseRightButtonDownEvent = Own(UIElement.MouseRightButtonDownEvent);
    public static readonly RoutedEvent PreviewMouseRightButtonUpEvent = Own(UIElement.PreviewMouseRightButtonUpEvent);
    public static readonly RoutedEvent MouseRightButtonUpEvent = Own(UIElement.MouseRightButtonUpEvent);
    public static readonly RoutedEvent GotMouseCaptureEvent = Own(UIElement.GotMouseCaptureEvent);
    public static readonly RoutedEvent LostMouseCaptureEvent = Own(UIElement.LostMouseCaptureEvent);

    public static readonly RoutedEvent PreviewStylusDownEvent = Own(UIElement.PreviewStylusDownEvent);
    public static readonly RoutedEvent StylusDownEvent = Own(UIElement.StylusDownEvent);
    public static readonly RoutedEvent PreviewStylusMoveEvent = Own(UIElement.PreviewStylusMoveEvent);
    public static readonly RoutedEvent StylusMoveEvent = Own(UIElement.StylusMoveEvent);
    public static readonly RoutedEvent PreviewStylusUpEvent = Own(UIElement.PreviewStylusUpEvent);
    public static readonly RoutedEvent StylusUpEvent = Own(UIElement.StylusUpEvent);
    public static readonly RoutedEvent PreviewStylusInAirMoveEvent = Own(UIElement.PreviewStylusInAirMoveEvent);
    public static readonly RoutedEvent StylusInAirMoveEvent = Own(UIElement.StylusInAirMoveEvent);
    public static readonly RoutedEvent StylusEnterEvent = Own(UIElement.StylusEnterEvent);
    public static readonly RoutedEvent StylusLeaveEvent = Own(UIElement.StylusLeaveEvent);
    public static readonly RoutedEvent PreviewStylusInRangeEvent = Own(UIElement.PreviewStylusInRangeEvent);
    public static readonly RoutedEvent StylusInRangeEvent = Own(UIElement.StylusInRangeEvent);
    public static readonly RoutedEvent PreviewStylusOutOfRangeEvent = Own(UIElement.PreviewStylusOutOfRangeEvent);
    public static readonly RoutedEvent StylusOutOfRangeEvent = Own(UIElement.StylusOutOfRangeEvent);
    public static readonly RoutedEvent PreviewStylusButtonDownEvent = Own(UIElement.PreviewStylusButtonDownEvent);
    public static readonly RoutedEvent StylusButtonDownEvent = Own(UIElement.StylusButtonDownEvent);
    public static readonly RoutedEvent PreviewStylusButtonUpEvent = Own(UIElement.PreviewStylusButtonUpEvent);
    public static readonly RoutedEvent StylusButtonUpEvent = Own(UIElement.StylusButtonUpEvent);
    public static readonly RoutedEvent PreviewStylusSystemGestureEvent = Own(UIElement.PreviewStylusSystemGestureEvent);
    public static readonly RoutedEvent StylusSystemGestureEvent = Own(UIElement.StylusSystemGestureEvent);
    public static readonly RoutedEvent GotStylusCaptureEvent = Own(UIElement.GotStylusCaptureEvent);
    public static readonly RoutedEvent LostStylusCaptureEvent = Own(UIElement.LostStylusCaptureEvent);

    public static readonly RoutedEvent PreviewTouchDownEvent = Own(UIElement.PreviewTouchDownEvent);
    public static readonly RoutedEvent TouchDownEvent = Own(UIElement.TouchDownEvent);
    public static readonly RoutedEvent PreviewTouchMoveEvent = Own(UIElement.PreviewTouchMoveEvent);
    public static readonly RoutedEvent TouchMoveEvent = Own(UIElement.TouchMoveEvent);
    public static readonly RoutedEvent PreviewTouchUpEvent = Own(UIElement.PreviewTouchUpEvent);
    public static readonly RoutedEvent TouchUpEvent = Own(UIElement.TouchUpEvent);
    public static readonly RoutedEvent TouchEnterEvent = Own(UIElement.TouchEnterEvent);
    public static readonly RoutedEvent TouchLeaveEvent = Own(UIElement.TouchLeaveEvent);
    public static readonly RoutedEvent GotTouchCaptureEvent = Own(UIElement.GotTouchCaptureEvent);
    public static readonly RoutedEvent LostTouchCaptureEvent = Own(UIElement.LostTouchCaptureEvent);

    public static readonly RoutedEvent PreviewDragEnterEvent = Own(UIElement.PreviewDragEnterEvent);
    public static readonly RoutedEvent DragEnterEvent = Own(UIElement.DragEnterEvent);
    public static readonly RoutedEvent PreviewDragOverEvent = Own(UIElement.PreviewDragOverEvent);
    public static readonly RoutedEvent DragOverEvent = Own(UIElement.DragOverEvent);
    public static readonly RoutedEvent PreviewDragLeaveEvent = Own(UIElement.PreviewDragLeaveEvent);
    public static readonly RoutedEvent DragLeaveEvent = Own(UIElement.DragLeaveEvent);
    public static readonly RoutedEvent PreviewDropEvent = Own(UIElement.PreviewDropEvent);
    public static readonly RoutedEvent DropEvent = Own(UIElement.DropEvent);
    public static readonly RoutedEvent PreviewGiveFeedbackEvent = Own(UIElement.PreviewGiveFeedbackEvent);
    public static readonly RoutedEvent GiveFeedbackEvent = Own(UIElement.GiveFeedbackEvent);
    public static readonly RoutedEvent PreviewQueryContinueDragEvent = Own(UIElement.PreviewQueryContinueDragEvent);
    public static readonly RoutedEvent QueryContinueDragEvent = Own(UIElement.QueryContinueDragEvent);
    public static readonly RoutedEvent QueryCursorEvent = Own(UIElement.QueryCursorEvent);

    public event DependencyPropertyChangedEventHandler? FocusableChanged;
    public event DependencyPropertyChangedEventHandler? IsEnabledChanged;
    public event DependencyPropertyChangedEventHandler? IsHitTestVisibleChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsVisibleChanged;

    public event KeyEventHandler PreviewKeyDown
    {
        add => AddHandler(PreviewKeyDownEvent, value);
        remove => RemoveHandler(PreviewKeyDownEvent, value);
    }

    public event KeyEventHandler KeyDown
    {
        add => AddHandler(KeyDownEvent, value);
        remove => RemoveHandler(KeyDownEvent, value);
    }

    public event KeyEventHandler PreviewKeyUp
    {
        add => AddHandler(PreviewKeyUpEvent, value);
        remove => RemoveHandler(PreviewKeyUpEvent, value);
    }

    public event KeyEventHandler KeyUp
    {
        add => AddHandler(KeyUpEvent, value);
        remove => RemoveHandler(KeyUpEvent, value);
    }

    public event TextCompositionEventHandler PreviewTextInput
    {
        add => AddHandler(PreviewTextInputEvent, value);
        remove => RemoveHandler(PreviewTextInputEvent, value);
    }

    public event TextCompositionEventHandler TextInput
    {
        add => AddHandler(TextInputEvent, value);
        remove => RemoveHandler(TextInputEvent, value);
    }

    public event KeyboardFocusChangedEventHandler PreviewGotKeyboardFocus
    {
        add => AddHandler(PreviewGotKeyboardFocusEvent, value);
        remove => RemoveHandler(PreviewGotKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler GotKeyboardFocus
    {
        add => AddHandler(GotKeyboardFocusEvent, value);
        remove => RemoveHandler(GotKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus
    {
        add => AddHandler(PreviewLostKeyboardFocusEvent, value);
        remove => RemoveHandler(PreviewLostKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler LostKeyboardFocus
    {
        add => AddHandler(LostKeyboardFocusEvent, value);
        remove => RemoveHandler(LostKeyboardFocusEvent, value);
    }

    public event RoutedEventHandler GotFocus
    {
        add => AddHandler(GotFocusEvent, value);
        remove => RemoveHandler(GotFocusEvent, value);
    }

    public event RoutedEventHandler LostFocus
    {
        add => AddHandler(LostFocusEvent, value);
        remove => RemoveHandler(LostFocusEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseDown
    {
        add => AddHandler(PreviewMouseDownEvent, value);
        remove => RemoveHandler(PreviewMouseDownEvent, value);
    }

    public event MouseButtonEventHandler MouseDown
    {
        add => AddHandler(MouseDownEvent, value);
        remove => RemoveHandler(MouseDownEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseUp
    {
        add => AddHandler(PreviewMouseUpEvent, value);
        remove => RemoveHandler(PreviewMouseUpEvent, value);
    }

    public event MouseButtonEventHandler MouseUp
    {
        add => AddHandler(MouseUpEvent, value);
        remove => RemoveHandler(MouseUpEvent, value);
    }

    public event MouseEventHandler PreviewMouseMove
    {
        add => AddHandler(PreviewMouseMoveEvent, value);
        remove => RemoveHandler(PreviewMouseMoveEvent, value);
    }

    public event MouseEventHandler MouseMove
    {
        add => AddHandler(MouseMoveEvent, value);
        remove => RemoveHandler(MouseMoveEvent, value);
    }

    public event MouseEventHandler MouseEnter
    {
        add => AddHandler(MouseEnterEvent, value);
        remove => RemoveHandler(MouseEnterEvent, value);
    }

    public event MouseEventHandler MouseLeave
    {
        add => AddHandler(MouseLeaveEvent, value);
        remove => RemoveHandler(MouseLeaveEvent, value);
    }

    public event MouseWheelEventHandler PreviewMouseWheel
    {
        add => AddHandler(PreviewMouseWheelEvent, value);
        remove => RemoveHandler(PreviewMouseWheelEvent, value);
    }

    public event MouseWheelEventHandler MouseWheel
    {
        add => AddHandler(MouseWheelEvent, value);
        remove => RemoveHandler(MouseWheelEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseLeftButtonDown
    {
        add => AddHandler(PreviewMouseLeftButtonDownEvent, value);
        remove => RemoveHandler(PreviewMouseLeftButtonDownEvent, value);
    }

    public event MouseButtonEventHandler MouseLeftButtonDown
    {
        add => AddHandler(MouseLeftButtonDownEvent, value);
        remove => RemoveHandler(MouseLeftButtonDownEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseLeftButtonUp
    {
        add => AddHandler(PreviewMouseLeftButtonUpEvent, value);
        remove => RemoveHandler(PreviewMouseLeftButtonUpEvent, value);
    }

    public event MouseButtonEventHandler MouseLeftButtonUp
    {
        add => AddHandler(MouseLeftButtonUpEvent, value);
        remove => RemoveHandler(MouseLeftButtonUpEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseRightButtonDown
    {
        add => AddHandler(PreviewMouseRightButtonDownEvent, value);
        remove => RemoveHandler(PreviewMouseRightButtonDownEvent, value);
    }

    public event MouseButtonEventHandler MouseRightButtonDown
    {
        add => AddHandler(MouseRightButtonDownEvent, value);
        remove => RemoveHandler(MouseRightButtonDownEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseRightButtonUp
    {
        add => AddHandler(PreviewMouseRightButtonUpEvent, value);
        remove => RemoveHandler(PreviewMouseRightButtonUpEvent, value);
    }

    public event MouseButtonEventHandler MouseRightButtonUp
    {
        add => AddHandler(MouseRightButtonUpEvent, value);
        remove => RemoveHandler(MouseRightButtonUpEvent, value);
    }

    public event MouseEventHandler GotMouseCapture
    {
        add => AddHandler(GotMouseCaptureEvent, value);
        remove => RemoveHandler(GotMouseCaptureEvent, value);
    }

    public event MouseEventHandler LostMouseCapture
    {
        add => AddHandler(LostMouseCaptureEvent, value);
        remove => RemoveHandler(LostMouseCaptureEvent, value);
    }

    public event StylusDownEventHandler PreviewStylusDown
    {
        add => AddHandler(PreviewStylusDownEvent, value);
        remove => RemoveHandler(PreviewStylusDownEvent, value);
    }

    public event StylusDownEventHandler StylusDown
    {
        add => AddHandler(StylusDownEvent, value);
        remove => RemoveHandler(StylusDownEvent, value);
    }

    public event StylusEventHandler PreviewStylusMove
    {
        add => AddHandler(PreviewStylusMoveEvent, value);
        remove => RemoveHandler(PreviewStylusMoveEvent, value);
    }

    public event StylusEventHandler StylusMove
    {
        add => AddHandler(StylusMoveEvent, value);
        remove => RemoveHandler(StylusMoveEvent, value);
    }

    public event StylusEventHandler PreviewStylusUp
    {
        add => AddHandler(PreviewStylusUpEvent, value);
        remove => RemoveHandler(PreviewStylusUpEvent, value);
    }

    public event StylusEventHandler StylusUp
    {
        add => AddHandler(StylusUpEvent, value);
        remove => RemoveHandler(StylusUpEvent, value);
    }

    public event StylusEventHandler PreviewStylusInAirMove
    {
        add => AddHandler(PreviewStylusInAirMoveEvent, value);
        remove => RemoveHandler(PreviewStylusInAirMoveEvent, value);
    }

    public event StylusEventHandler StylusInAirMove
    {
        add => AddHandler(StylusInAirMoveEvent, value);
        remove => RemoveHandler(StylusInAirMoveEvent, value);
    }

    public event StylusEventHandler StylusEnter
    {
        add => AddHandler(StylusEnterEvent, value);
        remove => RemoveHandler(StylusEnterEvent, value);
    }

    public event StylusEventHandler StylusLeave
    {
        add => AddHandler(StylusLeaveEvent, value);
        remove => RemoveHandler(StylusLeaveEvent, value);
    }

    public event StylusEventHandler PreviewStylusInRange
    {
        add => AddHandler(PreviewStylusInRangeEvent, value);
        remove => RemoveHandler(PreviewStylusInRangeEvent, value);
    }

    public event StylusEventHandler StylusInRange
    {
        add => AddHandler(StylusInRangeEvent, value);
        remove => RemoveHandler(StylusInRangeEvent, value);
    }

    public event StylusEventHandler PreviewStylusOutOfRange
    {
        add => AddHandler(PreviewStylusOutOfRangeEvent, value);
        remove => RemoveHandler(PreviewStylusOutOfRangeEvent, value);
    }

    public event StylusEventHandler StylusOutOfRange
    {
        add => AddHandler(StylusOutOfRangeEvent, value);
        remove => RemoveHandler(StylusOutOfRangeEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonDown
    {
        add => AddHandler(PreviewStylusButtonDownEvent, value);
        remove => RemoveHandler(PreviewStylusButtonDownEvent, value);
    }

    public event StylusButtonEventHandler StylusButtonDown
    {
        add => AddHandler(StylusButtonDownEvent, value);
        remove => RemoveHandler(StylusButtonDownEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonUp
    {
        add => AddHandler(PreviewStylusButtonUpEvent, value);
        remove => RemoveHandler(PreviewStylusButtonUpEvent, value);
    }

    public event StylusButtonEventHandler StylusButtonUp
    {
        add => AddHandler(StylusButtonUpEvent, value);
        remove => RemoveHandler(StylusButtonUpEvent, value);
    }

    public event StylusSystemGestureEventHandler PreviewStylusSystemGesture
    {
        add => AddHandler(PreviewStylusSystemGestureEvent, value);
        remove => RemoveHandler(PreviewStylusSystemGestureEvent, value);
    }

    public event StylusSystemGestureEventHandler StylusSystemGesture
    {
        add => AddHandler(StylusSystemGestureEvent, value);
        remove => RemoveHandler(StylusSystemGestureEvent, value);
    }

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

    public event EventHandler<TouchEventArgs> PreviewTouchDown
    {
        add => AddHandler(PreviewTouchDownEvent, value);
        remove => RemoveHandler(PreviewTouchDownEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchDown
    {
        add => AddHandler(TouchDownEvent, value);
        remove => RemoveHandler(TouchDownEvent, value);
    }

    public event EventHandler<TouchEventArgs> PreviewTouchMove
    {
        add => AddHandler(PreviewTouchMoveEvent, value);
        remove => RemoveHandler(PreviewTouchMoveEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchMove
    {
        add => AddHandler(TouchMoveEvent, value);
        remove => RemoveHandler(TouchMoveEvent, value);
    }

    public event EventHandler<TouchEventArgs> PreviewTouchUp
    {
        add => AddHandler(PreviewTouchUpEvent, value);
        remove => RemoveHandler(PreviewTouchUpEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchUp
    {
        add => AddHandler(TouchUpEvent, value);
        remove => RemoveHandler(TouchUpEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchEnter
    {
        add => AddHandler(TouchEnterEvent, value);
        remove => RemoveHandler(TouchEnterEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchLeave
    {
        add => AddHandler(TouchLeaveEvent, value);
        remove => RemoveHandler(TouchLeaveEvent, value);
    }

    public event EventHandler<TouchEventArgs> GotTouchCapture
    {
        add => AddHandler(GotTouchCaptureEvent, value);
        remove => RemoveHandler(GotTouchCaptureEvent, value);
    }

    public event EventHandler<TouchEventArgs> LostTouchCapture
    {
        add => AddHandler(LostTouchCaptureEvent, value);
        remove => RemoveHandler(LostTouchCaptureEvent, value);
    }

    public event DragEventHandler PreviewDragEnter
    {
        add => AddHandler(PreviewDragEnterEvent, value);
        remove => RemoveHandler(PreviewDragEnterEvent, value);
    }

    public event DragEventHandler DragEnter
    {
        add => AddHandler(DragEnterEvent, value);
        remove => RemoveHandler(DragEnterEvent, value);
    }

    public event DragEventHandler PreviewDragOver
    {
        add => AddHandler(PreviewDragOverEvent, value);
        remove => RemoveHandler(PreviewDragOverEvent, value);
    }

    public event DragEventHandler DragOver
    {
        add => AddHandler(DragOverEvent, value);
        remove => RemoveHandler(DragOverEvent, value);
    }

    public event DragEventHandler PreviewDragLeave
    {
        add => AddHandler(PreviewDragLeaveEvent, value);
        remove => RemoveHandler(PreviewDragLeaveEvent, value);
    }

    public event DragEventHandler DragLeave
    {
        add => AddHandler(DragLeaveEvent, value);
        remove => RemoveHandler(DragLeaveEvent, value);
    }

    public event DragEventHandler PreviewDrop
    {
        add => AddHandler(PreviewDropEvent, value);
        remove => RemoveHandler(PreviewDropEvent, value);
    }

    public event DragEventHandler Drop
    {
        add => AddHandler(DropEvent, value);
        remove => RemoveHandler(DropEvent, value);
    }

    public event GiveFeedbackEventHandler PreviewGiveFeedback
    {
        add => AddHandler(PreviewGiveFeedbackEvent, value);
        remove => RemoveHandler(PreviewGiveFeedbackEvent, value);
    }

    public event GiveFeedbackEventHandler GiveFeedback
    {
        add => AddHandler(GiveFeedbackEvent, value);
        remove => RemoveHandler(GiveFeedbackEvent, value);
    }

    public event QueryContinueDragEventHandler PreviewQueryContinueDrag
    {
        add => AddHandler(PreviewQueryContinueDragEvent, value);
        remove => RemoveHandler(PreviewQueryContinueDragEvent, value);
    }

    public event QueryContinueDragEventHandler QueryContinueDrag
    {
        add => AddHandler(QueryContinueDragEvent, value);
        remove => RemoveHandler(QueryContinueDragEvent, value);
    }

    public event QueryCursorEventHandler QueryCursor
    {
        add => AddHandler(QueryCursorEvent, value);
        remove => RemoveHandler(QueryCursorEvent, value);
    }
}
