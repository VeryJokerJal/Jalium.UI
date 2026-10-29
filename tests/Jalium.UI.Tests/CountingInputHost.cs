using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// 供 WindowInputDispatcher 相关测试共用的最小输入宿主：命中目标可控、命中次数可数，
/// 其余成员一律给中性实现。
/// </summary>
internal sealed class CountingInputHost : IInputDispatcherHost, IDisposable
{
    private readonly RealTimeStylus _realTimeStylus;

    public CountingInputHost()
    {
        Self = new Window();
        OverlayLayer = new OverlayLayer();
        _realTimeStylus = new RealTimeStylus(Self);
    }

    public UIElement? HitTarget { get; set; }
    public int HitTestCount { get; private set; }
    public nint NativeHandle { get; set; }
    public bool TitleBarVisible { get; set; }
    public int TrackMouseLeaveRequestCount { get; private set; }
    public bool TrackMouseLeaveDefaultResult { get; set; } = true;
    public Queue<bool> TrackMouseLeaveResults { get; } = [];
    public List<Popup> ExternalPopups { get; } = [];
    public Window Self { get; }
    public nint Handle => NativeHandle;
    public OverlayLayer OverlayLayer { get; }
    public IReadOnlyList<Popup> ActiveExternalPopups => ExternalPopups;
    public ContentDialog? ActiveContentDialog => null;
    public IReadOnlyList<ContentDialog> ActiveInPlaceDialogs => Array.Empty<ContentDialog>();
    public WindowTitleBarStyle TitleBarStyle => default;
    public TitleBar? TitleBar => null;
    public bool CanOpenDevTools => false;
    public bool CanToggleDebugHud => false;
    public bool DebugHudEnabled { get; set; }
    public Visibility DebugHudOverlayVisibility { set { } }
    public double DpiScale => 1;
    public RealTimeStylus RealTimeStylus => _realTimeStylus;

    public UIElement? HitTestElement(Point windowPosition, string tag)
    {
        HitTestCount++;
        return HitTarget;
    }

    public HitTestResult? HitIgnoringOverlay(Point windowPosition) => null;
    public bool IsTitleBarVisible() => TitleBarVisible;
    public TitleBarButton? GetTitleBarButtonAtPoint(Point point, double windowWidth = 0) => null;
    public UIElement GetKeyboardEventTarget() => Self;
    public UIElement? GetTextInputTarget() => null;
    public ContentDialog? FindContainingInPlaceDialog() => null;
    public Button? FindButton(UIElement root, Func<Button, bool> predicate) => null;
    public void ToggleDevTools() { }
    public void OpenDevTools() { }
    public void ActivateDevToolsPicker() { }
    public bool OnPreviewWindowKeyDown(Key key, ModifierKeys modifiers, bool isRepeat) => false;
    public bool OnPreviewWindowKeyUp(Key key, ModifierKeys modifiers) => false;
    public bool OnPreviewWindowMouseDown(MouseButton button, Point position, int clickCount) => false;
    public bool OnPreviewWindowMouseUp(MouseButton button, Point position) => false;
    public bool OnPreviewWindowMouseMove(Point position) => false;
    public bool OnPreviewWindowMouseWheel(int delta, Point position) => false;
    public void InvalidateWindow() { }
    public void RequestFullInvalidation() { }
    public bool RequestTrackMouseLeave()
    {
        TrackMouseLeaveRequestCount++;
        return TrackMouseLeaveResults.Count > 0
            ? TrackMouseLeaveResults.Dequeue()
            : TrackMouseLeaveDefaultResult;
    }
    public void SetPlatformCursor(int cursorType) { }
    public void UpdateInputMethodAssociation() { }
    public bool IsPopupWindow(nint hwnd) => false;
    public bool IsVirtualKeyDown(int nVirtKey) => false;
    public void WakeRenderPipeline() { }

    public void Dispose()
    {
        UIElement.ForceReleaseMouseCapture();
        _realTimeStylus.Dispose();
    }
}
