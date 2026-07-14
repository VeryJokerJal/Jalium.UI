using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using System.Text;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using FormatConvertedBitmap = Jalium.UI.Media.Imaging.FormatConvertedBitmap;
using RenderTargetBitmap = Jalium.UI.Media.Imaging.RenderTargetBitmap;

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
        if (OperatingSystem.IsWindows())
        {
            OleDropTarget.Initialize();
            DragDrop.DoDragDropOverride = DoDragDropManaged;
            DragDrop.DoShellDragDropOverride = OleDragSource.DoDragDrop;
        }
        else if (OperatingSystem.IsLinux())
        {
            DragDrop.DoDragDropOverride = DoLinuxDragDrop;
            DragDrop.DoShellDragDropOverride = DoLinuxDragDrop;
        }
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
            if (current is IWindowHost windowHost)
            {
                hwnd = windowHost.Handle;
                dpiScale = windowHost.DpiScale;
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
        bool dragVisualAdded = false;

        bool showVisual = DragDrop.GetShowDragVisual(sourceElement);
        if (showVisual && window != null && sourceElement is FrameworkElement sourceFE)
        {
            var sourceBounds = sourceElement.GetScreenBounds();
            var clickPos = GetClientMousePosition(hwnd, dpiScale);

            // A caller-supplied drag image (from the DoDragDrop overload or the
            // DragImage attached property) wins over the automatic element clone.
            object? customImage = DragDrop.PendingDragImage ?? sourceElement.GetValue(DragDrop.DragImageProperty);
            FrameworkElement? customVisual = CreateDragVisualFromImage(customImage);

            if (customVisual != null)
            {
                dragVisual = customVisual;

                // Hotspot: explicit offset if provided, otherwise the image's
                // top-left corner tracks the pointer.
                Point offset = DragDrop.HasPendingDragImageOffset
                    ? DragDrop.PendingDragImageOffset
                    : DragDrop.GetDragImageOffset(sourceElement);
                dragOffsetX = offset.X;
                dragOffsetY = offset.Y;
            }
            else
            {
                dragVisual = CreateDragVisual(sourceFE);
                dragOffsetX = clickPos.X - sourceBounds.X;
                dragOffsetY = clickPos.Y - sourceBounds.Y;
            }

            Canvas.SetLeft(dragVisual, clickPos.X - dragOffsetX);
            Canvas.SetTop(dragVisual, clickPos.Y - dragOffsetY);
            window.OverlayLayer.Children.Add(dragVisual);
            dragVisualAdded = true;
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
            if (dragVisualAdded && dragVisual != null)
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
    /// Builds a drag visual from a caller-supplied drag image. Accepts an
    /// <see cref="ImageSource"/> (wrapped in a lightweight <see cref="Image"/>) or
    /// an unparented <see cref="FrameworkElement"/> used directly. Returns
    /// <see langword="null"/> when no usable image was supplied so the caller can
    /// fall back to cloning the source element.
    /// </summary>
    private static FrameworkElement? CreateDragVisualFromImage(object? image)
    {
        switch (image)
        {
            case ImageSource source:
                double w = source.Width > 0 ? source.Width : 32;
                double h = source.Height > 0 ? source.Height : 32;
                return new Image
                {
                    Source = source,
                    Width = w,
                    Height = h,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                    Opacity = 0.85,
                };

            // A live element the caller built for the drag. Only reuse it when it
            // is not already parented, to avoid ripping it out of another tree.
            case FrameworkElement element when element.VisualParent == null:
                element.IsHitTestVisible = false;
                return element;

            default:
                return null;
        }
    }

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

    #region Part

    private static DragDropEffects DoLinuxDragDrop(
        DependencyObject dragSource,
        IDataObject data,
        DragDropEffects allowedEffects)
    {
        Window? window = FindOwningWindow(dragSource);
        if (window == null)
            return DragDropEffects.None;

        using var payload = LinuxDragPayload.Create(data);
        if (payload.Items.Length == 0)
            return DragDropEffects.None;

        using LinuxDragImagePayload? dragImage = LinuxDragImagePayload.TryCreate(dragSource);

        uint effect = window.BeginPlatformDrag(
            payload.Items,
            (uint)allowedEffects & 0x07,
            nativeEffect => RaiseLinuxGiveFeedback(
                dragSource, (DragDropEffects)(nativeEffect & 0x07)),
            (nativeKeyStates, escapePressed) => RaiseLinuxQueryContinueDrag(
                dragSource, (DragDropKeyStates)nativeKeyStates, escapePressed),
            dragImage?.Image);
        return (DragDropEffects)(effect & 0x07);
    }

    internal static void RaiseLinuxGiveFeedback(
        DependencyObject dragSource,
        DragDropEffects effects)
    {
        var preview = new GiveFeedbackEventArgs(DragDrop.PreviewGiveFeedbackEvent, effects);
        RaiseSourceEvent(dragSource, preview);
        if (preview.Handled)
            return;

        RaiseSourceEvent(
            dragSource,
            new GiveFeedbackEventArgs(DragDrop.GiveFeedbackEvent, effects)
            {
                UseDefaultCursors = preview.UseDefaultCursors,
            });
    }

    internal static PlatformDragContinueAction RaiseLinuxQueryContinueDrag(
        DependencyObject dragSource,
        DragDropKeyStates keyStates,
        bool escapePressed)
    {
        var preview = new QueryContinueDragEventArgs(
            DragDrop.PreviewQueryContinueDragEvent, keyStates, escapePressed);
        RaiseSourceEvent(dragSource, preview);

        DragAction action = preview.Action;
        bool handled = preview.Handled;
        if (!preview.Handled)
        {
            var bubble = new QueryContinueDragEventArgs(
                DragDrop.QueryContinueDragEvent, keyStates, escapePressed)
            {
                Action = action,
            };
            RaiseSourceEvent(dragSource, bubble);
            action = bubble.Action;
            handled = bubble.Handled;
        }

        // Match the desktop drag-source contract: Escape cancels by default,
        // and releasing every pointer button requests a drop.  Handlers can
        // override either default by setting Action and marking the routed
        // event handled.
        if (!handled && action == DragAction.Continue)
        {
            const DragDropKeyStates pointerButtons =
                DragDropKeyStates.LeftMouseButton |
                DragDropKeyStates.RightMouseButton |
                DragDropKeyStates.MiddleMouseButton;
            if (escapePressed)
                action = DragAction.Cancel;
            else if ((keyStates & pointerButtons) == 0)
                action = DragAction.Drop;
        }

        return action switch
        {
            DragAction.Drop => PlatformDragContinueAction.Drop,
            DragAction.Cancel => PlatformDragContinueAction.Cancel,
            _ => PlatformDragContinueAction.Continue,
        };
    }

    private static void RaiseSourceEvent(DependencyObject source, RoutedEventArgs args)
    {
        switch (source)
        {
            case UIElement element:
                element.RaiseEvent(args);
                break;
            case ContentElement content:
                content.RaiseEvent(args);
                break;
        }
    }

    private static Window? FindOwningWindow(DependencyObject source)
    {
        if (source is Window sourceWindow)
            return sourceWindow;
        if (source is not Visual visual)
            return null;

        Visual? current = visual;
        while (current != null)
        {
            if (current is Window window)
                return window;
            current = current.VisualParent;
        }
        return null;
    }

    private sealed class LinuxDragPayload : IDisposable
    {
        private readonly List<nint> _allocations = [];
        public NativeDragDataItem[] Items { get; private set; } = [];

        public static LinuxDragPayload Create(IDataObject data)
        {
            var payload = new LinuxDragPayload();
            var representations = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] files && files.Length != 0)
            {
                representations["text/uri-list"] = Encoding.UTF8.GetBytes(BuildUriList(files));
            }

            string? text = data.GetData(DataFormats.UnicodeText) as string ??
                           data.GetData(DataFormats.Text) as string ??
                           data.GetData(DataFormats.StringFormat) as string;
            if (text != null)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(text);
                representations["text/plain;charset=utf-8"] = utf8;
                representations["text/plain"] = utf8;
                representations["UTF8_STRING"] = utf8;
            }

            foreach (string format in data.GetFormats(false))
            {
                if (!format.Contains('/', StringComparison.Ordinal) || representations.ContainsKey(format))
                    continue;
                object? value = data.GetData(format, false);
                if (value is byte[] raw)
                    representations[format] = raw;
                else if (value is string stringValue)
                    representations[format] = Encoding.UTF8.GetBytes(stringValue);
            }

            var items = new List<NativeDragDataItem>(representations.Count);
            foreach ((string mime, byte[] representation) in representations)
            {
                // MIME names are ASCII by specification; HGlobal keeps the
                // allocator paired with the shared disposal path below.
                nint mimePointer = Marshal.StringToHGlobalAnsi(mime);
                payload._allocations.Add(mimePointer);

                int allocationSize = Math.Max(1, representation.Length);
                nint dataPointer = Marshal.AllocHGlobal(allocationSize);
                payload._allocations.Add(dataPointer);
                if (representation.Length != 0)
                    Marshal.Copy(representation, 0, dataPointer, representation.Length);

                items.Add(new NativeDragDataItem
                {
                    MimeType = mimePointer,
                    Data = dataPointer,
                    DataSize = (uint)representation.Length,
                });
            }
            payload.Items = [.. items];
            return payload;
        }

        private static string BuildUriList(IEnumerable<string> files)
        {
            var builder = new StringBuilder();
            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;
                if (Uri.TryCreate(file, UriKind.Absolute, out Uri? existing) && existing.IsFile)
                    builder.Append(existing.AbsoluteUri);
                else
                    builder.Append(new Uri(Path.GetFullPath(file)).AbsoluteUri);
                builder.Append("\r\n");
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            foreach (nint allocation in _allocations)
                Marshal.FreeHGlobal(allocation);
            _allocations.Clear();
            Items = [];
        }
    }

    private sealed class LinuxDragImagePayload : IDisposable
    {
        private nint _pixels;

        private LinuxDragImagePayload(
            byte[] pixels, int width, int height, int stride, Point hotspot)
        {
            _pixels = Marshal.AllocHGlobal(pixels.Length);
            Marshal.Copy(pixels, 0, _pixels, pixels.Length);
            Image = new NativeDragImage
            {
                BgraPixels = _pixels,
                Width = (uint)width,
                Height = (uint)height,
                Stride = (uint)stride,
                HotspotX = Math.Clamp((int)Math.Round(hotspot.X), 0, width - 1),
                HotspotY = Math.Clamp((int)Math.Round(hotspot.Y), 0, height - 1),
            };
        }

        internal NativeDragImage Image { get; }

        internal static LinuxDragImagePayload? TryCreate(DependencyObject dragSource)
        {
            if (!DragDrop.GetShowDragVisual(dragSource))
                return null;

            try
            {
                object? customImage = DragDrop.PendingDragImage ??
                                      dragSource.GetValue(DragDrop.DragImageProperty);
                BitmapSource? bitmap = customImage as BitmapSource;
                FrameworkElement? visual = customImage as FrameworkElement;
                visual ??= dragSource as FrameworkElement;

                if (bitmap == null && visual != null)
                {
                    double logicalWidth = visual.ActualWidth > 0
                        ? visual.ActualWidth : visual.DesiredSize.Width;
                    double logicalHeight = visual.ActualHeight > 0
                        ? visual.ActualHeight : visual.DesiredSize.Height;
                    if ((!double.IsFinite(logicalWidth) || logicalWidth <= 0 ||
                         !double.IsFinite(logicalHeight) || logicalHeight <= 0) &&
                        visual.VisualParent == null)
                    {
                        visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        logicalWidth = visual.DesiredSize.Width;
                        logicalHeight = visual.DesiredSize.Height;
                        if (logicalWidth > 0 && logicalHeight > 0)
                            visual.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
                    }

                    int renderWidth = Math.Clamp(
                        (int)Math.Ceiling(Math.Max(1, logicalWidth)), 1, 512);
                    int renderHeight = Math.Clamp(
                        (int)Math.Ceiling(Math.Max(1, logicalHeight)), 1, 512);
                    var rendered = new RenderTargetBitmap(
                        renderWidth, renderHeight, 96, 96, PixelFormat.Bgra32);
                    rendered.Clear(Color.FromArgb(0, 0, 0, 0));
                    rendered.Render(visual);
                    bitmap = rendered;
                }

                if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 ||
                    bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096)
                {
                    return null;
                }

                BitmapSource normalized = bitmap.Format == PixelFormat.Bgra32
                    ? bitmap
                    : new FormatConvertedBitmap(bitmap, PixelFormat.Bgra32, null, 0);
                int width = normalized.PixelWidth;
                int height = normalized.PixelHeight;
                int stride = checked(width * 4);
                byte[] pixels = new byte[checked(stride * height)];
                normalized.CopyPixels(pixels, stride, 0);

                bool hasExplicitOffset = DragDrop.HasPendingDragImageOffset;
                Point hotspot = hasExplicitOffset
                    ? DragDrop.PendingDragImageOffset
                    : DragDrop.GetDragImageOffset(dragSource);
                if (customImage == null && !hasExplicitOffset &&
                    hotspot.X == 0 && hotspot.Y == 0)
                {
                    hotspot = new Point(width / 2.0, height / 2.0);
                }
                return new LinuxDragImagePayload(
                    pixels, width, height, stride, hotspot);
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or
                    NotSupportedException or OverflowException or OutOfMemoryException)
            {
                // A drag remains functional when a custom visual cannot be
                // rasterized; Wayland still receives a transparent icon surface.
                return null;
            }
        }

        public void Dispose()
        {
            nint pixels = Interlocked.Exchange(ref _pixels, nint.Zero);
            if (pixels != nint.Zero)
                Marshal.FreeHGlobal(pixels);
        }
    }

    #endregion
}

