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
    void Hide();
    void Close();

    void SetTitle(string title);
    void Resize(int width, int height);
    void Move(int x, int y);

    int GetWidth();
    int GetHeight();

    void SetState(WindowState state);
    WindowState GetState();

    void Invalidate();

    float GetDpiScale();
    int GetMonitorRefreshRate();

    void SetCursor(int cursorShape);

    /// <summary>
    /// Sets the event handler callback. The platform window will invoke this
    /// callback for all platform events (resize, mouse, keyboard, etc.).
    /// </summary>
    void SetEventHandler(Action<PlatformEvent>? handler);
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

    // Wheel
    public float WheelDeltaX, WheelDeltaY;

    // Key
    public int KeyCode;
    public int ScanCode;
    public int IsRepeat;

    // Character
    public uint Codepoint;

    // Pointer
    public uint PointerId;
    public float PointerX, PointerY;
    public float Pressure;
    public float TiltX, TiltY, Twist;
    public int PointerType;

    // State
    public int NewState;

    // Safe area (physical pixels)
    public float SafeAreaTop, SafeAreaBottom, SafeAreaLeft, SafeAreaRight;

    // Keyboard
    public int KeyboardVisible;
    public int KeyboardHeightPx;

    // Orientation (0=portrait, 1=landscape, 2=portrait-reverse, 3=landscape-reverse)
    public int Orientation;
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
    Quit = 99,
}
