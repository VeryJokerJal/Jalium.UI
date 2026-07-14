using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>Provides the primary mouse device and mouse routed events.</summary>
public static class Mouse
{
    private const int IntermediatePointCapacity = 64;

    private static readonly SystemMouseDevice s_primaryDevice = new();
    private static readonly object s_stateGate = new();
    private static readonly List<Point> s_intermediatePoints = [];
    private static Point s_lastPosition;
    private static MouseButtonStates s_buttons = MouseButtonStates.AllReleased;
    private static IInputElement? s_capturedByStaticApi;
    private static CaptureMode s_captureMode;
    private static Cursor? s_overrideCursor;
    private static Cursor? s_currentCursor;

    static Mouse()
    {
        InputManager.Current.RegisterPrimaryMouseDevice(s_primaryDevice);
    }

    /// <summary>The standard wheel delta for one line/notch.</summary>
    public const int MouseWheelDeltaForOneLine = 120;

    #region Routed events

    public static readonly RoutedEvent PreviewMouseMoveEvent = UIElement.PreviewMouseMoveEvent;
    public static readonly RoutedEvent MouseMoveEvent = UIElement.MouseMoveEvent;
    public static readonly RoutedEvent PreviewMouseDownEvent = UIElement.PreviewMouseDownEvent;
    public static readonly RoutedEvent MouseDownEvent = UIElement.MouseDownEvent;
    public static readonly RoutedEvent PreviewMouseUpEvent = UIElement.PreviewMouseUpEvent;
    public static readonly RoutedEvent MouseUpEvent = UIElement.MouseUpEvent;
    public static readonly RoutedEvent PreviewMouseWheelEvent = UIElement.PreviewMouseWheelEvent;
    public static readonly RoutedEvent MouseWheelEvent = UIElement.MouseWheelEvent;
    public static readonly RoutedEvent MouseEnterEvent = UIElement.MouseEnterEvent;
    public static readonly RoutedEvent MouseLeaveEvent = UIElement.MouseLeaveEvent;
    public static readonly RoutedEvent GotMouseCaptureEvent = UIElement.GotMouseCaptureEvent;
    public static readonly RoutedEvent LostMouseCaptureEvent = UIElement.LostMouseCaptureEvent;
    public static readonly RoutedEvent QueryCursorEvent = UIElement.QueryCursorEvent;

    public static readonly RoutedEvent PreviewMouseDownOutsideCapturedElementEvent =
        EventManager.RegisterRoutedEvent(
            "PreviewMouseDownOutsideCapturedElement",
            RoutingStrategy.Tunnel,
            typeof(MouseButtonEventHandler),
            typeof(Mouse));

    public static readonly RoutedEvent PreviewMouseUpOutsideCapturedElementEvent =
        EventManager.RegisterRoutedEvent(
            "PreviewMouseUpOutsideCapturedElement",
            RoutingStrategy.Tunnel,
            typeof(MouseButtonEventHandler),
            typeof(Mouse));

    #endregion

    #region Attached routed-event helpers