/// <summary>
/// Linux drag source and drop-target bridge. The X11 XDND and Wayland
/// data-device implementations both produce the same native event stream, so
/// hit testing and routed-event semantics stay identical across compositors.
/// </summary>
internal static class LinuxDropTarget
{
    private sealed class DropState
    {
        public ulong SessionId;
        public UIElement? CurrentTarget;
        public DataObject CurrentData = new();
        public DragDropEffects AllowedEffects;
        public Point Position;
    }

    private static readonly Dictionary<Window, DropState> s_states = [];

    internal static void RevokeWindow(Window window)
    {
        if (s_states.Remove(window, out DropState? state))
            RaiseLeave(state);
    }

    internal static void ProcessEvent(Window window, PlatformEvent evt)
    {
        if (!OperatingSystem.IsLinux())
            return;

        switch (evt.Type)
        {
            case PlatformEventType.DragEnter:
                BeginOrReplace(window, evt);
                break;

            case PlatformEventType.DragOver:
                ProcessOver(window, evt);
                break;

            case PlatformEventType.Drop:
                ProcessDrop(window, evt);
                break;

            case PlatformEventType.DragLeave:
                End(window, evt, notifyNative: true);
                break;

            case PlatformEventType.DragFinished:
                // Source completion is reported to the synchronous
                // jalium_drag_begin call. It is intentionally not routed as a
                // target event, but it must tear down a possible self-drag.
                End(window, evt, notifyNative: false);
                break;
        }
    }

