using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Platform-neutral window abstraction. Implementations delegate to
/// platform-specific windowing APIs (Win32 HWND, X11 Window, ANativeWindow).
/// </summary>
internal interface IPlatformWindow : IDisposable
{
    /// <summary>Gets the native window handle (HWND, X11 Window ID, or ANativeWindow*).</summary>
    nint NativeHandle { get; }

    /// <summary>Gets a platform-neutral surface descriptor for creating render targets.</summary>
    NativeSurfaceDescriptor GetSurface();

    void Show();
    void Show(bool activate) => Show();
    void Hide();
    void Close();

    void SetTitle(string title);
    void Resize(int width, int height);
    void Move(int x, int y);

    /// <summary>Applies startup placement using native outer frames and screen coordinates.</summary>
    bool ApplyStartupLocation(WindowStartupLocation location, nint ownerNativeHandle) => false;

    int GetWidth();
    int GetHeight();

    /// <summary>Gets the window origin in screen coordinates (physical pixels).</summary>
    void GetPosition(out int x, out int y);

    /// <summary>Gets the client view's top-left origin in physical screen pixels.</summary>
    bool TryGetClientOrigin(out int x, out int y)
    {
        GetPosition(out x, out y);
        return true;
    }

    /// <summary>Gets saved normal geometry when the platform has already changed its frame.</summary>
    bool TryGetRestoreBounds(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        return false;
    }

    /// <summary>Applies min/max client-size constraints in physical pixels (0 = unbounded).</summary>
    void SetMinMaxSize(int minWidth, int minHeight, int maxWidth, int maxHeight);

    /// <summary>Starts a window-system-driven interactive move (call from a mouse press handler).</summary>
    bool BeginMoveDrag();

    /// <summary>Performs the platform's configured title-bar double-click action.</summary>
    bool PerformTitleBarDoubleClick() => false;

    /// <summary>Starts a window-system-driven interactive resize from the given edge.</summary>
    bool BeginResizeDrag(int edge);

    /// <summary>Sets the taskbar/window icon from BGRA pixels; null clears. Returns false when unsupported.</summary>
    bool SetIcon(uint[]? bgraPixels, int width, int height);

    /// <summary>Toggles always-on-top. Returns false when the window system does not support it.</summary>
    bool SetTopmost(bool topmost);

    /// <summary>Enables or disables native input delivery for the window.</summary>
    bool SetEnabled(bool enabled);

    /// <summary>Sets whole-window opacity. Returns false when the window system has no such protocol.</summary>
    bool SetOpacity(double opacity);

    /// <summary>Toggles taskbar/switcher visibility where supported.</summary>
    bool SetShowInTaskbar(bool showInTaskbar);

    /// <summary>Toggles interactive resizing while preserving explicit min/max constraints.</summary>
    bool SetResizable(bool resizable);

    /// <summary>Toggles server-side window decorations where supported.</summary>
    bool SetDecorated(bool decorated);

    /// <summary>Applies all style capabilities atomically where supported.</summary>
    bool SetStyle(uint style) => false;

    /// <summary>Extends a native macOS client area behind the system title bar.</summary>
    bool SetExtendedTitleBar(bool extended) => false;

    /// <summary>Aligns native macOS caption buttons with the toolbar's content.</summary>
    bool SetTitleBarContentHeight(double height) => false;

    /// <summary>Sets the native material corresponding to WindowBackdropType.</summary>
    bool SetSystemBackdrop(int backdrop) => false;

    /// <summary>Updates the transient owner relationship. A zero handle clears it.</summary>
    bool SetOwner(nint ownerNativeHandle);

    /// <summary>Requests foreground activation. Returns false when the compositor owns activation.</summary>
    bool Activate();

    /// <summary>
    /// Requests the compositor/window manager system menu at a client-local
    /// position expressed in physical pixels. Returns false when unsupported.
    /// </summary>
    bool ShowSystemMenu(int x, int y);

    void SetState(WindowState state);
    WindowState GetState();

    void Invalidate();

    float GetDpiScale();
    int GetMonitorRefreshRate();

    void SetCursor(int cursorShape);

    /// <summary>
    /// Updates the native text-input context. Surrounding text is UTF-16 in
    /// managed code while cursor/anchor offsets in this platform value have
    /// already been converted to UTF-8 byte offsets.
    /// </summary>
    void UpdateImeContext(PlatformImeContext context);

