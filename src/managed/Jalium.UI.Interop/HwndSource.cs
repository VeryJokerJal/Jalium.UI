using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Media;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Interop;

/// <summary>Specifies the parameters used to create an <see cref="HwndSource"/>.</summary>
public partial struct HwndSourceParameters
{
    private const int DefaultWindowStyle = unchecked((int)0x12CF0000);

    private int _classStyle;
    private int _style;
    private int _extendedStyle;
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private bool _hasAssignedSize;
    private string? _name;
    private IntPtr _parent;
    private HwndSourceHook? _hook;
    private bool _adjustSizingForNonClientArea;
    private bool _treatAncestorsAsNonClientArea;
    private bool _usesPerPixelOpacity;
    private bool _usesPerPixelTransparency;
    private RestoreFocusMode? _restoreFocusMode;
    private bool? _acquireHwndFocusInMenuMode;
    private bool? _treatAsInputRoot;

    /// <summary>Initializes parameters for a window with the specified name.</summary>
    public HwndSourceParameters(string name)
    {
        this = default;
        _name = name;
        _style = DefaultWindowStyle;
        _x = CW_USEDEFAULT;
        _y = CW_USEDEFAULT;
        _width = 1;
        _height = 1;
    }

    /// <summary>Initializes parameters for a window with the specified name and size.</summary>
    public HwndSourceParameters(string name, int width, int height)
        : this(name)
    {
        SetSize(width, height);
    }

    /// <summary>Gets or sets the name of the native window.</summary>
    public string? WindowName { readonly get => _name; set => _name = value; }

    /// <summary>Gets or sets the window width.</summary>
    public int Width
    {
        readonly get => _width;
        set
        {
            _width = value;
            _hasAssignedSize = true;
        }
    }

    /// <summary>Gets or sets the window height.</summary>
    public int Height
    {
        readonly get => _height;
        set
        {
            _height = value;
            _hasAssignedSize = true;
        }
    }

    /// <summary>Gets or sets the x-coordinate of the window.</summary>
    public int PositionX { readonly get => _x; set => _x = value; }

    /// <summary>Gets or sets the y-coordinate of the window.</summary>
    public int PositionY { readonly get => _y; set => _y = value; }

    /// <summary>Gets or sets the native window style.</summary>
    public int WindowStyle { readonly get => _style; set => _style = value; }

    /// <summary>Gets or sets the native extended window style.</summary>
    public int ExtendedWindowStyle { readonly get => _extendedStyle; set => _extendedStyle = value; }

    /// <summary>Gets or sets the parent window handle.</summary>
    public IntPtr ParentWindow { readonly get => _parent; set => _parent = value; }

    /// <summary>Gets or sets the native window class style.</summary>
    public int WindowClassStyle { readonly get => _classStyle; set => _classStyle = value; }

    /// <summary>Gets or sets the hook installed when the source is created.</summary>
    public HwndSourceHook? HwndSourceHook { readonly get => _hook; set => _hook = value; }

    /// <summary>Gets or sets whether the assigned size describes the client area.</summary>
    public bool AdjustSizingForNonClientArea
    {
        readonly get => _adjustSizingForNonClientArea;
        set => _adjustSizingForNonClientArea = value;
    }

    /// <summary>Gets or sets whether ancestors participate in non-client hit testing.</summary>
    public bool TreatAncestorsAsNonClientArea
    {
        readonly get => _treatAncestorsAsNonClientArea;
        set => _treatAncestorsAsNonClientArea = value;
    }

    /// <summary>Gets or sets whether the window uses per-pixel opacity.</summary>
    public bool UsesPerPixelOpacity
    {
        readonly get => _usesPerPixelOpacity;
        set => _usesPerPixelOpacity = value;
    }

    /// <summary>Gets or sets the compatibility alias for per-pixel transparency.</summary>
    public bool UsesPerPixelTransparency
    {
        readonly get => _usesPerPixelTransparency;
        set => _usesPerPixelTransparency = value;
    }

    /// <summary>Gets or sets how keyboard focus is restored when the source is reactivated.</summary>
    public RestoreFocusMode RestoreFocusMode
    {
        readonly get => _restoreFocusMode ?? Jalium.UI.Input.RestoreFocusMode.Auto;
        set => _restoreFocusMode = value;
    }

