using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Callback interface that <see cref="WindowInputDispatcher"/> uses to interact
/// with the host <see cref="Window"/>. Keeps the dispatcher decoupled from
/// Window internals and enables unit testing with mocks.
/// </summary>
internal interface IInputDispatcherHost
{
    // ── Identity ──

    /// <summary>The host window itself, used as a fallback event target.</summary>
    Window Self { get; }

    /// <summary>Native window handle (HWND on Win32, or platform handle).</summary>
    nint Handle { get; }

    // ── Hit Testing ──

    UIElement? HitTestElement(Point windowPosition, string tag);
    HitTestResult? HitIgnoringOverlay(Point windowPosition);

    // ── Overlay / Popup ──

    OverlayLayer OverlayLayer { get; }
    IReadOnlyList<Popup> ActiveExternalPopups { get; }
    ContentDialog? ActiveContentDialog { get; }
    IReadOnlyList<ContentDialog> ActiveInPlaceDialogs { get; }

    // ── Title Bar ──

    bool IsTitleBarVisible();
    TitleBarButton? GetTitleBarButtonAtPoint(Point point, double windowWidth = 0);
    WindowTitleBarStyle TitleBarStyle { get; }
    TitleBar? TitleBar { get; }

    // ── Focus / Keyboard Targets ──

    UIElement GetKeyboardEventTarget();
    UIElement? GetTextInputTarget();
    ContentDialog? FindContainingInPlaceDialog();
    Button? FindButton(UIElement root, Func<Button, bool> predicate);

    // ── DevTools ──

    bool CanOpenDevTools { get; }
    void ToggleDevTools();
    void OpenDevTools();
    void ActivateDevToolsPicker();

    // ── Debug HUD ──

    /// <summary>
    /// When <see langword="false"/>, F3 is a no-op — the application has not
    /// opted in via <c>app.UseDebugHud()</c>. When <see langword="true"/>,
    /// F3 toggles <see cref="DebugHudEnabled"/> as usual.
    /// </summary>
    bool CanToggleDebugHud { get; }

    bool DebugHudEnabled { get; set; }
    Visibility DebugHudOverlayVisibility { set; }

    // ── Preview Hooks (virtual methods on Window) ──

    bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat);
    bool OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers);
    bool OnPreviewWindowMouseDown(MouseButton button, Point position, int clickCount);
    bool OnPreviewWindowMouseUp(MouseButton button, Point position);
    bool OnPreviewWindowMouseMove(Point position);
    bool OnPreviewWindowMouseWheel(int delta, Point position);

    // ── Window Actions ──

    void InvalidateWindow();
    void RequestFullInvalidation();
    void RequestTrackMouseLeave();

    // ── DPI ──

    double DpiScale { get; }

    // ── Cursor ──

    void SetPlatformCursor(int cursorType);

    // ── IME ──

    void UpdateInputMethodAssociation();

    // ── Window State ──

    bool IsPopupWindow(nint hwnd);
    bool IsVirtualKeyDown(int nVirtKey);
    void WakeRenderPipeline();

    // ── RealTimeStylus ──

    Jalium.UI.Input.StylusPlugIns.RealTimeStylus RealTimeStylus { get; }
}
