using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class WindowInputDispatcherThumbCaptureTests
{
    [Fact]
    public void CapturedThumb_MoveAndRelease_BypassHitTestAndReleaseImmediately()
    {
        using var host = new CountingInputHost();
        var thumb = new Thumb { Width = 12, Height = 80 };
        host.HitTarget = thumb;
        var dispatcher = new WindowInputDispatcher(host);

        var pressed = MouseButtonStates.AllReleased with { Left = MouseButtonState.Pressed };
        dispatcher.HandleMouseDown(
            MouseButton.Left,
            new Point(6, 10),
            pressed,
            ModifierKeys.None,
            clickCount: 1,
            timestamp: 1);

        Assert.True(thumb.IsMouseCaptured);
        var hitTestsAfterPress = host.HitTestCount;

        dispatcher.HandleMouseMove(
            new Point(6, 60),
            pressed,
            ModifierKeys.None,
            timestamp: 2);
        dispatcher.HandleMouseUp(
            MouseButton.Left,
            new Point(6, 60),
            MouseButtonStates.AllReleased,
            ModifierKeys.None,
            timestamp: 3);

        Assert.Equal(hitTestsAfterPress, host.HitTestCount);
        Assert.False(thumb.IsMouseCaptured);
    }

    private sealed class CountingInputHost : IInputDispatcherHost, IDisposable
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
        public Window Self { get; }
        public nint Handle => nint.Zero;
        public OverlayLayer OverlayLayer { get; }
        public IReadOnlyList<Popup> ActiveExternalPopups => Array.Empty<Popup>();
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
        public bool IsTitleBarVisible() => false;
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
        public void RequestTrackMouseLeave() { }
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
}