    /// <summary>Gets or sets whether menu mode acquires native focus.</summary>
    public bool AcquireHwndFocusInMenuMode
    {
        readonly get => _acquireHwndFocusInMenuMode ?? HwndSource.DefaultAcquireHwndFocusInMenuMode;
        set => _acquireHwndFocusInMenuMode = value;
    }

    /// <summary>Gets or sets whether the source is an input root.</summary>
    public bool TreatAsInputRoot
    {
        readonly get => _treatAsInputRoot ?? true;
        set => _treatAsInputRoot = value;
    }

    /// <summary>Gets whether a width or height was explicitly assigned.</summary>
    public readonly bool HasAssignedSize => _hasAssignedSize;

    /// <summary>Assigns both window coordinates.</summary>
    public void SetPosition(int x, int y)
    {
        _x = x;
        _y = y;
    }

    /// <summary>Assigns both window dimensions.</summary>
    public void SetSize(int width, int height)
    {
        _width = width;
        _height = height;
        _hasAssignedSize = true;
    }

    /// <summary>Compares the native creation parameters used by the source.</summary>
    public readonly bool Equals(HwndSourceParameters obj) =>
        _classStyle == obj._classStyle &&
        _style == obj._style &&
        _extendedStyle == obj._extendedStyle &&
        _x == obj._x &&
        _y == obj._y &&
        _width == obj._width &&
        _height == obj._height &&
        _hasAssignedSize == obj._hasAssignedSize &&
        string.Equals(_name, obj._name, StringComparison.Ordinal) &&
        _parent == obj._parent &&
        Equals(_hook, obj._hook) &&
        _adjustSizingForNonClientArea == obj._adjustSizingForNonClientArea &&
        _usesPerPixelOpacity == obj._usesPerPixelOpacity &&
        _usesPerPixelTransparency == obj._usesPerPixelTransparency;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) =>
        obj is HwndSourceParameters parameters && Equals(parameters);

    /// <inheritdoc />
    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_classStyle);
        hash.Add(_style);
        hash.Add(_extendedStyle);
        hash.Add(_x);
        hash.Add(_y);
        hash.Add(_width);
        hash.Add(_height);
        hash.Add(_hasAssignedSize);
        hash.Add(_name, StringComparer.Ordinal);
        hash.Add(_parent);
        hash.Add(_hook);
        hash.Add(_adjustSizingForNonClientArea);
        hash.Add(_usesPerPixelOpacity);
        hash.Add(_usesPerPixelTransparency);
        return hash.ToHashCode();
    }

    /// <summary>Compares two parameter structures.</summary>
    public static bool operator ==(HwndSourceParameters a, HwndSourceParameters b) => a.Equals(b);

    /// <summary>Compares two parameter structures.</summary>
    public static bool operator !=(HwndSourceParameters a, HwndSourceParameters b) => !a.Equals(b);
}

/// <summary>Presents a Jalium visual tree in a native Win32 window.</summary>
public partial class HwndSource : PresentationSource, IDisposable, IKeyboardInputSink, IWin32Window
{
    private const uint WmNcDestroy = 0x0082;
    private const uint WmDpiChanged = 0x02E0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly ConcurrentDictionary<IntPtr, WeakReference<HwndSource>> Sources = new();
    private static readonly object WindowClassGate = new();
    private static readonly Dictionary<int, string> WindowClasses = new();
    private static readonly NativeWndProc WindowProcedure = StaticWindowProc;
    private static bool _defaultAcquireHwndFocusInMenuMode = true;

    private readonly List<KeyboardInputSite> _keyboardInputSites = [];
    private HwndSourceHook? _hooks;
    private HwndTarget? _hwndTarget;
    private IKeyboardInputSite? _keyboardInputSite;
    private Visual? _rootVisual;
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _disposing;
    private bool _acquireHwndFocusInMenuMode;
    private bool _usesPerPixelOpacity;
    private RestoreFocusMode _restoreFocusMode;
    private SizeToContent _sizeToContent;