    public static void AddPreviewMouseMoveHandler(DependencyObject element, MouseEventHandler handler) => AddHandler(element, PreviewMouseMoveEvent, handler);
    public static void RemovePreviewMouseMoveHandler(DependencyObject element, MouseEventHandler handler) => RemoveHandler(element, PreviewMouseMoveEvent, handler);
    public static void AddMouseMoveHandler(DependencyObject element, MouseEventHandler handler) => AddHandler(element, MouseMoveEvent, handler);
    public static void RemoveMouseMoveHandler(DependencyObject element, MouseEventHandler handler) => RemoveHandler(element, MouseMoveEvent, handler);
    public static void AddPreviewMouseDownHandler(DependencyObject element, MouseButtonEventHandler handler) => AddHandler(element, PreviewMouseDownEvent, handler);
    public static void RemovePreviewMouseDownHandler(DependencyObject element, MouseButtonEventHandler handler) => RemoveHandler(element, PreviewMouseDownEvent, handler);
    public static void AddMouseDownHandler(DependencyObject element, MouseButtonEventHandler handler) => AddHandler(element, MouseDownEvent, handler);
    public static void RemoveMouseDownHandler(DependencyObject element, MouseButtonEventHandler handler) => RemoveHandler(element, MouseDownEvent, handler);
    public static void AddPreviewMouseUpHandler(DependencyObject element, MouseButtonEventHandler handler) => AddHandler(element, PreviewMouseUpEvent, handler);
    public static void RemovePreviewMouseUpHandler(DependencyObject element, MouseButtonEventHandler handler) => RemoveHandler(element, PreviewMouseUpEvent, handler);
    public static void AddMouseUpHandler(DependencyObject element, MouseButtonEventHandler handler) => AddHandler(element, MouseUpEvent, handler);
    public static void RemoveMouseUpHandler(DependencyObject element, MouseButtonEventHandler handler) => RemoveHandler(element, MouseUpEvent, handler);
    public static void AddPreviewMouseWheelHandler(DependencyObject element, MouseWheelEventHandler handler) => AddHandler(element, PreviewMouseWheelEvent, handler);
    public static void RemovePreviewMouseWheelHandler(DependencyObject element, MouseWheelEventHandler handler) => RemoveHandler(element, PreviewMouseWheelEvent, handler);
    public static void AddMouseWheelHandler(DependencyObject element, MouseWheelEventHandler handler) => AddHandler(element, MouseWheelEvent, handler);
    public static void RemoveMouseWheelHandler(DependencyObject element, MouseWheelEventHandler handler) => RemoveHandler(element, MouseWheelEvent, handler);
    public static void AddMouseEnterHandler(DependencyObject element, MouseEventHandler handler) => AddHandler(element, MouseEnterEvent, handler);
    public static void RemoveMouseEnterHandler(DependencyObject element, MouseEventHandler handler) => RemoveHandler(element, MouseEnterEvent, handler);
    public static void AddMouseLeaveHandler(DependencyObject element, MouseEventHandler handler) => AddHandler(element, MouseLeaveEvent, handler);
    public static void RemoveMouseLeaveHandler(DependencyObject element, MouseEventHandler handler) => RemoveHandler(element, MouseLeaveEvent, handler);
    public static void AddGotMouseCaptureHandler(DependencyObject element, MouseEventHandler handler) => AddHandler(element, GotMouseCaptureEvent, handler);
    public static void RemoveGotMouseCaptureHandler(DependencyObject element, MouseEventHandler handler) => RemoveHandler(element, GotMouseCaptureEvent, handler);
    public static void AddLostMouseCaptureHandler(DependencyObject element, MouseEventHandler handler) => AddHandler(element, LostMouseCaptureEvent, handler);
    public static void RemoveLostMouseCaptureHandler(DependencyObject element, MouseEventHandler handler) => RemoveHandler(element, LostMouseCaptureEvent, handler);
    public static void AddQueryCursorHandler(DependencyObject element, QueryCursorEventHandler handler) => AddHandler(element, QueryCursorEvent, handler);
    public static void RemoveQueryCursorHandler(DependencyObject element, QueryCursorEventHandler handler) => RemoveHandler(element, QueryCursorEvent, handler);
    public static void AddPreviewMouseDownOutsideCapturedElementHandler(DependencyObject element, MouseButtonEventHandler handler) => AddHandler(element, PreviewMouseDownOutsideCapturedElementEvent, handler);
    public static void RemovePreviewMouseDownOutsideCapturedElementHandler(DependencyObject element, MouseButtonEventHandler handler) => RemoveHandler(element, PreviewMouseDownOutsideCapturedElementEvent, handler);
    public static void AddPreviewMouseUpOutsideCapturedElementHandler(DependencyObject element, MouseButtonEventHandler handler) => AddHandler(element, PreviewMouseUpOutsideCapturedElementEvent, handler);
    public static void RemovePreviewMouseUpOutsideCapturedElementHandler(DependencyObject element, MouseButtonEventHandler handler) => RemoveHandler(element, PreviewMouseUpOutsideCapturedElementEvent, handler);

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

    #endregion

    #region Device state

    /// <summary>Gets the primary logical mouse device.</summary>
    public static MouseDevice PrimaryDevice => s_primaryDevice;