    private static void BeginOrReplace(Window window, PlatformEvent evt)
    {
        if (s_states.Remove(window, out DropState? previous))
            RaiseLeave(previous);

        var state = new DropState
        {
            SessionId = evt.DragSessionId,
            CurrentData = CreateDataObject(evt.DragMimeTypes, evt.DragDataMimeType, evt.DragData),
            AllowedEffects = MaskEffects(evt.DragAllowedEffects),
            Position = ToDip(window, evt),
        };
        s_states[window] = state;

        state.CurrentTarget = HitDropTarget(window, state.Position);
        DragDropEffects selected = DragDropEffects.None;
        if (state.CurrentTarget != null)
        {
            selected = RaiseDragEvent(
                state.CurrentTarget,
                DragDrop.PreviewDragEnterEvent,
                DragDrop.DragEnterEvent,
                state.CurrentData,
                MapKeyStates(evt.DragKeyStates),
                state.AllowedEffects,
                state.Position);
        }

        window.SetPlatformDragEffect(state.SessionId,
            (uint)SelectSingleEffect(selected, state.AllowedEffects, evt.DragKeyStates));
    }

    private static void ProcessOver(Window window, PlatformEvent evt)
    {
        if (!s_states.TryGetValue(window, out DropState? state) || state.SessionId != evt.DragSessionId)
        {
            BeginOrReplace(window, evt);
            if (!s_states.TryGetValue(window, out state))
                return;
        }

        state.Position = ToDip(window, evt);
        state.AllowedEffects = MaskEffects(evt.DragAllowedEffects);
        UIElement? target = HitDropTarget(window, state.Position);

        if (target != state.CurrentTarget)
        {
            if (state.CurrentTarget != null)
            {
                _ = RaiseDragEvent(
                    state.CurrentTarget,
                    DragDrop.PreviewDragLeaveEvent,
                    DragDrop.DragLeaveEvent,
                    state.CurrentData,
                    MapKeyStates(evt.DragKeyStates),
                    state.AllowedEffects,
                    state.Position);
            }

            state.CurrentTarget = target;
            if (target != null)
            {
                _ = RaiseDragEvent(
                    target,
                    DragDrop.PreviewDragEnterEvent,
                    DragDrop.DragEnterEvent,
                    state.CurrentData,
                    MapKeyStates(evt.DragKeyStates),
                    state.AllowedEffects,
                    state.Position);
            }
        }

        DragDropEffects selected = DragDropEffects.None;
        if (state.CurrentTarget != null)
        {
            selected = RaiseDragEvent(
                state.CurrentTarget,
                DragDrop.PreviewDragOverEvent,
                DragDrop.DragOverEvent,
                state.CurrentData,
                MapKeyStates(evt.DragKeyStates),
                state.AllowedEffects,
                state.Position);
        }

        window.SetPlatformDragEffect(state.SessionId,
            (uint)SelectSingleEffect(selected, state.AllowedEffects, evt.DragKeyStates));
    }

