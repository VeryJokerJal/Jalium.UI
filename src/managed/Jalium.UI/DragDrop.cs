using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace Jalium.UI;

/// <summary>
/// Provides methods for drag-and-drop operations.
/// </summary>
public static partial class DragDrop
{
    #region Dependency Properties (Attached)

    /// <summary>
    /// Identifies the AllowDrop attached property.
    /// </summary>
    public static readonly DependencyProperty AllowDropProperty =
        DependencyProperty.RegisterAttached("AllowDrop", typeof(bool), typeof(DragDrop),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsDropTarget attached property.
    /// </summary>
    public static readonly DependencyProperty IsDropTargetProperty =
        DependencyProperty.RegisterAttached("IsDropTarget", typeof(bool), typeof(DragDrop),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the AllowDrop value for an element.
    /// </summary>
    public static bool GetAllowDrop(DependencyObject obj) =>
        (bool)(obj.GetValue(AllowDropProperty) ?? false);

    /// <summary>
    /// Sets the AllowDrop value for an element.
    /// </summary>
    public static void SetAllowDrop(DependencyObject obj, bool value) =>
        obj.SetValue(AllowDropProperty, value);

    /// <summary>
    /// Gets the IsDropTarget value for an element.
    /// </summary>
    public static bool GetIsDropTarget(DependencyObject obj) =>
        (bool)(obj.GetValue(IsDropTargetProperty) ?? false);

    /// <summary>
    /// Sets the IsDropTarget value for an element.
    /// </summary>
    public static void SetIsDropTarget(DependencyObject obj, bool value) =>
        obj.SetValue(IsDropTargetProperty, value);

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the PreviewDragEnter routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDragEnterEvent =
        EventManager.RegisterRoutedEvent("PreviewDragEnter", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the DragEnter routed event.
    /// </summary>
    public static readonly RoutedEvent DragEnterEvent =
        EventManager.RegisterRoutedEvent("DragEnter", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDragOver routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDragOverEvent =
        EventManager.RegisterRoutedEvent("PreviewDragOver", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the DragOver routed event.
    /// </summary>
    public static readonly RoutedEvent DragOverEvent =
        EventManager.RegisterRoutedEvent("DragOver", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDragLeave routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDragLeaveEvent =
        EventManager.RegisterRoutedEvent("PreviewDragLeave", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the DragLeave routed event.
    /// </summary>
    public static readonly RoutedEvent DragLeaveEvent =
        EventManager.RegisterRoutedEvent("DragLeave", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the PreviewDrop routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewDropEvent =
        EventManager.RegisterRoutedEvent("PreviewDrop", RoutingStrategy.Tunnel,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the Drop routed event.
    /// </summary>
    public static readonly RoutedEvent DropEvent =
        EventManager.RegisterRoutedEvent("Drop", RoutingStrategy.Bubble,
            typeof(DragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the QueryContinueDrag routed event.
    /// </summary>
    public static readonly RoutedEvent QueryContinueDragEvent =
        EventManager.RegisterRoutedEvent("QueryContinueDrag", RoutingStrategy.Bubble,
            typeof(QueryContinueDragEventHandler), typeof(DragDrop));

    /// <summary>
    /// Identifies the GiveFeedback routed event.
    /// </summary>
    public static readonly RoutedEvent GiveFeedbackEvent =
        EventManager.RegisterRoutedEvent("GiveFeedback", RoutingStrategy.Bubble,
            typeof(GiveFeedbackEventHandler), typeof(DragDrop));

    #endregion

    #region Event Handler Delegates

    /// <summary>
    /// Adds a handler for the DragEnter event.
    /// </summary>
    public static void AddDragEnterHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(DragEnterEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the DragEnter event.
    /// </summary>
    public static void RemoveDragEnterHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(DragEnterEvent, handler);
        }
    }

    /// <summary>
    /// Adds a handler for the DragOver event.
    /// </summary>
    public static void AddDragOverHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(DragOverEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the DragOver event.
    /// </summary>
    public static void RemoveDragOverHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(DragOverEvent, handler);
        }
    }

    /// <summary>
    /// Adds a handler for the DragLeave event.
    /// </summary>
    public static void AddDragLeaveHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(DragLeaveEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the DragLeave event.
    /// </summary>
    public static void RemoveDragLeaveHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(DragLeaveEvent, handler);
        }
    }

    /// <summary>
    /// Adds a handler for the Drop event.
    /// </summary>
    public static void AddDropHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(DropEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the Drop event.
    /// </summary>
    public static void RemoveDropHandler(DependencyObject element, DragEventHandler handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(DropEvent, handler);
        }
    }

    #endregion

    #region DoDragDrop

    private static bool _isDragging;
    private static IDataObject? _currentData;
    private static DragDropEffects _currentEffects;

    /// <summary>
    /// Initiates a drag-and-drop operation.
    /// </summary>
    /// <param name="dragSource">The source element.</param>
    /// <param name="data">The data to transfer.</param>
    /// <param name="allowedEffects">The allowed effects.</param>
    /// <returns>The final effect.</returns>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects)
    {
        if (_isDragging)
            return DragDropEffects.None;

        _isDragging = true;
        _currentData = data as IDataObject ?? new DataObject(data);
        _currentEffects = allowedEffects;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return DoDragDropWindows(dragSource, _currentData, allowedEffects);
            }

            return DoDragDropManaged(dragSource, _currentData, allowedEffects);
        }
        finally
        {
            _isDragging = false;
            _currentData = null;
        }
    }

    private static DragDropEffects DoDragDropWindows(DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects)
    {
        // Windows OLE drag-drop implementation would go here
        // For now, use managed implementation
        return DoDragDropManaged(dragSource, data, allowedEffects);
    }

    /// <summary>
    /// Managed drag-and-drop state machine.
    /// Runs a Win32 nested message loop that tracks mouse movement, performs hit testing
    /// to find drop targets, and fires DragEnter/DragOver/DragLeave/Drop events.
    /// This method is synchronous and blocks until the drag operation completes
    /// (mouse button release or Escape key).
    /// </summary>
    private static DragDropEffects DoDragDropManaged(DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects)
    {
        var sourceElement = dragSource as UIElement;
        if (sourceElement == null)
            return DragDropEffects.None;

        // Walk the visual tree to find the hosting window (IWindowHost with an HWND).
        // We need the HWND for ScreenToClient conversion and to anchor the hit-test tree.
        nint hwnd = nint.Zero;
        double dpiScale = 1.0;
        FrameworkElement? rootVisual = null;

        Visual? current = sourceElement;
        while (current != null)
        {
            if (current is IWindowHost)
            {
                // Use reflection-free approach: check for Handle property via known Window pattern.
                // Window exposes 'Handle' and 'DpiScale' as public properties.
                var type = current.GetType();
                var handleProp = type.GetProperty("Handle");
                var dpiProp = type.GetProperty("DpiScale");
                if (handleProp != null)
                    hwnd = (nint)(handleProp.GetValue(current) ?? nint.Zero);
                if (dpiProp != null)
                    dpiScale = (double)(dpiProp.GetValue(current) ?? 1.0);
                rootVisual = current as FrameworkElement;
                break;
            }
            current = current.VisualParent;
        }

        if (hwnd == nint.Zero || rootVisual == null)
            return DragDropEffects.None;

        // Capture the mouse so we receive move/up messages even outside the window
        sourceElement.CaptureMouse();

        var finalEffects = DragDropEffects.None;
        UIElement? currentTarget = null;
        bool cancelled = false;

        try
        {
            // Run a nested Win32 message loop (same pattern as Window.ShowDialog).
            // Each iteration: pump one message, then poll the mouse/keyboard state to
            // drive the drag-drop state machine.
            while (true)
            {
                // Peek for a message (non-blocking check first to see if loop should end)
                if (PeekMessageW(out DragDropMSG msg, nint.Zero, 0, 0, PM_REMOVE))
                {
                    _ = TranslateMessage(ref msg);
                    _ = DispatchMessageW(ref msg);
                }

                // ------------------------------------------------------------------
                // Poll keyboard: check for Escape to cancel
                // ------------------------------------------------------------------
                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
                {
                    cancelled = true;
                    break;
                }

                // ------------------------------------------------------------------
                // Poll mouse button: if the left button is no longer pressed, drop
                // ------------------------------------------------------------------
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                if (!leftDown)
                {
                    // Mouse released – perform the drop
                    if (currentTarget != null && finalEffects != DragDropEffects.None)
                    {
                        var dropPos = GetClientMousePosition(hwnd, dpiScale);
                        var keyStates = GetCurrentDragKeyStates();

                        // PreviewDrop (tunnel)
                        var previewDropArgs = new DragEventArgs(
                            PreviewDropEvent, data, keyStates, allowedEffects, dropPos);
                        currentTarget.RaiseEvent(previewDropArgs);

                        if (!previewDropArgs.Handled)
                        {
                            // Drop (bubble)
                            var dropArgs = new DragEventArgs(
                                DropEvent, data, keyStates, allowedEffects, dropPos);
                            currentTarget.RaiseEvent(dropArgs);
                            finalEffects = dropArgs.Effects;
                        }
                        else
                        {
                            finalEffects = previewDropArgs.Effects;
                        }
                    }
                    break;
                }

                // ------------------------------------------------------------------
                // Poll mouse position and perform hit testing
                // ------------------------------------------------------------------
                var position = GetClientMousePosition(hwnd, dpiScale);
                var hitResult = rootVisual.HitTest(position);
                UIElement? hitElement = hitResult?.VisualHit as UIElement;

                // Walk up from the hit element to find the nearest ancestor with AllowDrop=true
                UIElement? dropTarget = FindDropTarget(hitElement);

                if (dropTarget != currentTarget)
                {
                    var dragKeyStates = GetCurrentDragKeyStates();

                    // Fire DragLeave on the old target
                    if (currentTarget != null)
                    {
                        var leaveArgs = new DragEventArgs(
                            PreviewDragLeaveEvent, data, dragKeyStates, allowedEffects, position);
                        currentTarget.RaiseEvent(leaveArgs);

                        if (!leaveArgs.Handled)
                        {
                            var bubbleLeaveArgs = new DragEventArgs(
                                DragLeaveEvent, data, dragKeyStates, allowedEffects, position);
                            currentTarget.RaiseEvent(bubbleLeaveArgs);
                        }
                    }

                    currentTarget = dropTarget;

                    // Fire DragEnter on the new target
                    if (currentTarget != null)
                    {
                        var enterArgs = new DragEventArgs(
                            PreviewDragEnterEvent, data, dragKeyStates, allowedEffects, position);
                        currentTarget.RaiseEvent(enterArgs);

                        if (!enterArgs.Handled)
                        {
                            var bubbleEnterArgs = new DragEventArgs(
                                DragEnterEvent, data, dragKeyStates, allowedEffects, position);
                            currentTarget.RaiseEvent(bubbleEnterArgs);
                            finalEffects = bubbleEnterArgs.Effects;
                        }
                        else
                        {
                            finalEffects = enterArgs.Effects;
                        }
                    }
                    else
                    {
                        finalEffects = DragDropEffects.None;
                    }
                }
                else if (currentTarget != null)
                {
                    // Same target – fire DragOver
                    var dragKeyStates = GetCurrentDragKeyStates();

                    var previewOverArgs = new DragEventArgs(
                        PreviewDragOverEvent, data, dragKeyStates, allowedEffects, position);
                    currentTarget.RaiseEvent(previewOverArgs);

                    if (!previewOverArgs.Handled)
                    {
                        var overArgs = new DragEventArgs(
                            DragOverEvent, data, dragKeyStates, allowedEffects, position);
                        currentTarget.RaiseEvent(overArgs);
                        finalEffects = overArgs.Effects;
                    }
                    else
                    {
                        finalEffects = previewOverArgs.Effects;
                    }
                }

                // ------------------------------------------------------------------
                // Fire GiveFeedback on the drag source so it can update the cursor
                // ------------------------------------------------------------------
                var feedbackArgs = new GiveFeedbackEventArgs(GiveFeedbackEvent, finalEffects);
                sourceElement.RaiseEvent(feedbackArgs);

                // ------------------------------------------------------------------
                // Fire QueryContinueDrag on the drag source
                // ------------------------------------------------------------------
                var escapePressed = (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
                var queryContinueArgs = new QueryContinueDragEventArgs(
                    QueryContinueDragEvent, GetCurrentDragKeyStates(), escapePressed);
                sourceElement.RaiseEvent(queryContinueArgs);

                if (queryContinueArgs.Action == DragAction.Cancel)
                {
                    cancelled = true;
                    break;
                }
                else if (queryContinueArgs.Action == DragAction.Drop)
                {
                    // Force drop (same as mouse release path)
                    if (currentTarget != null && finalEffects != DragDropEffects.None)
                    {
                        var dropPos = GetClientMousePosition(hwnd, dpiScale);
                        var keyStates = GetCurrentDragKeyStates();

                        var previewDropArgs = new DragEventArgs(
                            PreviewDropEvent, data, keyStates, allowedEffects, dropPos);
                        currentTarget.RaiseEvent(previewDropArgs);

                        if (!previewDropArgs.Handled)
                        {
                            var dropArgs = new DragEventArgs(
                                DropEvent, data, keyStates, allowedEffects, dropPos);
                            currentTarget.RaiseEvent(dropArgs);
                            finalEffects = dropArgs.Effects;
                        }
                        else
                        {
                            finalEffects = previewDropArgs.Effects;
                        }
                    }
                    break;
                }

                // Yield CPU time – avoid spinning at 100%.
                // MsgWaitForMultipleObjectsEx will wake on any input message.
                _ = MsgWaitForMultipleObjectsEx(0, nint.Zero, 16, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
            }
        }
        finally
        {
            // Fire DragLeave on the current target if we're cancelling
            if (cancelled && currentTarget != null)
            {
                var leaveArgs = new DragEventArgs(
                    DragLeaveEvent, data, GetCurrentDragKeyStates(), allowedEffects,
                    GetClientMousePosition(hwnd, dpiScale));
                currentTarget.RaiseEvent(leaveArgs);
            }

            // Release mouse capture
            sourceElement.ReleaseMouseCapture();
        }

        return cancelled ? DragDropEffects.None : finalEffects;
    }

    /// <summary>
    /// Walks up the visual tree from the hit element to find the nearest UIElement
    /// that has AllowDrop set to true.
    /// </summary>
    private static UIElement? FindDropTarget(UIElement? element)
    {
        Visual? current = element;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                bool allowDrop = (bool)(uiElement.GetValue(AllowDropProperty) ?? false);
                if (allowDrop)
                    return uiElement;
            }
            current = current.VisualParent;
        }
        return null;
    }

    /// <summary>
    /// Gets the current mouse position in client-area DIP coordinates.
    /// </summary>
    private static Point GetClientMousePosition(nint hwnd, double dpiScale)
    {
        if (!GetCursorPos(out DragDropPOINT screenPt))
            return new Point(0, 0);

        _ = ScreenToClient(hwnd, ref screenPt);
        return new Point(screenPt.X / dpiScale, screenPt.Y / dpiScale);
    }

    /// <summary>
    /// Builds the current DragDropKeyStates from the keyboard and mouse state.
    /// </summary>
    private static DragDropKeyStates GetCurrentDragKeyStates()
    {
        var states = DragDropKeyStates.None;

        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0)
            states |= DragDropKeyStates.LeftMouseButton;
        if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
            states |= DragDropKeyStates.RightMouseButton;
        if ((GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0)
            states |= DragDropKeyStates.MiddleMouseButton;
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
            states |= DragDropKeyStates.ShiftKey;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
            states |= DragDropKeyStates.ControlKey;
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
            states |= DragDropKeyStates.AltKey;

        return states;
    }

    /// <summary>
    /// Gets whether a drag operation is in progress.
    /// </summary>
    public static bool IsDragging => _isDragging;

    #endregion

    #region Win32 Interop

    // Virtual key codes
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt key

    // PeekMessage flags
    private const uint PM_REMOVE = 0x0001;

    // MsgWaitForMultipleObjectsEx flags
    private const uint QS_ALLINPUT = 0x04FF;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct DragDropPOINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragDropMSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public DragDropPOINT pt;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out DragDropPOINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hWnd, ref DragDropPOINT lpPoint);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out DragDropMSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref DragDropMSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessageW(ref DragDropMSG lpMsg);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(
        uint nCount, nint pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    #endregion
}

/// <summary>
/// Specifies the effects of a drag-and-drop operation.
/// </summary>
[Flags]
public enum DragDropEffects
{
    /// <summary>
    /// No operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// The data is copied.
    /// </summary>
    Copy = 1,

    /// <summary>
    /// The data is moved.
    /// </summary>
    Move = 2,

    /// <summary>
    /// A link is created.
    /// </summary>
    Link = 4,

    /// <summary>
    /// A scroll operation is started.
    /// </summary>
    Scroll = -2147483648,

    /// <summary>
    /// All operations are allowed.
    /// </summary>
    All = Copy | Move | Link | Scroll
}

/// <summary>
/// Provides data for drag-and-drop events.
/// </summary>
public sealed class DragEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the data associated with this drag operation.
    /// </summary>
    public IDataObject Data { get; }

    /// <summary>
    /// Gets the current key state.
    /// </summary>
    public DragDropKeyStates KeyStates { get; }

    /// <summary>
    /// Gets the allowed effects.
    /// </summary>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>
    /// Gets or sets the target drop effect.
    /// </summary>
    public DragDropEffects Effects { get; set; }

    private readonly Point _position;

    /// <summary>
    /// Creates a new DragEventArgs.
    /// </summary>
    public DragEventArgs(RoutedEvent routedEvent, IDataObject data, DragDropKeyStates keyStates, DragDropEffects allowedEffects, Point position)
        : base(routedEvent)
    {
        Data = data;
        KeyStates = keyStates;
        AllowedEffects = allowedEffects;
        Effects = allowedEffects;
        _position = position;
    }

    /// <summary>
    /// Gets the position relative to the specified element.
    /// </summary>
    public Point GetPosition(IInputElement relativeTo)
    {
        // Simplified - in full implementation, would transform coordinates
        return _position;
    }
}

/// <summary>
/// Delegate for drag events.
/// </summary>
public delegate void DragEventHandler(object sender, DragEventArgs e);

/// <summary>
/// Specifies key states during drag-and-drop.
/// </summary>
[Flags]
public enum DragDropKeyStates
{
    /// <summary>
    /// No key pressed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Left mouse button pressed.
    /// </summary>
    LeftMouseButton = 1,

    /// <summary>
    /// Right mouse button pressed.
    /// </summary>
    RightMouseButton = 2,

    /// <summary>
    /// Shift key pressed.
    /// </summary>
    ShiftKey = 4,

    /// <summary>
    /// Control key pressed.
    /// </summary>
    ControlKey = 8,

    /// <summary>
    /// Middle mouse button pressed.
    /// </summary>
    MiddleMouseButton = 16,

    /// <summary>
    /// Alt key pressed.
    /// </summary>
    AltKey = 32
}

/// <summary>
/// Provides data for the QueryContinueDrag event.
/// </summary>
public sealed class QueryContinueDragEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the current key state.
    /// </summary>
    public DragDropKeyStates KeyStates { get; }

    /// <summary>
    /// Gets whether the Escape key was pressed.
    /// </summary>
    public bool EscapePressed { get; }

    /// <summary>
    /// Gets or sets the drag action.
    /// </summary>
    public DragAction Action { get; set; }

    /// <summary>
    /// Creates a new QueryContinueDragEventArgs.
    /// </summary>
    public QueryContinueDragEventArgs(RoutedEvent routedEvent, DragDropKeyStates keyStates, bool escapePressed)
        : base(routedEvent)
    {
        KeyStates = keyStates;
        EscapePressed = escapePressed;
        Action = escapePressed ? DragAction.Cancel : DragAction.Continue;
    }
}

/// <summary>
/// Delegate for QueryContinueDrag events.
/// </summary>
public delegate void QueryContinueDragEventHandler(object sender, QueryContinueDragEventArgs e);

/// <summary>
/// Specifies the action for a drag operation.
/// </summary>
public enum DragAction
{
    /// <summary>
    /// Continue the drag operation.
    /// </summary>
    Continue,

    /// <summary>
    /// Drop the data.
    /// </summary>
    Drop,

    /// <summary>
    /// Cancel the drag operation.
    /// </summary>
    Cancel
}

/// <summary>
/// Provides data for the GiveFeedback event.
/// </summary>
public sealed class GiveFeedbackEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the current effects.
    /// </summary>
    public DragDropEffects Effects { get; }

    /// <summary>
    /// Gets or sets whether to use default cursors.
    /// </summary>
    public bool UseDefaultCursors { get; set; } = true;

    /// <summary>
    /// Creates a new GiveFeedbackEventArgs.
    /// </summary>
    public GiveFeedbackEventArgs(RoutedEvent routedEvent, DragDropEffects effects)
        : base(routedEvent)
    {
        Effects = effects;
    }
}

/// <summary>
/// Delegate for GiveFeedback events.
/// </summary>
public delegate void GiveFeedbackEventHandler(object sender, GiveFeedbackEventArgs e);

/// <summary>
/// Provides a format-independent mechanism for transferring data.
/// </summary>
public interface IDataObject
{
    /// <summary>
    /// Gets the data in the specified format.
    /// </summary>
    object? GetData(string format);

    /// <summary>
    /// Gets the data in the specified type format.
    /// </summary>
    object? GetData(Type format);

    /// <summary>
    /// Gets the data in the specified format, optionally converting it.
    /// </summary>
    object? GetData(string format, bool autoConvert);

    /// <summary>
    /// Checks if the data is available in the specified format.
    /// </summary>
    bool GetDataPresent(string format);

    /// <summary>
    /// Checks if the data is available in the specified type format.
    /// </summary>
    bool GetDataPresent(Type format);

    /// <summary>
    /// Checks if the data is available in the specified format.
    /// </summary>
    bool GetDataPresent(string format, bool autoConvert);

    /// <summary>
    /// Gets all available formats.
    /// </summary>
    string[] GetFormats();

    /// <summary>
    /// Gets all available formats.
    /// </summary>
    string[] GetFormats(bool autoConvert);

    /// <summary>
    /// Sets data in the specified format.
    /// </summary>
    void SetData(string format, object data);

    /// <summary>
    /// Sets data with the specified type as format.
    /// </summary>
    void SetData(Type format, object data);

    /// <summary>
    /// Sets data in the specified format.
    /// </summary>
    void SetData(string format, object data, bool autoConvert);

    /// <summary>
    /// Sets data with auto-detected format.
    /// </summary>
    void SetData(object data);
}

/// <summary>
/// Implements IDataObject for data transfer.
/// </summary>
public sealed class DataObject : IDataObject
{
    private readonly Dictionary<string, object> _data = new();

    /// <summary>
    /// Creates an empty DataObject.
    /// </summary>
    public DataObject()
    {
    }

    /// <summary>
    /// Creates a DataObject with the specified data.
    /// </summary>
    public DataObject(object data)
    {
        SetData(data);
    }

    /// <summary>
    /// Creates a DataObject with the specified format and data.
    /// </summary>
    public DataObject(string format, object data)
    {
        SetData(format, data);
    }

    /// <inheritdoc />
    public object? GetData(string format)
    {
        return _data.TryGetValue(format, out var data) ? data : null;
    }

    /// <inheritdoc />
    public object? GetData(Type format)
    {
        return GetData(format.FullName ?? format.Name);
    }

    /// <inheritdoc />
    public object? GetData(string format, bool autoConvert)
    {
        return GetData(format);
    }

    /// <inheritdoc />
    public bool GetDataPresent(string format)
    {
        return _data.ContainsKey(format);
    }

    /// <inheritdoc />
    public bool GetDataPresent(Type format)
    {
        return GetDataPresent(format.FullName ?? format.Name);
    }

    /// <inheritdoc />
    public bool GetDataPresent(string format, bool autoConvert)
    {
        return GetDataPresent(format);
    }

    /// <inheritdoc />
    public string[] GetFormats()
    {
        return _data.Keys.ToArray();
    }

    /// <inheritdoc />
    public string[] GetFormats(bool autoConvert)
    {
        return GetFormats();
    }

    /// <inheritdoc />
    public void SetData(string format, object data)
    {
        _data[format] = data;
    }

    /// <inheritdoc />
    public void SetData(Type format, object data)
    {
        SetData(format.FullName ?? format.Name, data);
    }

    /// <inheritdoc />
    public void SetData(string format, object data, bool autoConvert)
    {
        SetData(format, data);
    }

    /// <inheritdoc />
    public void SetData(object data)
    {
        var type = data.GetType();
        SetData(type.FullName ?? type.Name, data);

        // Add common formats
        if (data is string text)
        {
            SetData(DataFormats.Text, text);
            SetData(DataFormats.UnicodeText, text);
        }
        else if (data is string[] files)
        {
            SetData(DataFormats.FileDrop, files);
        }
    }

    /// <summary>
    /// Checks if the data contains file drop data.
    /// </summary>
    public bool ContainsFileDropList()
    {
        return GetDataPresent(DataFormats.FileDrop);
    }

    /// <summary>
    /// Gets the file drop list.
    /// </summary>
    public StringCollection GetFileDropList()
    {
        var files = GetData(DataFormats.FileDrop) as string[];
        var collection = new StringCollection();
        if (files != null)
        {
            collection.AddRange(files);
        }
        return collection;
    }

    /// <summary>
    /// Sets the file drop list.
    /// </summary>
    public void SetFileDropList(StringCollection fileDropList)
    {
        var files = new string[fileDropList.Count];
        fileDropList.CopyTo(files, 0);
        SetData(DataFormats.FileDrop, files);
    }

    /// <summary>
    /// Checks if the data contains text.
    /// </summary>
    public bool ContainsText()
    {
        return GetDataPresent(DataFormats.Text) || GetDataPresent(DataFormats.UnicodeText);
    }

    /// <summary>
    /// Gets the text data.
    /// </summary>
    public string GetText()
    {
        return GetData(DataFormats.UnicodeText) as string ?? GetData(DataFormats.Text) as string ?? string.Empty;
    }

    /// <summary>
    /// Sets the text data.
    /// </summary>
    public void SetText(string textData)
    {
        SetData(DataFormats.Text, textData);
        SetData(DataFormats.UnicodeText, textData);
    }
}

/// <summary>
/// Provides standard data format names.
/// </summary>
public static class DataFormats
{
    /// <summary>
    /// Plain text format.
    /// </summary>
    public const string Text = "Text";

    /// <summary>
    /// Unicode text format.
    /// </summary>
    public const string UnicodeText = "UnicodeText";

    /// <summary>
    /// RTF format.
    /// </summary>
    public const string Rtf = "Rich Text Format";

    /// <summary>
    /// HTML format.
    /// </summary>
    public const string Html = "HTML Format";

    /// <summary>
    /// File drop list format.
    /// </summary>
    public const string FileDrop = "FileDrop";

    /// <summary>
    /// Bitmap format.
    /// </summary>
    public const string Bitmap = "Bitmap";

    /// <summary>
    /// DIB format.
    /// </summary>
    public const string Dib = "DeviceIndependentBitmap";

    /// <summary>
    /// XAML format.
    /// </summary>
    public const string Xaml = "Xaml";

    /// <summary>
    /// XAML package format.
    /// </summary>
    public const string XamlPackage = "XamlPackage";

    /// <summary>
    /// Serializable format.
    /// </summary>
    public const string Serializable = "PersistentObject";

    /// <summary>
    /// String format.
    /// </summary>
    public const string StringFormat = "System.String";

    /// <summary>
    /// Locale format.
    /// </summary>
    public const string Locale = "Locale";

    /// <summary>
    /// OEM text format.
    /// </summary>
    public const string OemText = "OEMText";

    /// <summary>
    /// CSV format.
    /// </summary>
    public const string CommaSeparatedValue = "CSV";
}