    /// <summary>Gets the element directly under the mouse pointer.</summary>
    public static IInputElement? DirectlyOver => UIElement.MouseDirectlyOverElement;

    /// <summary>Gets the element that currently owns mouse capture.</summary>
    public static IInputElement? Captured
    {
        get
        {
            if (UIElement.MouseCapturedElement is { } capturedElement)
                return capturedElement;

            if (s_capturedByStaticApi is { IsMouseCaptured: true } captured)
                return captured;

            return null;
        }
    }

    /// <summary>Gets the most recently reported pointer position in host coordinates.</summary>
    public static Point Position
    {
        get
        {
            lock (s_stateGate)
                return s_lastPosition;
        }
        internal set => UpdateState(value, UIElement.MouseDirectlyOverElement, s_buttons);
    }

    /// <summary>Gets or sets the cursor that overrides all element cursors.</summary>
    public static Cursor? OverrideCursor
    {
        get => s_overrideCursor;
        set
        {
            if (ReferenceEquals(s_overrideCursor, value))
                return;

            s_overrideCursor = value;
            UpdateCursor();
        }
    }

    public static MouseButtonState LeftButton => s_buttons.Left;
    public static MouseButtonState MiddleButton => s_buttons.Middle;
    public static MouseButtonState RightButton => s_buttons.Right;
    public static MouseButtonState XButton1 => s_buttons.XButton1;
    public static MouseButtonState XButton2 => s_buttons.XButton2;

    internal static Cursor? CurrentCursor => s_currentCursor;
    internal static CaptureMode CurrentCaptureMode
    {
        get
        {
            IInputElement? captured = Captured;
            if (captured is null)
                return CaptureMode.None;
            return ReferenceEquals(captured, s_capturedByStaticApi)
                ? s_captureMode
                : CaptureMode.Element;
        }
    }

    internal static void UpdateState(Point position, UIElement? directlyOver, MouseButtonStates buttons)
    {
        lock (s_stateGate)
        {
            if (s_intermediatePoints.Count == 0 || s_intermediatePoints[^1] != position)
            {
                if (s_intermediatePoints.Count == IntermediatePointCapacity)
                    s_intermediatePoints.RemoveAt(0);
                s_intermediatePoints.Add(position);
            }

            s_lastPosition = position;
            s_buttons = buttons;
        }

        UIElement.SetMouseDirectlyOverElement(directlyOver);
        s_primaryDevice.DirectlyOver = directlyOver;
        s_primaryDevice.Captured = Captured as UIElement;
    }

    #endregion

    #region Capture and queries

    public static bool Capture(IInputElement? element) => Capture(element, CaptureMode.Element);

    public static bool Capture(IInputElement? element, CaptureMode captureMode)
    {
        if (!Enum.IsDefined(captureMode))
            throw new InvalidEnumArgumentException(nameof(captureMode), (int)captureMode, typeof(CaptureMode));

        if (element is null)
            captureMode = CaptureMode.None;

        if (captureMode == CaptureMode.None)
        {
            Captured?.ReleaseMouseCapture();
            UIElement.ForceReleaseMouseCapture();
            s_capturedByStaticApi = null;
            s_captureMode = CaptureMode.None;
            s_primaryDevice.Captured = null;
            return true;
        }

        if (!element!.IsEnabled)
            return false;

        IInputElement? previous = Captured;
        if (ReferenceEquals(previous, element))
        {
            s_capturedByStaticApi = element;
            s_captureMode = captureMode;
            s_primaryDevice.Captured = element as UIElement;
            return true;
        }

        previous?.ReleaseMouseCapture();

        if (!element.CaptureMouse())
            return false;

        s_capturedByStaticApi = element;
        s_captureMode = captureMode;
        s_primaryDevice.Captured = element as UIElement;
        return true;
    }

    public static Point GetPosition(IInputElement? relativeTo) => ConvertPosition(Position, relativeTo);

    public static int GetIntermediatePoints(IInputElement? relativeTo, Point[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length == 0)
            return 0;

        Point[] snapshot;
        lock (s_stateGate)
            snapshot = s_intermediatePoints.ToArray();

        int count = Math.Min(points.Length, snapshot.Length);
        int sourceStart = snapshot.Length - count;
        for (int index = 0; index < count; index++)
            points[index] = ConvertPosition(snapshot[sourceStart + index], relativeTo);
        return count;
    }