    private static void ProcessDrop(Window window, PlatformEvent evt)
    {
        if (!s_states.TryGetValue(window, out DropState? state) || state.SessionId != evt.DragSessionId)
        {
            BeginOrReplace(window, evt);
            if (!s_states.TryGetValue(window, out state))
                return;
        }

        state.Position = ToDip(window, evt);
        state.AllowedEffects = MaskEffects(evt.DragAllowedEffects);
        state.CurrentData = CreateDataObject(evt.DragMimeTypes, evt.DragDataMimeType, evt.DragData);
        UIElement? target = HitDropTarget(window, state.Position) ?? state.CurrentTarget;
        DragDropEffects selected = DragDropEffects.None;
        if (target != null)
        {
            selected = RaiseDragEvent(
                target,
                DragDrop.PreviewDropEvent,
                DragDrop.DropEvent,
                state.CurrentData,
                MapKeyStates(evt.DragKeyStates),
                state.AllowedEffects,
                state.Position);
        }

        window.SetPlatformDragEffect(state.SessionId,
            (uint)SelectSingleEffect(selected, state.AllowedEffects, evt.DragKeyStates));
        state.CurrentTarget = null;
        s_states.Remove(window);
    }

    private static void End(Window window, PlatformEvent evt, bool notifyNative)
    {
        if (s_states.Remove(window, out DropState? state))
            RaiseLeave(state);
        if (notifyNative)
            window.SetPlatformDragEffect(evt.DragSessionId, 0);
    }