    /// <summary>Initializes a source from a complete parameter structure.</summary>
    public HwndSource(HwndSourceParameters parameters)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HwndSource requires Win32 windowing support.");
        }

        _hooks = parameters.HwndSourceHook;
        _acquireHwndFocusInMenuMode = parameters.AcquireHwndFocusInMenuMode;
        _restoreFocusMode = parameters.RestoreFocusMode;
        _usesPerPixelOpacity = parameters.UsesPerPixelOpacity || parameters.UsesPerPixelTransparency;

        CreateNativeWindow(parameters);
    }

    /// <summary>Initializes a source whose size is selected by Win32.</summary>
    public HwndSource(int classStyle, int style, int exStyle, int x, int y, string name, IntPtr parent)
        : this(CreateParameters(classStyle, style, exStyle, x, y, name, parent))
    {
    }

    /// <summary>Initializes a source with an explicit size.</summary>
    public HwndSource(
        int classStyle,
        int style,
        int exStyle,
        int x,
        int y,
        int width,
        int height,
        string name,
        IntPtr parent)
        : this(CreateParameters(classStyle, style, exStyle, x, y, width, height, name, parent, false))
    {
    }

    /// <summary>Initializes a source with an explicit client or window size.</summary>
    public HwndSource(
        int classStyle,
        int style,
        int exStyle,
        int x,
        int y,
        int width,
        int height,
        string name,
        IntPtr parent,
        bool adjustSizingForNonClientArea)
        : this(CreateParameters(
            classStyle,
            style,
            exStyle,
            x,
            y,
            width,
            height,
            name,
            parent,
            adjustSizingForNonClientArea))
    {
    }

    /// <summary>Gets or sets the process default used by new parameter structures.</summary>
    public static bool DefaultAcquireHwndFocusInMenuMode
    {
        get => _defaultAcquireHwndFocusInMenuMode;
        set => _defaultAcquireHwndFocusInMenuMode = value;
    }

    /// <summary>Gets the native window handle.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>Gets the composition target attached to the native window.</summary>
    public new HwndTarget CompositionTarget =>
        _hwndTarget ?? throw new ObjectDisposedException(nameof(HwndSource));

    /// <inheritdoc />
    public override Visual? RootVisual
    {
        get => _rootVisual;
        set
        {
            CheckDisposed();
            _rootVisual = value;
            CompositionTarget.RootVisual = value;
        }
    }

    /// <inheritdoc />
    public override bool IsDisposed => _disposed;

    /// <summary>Gets whether menu mode acquires native focus.</summary>
    public bool AcquireHwndFocusInMenuMode => _acquireHwndFocusInMenuMode;

    /// <summary>Gets the configured focus restoration mode.</summary>
    public RestoreFocusMode RestoreFocusMode => _restoreFocusMode;

    /// <summary>Gets whether this source uses per-pixel opacity.</summary>
    public bool UsesPerPixelOpacity => _usesPerPixelOpacity;

    /// <summary>Gets or sets automatic content sizing for this source.</summary>
    public SizeToContent SizeToContent
    {
        get => _sizeToContent;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SizeToContent));
            }

            CheckDisposed();
            if (_sizeToContent == value)
            {
                return;
            }

            _sizeToContent = value;
            SizeToContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the registered child keyboard sinks.</summary>
    public IEnumerable<IKeyboardInputSink> ChildKeyboardInputSinks
    {
        get
        {
            lock (_keyboardInputSites)
            {
                return _keyboardInputSites.Select(static site => site.Sink).ToArray();
            }
        }
    }

    /// <summary>Occurs after a content-driven native resize.</summary>
    public event AutoResizedEventHandler? AutoResized;

    /// <summary>Occurs when the native window's DPI changes.</summary>
    public event HwndDpiChangedEventHandler? DpiChanged;

    /// <summary>Occurs when <see cref="SizeToContent"/> changes.</summary>
    public event EventHandler? SizeToContentChanged;

    /// <summary>Occurs when the source is disposed.</summary>
    public event EventHandler? Disposed;

    /// <summary>Returns the source associated with a live native window.</summary>
    public static HwndSource? FromHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Sources.TryGetValue(hwnd, out var weakSource))
        {
            return null;
        }

        if (weakSource.TryGetTarget(out var source) && !source._disposed)
        {
            return source;
        }

        Sources.TryRemove(hwnd, out _);
        return null;
    }

    /// <summary>Adds a native message hook.</summary>
    public void AddHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        CheckDisposed();
        _hooks += hook;
    }

    /// <summary>Removes a native message hook.</summary>
    public void RemoveHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks -= hook;
    }

    /// <summary>Creates a handle reference rooted by this source.</summary>
    public HandleRef CreateHandleRef() => new(this, _hwnd);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Gets the per-source composition target.</summary>
    protected override Jalium.UI.Media.CompositionTarget GetCompositionTargetCore() => CompositionTarget!;

    /// <summary>Gets or sets this sink's parent keyboard input site.</summary>
    protected IKeyboardInputSite? KeyboardInputSiteCore
    {
        get => _keyboardInputSite;
        set => _keyboardInputSite = value;
    }

    /// <summary>Registers a child keyboard sink.</summary>
    protected IKeyboardInputSite RegisterKeyboardInputSinkCore(IKeyboardInputSink sink)
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

    /// <summary>Moves keyboard focus into the root visual.</summary>
    protected virtual bool TabIntoCore(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _rootVisual is UIElement element && element.MoveFocus(request);
    }

    /// <summary>Processes a keyboard accelerator not handled by a child sink.</summary>
    protected virtual bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers) => false;

    /// <summary>Processes a character message not handled by a child sink.</summary>
    protected virtual bool TranslateCharCore(ref MSG msg, ModifierKeys modifiers) => false;

    /// <summary>Processes a mnemonic not handled by a child sink.</summary>
    protected virtual bool OnMnemonicCore(ref MSG msg, ModifierKeys modifiers) => false;

    /// <summary>Gets whether native focus is within this source.</summary>
    protected virtual bool HasFocusWithinCore()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero)
        {
            return false;
        }

        var focused = GetFocus();
        return focused == _hwnd || (focused != IntPtr.Zero && IsChild(_hwnd, focused));
    }

    /// <summary>Raises the source DPI transition notification.</summary>
    protected virtual void OnDpiChanged(HwndDpiChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        DpiChanged?.Invoke(this, e);
    }

    IKeyboardInputSite? IKeyboardInputSink.KeyboardInputSite
    {
        get => KeyboardInputSiteCore;
        set => KeyboardInputSiteCore = value;
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

    private static HwndSourceParameters CreateParameters(
        int classStyle,
        int style,
        int exStyle,
        int x,
        int y,
        string name,
        IntPtr parent)
    {
        var parameters = new HwndSourceParameters(name)
        {
            WindowClassStyle = classStyle,
            WindowStyle = style,
            ExtendedWindowStyle = exStyle,
            ParentWindow = parent,
        };
        parameters.SetPosition(x, y);
        return parameters;
    }

    private static HwndSourceParameters CreateParameters(
        int classStyle,
        int style,
        int exStyle,
        int x,
        int y,
        int width,
        int height,
        string name,
        IntPtr parent,
        bool adjustSizingForNonClientArea)
    {
        var parameters = new HwndSourceParameters(name, width, height)
        {
            WindowClassStyle = classStyle,
            WindowStyle = style,
            ExtendedWindowStyle = exStyle,
            ParentWindow = parent,
            AdjustSizingForNonClientArea = adjustSizingForNonClientArea,
        };
        parameters.SetPosition(x, y);
        return parameters;
    }

    private void CreateNativeWindow(HwndSourceParameters parameters)
    {
        string className = EnsureWindowClass(parameters.WindowClassStyle);
        uint style = unchecked((uint)parameters.WindowStyle);
        uint exStyle = unchecked((uint)parameters.ExtendedWindowStyle);
        if (_usesPerPixelOpacity)
        {
            exStyle |= WS_EX_LAYERED;
        }

        int width = parameters.Width;
        int height = parameters.Height;
        if (parameters.AdjustSizingForNonClientArea && parameters.HasAssignedSize)
        {
            var rect = new RECT { right = width, bottom = height };
            if (AdjustWindowRectEx(ref rect, style, false, exStyle))
            {
                width = rect.right - rect.left;
                height = rect.bottom - rect.top;
            }
        }

        IntPtr hwnd = CreateWindowEx(
            exStyle,
            className,
            parameters.WindowName ?? string.Empty,
            style,
            parameters.PositionX,
            parameters.PositionY,
            width,
            height,
            parameters.ParentWindow,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _hwnd = hwnd;
        Sources[hwnd] = new WeakReference<HwndSource>(this);

        try
        {
            _hwndTarget = new HwndTarget(hwnd)
            {
                UsesPerPixelOpacity = _usesPerPixelOpacity,
            };
        }
        catch
        {
            Sources.TryRemove(hwnd, out _);
            _hwnd = IntPtr.Zero;
            _ = DestroyWindow(hwnd);
            throw;
        }
    }

    private static string EnsureWindowClass(int classStyle)
    {
        lock (WindowClassGate)
        {
            if (WindowClasses.TryGetValue(classStyle, out string? existing))
            {
                return existing;
            }

            string className = $"Jalium.UI.HwndSource.{Environment.ProcessId}.{unchecked((uint)classStyle):X8}";
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = unchecked((uint)classStyle),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
                hInstance = GetModuleHandle(null),
                lpszClassName = className,
            };

            ushort atom = RegisterClassEx(ref windowClass);
            int error = Marshal.GetLastPInvokeError();
            if (atom == 0 && error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error);
            }

            WindowClasses[classStyle] = className;
            return className;
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        HwndSource? source = FromHwnd(hwnd);
        if (source == null)
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        bool handled = false;
        IntPtr result = source.InvokeHooks(hwnd, unchecked((int)msg), wParam, lParam, ref handled);
        if (!handled && msg == WmDpiChanged)
        {
            source.ProcessDpiChanged(wParam, lParam);
        }

        if (msg == WmNcDestroy)
        {
            source.OnNativeWindowDestroyed(hwnd);
        }

        return handled ? result : DefWindowProc(hwnd, msg, wParam, lParam);
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

    private void ProcessDpiChanged(IntPtr wParam, IntPtr lParam)
    {
        uint packedDpi = unchecked((uint)wParam.ToInt64());
        double dpiX = (packedDpi & 0xFFFF) / 96d;
        double dpiY = ((packedDpi >> 16) & 0xFFFF) / 96d;
        if (dpiX <= 0 || dpiY <= 0)
        {
            return;
        }

        DpiScale oldDpi = _hwndTarget?.DpiScale ?? new DpiScale(1, 1);
        var newDpi = new DpiScale(dpiX, dpiY);
        Rect suggestedRect = Rect.Empty;
        if (lParam != IntPtr.Zero)
        {
            RECT rect = Marshal.PtrToStructure<RECT>(lParam);
            suggestedRect = new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        }

        var args = new HwndDpiChangedEventArgs(oldDpi, newDpi, suggestedRect);
        OnDpiChanged(args);
        _hwndTarget?.UpdateDpi(newDpi);

        if (_rootVisual is FrameworkElement element)
        {
            element.NotifyDpiChangedRecursive(oldDpi, newDpi);
        }

        if (!args.Handled && !suggestedRect.IsEmpty && _hwnd != IntPtr.Zero)
        {
            _ = SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                (int)suggestedRect.X,
                (int)suggestedRect.Y,
                (int)suggestedRect.Width,
                (int)suggestedRect.Height,
                SwpNoZOrder | SwpNoActivate);
        }
    }

    private void OnNativeWindowDestroyed(IntPtr hwnd)
    {
        Sources.TryRemove(hwnd, out _);
        if (_hwnd == hwnd)
        {
            _hwnd = IntPtr.Zero;
        }

        CompleteDisposal();
    }

    private void Dispose(bool disposing)
    {
        if (_disposed || _disposing)
        {
            return;
        }

        _disposing = true;
        IntPtr hwnd = _hwnd;
        if (hwnd != IntPtr.Zero)
        {
            Sources.TryRemove(hwnd, out _);
            _hwnd = IntPtr.Zero;
            if (OperatingSystem.IsWindows() && IsWindow(hwnd))
            {
                _ = DestroyWindow(hwnd);
            }
        }

        CompleteDisposal();
    }

    private void CompleteDisposal()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposing = false;
        _rootVisual = null;
        _hwndTarget?.Dispose();
        _hwndTarget = null;

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

        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private void CheckDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr NativeWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Kept as a real event backing field even though automatic sizing is initiated by a
    // future layout bridge. Referencing it suppresses the compiler's unused-event warning.
    private void RaiseAutoResized(Size size) => AutoResized?.Invoke(this, new AutoResizedEventArgs(size));
}

/// <summary>Represents a delegate that processes native window messages.</summary>
public delegate IntPtr HwndSourceHook(
    IntPtr hwnd,
    int msg,
    IntPtr wParam,
    IntPtr lParam,
    ref bool handled);
