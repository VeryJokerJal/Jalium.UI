using Jalium.UI.Input;

namespace Jalium.UI;

/// <summary>
/// Interface for elements that can receive input and focus.
/// </summary>
public interface IInputElement
{
    /// <summary>
    /// Raises a routed event on this input element.
    /// </summary>
    void RaiseEvent(RoutedEventArgs e);

    /// <summary>
    /// Adds an instance handler for a routed event.
    /// </summary>
    void AddHandler(RoutedEvent routedEvent, Delegate handler);

    /// <summary>
    /// Removes an instance handler for a routed event.
    /// </summary>
    void RemoveHandler(RoutedEvent routedEvent, Delegate handler);

    /// <summary>
    /// Gets a value indicating whether this element can receive focus.
    /// </summary>
    bool Focusable { get; set; }

    /// <summary>
    /// Gets a value indicating whether this element is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether this element has keyboard focus.
    /// </summary>
    bool IsKeyboardFocused { get; }

    /// <summary>
    /// Gets a value indicating whether keyboard focus is within this element.
    /// </summary>
    bool IsKeyboardFocusWithin { get; }

    /// <summary>
    /// Gets a value indicating whether the mouse is over this element or one of its descendants.
    /// </summary>
    bool IsMouseOver { get; }

    /// <summary>
    /// Gets a value indicating whether the mouse is directly over this element.
    /// </summary>
    bool IsMouseDirectlyOver { get; }

    /// <summary>
    /// Gets a value indicating whether this element has mouse capture.
    /// </summary>
    bool IsMouseCaptured { get; }

    /// <summary>
    /// Gets a value indicating whether the stylus is over this element or one of its descendants.
    /// </summary>
    bool IsStylusOver { get; }

    /// <summary>
    /// Gets a value indicating whether the stylus is directly over this element.
    /// </summary>
    bool IsStylusDirectlyOver { get; }

    /// <summary>
    /// Gets a value indicating whether this element has stylus capture.
    /// </summary>
    bool IsStylusCaptured { get; }

    /// <summary>
    /// Attempts to set focus to this element.
    /// </summary>
    /// <returns>True if focus was set; otherwise, false.</returns>
    bool Focus();

    /// <summary>
    /// Captures the mouse to this element.
    /// </summary>
    bool CaptureMouse();

    /// <summary>
    /// Releases mouse capture from this element.
    /// </summary>
    void ReleaseMouseCapture();

    /// <summary>
    /// Captures the current stylus to this element.
    /// </summary>
    bool CaptureStylus();

    /// <summary>
    /// Releases stylus capture from this element.
    /// </summary>
    void ReleaseStylusCapture();

    event Input.MouseButtonEventHandler PreviewMouseLeftButtonDown;
    event Input.MouseButtonEventHandler MouseLeftButtonDown;
    event Input.MouseButtonEventHandler PreviewMouseLeftButtonUp;
    event Input.MouseButtonEventHandler MouseLeftButtonUp;
    event Input.MouseButtonEventHandler PreviewMouseRightButtonDown;
    event Input.MouseButtonEventHandler MouseRightButtonDown;
    event Input.MouseButtonEventHandler PreviewMouseRightButtonUp;
    event Input.MouseButtonEventHandler MouseRightButtonUp;
    event Input.MouseEventHandler PreviewMouseMove;
    event Input.MouseEventHandler MouseMove;
    event Input.MouseWheelEventHandler PreviewMouseWheel;
    event Input.MouseWheelEventHandler MouseWheel;
    event Input.MouseEventHandler MouseEnter;
    event Input.MouseEventHandler MouseLeave;
    event Input.MouseEventHandler GotMouseCapture;
    event Input.MouseEventHandler LostMouseCapture;

    event Input.KeyEventHandler PreviewKeyDown;
    event Input.KeyEventHandler KeyDown;
    event Input.KeyEventHandler PreviewKeyUp;
    event Input.KeyEventHandler KeyUp;
    event KeyboardFocusChangedEventHandler PreviewGotKeyboardFocus;
    event KeyboardFocusChangedEventHandler GotKeyboardFocus;
    event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus;
    event KeyboardFocusChangedEventHandler LostKeyboardFocus;

    event StylusEventHandler GotStylusCapture;
    event StylusEventHandler LostStylusCapture;
    event StylusButtonEventHandler PreviewStylusButtonDown;
    event StylusButtonEventHandler PreviewStylusButtonUp;
    event StylusDownEventHandler PreviewStylusDown;
    event StylusEventHandler PreviewStylusInAirMove;
    event StylusEventHandler PreviewStylusInRange;
    event StylusEventHandler PreviewStylusMove;
    event StylusEventHandler PreviewStylusOutOfRange;
    event StylusSystemGestureEventHandler PreviewStylusSystemGesture;
    event StylusEventHandler PreviewStylusUp;
    event TextCompositionEventHandler PreviewTextInput;
    event StylusButtonEventHandler StylusButtonDown;
    event StylusButtonEventHandler StylusButtonUp;
    event StylusDownEventHandler StylusDown;
    event StylusEventHandler StylusEnter;
    event StylusEventHandler StylusInAirMove;
    event StylusEventHandler StylusInRange;
    event StylusEventHandler StylusLeave;
    event StylusEventHandler StylusMove;
    event StylusEventHandler StylusOutOfRange;
    event StylusSystemGestureEventHandler StylusSystemGesture;
    event StylusEventHandler StylusUp;
    event TextCompositionEventHandler TextInput;
}
