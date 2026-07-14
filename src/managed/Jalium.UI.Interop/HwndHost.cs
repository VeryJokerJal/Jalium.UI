using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Media;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Interop;

/// <summary>Hosts a native Win32 child window in the Jalium visual tree.</summary>
public abstract class HwndHost : FrameworkElement, IDisposable, IKeyboardInputSink, IWin32Window
{
    private const uint WmNcDestroy = 0x0082;
    private static readonly NativeSubclassProc SubclassProcedure = StaticSubclassProc;
    private static readonly ConcurrentDictionary<SubclassKey, WeakReference<HwndHost>> Subclasses = new();
    private static long _nextSubclassId;

    private readonly List<KeyboardInputSite> _keyboardInputSites = [];
    private readonly nuint _subclassId = unchecked((nuint)Interlocked.Increment(ref _nextSubclassId));
    private HwndSourceHook? _hooks;
    private IKeyboardInputSite? _keyboardInputSite;
    private bool _disposed;

    /// <summary>Identifies the <see cref="DpiChanged"/> routed event.</summary>
    public static readonly RoutedEvent DpiChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(DpiChanged),
        RoutingStrategy.Bubble,
        typeof(DpiChangedEventHandler),
        typeof(HwndHost));

    /// <summary>Gets the hosted native window handle.</summary>
    public IntPtr Handle { get; private set; }

    /// <summary>Occurs when the hosted window's DPI changes.</summary>
    public event DpiChangedEventHandler DpiChanged
    {
        add => AddHandler(DpiChangedEvent, value);
        remove => RemoveHandler(DpiChangedEvent, value);
    }

    /// <summary>Occurs for native messages sent to the hosted window.</summary>
    public event HwndSourceHook MessageHook
    {
        add => AddHook(value);
        remove => RemoveHook(value);
    }

    /// <summary>Creates the child native window.</summary>
    protected abstract HandleRef BuildWindowCore(HandleRef hwndParent);

    /// <summary>Destroys the child native window.</summary>
    protected abstract void DestroyWindowCore(HandleRef hwnd);

    /// <summary>Sets the real native handle returned by <see cref="BuildWindowCore"/>.</summary>
    protected void SetHandle(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Handle == hwnd)
        {
            return;
        }

        DetachSubclass();
        if (hwnd == IntPtr.Zero)
        {
            Handle = IntPtr.Zero;
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HwndHost requires Win32 windowing support.");
        }

        if (!IsWindow(hwnd))
        {
            throw new ArgumentException("The specified handle is not a valid window handle.", nameof(hwnd));
        }

        var key = new SubclassKey(hwnd, _subclassId);
        Subclasses[key] = new WeakReference<HwndHost>(this);
        if (!SetWindowSubclass(hwnd, SubclassProcedure, _subclassId, UIntPtr.Zero))
        {
            Subclasses.TryRemove(key, out _);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Handle = hwnd;
    }

    /// <summary>Adds a native message hook.</summary>
    protected void AddHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks += hook;
    }

    /// <summary>Removes a native message hook.</summary>
    protected void RemoveHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks -= hook;
    }

    /// <summary>Processes native messages not handled by a registered hook.</summary>
    protected virtual IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled) => IntPtr.Zero;

    /// <summary>Called after the hosted native window is repositioned.</summary>
    protected virtual void OnWindowPositionChanged(Rect rcBoundingBox)
    {
    }

    /// <summary>Gets whether native keyboard focus is within the hosted window.</summary>
    protected virtual bool HasFocusWithinCore()
    {
        if (!OperatingSystem.IsWindows() || Handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr focused = GetFocus();
        return focused == Handle || (focused != IntPtr.Zero && IsChild(Handle, focused));
    }

    /// <summary>Processes a mnemonic.</summary>
    protected virtual bool OnMnemonicCore(ref MSG msg, ModifierKeys modifiers) => false;

    /// <summary>Registers a child keyboard input sink.</summary>
    protected virtual IKeyboardInputSite RegisterKeyboardInputSinkCore(IKeyboardInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var site = new KeyboardInputSite(this, sink, registeredSite =>
        {
            lock (_keyboardInputSites)
            {
                _keyboardInputSites.Remove(registeredSite);
            }
        });

        lock (_keyboardInputSites)
        {
            _keyboardInputSites.Add(site);
        }

        sink.KeyboardInputSite = site;
        return site;
    }

    /// <summary>Moves keyboard focus into this host.</summary>
    protected virtual bool TabIntoCore(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows() || Handle == IntPtr.Zero)
        {
            return false;
        }

        return SetFocus(Handle) != IntPtr.Zero || GetFocus() == Handle;
    }

    /// <summary>Processes a keyboard accelerator.</summary>
    protected virtual bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers) => false;

    /// <summary>Processes a character message.</summary>
    protected virtual bool TranslateCharCore(ref MSG msg, ModifierKeys modifiers) => false;

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RaiseEvent(new DpiChangedEventArgs(oldDpi, newDpi)
        {
            RoutedEvent = DpiChangedEvent,
            Source = this,
        });
    }

    /// <summary>Updates the hosted window to match the element's current layout bounds.</summary>
    public void UpdateWindowPos()
    {
        if (!OperatingSystem.IsWindows() || Handle == IntPtr.Zero || !IsWindow(Handle))
        {
            return;
        }

        double x = 0;
        double y = 0;
        double dpi = 1;
        Visual? current = this;
        while (current != null)
        {
            if (current is IWindowHost windowHost)
            {
                dpi = windowHost.DpiScale > 0 ? windowHost.DpiScale : 1;
                break;
            }

            if (current is FrameworkElement element)
            {
                x += element.VisualBounds.X;
                y += element.VisualBounds.Y;
            }

            current = current.VisualParent;
        }

        int pixelX = (int)Math.Round(x * dpi);
        int pixelY = (int)Math.Round(y * dpi);
        int pixelWidth = Math.Max(0, (int)Math.Round(RenderSize.Width * dpi));
        int pixelHeight = Math.Max(0, (int)Math.Round(RenderSize.Height * dpi));
        _ = SetWindowPos(
            Handle,
            IntPtr.Zero,
            pixelX,
            pixelY,
            pixelWidth,
            pixelHeight,
            SWP_NOZORDER | SWP_NOACTIVATE);

        OnWindowPositionChanged(new Rect(x, y, RenderSize.Width, RenderSize.Height));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the hosted native window and keyboard registrations.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        IntPtr hwnd = Handle;
        DetachSubclass();
        Handle = IntPtr.Zero;
        _disposed = true;

        if (hwnd != IntPtr.Zero)
        {
            DestroyWindowCore(new HandleRef(this, hwnd));
        }

        KeyboardInputSite[] sites;
        lock (_keyboardInputSites)
        {
            sites = _keyboardInputSites.ToArray();
            _keyboardInputSites.Clear();
        }

        foreach (KeyboardInputSite site in sites)
        {
            site.Unregister();
        }
    }

    IKeyboardInputSite? IKeyboardInputSink.KeyboardInputSite
    {
        get => _keyboardInputSite;
        set => _keyboardInputSite = value;
    }

    IKeyboardInputSite IKeyboardInputSink.RegisterKeyboardInputSink(IKeyboardInputSink sink) =>
        RegisterKeyboardInputSinkCore(sink);

    bool IKeyboardInputSink.TabInto(TraversalRequest request) => TabIntoCore(request);

    bool IKeyboardInputSink.TranslateAccelerator(ref MSG msg, ModifierKeys modifiers) =>
        TranslateAcceleratorCore(ref msg, modifiers);

    bool IKeyboardInputSink.TranslateChar(ref MSG msg, ModifierKeys modifiers) =>
        TranslateCharCore(ref msg, modifiers);

    bool IKeyboardInputSink.OnMnemonic(ref MSG msg, ModifierKeys modifiers) =>
        OnMnemonicCore(ref msg, modifiers);

    bool IKeyboardInputSink.HasFocusWithin() => HasFocusWithinCore();

    private static IntPtr StaticSubclassProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        nuint subclassId,
        UIntPtr referenceData)
    {
        var key = new SubclassKey(hwnd, subclassId);
        if (!Subclasses.TryGetValue(key, out var weakHost) || !weakHost.TryGetTarget(out HwndHost? host))
        {
            Subclasses.TryRemove(key, out _);
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        bool handled = false;
        IntPtr result = host.InvokeHooks(hwnd, unchecked((int)message), wParam, lParam, ref handled);
        if (!handled)
        {
            result = host.WndProc(hwnd, unchecked((int)message), wParam, lParam, ref handled);
        }

        if (message == WmNcDestroy)
        {
            Subclasses.TryRemove(key, out _);
            if (host.Handle == hwnd)
            {
                host.Handle = IntPtr.Zero;
            }
        }

        return handled ? result : DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private IntPtr InvokeHooks(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        IntPtr result = IntPtr.Zero;
        var hooks = _hooks;
        if (hooks == null)
        {
            return result;
        }

        foreach (HwndSourceHook hook in hooks.GetInvocationList())
        {
            result = hook(hwnd, message, wParam, lParam, ref handled);
            if (handled)
            {
                break;
            }
        }

        return result;
    }

    private void DetachSubclass()
    {
        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Subclasses.TryRemove(new SubclassKey(hwnd, _subclassId), out _);
        if (OperatingSystem.IsWindows() && IsWindow(hwnd))
        {
            _ = RemoveWindowSubclass(hwnd, SubclassProcedure, _subclassId);
        }
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd,
        NativeSubclassProc callback,
        nuint subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hwnd,
        NativeSubclassProc callback,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr NativeSubclassProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        nuint subclassId,
        UIntPtr referenceData);

    private readonly record struct SubclassKey(IntPtr Handle, nuint Id);
}