    private static void RaiseLeave(DropState state)
    {
        if (state.CurrentTarget == null)
            return;
        _ = RaiseDragEvent(
            state.CurrentTarget,
            DragDrop.PreviewDragLeaveEvent,
            DragDrop.DragLeaveEvent,
            state.CurrentData,
            DragDropKeyStates.None,
            state.AllowedEffects,
            state.Position);
        state.CurrentTarget = null;
    }

    private static Point ToDip(Window window, PlatformEvent evt)
    {
        double scale = window.DpiScale > 0 ? window.DpiScale : 1;
        return new Point(evt.MouseX / scale, evt.MouseY / scale);
    }

    private static UIElement? HitDropTarget(Window window, Point position)
    {
        UIElement? hit = window.HitTest(position)?.VisualHit as UIElement;
        return DragDropPlatform.FindDropTargetElement(hit);
    }

    private static DragDropEffects RaiseDragEvent(
        UIElement target,
        RoutedEvent previewEvent,
        RoutedEvent bubbleEvent,
        DataObject data,
        DragDropKeyStates keys,
        DragDropEffects allowed,
        Point position)
    {
        var preview = new DragEventArgs(previewEvent, data, keys, allowed, position);
        target.RaiseEvent(preview);
        if (preview.Handled)
            return preview.Effects;

        var bubble = new DragEventArgs(bubbleEvent, data, keys, allowed, position);
        target.RaiseEvent(bubble);
        return bubble.Effects;
    }

