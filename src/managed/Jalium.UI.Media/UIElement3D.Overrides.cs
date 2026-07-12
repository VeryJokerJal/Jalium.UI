using Jalium.UI.Input;

namespace Jalium.UI;

public abstract partial class UIElement3D
{
    protected virtual Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;

    protected virtual void OnAccessKey(AccessKeyEventArgs e)
    {
    }

    protected virtual void OnGotFocus(RoutedEventArgs e)
    {
    }

    protected virtual void OnLostFocus(RoutedEventArgs e)
    {
    }

    protected virtual void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseCaptureWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseDirectlyOverChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusCaptureWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusDirectlyOverChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected internal virtual void OnPreviewKeyDown(KeyEventArgs e)
    {
    }

    protected internal virtual void OnKeyDown(KeyEventArgs e)
    {
    }

    protected internal virtual void OnPreviewKeyUp(KeyEventArgs e)
    {
    }

    protected internal virtual void OnKeyUp(KeyEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTextInput(TextCompositionEventArgs e)
    {
    }

    protected internal virtual void OnTextInput(TextCompositionEventArgs e)
    {
    }

    protected internal virtual void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnPreviewLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseMove(MouseEventArgs e)
    {
    }

    protected internal virtual void OnMouseMove(MouseEventArgs e)
    {
    }

    protected internal virtual void OnMouseEnter(MouseEventArgs e)
    {
    }

    protected internal virtual void OnMouseLeave(MouseEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
    }

    protected internal virtual void OnMouseWheel(MouseWheelEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnGotMouseCapture(MouseEventArgs e)
    {
    }

    protected internal virtual void OnLostMouseCapture(MouseEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusDown(StylusDownEventArgs e)
    {
    }

    protected internal virtual void OnStylusDown(StylusDownEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusUp(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusUp(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusInAirMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusInAirMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusEnter(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusLeave(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusInRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusInRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusOutOfRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusOutOfRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusSystemGesture(StylusSystemGestureEventArgs e)
    {
    }

    protected internal virtual void OnStylusSystemGesture(StylusSystemGestureEventArgs e)
    {
    }

    protected internal virtual void OnGotStylusCapture(StylusEventArgs e)
    {
    }

    protected internal virtual void OnLostStylusCapture(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTouchDown(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchDown(TouchEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTouchMove(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchMove(TouchEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTouchUp(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchUp(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchEnter(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchLeave(TouchEventArgs e)
    {
    }

    protected internal virtual void OnGotTouchCapture(TouchEventArgs e)
    {
    }

    protected internal virtual void OnLostTouchCapture(TouchEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDragEnter(DragEventArgs e)
    {
    }

    protected internal virtual void OnDragEnter(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDragOver(DragEventArgs e)
    {
    }

    protected internal virtual void OnDragOver(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDragLeave(DragEventArgs e)
    {
    }

    protected internal virtual void OnDragLeave(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDrop(DragEventArgs e)
    {
    }

    protected internal virtual void OnDrop(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewGiveFeedback(GiveFeedbackEventArgs e)
    {
    }

    protected internal virtual void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
    }

    protected internal virtual void OnPreviewQueryContinueDrag(QueryContinueDragEventArgs e)
    {
    }

    protected internal virtual void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
    }

    protected internal virtual void OnQueryCursor(QueryCursorEventArgs e)
    {
    }

    private void InvokeClassHandler(RoutedEventArgs e)
    {
        var routedEvent = e.RoutedEvent;
        if (ReferenceEquals(routedEvent, PreviewKeyDownEvent)) OnPreviewKeyDown((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, KeyDownEvent)) OnKeyDown((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewKeyUpEvent)) OnPreviewKeyUp((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, KeyUpEvent)) OnKeyUp((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTextInputEvent)) OnPreviewTextInput((TextCompositionEventArgs)e);
        else if (ReferenceEquals(routedEvent, TextInputEvent)) OnTextInput((TextCompositionEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewGotKeyboardFocusEvent)) OnPreviewGotKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotKeyboardFocusEvent)) OnGotKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewLostKeyboardFocusEvent)) OnPreviewLostKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostKeyboardFocusEvent)) OnLostKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotFocusEvent)) OnGotFocus(e);
        else if (ReferenceEquals(routedEvent, LostFocusEvent)) OnLostFocus(e);
        else if (ReferenceEquals(routedEvent, PreviewMouseDownEvent)) OnPreviewMouseDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseDownEvent)) OnMouseDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseUpEvent)) OnPreviewMouseUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseUpEvent)) OnMouseUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseMoveEvent)) OnPreviewMouseMove((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseMoveEvent)) OnMouseMove((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseEnterEvent)) OnMouseEnter((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseLeaveEvent)) OnMouseLeave((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseWheelEvent)) OnPreviewMouseWheel((MouseWheelEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseWheelEvent)) OnMouseWheel((MouseWheelEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseLeftButtonDownEvent)) OnPreviewMouseLeftButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseLeftButtonDownEvent)) OnMouseLeftButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseLeftButtonUpEvent)) OnPreviewMouseLeftButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseLeftButtonUpEvent)) OnMouseLeftButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseRightButtonDownEvent)) OnPreviewMouseRightButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseRightButtonDownEvent)) OnMouseRightButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseRightButtonUpEvent)) OnPreviewMouseRightButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseRightButtonUpEvent)) OnMouseRightButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotMouseCaptureEvent)) OnGotMouseCapture((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostMouseCaptureEvent)) OnLostMouseCapture((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusDownEvent)) OnPreviewStylusDown((StylusDownEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusDownEvent)) OnStylusDown((StylusDownEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusMoveEvent)) OnPreviewStylusMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusMoveEvent)) OnStylusMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusUpEvent)) OnPreviewStylusUp((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusUpEvent)) OnStylusUp((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusInAirMoveEvent)) OnPreviewStylusInAirMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusInAirMoveEvent)) OnStylusInAirMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusEnterEvent)) OnStylusEnter((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusLeaveEvent)) OnStylusLeave((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusInRangeEvent)) OnPreviewStylusInRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusInRangeEvent)) OnStylusInRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusOutOfRangeEvent)) OnPreviewStylusOutOfRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusOutOfRangeEvent)) OnStylusOutOfRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusButtonDownEvent)) OnPreviewStylusButtonDown((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusButtonDownEvent)) OnStylusButtonDown((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusButtonUpEvent)) OnPreviewStylusButtonUp((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusButtonUpEvent)) OnStylusButtonUp((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusSystemGestureEvent)) OnPreviewStylusSystemGesture((StylusSystemGestureEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusSystemGestureEvent)) OnStylusSystemGesture((StylusSystemGestureEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotStylusCaptureEvent)) OnGotStylusCapture((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostStylusCaptureEvent)) OnLostStylusCapture((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTouchDownEvent)) OnPreviewTouchDown((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchDownEvent)) OnTouchDown((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTouchMoveEvent)) OnPreviewTouchMove((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchMoveEvent)) OnTouchMove((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTouchUpEvent)) OnPreviewTouchUp((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchUpEvent)) OnTouchUp((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchEnterEvent)) OnTouchEnter((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchLeaveEvent)) OnTouchLeave((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotTouchCaptureEvent)) OnGotTouchCapture((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostTouchCaptureEvent)) OnLostTouchCapture((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDragEnterEvent)) OnPreviewDragEnter((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DragEnterEvent)) OnDragEnter((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDragOverEvent)) OnPreviewDragOver((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DragOverEvent)) OnDragOver((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDragLeaveEvent)) OnPreviewDragLeave((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DragLeaveEvent)) OnDragLeave((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDropEvent)) OnPreviewDrop((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DropEvent)) OnDrop((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewGiveFeedbackEvent)) OnPreviewGiveFeedback((GiveFeedbackEventArgs)e);
        else if (ReferenceEquals(routedEvent, GiveFeedbackEvent)) OnGiveFeedback((GiveFeedbackEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewQueryContinueDragEvent)) OnPreviewQueryContinueDrag((QueryContinueDragEventArgs)e);
        else if (ReferenceEquals(routedEvent, QueryContinueDragEvent)) OnQueryContinueDrag((QueryContinueDragEventArgs)e);
        else if (ReferenceEquals(routedEvent, QueryCursorEvent)) OnQueryCursor((QueryCursorEventArgs)e);
    }
}
