// WindowsFormsHost hosts a System.Windows.Forms.Control and therefore depends on the
// Windows Forms framework reference, which is only available on a Windows-targeted TFM
// (net*-windows). On the cross-platform net10.0 build of Jalium.UI.Controls the entire
// type compiles out — hosting a Win32/Windows Forms control is meaningless off Windows.
#if WINDOWS

using System.Runtime.InteropServices;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using WinFormsControl = System.Windows.Forms.Control;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Controls;

/// <summary>
/// Hosts a <see cref="System.Windows.Forms.Control"/> as an element within Jalium.UI content.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WindowsFormsHost"/> bridges the Win32-based Windows Forms world into the
/// Jalium.UI visual tree. It builds on <see cref="HwndHost"/>, which provides the generic
/// HWND-hosting contract; this class supplies the Windows Forms specifics: it materialises
/// the hosted control's window handle, re-parents that handle under the Jalium.UI host
/// window and keeps it positioned and sized in sync with the element's arrange rectangle.
/// </para>
/// <para>
/// The hosted control is a real child window of the Jalium.UI window, so it receives input
/// and paint messages straight from the shared Win32 message loop owned by
/// <see cref="Window"/>. No additional <c>Application.DoEvents</c>-style pump is required.
/// </para>
/// <para>
/// This control is Windows-only. On non-Windows target frameworks the Windows Forms types
/// are unavailable, so the entire type compiles out via the <c>WINDOWS</c> conditional symbol.
/// </para>
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsFormsHost : HwndHost
{
    #region Win32 interop

    // Child-window styles unique to WinFormsHost; the shared GWL_STYLE/SWP_NOZORDER/
    // SWP_NOACTIVATE/SW_HIDE/SW_SHOWNA now come from Win32Constants (issue #151).
    private const int WS_CHILD = unchecked((int)0x40000000);
    private const int WS_VISIBLE = unchecked((int)0x10000000);
    private const int WS_CLIPSIBLINGS = unchecked((int)0x04000000);

    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static int GetWindowStyle(IntPtr hWnd)
        => IntPtr.Size == 8
            ? (int)(long)GetWindowLongPtr64(hWnd, GWL_STYLE)
            : GetWindowLong32(hWnd, GWL_STYLE);

    private static void SetWindowStyle(IntPtr hWnd, int style)
    {
        if (IntPtr.Size == 8)
            _ = SetWindowLongPtr64(hWnd, GWL_STYLE, (IntPtr)style);
        else
            _ = SetWindowLong32(hWnd, GWL_STYLE, style);
    }

    #endregion

    #region Fields

    private bool _disposed;
    private bool _windowBuilt;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFormsHost"/> class.
    /// </summary>
    public WindowsFormsHost()
    {
        Loaded += OnHostLoaded;
        Unloaded += OnHostUnloaded;
    }

    #region Child dependency property

    /// <summary>
    /// Identifies the <see cref="Child"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(
            nameof(Child),
            typeof(WinFormsControl),
            typeof(WindowsFormsHost),
            new PropertyMetadata(null, OnChildChanged));

    /// <summary>
    /// Gets or sets the <see cref="System.Windows.Forms.Control"/> hosted by this element.
    /// </summary>
    /// <remarks>
    /// Setting this property re-parents the supplied control's window handle under the
    /// Jalium.UI host window. Setting it to <see langword="null"/> detaches and disposes the
    /// previously hosted control.
    /// </remarks>
    public WinFormsControl? Child
    {
        get => (WinFormsControl?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    private static void OnChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WindowsFormsHost host)
        {
            host.OnChildChanged(e.OldValue as WinFormsControl, e.NewValue as WinFormsControl);
        }
    }

    /// <summary>
    /// Handles a change to the <see cref="Child"/> property by detaching the old control and
    /// attaching the new one.
    /// </summary>
    /// <param name="oldChild">The previously hosted control, or <see langword="null"/>.</param>
    /// <param name="newChild">The newly hosted control, or <see langword="null"/>.</param>
    protected virtual void OnChildChanged(WinFormsControl? oldChild, WinFormsControl? newChild)
    {
        // Tear down the host window so the stale child handle is released.
        if (oldChild != null)
        {
            DetachChild(oldChild);
        }

        // Build (or rebuild) the host window for the new child once we are connected
        // to a live Jalium.UI window. If we are not yet loaded, RebuildOrAttach is a
        // no-op and OnHostLoaded picks it up later.
        if (newChild != null)
        {
            EnsureChildBuilt();
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var child = Child;
        if (child == null)
        {
            return Size.Empty;
        }

        // Honour an explicitly requested Windows Forms preferred size, but never exceed
        // the space the Jalium.UI layout system is offering us.
        var preferred = child.PreferredSize;
        double width = double.IsInfinity(availableSize.Width)
            ? preferred.Width
            : Math.Min(availableSize.Width, Math.Max(preferred.Width, 0));
        double height = double.IsInfinity(availableSize.Height)
            ? preferred.Height
            : Math.Min(availableSize.Height, Math.Max(preferred.Height, 0));

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // The hosted window cannot be positioned from arrange size alone — it needs the
        // element's offset relative to the host window's client area, which is only
        // known after the visual bounds have been committed. UpdateChildLayout reads
        // that offset, so defer the actual SetWindowPos until the layout pass settles.
        UpdateChildLayout();
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnSizeChanged(sizeInfo);
        UpdateChildLayout();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        // The hosted child paints itself directly to its own HWND; the Jalium.UI render
        // pass only needs to keep the child window aligned with the latest layout.
        UpdateChildLayout();
    }

    /// <summary>
    /// Computes the offset of this element relative to the client area of its host window.
    /// </summary>
    private Point GetOffsetWithinHostWindow()
    {
        double x = 0, y = 0;
        Visual? current = this;
        while (current != null)
        {
            if (current is IWindowHost)
            {
                break;
            }

            if (current is FrameworkElement fe)
            {
                var bounds = fe.VisualBounds;
                x += bounds.X;
                y += bounds.Y;
            }

            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Walks up the visual tree to locate the <see cref="IWindowHost"/> that owns this element.
    /// </summary>
    private IWindowHost? FindHostWindow()
    {
        Visual? current = VisualParent;
        while (current != null)
        {
            if (current is IWindowHost host)
            {
                return host;
            }

            current = current.VisualParent;
        }

        return null;
    }

    /// <summary>
    /// Repositions and resizes the hosted child window so it tracks this element's
    /// arrange rectangle, and synchronises its visibility with <see cref="UIElement.Visibility"/>.
    /// </summary>
    private void UpdateChildLayout()
    {
        if (_disposed || !_windowBuilt)
        {
            return;
        }

        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return;
        }

        bool visible = Visibility == Visibility.Visible
                       && RenderSize.Width > 0
                       && RenderSize.Height > 0;

        if (!visible)
        {
            _ = ShowWindow(hwnd, SW_HIDE);
            OnWindowPositionChanged(Rect.Empty);
            return;
        }

        // Convert DIP layout coordinates into physical device pixels for the Win32 child.
        double dpi = FindHostWindow()?.DpiScale ?? 1.0;
        if (dpi <= 0)
        {
            dpi = 1.0;
        }

        var offset = GetOffsetWithinHostWindow();
        int x = (int)Math.Round(offset.X * dpi);
        int y = (int)Math.Round(offset.Y * dpi);
        int cx = Math.Max(0, (int)Math.Round(RenderSize.Width * dpi));
        int cy = Math.Max(0, (int)Math.Round(RenderSize.Height * dpi));

        _ = SetWindowPos(hwnd, IntPtr.Zero, x, y, cx, cy,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        OnWindowPositionChanged(new Rect(offset.X, offset.Y, RenderSize.Width, RenderSize.Height));
    }

    #endregion

    #region HwndHost overrides

    /// <summary>
    /// Builds the hosted window by realising the <see cref="Child"/> control's handle and
    /// re-parenting it under the supplied Jalium.UI host window.
    /// </summary>
    /// <param name="hwndParent">A handle reference to the parent (host) window.</param>
    /// <returns>A handle reference to the hosted Windows Forms control window.</returns>
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var child = Child;
        if (child == null)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        // Forcing the Handle property creates the underlying Win32 window for the control
        // (and, transitively, its children) on the current thread.
        IntPtr childHwnd = child.Handle;
        if (childHwnd == IntPtr.Zero)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        // A hosted control must be a clipped child window so it composes correctly inside
        // the Jalium.UI window rather than behaving like a stand-alone top-level window.
        int style = GetWindowStyle(childHwnd);
        int desired = (style | WS_CHILD | WS_CLIPSIBLINGS) & ~WS_VISIBLE;
        if (desired != style)
        {
            SetWindowStyle(childHwnd, desired);
        }

        _ = SetParent(childHwnd, hwndParent.Handle);

        return new HandleRef(child, childHwnd);
    }

    /// <summary>
    /// Destroys the hosted window by detaching it from the host window and disposing the
    /// hosted Windows Forms control.
    /// </summary>
    /// <param name="hwnd">A handle reference to the hosted window.</param>
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (hwnd.Handle != IntPtr.Zero && IsWindow(hwnd.Handle))
        {
            // Un-parent before disposing so destruction does not race the host window.
            _ = SetParent(hwnd.Handle, IntPtr.Zero);
        }

        if (hwnd.Wrapper is WinFormsControl control && !control.IsDisposed)
        {
            control.Dispose();
        }
    }

    #endregion

    #region Build / teardown lifecycle

    private void OnHostLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureChildBuilt();
        UpdateChildLayout();
    }

    private void OnHostUnloaded(object? sender, RoutedEventArgs e)
    {
        // Detached from the visual tree: tear down the host window but keep the Child
        // reference so the element can be re-hosted if it is added to a tree again.
        TeardownWindow(disposeChild: false);
    }

    /// <summary>
    /// Ensures the hosted window has been built for the current <see cref="Child"/>, provided
    /// the element is connected to a live Jalium.UI host window.
    /// </summary>
    private void EnsureChildBuilt()
    {
        if (_disposed || _windowBuilt)
        {
            return;
        }

        var child = Child;
        if (child == null)
        {
            return;
        }

        var hostWindow = FindHostWindow();
        IntPtr parentHwnd = hostWindow?.Handle ?? IntPtr.Zero;
        if (parentHwnd == IntPtr.Zero)
        {
            // Not connected to a native window yet — OnHostLoaded will retry.
            return;
        }

        var built = BuildWindowCore(new HandleRef(this, parentHwnd));
        if (built.Handle == IntPtr.Zero)
        {
            return;
        }

        SetHandle(built.Handle);
        _windowBuilt = true;

        // Show the freshly parented child at its current layout rectangle.
        _ = ShowWindow(built.Handle, SW_SHOWNA);
        UpdateChildLayout();
    }

    /// <summary>
    /// Tears down the hosted window for the current <see cref="Child"/>.
    /// </summary>
    /// <param name="disposeChild">
    /// <see langword="true"/> to dispose the hosted Windows Forms control; <see langword="false"/>
    /// to only un-parent it (so it may be re-hosted later).
    /// </param>
    private void TeardownWindow(bool disposeChild)
    {
        if (!_windowBuilt)
        {
            return;
        }

        IntPtr hwnd = Handle;
        var child = Child;

        if (disposeChild)
        {
            DestroyWindowCore(new HandleRef(child, hwnd));
        }
        else if (hwnd != IntPtr.Zero && IsWindow(hwnd))
        {
            // Hide and un-parent without destroying the control's window.
            _ = ShowWindow(hwnd, SW_HIDE);
            _ = SetParent(hwnd, IntPtr.Zero);
        }

        SetHandle(IntPtr.Zero);
        _windowBuilt = false;
    }

    /// <summary>
    /// Detaches and disposes a control that is no longer the <see cref="Child"/>.
    /// </summary>
    private void DetachChild(WinFormsControl oldChild)
    {
        if (_windowBuilt && Handle != IntPtr.Zero)
        {
            DestroyWindowCore(new HandleRef(oldChild, Handle));
            SetHandle(IntPtr.Zero);
            _windowBuilt = false;
        }
        else if (!oldChild.IsDisposed)
        {
            oldChild.Dispose();
        }
    }

    #endregion

    #region Dispose

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;

        if (disposing)
        {
            Loaded -= OnHostLoaded;
            Unloaded -= OnHostUnloaded;

            // Final teardown disposes the hosted control.
            TeardownWindow(disposeChild: true);
        }

        base.Dispose(disposing);
    }

    #endregion
}

#endif