    /// <summary>
    /// Sets the event handler callback. The platform window will invoke this
    /// callback for all platform events (resize, mouse, keyboard, etc.).
    /// </summary>
    void SetEventHandler(Action<PlatformEvent>? handler);
}

/// <summary>
/// Fully marshalled native IME context for a platform window.
/// </summary>
internal readonly record struct PlatformImeContext(
    bool Enabled,
    string? SurroundingText,
    int CursorUtf8ByteOffset,
    int AnchorUtf8ByteOffset,
    int CaretX,
    int CaretY,
    int CaretWidth,
    int CaretHeight)
{
    internal static PlatformImeContext Disabled => default;

    internal static PlatformImeContext Create(
        bool enabled,
        ImeSurroundingTextSnapshot? surroundingText,
        Rect localCaretRectangle,
        Point targetOrigin,
        double dpiScale,
        bool preserveDocumentOffsets = false)
    {
        if (!enabled)
            return Disabled;

        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
            dpiScale = 1.0;

        double localX = double.IsFinite(localCaretRectangle.X) ? localCaretRectangle.X : 0;
        double localY = double.IsFinite(localCaretRectangle.Y) ? localCaretRectangle.Y : 0;
        double localWidth = double.IsFinite(localCaretRectangle.Width) && localCaretRectangle.Width > 0
            ? localCaretRectangle.Width
            : 1;
        double localHeight = double.IsFinite(localCaretRectangle.Height) && localCaretRectangle.Height > 0
            ? localCaretRectangle.Height
            : 1;
        double originX = double.IsFinite(targetOrigin.X) ? targetOrigin.X : 0;
        double originY = double.IsFinite(targetOrigin.Y) ? targetOrigin.Y : 0;

        string? text = null;
        int cursorBytes = 0;
        int anchorBytes = 0;
        if (surroundingText is { } snapshot)
        {
            if (preserveDocumentOffsets)
            {
                // NSTextInputClient ranges are absolute UTF-16 document offsets.
                // A Wayland-sized excerpt would silently give AppKit wrong ranges.
                text = snapshot.Text ?? string.Empty;
                cursorBytes = ImeTextEncoding.GetUtf8ByteOffset(text,
                    ImeTextEncoding.SnapToGraphemeBoundary(text, snapshot.CursorIndex, forward: false));
                anchorBytes = ImeTextEncoding.GetUtf8ByteOffset(text,
                    ImeTextEncoding.SnapToGraphemeBoundary(text, snapshot.AnchorIndex, forward: false));
            }
            else if (ImeTextEncoding.TryCreateUtf8SurroundingWindow(
                    snapshot,
                    ImeTextEncoding.MaximumSurroundingTextUtf8Bytes,
                    out ImeSurroundingTextSnapshot window))
            {
                text = window.Text;
                cursorBytes = ImeTextEncoding.GetUtf8ByteOffset(text, window.CursorIndex);
                anchorBytes = ImeTextEncoding.GetUtf8ByteOffset(text, window.AnchorIndex);
            }
        }

        return new PlatformImeContext(
            true,
            text,
            cursorBytes,
            anchorBytes,
            ToPhysical(originX + localX, dpiScale),
            ToPhysical(originY + localY, dpiScale),
            Math.Max(1, ToPhysical(localWidth, dpiScale)),
            Math.Max(1, ToPhysical(localHeight, dpiScale)));
    }

    private static int ToPhysical(double value, double scale)
    {
        double scaled = value * scale;
        if (!double.IsFinite(scaled))
            return 0;
        if (scaled >= int.MaxValue)
            return int.MaxValue;
        if (scaled <= int.MinValue)
            return int.MinValue;
        return (int)Math.Round(scaled);
    }
}

/// <summary>Synchronous AppKit selection/replacement request and acknowledgement.</summary>
internal sealed class PlatformImeTextRequest(int start, int length, string? text, bool replace)
{
    internal int Start { get; } = start;
    internal int Length { get; } = length;
    internal string Text { get; } = text ?? string.Empty;
    internal bool Replace { get; } = replace;
    internal bool Applied { get; set; }
}