    public static bool SetCursor(Cursor? cursor)
    {
        s_currentCursor = s_overrideCursor ?? cursor ?? Cursors.None;
        return true;
    }

    public static void Synchronize()
    {
        s_buttons = new MouseButtonStates
        {
            Left = GetNativeButtonState(0x01),
            Right = GetNativeButtonState(0x02),
            Middle = GetNativeButtonState(0x04),
            XButton1 = GetNativeButtonState(0x05),
            XButton2 = GetNativeButtonState(0x06),
        };
        UpdateCursor();
    }

    public static void UpdateCursor()
    {
        Cursor? cursor = s_overrideCursor;
        if (cursor is null && DirectlyOver is FrameworkElement element && element.IsEnabled)
            cursor = FrameworkElement.ResolveEffectiveCursor(element);

        SetCursor(cursor ?? Cursors.Arrow);
    }

    internal static UIElement? GetMouseTarget(UIElement? hitTestResult)
    {
        if (Captured is not UIElement captured)
            return hitTestResult;

        if (CurrentCaptureMode == CaptureMode.SubTree && IsWithinVisualSubtree(hitTestResult, captured))
            return hitTestResult;

        return captured;
    }

    internal static void RaiseOutsideCapturedElementEvent(
        bool isButtonDown,
        UIElement? hitElement,
        Point position,
        MouseButton button,
        MouseButtonState buttonState,
        int clickCount,
        MouseButtonStates buttons,
        ModifierKeys modifiers,
        int timestamp)
    {
        if (Captured is not UIElement captured || IsWithinVisualSubtree(hitElement, captured))
            return;

        var args = new MouseButtonEventArgs(
            isButtonDown
                ? PreviewMouseDownOutsideCapturedElementEvent
                : PreviewMouseUpOutsideCapturedElementEvent,
            position,
            button,
            buttonState,
            clickCount,
            buttons.Left,
            buttons.Middle,
            buttons.Right,
            buttons.XButton1,
            buttons.XButton2,
            modifiers,
            timestamp);
        captured.RaiseEvent(args);
    }

    internal static void OnMouseLeaveWindow()
    {
        UpdateState(Position, null, s_buttons);
    }

    internal static void ForceReleaseCapture()
    {
        Capture(null, CaptureMode.None);
    }

    private static bool IsWithinVisualSubtree(UIElement? element, UIElement ancestor)
    {
        for (Visual? current = element; current is not null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static Point ConvertPosition(Point position, IInputElement? relativeTo)
    {
        if (relativeTo is not UIElement uiElement)
            return position;

        var args = new MouseEventArgs(
            MouseMoveEvent,
            position,
            s_buttons.Left,
            s_buttons.Middle,
            s_buttons.Right,
            s_buttons.XButton1,
            s_buttons.XButton2,
            ModifierKeys.None,
            Environment.TickCount);
        return args.GetPosition(uiElement);
    }

    private static MouseButtonState GetNativeButtonState(int virtualKey) =>
        (Keyboard.NativeMethods.GetKeyState(virtualKey) & unchecked((short)0x8000)) != 0
            ? MouseButtonState.Pressed
            : MouseButtonState.Released;

    #endregion

    private sealed class SystemMouseDevice : MouseDevice
    {
        public override IInputElement? Target => Captured ?? DirectlyOver;

        public override PresentationSource? ActiveSource => null;

        protected override MouseButtonState GetButtonState(MouseButton mouseButton) => mouseButton switch
        {
            MouseButton.Left => Mouse.LeftButton,
            MouseButton.Middle => Mouse.MiddleButton,
            MouseButton.Right => Mouse.RightButton,
            MouseButton.XButton1 => Mouse.XButton1,
            MouseButton.XButton2 => Mouse.XButton2,
            _ => throw new InvalidEnumArgumentException(nameof(mouseButton), (int)mouseButton, typeof(MouseButton)),
        };

        protected override Point GetPositionCore(IInputElement? relativeTo) => Mouse.GetPosition(relativeTo);
    }
}