    internal static DataObject CreateDataObject(
        string[]? mimeTypes,
        string? dataMimeType,
        byte[]? bytes)
    {
        var result = new DataObject();
        foreach (string mime in mimeTypes ?? [])
        {
            if (IsUriList(mime))
                result.SetData(DataFormats.FileDrop, Array.Empty<string>());
            else if (IsText(mime))
            {
                result.SetData(DataFormats.Text, string.Empty);
                result.SetData(DataFormats.UnicodeText, string.Empty);
            }
            result.SetData(mime, Array.Empty<byte>());
        }

        if (bytes == null || string.IsNullOrEmpty(dataMimeType))
            return result;

        result.SetData(dataMimeType, bytes);
        string text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        if (IsUriList(dataMimeType))
        {
            string[] files = ParseUriList(text);
            result.SetData(DataFormats.FileDrop, files);
        }
        else if (IsText(dataMimeType))
        {
            result.SetData(DataFormats.Text, text);
            result.SetData(DataFormats.UnicodeText, text);
            result.SetData(DataFormats.StringFormat, text);
        }
        return result;
    }

    internal static string[] ParseUriList(string uriList)
    {
        var paths = new List<string>();
        foreach (string rawLine in uriList.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                paths.Add(uri.LocalPath);
        }
        return [.. paths];
    }

    private static bool IsUriList(string mime) =>
        mime.Equals("text/uri-list", StringComparison.OrdinalIgnoreCase);

    private static bool IsText(string mime) =>
        mime.Equals("UTF8_STRING", StringComparison.OrdinalIgnoreCase) ||
        mime.Equals("STRING", StringComparison.OrdinalIgnoreCase) ||
        mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase);

    private static DragDropKeyStates MapKeyStates(uint states) =>
        (DragDropKeyStates)(states & 0x3f);

    private static DragDropEffects MaskEffects(uint effects) =>
        (DragDropEffects)(effects & 0x07);

    internal static DragDropEffects SelectSingleEffect(
        DragDropEffects requested,
        DragDropEffects allowed,
        uint keyStates)
    {
        DragDropEffects available = requested & allowed &
            (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        if ((keyStates & (uint)DragDropKeyStates.ControlKey) != 0 && available.HasFlag(DragDropEffects.Copy))
            return DragDropEffects.Copy;
        if ((keyStates & (uint)DragDropKeyStates.ShiftKey) != 0 && available.HasFlag(DragDropEffects.Move))
            return DragDropEffects.Move;
        if ((keyStates & (uint)DragDropKeyStates.AltKey) != 0 && available.HasFlag(DragDropEffects.Link))
            return DragDropEffects.Link;
        if (available.HasFlag(DragDropEffects.Copy)) return DragDropEffects.Copy;
        if (available.HasFlag(DragDropEffects.Move)) return DragDropEffects.Move;
        if (available.HasFlag(DragDropEffects.Link)) return DragDropEffects.Link;
        return DragDropEffects.None;
    }
}