/// <summary>Synchronous AppKit geometry query. Points and rectangles use client physical pixels.</summary>
internal sealed class PlatformImeGeometryRequest(int kind, int start, int length, Point point)
{
    internal int Kind { get; } = kind; // 0/1: committed range/point; 2/3: provisional range/point.
    internal int Start { get; } = start;
    internal int Length { get; } = length;
    internal Point Point { get; } = point;
    internal bool Handled { get; set; }
    internal ImeTextRangeGeometry Geometry { get; set; }
    internal int CharacterIndex { get; set; } = -1;
}

/// <summary>
/// Platform event data passed from the native platform layer to managed code.
/// </summary>
internal struct PlatformEvent
{
    public PlatformEventType Type;
    public nint WindowHandle;

    // Resize
    public int Width, Height;
    public bool IsUserInitiatedResize;

    // Move
    public int X, Y;

    // DPI
    public float DpiX, DpiY;
    public int SuggestedX, SuggestedY, SuggestedWidth, SuggestedHeight;

    // Mouse
    public float MouseX, MouseY;
    public int Button;
    public int Modifiers;
    public int ClickCount;
    public uint MouseButtons;
    public bool HasMouseButtonStates;

    // Wheel
    public float WheelDeltaX, WheelDeltaY;
    public bool WheelHasPreciseScrollingDeltas;
    public Input.MouseWheelPhase WheelPhase, WheelMomentumPhase;

    // Key
    public int KeyCode;
    public int ScanCode;
    public int IsRepeat;

    // Character
    public uint Codepoint;

    // IME composition (native text is copied before the callback returns)
    public string? CompositionText;
    public int CompositionCursor;

    // Wayland delete-surrounding lengths are UTF-8 byte counts.
    public int ImeDeleteBeforeUtf8ByteCount;
    public int ImeDeleteAfterUtf8ByteCount;
    public PlatformImeTextRequest? ImeTextRequest;
    public PlatformImeGeometryRequest? ImeGeometryRequest;

    // Pointer
    public uint PointerId;
    public float PointerX, PointerY;
    public float Pressure;
    public float TiltX, TiltY, Twist;
    public int PointerType;
    public uint PointerFlags;
    public int PointerToolType;
    public uint PointerButtons;
    public long PointerTimestampMillis;

    // State
    public int NewState;

    // Safe area (physical pixels)
    public float SafeAreaTop, SafeAreaBottom, SafeAreaLeft, SafeAreaRight;

    // Keyboard
    public int KeyboardVisible;
    public int KeyboardHeightPx;

    // Orientation (0=portrait, 1=landscape, 2=portrait-reverse, 3=landscape-reverse)
    public int Orientation;

    // Drag/drop. Native callback-owned buffers are copied before dispatch.
    public ulong DragSessionId;
    public uint DragKeyStates;
    public uint DragAllowedEffects;
    public string[]? DragMimeTypes;
    public string? DragDataMimeType;
    public byte[]? DragData;
}

internal enum PlatformEventType
{
    None = 0,

    CloseRequested = 1,
    Destroyed = 2,
    Resize = 3,
    Move = 4,
    DpiChanged = 5,
    Paint = 6,
    Activate = 7,
    Deactivate = 8,
    StateChanged = 9,
    MonitorsChanged = 10,
    PointingDevicesChanged = 11,
    ScrollBarSettingsChanged = 12,

    FocusGained = 20,
    FocusLost = 21,

    MouseMove = 30,
    MouseDown = 31,
    MouseUp = 32,
    MouseWheel = 33,
    MouseEnter = 34,
    MouseLeave = 35,

    KeyDown = 40,
    KeyUp = 41,
    CharInput = 42,
    CompositionStart = 43,
    CompositionUpdate = 44,
    CompositionEnd = 45,
    DeleteSurroundingText = 46,
    ImeTextRequest = 47,
    ImeGeometryRequest = 48,

    PointerDown = 50,
    PointerUp = 51,
    PointerMove = 52,
    PointerCancel = 53,

    AppPause = 60,
    AppResume = 61,
    AppDestroy = 62,
    LowMemory = 63,
    SafeAreaChanged = 64,
    KeyboardChanged = 65,
    OrientationChanged = 66,

    DispatcherWake = 70,

    DragEnter = 80,
    DragOver = 81,
    DragLeave = 82,
    Drop = 83,
    DragFinished = 84,

    Quit = 99,
}
