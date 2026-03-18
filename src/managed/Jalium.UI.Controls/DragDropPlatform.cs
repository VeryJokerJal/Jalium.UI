using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Platform-specific (Windows) drag-and-drop implementation.
/// Registers a managed DoDragDrop handler that runs a nested Win32 message loop,
/// performing hit testing and firing DragEnter/DragOver/DragLeave/Drop events.
/// </summary>
internal static partial class DragDropPlatform
{
    private static bool _initialized;

    /// <summary>
    /// Ensures the platform DoDragDrop handler is registered with <see cref="DragDrop"/>.
    /// Called once during application startup (e.g. from Window initialization).
    /// </summary>
    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        OleDropTarget.Initialize();
        DragDrop.DoDragDropOverride = DoDragDropManaged;
    }

    /// <summary>
    /// Managed drag-and-drop state machine.
    /// Runs a Win32 nested message loop that tracks mouse movement, performs hit testing
    /// to find drop targets, and fires DragEnter/DragOver/DragLeave/Drop events.
    /// </summary>
    private static DragDropEffects DoDragDropManaged(DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects)
    {
        var sourceElement = dragSource as UIElement;
        if (sourceElement == null)
            return DragDropEffects.None;

        nint hwnd = nint.Zero;
        double dpiScale = 1.0;
        FrameworkElement? rootVisual = null;

        Visual? current = sourceElement;
        while (current != null)
        {
            if (current is IWindowHost)
            {
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

        sourceElement.CaptureMouse();

        // Create semi-transparent drag visual that follows the cursor
        var window = rootVisual as Window;
        FrameworkElement? dragVisual = null;
        double dragOffsetX = 0, dragOffsetY = 0;

        bool showVisual = DragDrop.GetShowDragVisual(sourceElement);
        if (showVisual && window != null && sourceElement is FrameworkElement sourceFE)
        {
            var sourceBounds = sourceElement.GetScreenBounds();
            var clickPos = GetClientMousePosition(hwnd, dpiScale);
            dragOffsetX = clickPos.X - sourceBounds.X;
            dragOffsetY = clickPos.Y - sourceBounds.Y;

            dragVisual = CreateDragVisual(sourceFE);
            Canvas.SetLeft(dragVisual, clickPos.X - dragOffsetX);
            Canvas.SetTop(dragVisual, clickPos.Y - dragOffsetY);
            window.OverlayLayer.Children.Add(dragVisual);
            dragVisual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        var finalEffects = DragDropEffects.None;
        UIElement? currentTarget = null;
        bool cancelled = false;

        try
        {
            while (true)
            {
                if (PeekMessageW(out DragDropMSG msg, nint.Zero, 0, 0, PM_REMOVE))
                {
                    _ = TranslateMessage(ref msg);
                    _ = DispatchMessageW(ref msg);
                }

                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
                {
                    cancelled = true;
                    break;
                }

                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                if (!leftDown)
                {
                    if (currentTarget != null && finalEffects != DragDropEffects.None)
                    {
                        var dropPos = GetClientMousePosition(hwnd, dpiScale);
                        var keyStates = GetCurrentDragKeyStates();

                        var previewDropArgs = new DragEventArgs(DragDrop.PreviewDropEvent, data, keyStates, allowedEffects, dropPos);
                        currentTarget.RaiseEvent(previewDropArgs);

                        if (!previewDropArgs.Handled)
                        {
                            var dropArgs = new DragEventArgs(DragDrop.DropEvent, data, keyStates, allowedEffects, dropPos);
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

                var position = GetClientMousePosition(hwnd, dpiScale);

                // Update drag visual position
                if (dragVisual != null)
                {
                    Canvas.SetLeft(dragVisual, position.X - dragOffsetX);
                    Canvas.SetTop(dragVisual, position.Y - dragOffsetY);
                }

                var hitResult = rootVisual.HitTest(position);
                UIElement? hitElement = hitResult?.VisualHit as UIElement;
                UIElement? dropTarget = FindDropTargetElement(hitElement);

                if (dropTarget != currentTarget)
                {
                    var dragKeyStates = GetCurrentDragKeyStates();

                    if (currentTarget != null)
                    {
                        var leaveArgs = new DragEventArgs(DragDrop.PreviewDragLeaveEvent, data, dragKeyStates, allowedEffects, position);
                        currentTarget.RaiseEvent(leaveArgs);
                        if (!leaveArgs.Handled)
                        {
                            var bubbleLeaveArgs = new DragEventArgs(DragDrop.DragLeaveEvent, data, dragKeyStates, allowedEffects, position);
                            currentTarget.RaiseEvent(bubbleLeaveArgs);
                        }
                    }

                    currentTarget = dropTarget;

                    if (currentTarget != null)
                    {
                        var enterArgs = new DragEventArgs(DragDrop.PreviewDragEnterEvent, data, dragKeyStates, allowedEffects, position);
                        currentTarget.RaiseEvent(enterArgs);
                        if (!enterArgs.Handled)
                        {
                            var bubbleEnterArgs = new DragEventArgs(DragDrop.DragEnterEvent, data, dragKeyStates, allowedEffects, position);
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
                    var dragKeyStates = GetCurrentDragKeyStates();

                    var previewOverArgs = new DragEventArgs(DragDrop.PreviewDragOverEvent, data, dragKeyStates, allowedEffects, position);
                    currentTarget.RaiseEvent(previewOverArgs);
                    if (!previewOverArgs.Handled)
                    {
                        var overArgs = new DragEventArgs(DragDrop.DragOverEvent, data, dragKeyStates, allowedEffects, position);
                        currentTarget.RaiseEvent(overArgs);
                        finalEffects = overArgs.Effects;
                    }
                    else
                    {
                        finalEffects = previewOverArgs.Effects;
                    }
                }

                var feedbackArgs = new GiveFeedbackEventArgs(DragDrop.GiveFeedbackEvent, finalEffects);
                sourceElement.RaiseEvent(feedbackArgs);

                var escapePressed = (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
                var queryContinueArgs = new QueryContinueDragEventArgs(DragDrop.QueryContinueDragEvent, GetCurrentDragKeyStates(), escapePressed);
                sourceElement.RaiseEvent(queryContinueArgs);

                if (queryContinueArgs.Action == DragAction.Cancel)
                {
                    cancelled = true;
                    break;
                }
                else if (queryContinueArgs.Action == DragAction.Drop)
                {
                    if (currentTarget != null && finalEffects != DragDropEffects.None)
                    {
                        var dropPos = GetClientMousePosition(hwnd, dpiScale);
                        var keyStates = GetCurrentDragKeyStates();

                        var previewDropArgs = new DragEventArgs(DragDrop.PreviewDropEvent, data, keyStates, allowedEffects, dropPos);
                        currentTarget.RaiseEvent(previewDropArgs);
                        if (!previewDropArgs.Handled)
                        {
                            var dropArgs = new DragEventArgs(DragDrop.DropEvent, data, keyStates, allowedEffects, dropPos);
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

                _ = MsgWaitForMultipleObjectsEx(0, nint.Zero, 16, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
            }
        }
        finally
        {
            // Remove drag visual
            if (dragVisual != null)
                window?.OverlayLayer.Children.Remove(dragVisual);

            if (cancelled && currentTarget != null)
            {
                var leaveArgs = new DragEventArgs(DragDrop.DragLeaveEvent, data, GetCurrentDragKeyStates(), allowedEffects, GetClientMousePosition(hwnd, dpiScale));
                currentTarget.RaiseEvent(leaveArgs);
            }

            sourceElement.ReleaseMouseCapture();
        }

        return cancelled ? DragDropEffects.None : finalEffects;
    }

    internal static UIElement? FindDropTargetElement(UIElement? element)
    {
        Visual? current = element;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                bool allowDrop = (bool)(uiElement.GetValue(DragDrop.AllowDropProperty) ?? false);
                if (allowDrop)
                    return uiElement;
            }
            current = current.VisualParent;
        }
        return null;
    }

    #region Drag Visual

    /// <summary>
    /// Creates a semi-transparent clone of the source element to follow the cursor during drag.
    /// </summary>
    private static FrameworkElement CreateDragVisual(FrameworkElement source)
    {
        var clone = CloneElement(source);
        if (clone == null)
        {
            // Fallback: translucent rectangle matching source size
            double w = source.ActualWidth > 0 ? source.ActualWidth : source.DesiredSize.Width;
            double h = source.ActualHeight > 0 ? source.ActualHeight : source.DesiredSize.Height;
            clone = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(Color.FromArgb(140, 80, 80, 80)),
                CornerRadius = new CornerRadius(4),
            };
        }

        clone.Opacity = 0.7;
        clone.IsHitTestVisible = false;
        return clone;
    }

    /// <summary>
    /// Shallow-clones the visual properties of common element types.
    /// </summary>
    private static FrameworkElement? CloneElement(FrameworkElement source)
    {
        switch (source)
        {
            case Border border:
                var cb = new Border
                {
                    Width = border.ActualWidth > 0 ? border.ActualWidth : border.Width,
                    Height = border.ActualHeight > 0 ? border.ActualHeight : border.Height,
                    Background = border.Background,
                    BorderBrush = border.BorderBrush,
                    BorderThickness = border.BorderThickness,
                    CornerRadius = border.CornerRadius,
                    Padding = border.Padding,
                };
                if (border.Child is FrameworkElement childFE)
                    cb.Child = CloneElement(childFE);
                return cb;

            case TextBlock tb:
                return new TextBlock
                {
                    Text = tb.Text,
                    Foreground = tb.Foreground,
                    FontSize = tb.FontSize,
                    FontFamily = tb.FontFamily,
                    FontWeight = tb.FontWeight,
                    TextWrapping = tb.TextWrapping,
                };

            case Controls.StackPanel sp:
                var cs = new Controls.StackPanel { Orientation = sp.Orientation };
                foreach (UIElement child in sp.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        var cc = CloneElement(fe);
                        if (cc != null) cs.Children.Add(cc);
                    }
                }
                return cs;

            default:
                return null;
        }
    }

    #endregion

    private static Point GetClientMousePosition(nint hwnd, double dpiScale)
    {
        if (!GetCursorPos(out DragDropPOINT screenPt))
            return new Point(0, 0);
        _ = ScreenToClient(hwnd, ref screenPt);
        return new Point(screenPt.X / dpiScale, screenPt.Y / dpiScale);
    }

    private static DragDropKeyStates GetCurrentDragKeyStates()
    {
        var states = DragDropKeyStates.None;
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0) states |= DragDropKeyStates.LeftMouseButton;
        if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0) states |= DragDropKeyStates.RightMouseButton;
        if ((GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0) states |= DragDropKeyStates.MiddleMouseButton;
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) states |= DragDropKeyStates.ShiftKey;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) states |= DragDropKeyStates.ControlKey;
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) states |= DragDropKeyStates.AltKey;
        return states;
    }

    #region Win32 Interop

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const uint PM_REMOVE = 0x0001;
    private const uint QS_ALLINPUT = 0x04FF;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct DragDropPOINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragDropMSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public DragDropPOINT pt; }

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
    private static extern uint MsgWaitForMultipleObjectsEx(uint nCount, nint pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    #endregion
}
