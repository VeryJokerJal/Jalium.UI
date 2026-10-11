using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupWheelButtonTests
{
    [Theory]
    [InlineData(0, MouseWheelPhase.None, MouseWheelPhase.None)]
    [InlineData(0, MouseWheelPhase.Ended, MouseWheelPhase.None)]
    [InlineData(0, MouseWheelPhase.None, MouseWheelPhase.Ended)]
    [InlineData(0.125, MouseWheelPhase.Changed, MouseWheelPhase.None)]
    [InlineData(-0.125, MouseWheelPhase.Changed, MouseWheelPhase.None)]
    [InlineData(0.125, MouseWheelPhase.Ended, MouseWheelPhase.None)]
    [InlineData(-0.125, MouseWheelPhase.None, MouseWheelPhase.Ended)]
    public void PopupRoutesPhaseOnlyAndSubUnitPacketsWithoutLosingLifecycle(
        double vertical, MouseWheelPhase phase, MouseWheelPhase momentum)
    {
        using var fixture = new Fixture();
        var received = new List<MouseWheelEventArgs>();
        fixture.Target.PreviewMouseWheel += (_, e) => received.Add(e);
        fixture.Target.MouseWheel += (_, e) => received.Add(e);
        fixture.Send(new PlatformEvent
        {
            Type = PlatformEventType.MouseWheel, MouseX = 100, MouseY = 100,
            HasMouseButtonStates = true, WheelDeltaY = (float)(vertical / 120),
            WheelHasPreciseScrollingDeltas = true, WheelPhase = phase, WheelMomentumPhase = momentum,
        });
        Assert.Equal(2, received.Count);
        Assert.All(received, e =>
        {
            Assert.Equal(0, e.Delta);
            Assert.Equal(vertical, e.VerticalDelta, 6);
            Assert.Equal(phase, e.Phase);
            Assert.Equal(momentum, e.MomentumPhase);
            Assert.False(e.Handled);
        });
    }

    public static IEnumerable<object[]> Snapshots()
    {
        foreach (uint mask in new uint[] { 0, 1, 2, 4, 8, 16, 31, 0xffffffe0 })
            foreach (bool capture in new[] { false, true })
                yield return [mask, capture];
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void PlatformWheelSnapshotReachesPreviewBubblePointerAndGlobalState(uint mask, bool capture)
    {
        using var fixture = new Fixture();
        if (capture) Assert.True(fixture.Target.CaptureMouse());
        var seen = new List<string>();
        uint expected = mask & 31;
        fixture.Root.PreviewMouseWheel += (_, e) =>
        {
            if (e.Phase == MouseWheelPhase.Cancelled) return; // Popup teardown cancels the gesture.
            AssertButtons(expected, e);
            Assert.Equal(new Point(capture ? 400 : 50, 50), e.Position);
            Assert.Equal(ModifierKeys.Shift | ModifierKeys.Alt, e.KeyboardModifiers);
            seen.Add("preview");
        };
        fixture.Target.MouseWheel += (_, e) =>
        {
            AssertButtons(expected, e);
            Assert.Equal(15, e.HorizontalDelta);
            Assert.Equal(-30, e.VerticalDelta);
            Assert.Equal(capture, e.HasPreciseScrollingDeltas);
            Assert.Equal(MouseWheelPhase.Changed, e.Phase);
            Assert.Equal(MouseWheelPhase.Began, e.MomentumPhase);
            seen.Add("bubble");
        };
        fixture.Target.AddHandler(PointerEvents.PointerWheelChangedEvent, new PointerWheelChangedEventHandler((_, e) =>
        {
            var p = e.Pointer.Properties;
            Assert.Equal((expected & 1) != 0, p.IsLeftButtonPressed);
            Assert.Equal((expected & 2) != 0, p.IsRightButtonPressed);
            Assert.Equal((expected & 4) != 0, p.IsMiddleButtonPressed);
            Assert.Equal((expected & 8) != 0, p.IsXButton1Pressed);
            Assert.Equal((expected & 16) != 0, p.IsXButton2Pressed);
            Assert.Equal(-30, p.MouseWheelDelta);
            seen.Add("pointer");
        }));
        var focus = Keyboard.FocusedElement;
        fixture.Wheel(mask, true, capture ? 800 : 100, capture);
        Assert.Equal(["preview", "bubble", "pointer"], seen);
        AssertGlobal(expected);
        Assert.Same(focus, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData(0, 1u)]
    [InlineData(1, 2u)]
    [InlineData(2, 4u)]
    [InlineData(3, 8u)]
    [InlineData(4, 16u)]
    public void UnavailableSnapshotPreservesPreviouslyDeliveredButton(int button, uint expected)
    {
        using var fixture = new Fixture();
        fixture.Send(new PlatformEvent { Type = PlatformEventType.MouseDown, Button = button, MouseX = 100, MouseY = 100 });
        fixture.Target.MouseWheel += (_, e) => AssertButtons(expected, e);
        fixture.Wheel(31, false);
        AssertGlobal(expected);
    }

    [Fact]
    public void ReleasedSnapshotClearsStaleButtonsWithoutInventingMouseUp()
    {
        using var fixture = new Fixture();
        for (int button = 0; button < 5; button++)
            fixture.Send(new PlatformEvent { Type = PlatformEventType.MouseDown, Button = button, MouseX = 100, MouseY = 100 });
        int ups = 0;
        fixture.Target.MouseUp += (_, _) => ups++;
        fixture.Target.MouseWheel += (_, e) => AssertButtons(0, e);
        fixture.Wheel(0, true);
        AssertGlobal(0);
        Assert.Equal(0, ups);
    }

    [Fact]
    public void SnapshotUpdatesThePopupCacheForFollowingMoveAndLegacyWheel()
    {
        using var fixture = new Fixture();
        fixture.Wheel(31, true);
        int moves = 0, wheels = 0;
        fixture.Target.MouseMove += (_, e) => { AssertButtons(31, e); moves++; };
        fixture.Target.MouseWheel += (_, e) => { AssertButtons(31, e); wheels++; };
        fixture.Send(new PlatformEvent { Type = PlatformEventType.MouseMove, MouseX = 100, MouseY = 100 });
        fixture.Wheel(0, false);
        Assert.Equal(1, moves);
        Assert.Equal(1, wheels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewHandledOrCanceledKeepsSnapshotAndSuppressesPointerPromotion(bool cancel)
    {
        using var fixture = new Fixture();
        int bubbles = 0, pointers = 0, cancellations = 0;
        fixture.Root.PreviewMouseWheel += (_, e) =>
        {
            if (e.Phase == MouseWheelPhase.Cancelled) return;
            AssertButtons(18, e); e.Handled = true; e.Cancel = cancel;
        };
        fixture.Target.MouseWheel += (_, _) => bubbles++;
        fixture.Target.AddHandler(PointerEvents.PointerWheelChangedEvent,
            new PointerWheelChangedEventHandler((_, _) => pointers++));
        fixture.Target.PointerCancel += (_, _) => cancellations++;
        fixture.Wheel(18, true);
        AssertGlobal(18);
        Assert.Equal(0, bubbles);
        Assert.Equal(0, pointers);
        Assert.Equal(cancel ? 1 : 0, cancellations);
    }

    [Fact]
    public void DisposedPopupIgnoresLateWheelAndRecreatedPopupStartsWithReleasedSnapshot()
    {
        using var first = new Fixture();
        first.Wheel(31, true);
        first.Dispose();
        int callbacks = 0;
        first.Target.MouseWheel += (_, _) => callbacks++;
        Mouse.UpdateState(default, null, new MouseButtonStates { Right = MouseButtonState.Pressed });
        first.Wheel(0, true);
        Assert.Equal(0, callbacks);
        AssertGlobal(2);
        using var second = new Fixture();
        second.Target.MouseWheel += (_, e) => AssertButtons(0, e);
        second.Wheel(0, true);
        AssertGlobal(0);
    }

    private static void AssertButtons(uint mask, MouseEventArgs e)
    {
        Assert.Equal(State(mask, 1), e.LeftButton);
        Assert.Equal(State(mask, 2), e.RightButton);
        Assert.Equal(State(mask, 4), e.MiddleButton);
        Assert.Equal(State(mask, 8), e.XButton1);
        Assert.Equal(State(mask, 16), e.XButton2);
        AssertGlobal(mask);
    }

    private static MouseButtonState State(uint mask, uint bit) =>
        (mask & bit) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released;

    private static void AssertGlobal(uint mask)
    {
        Assert.Equal(State(mask, 1), Mouse.LeftButton);
        Assert.Equal(State(mask, 2), Mouse.RightButton);
        Assert.Equal(State(mask, 4), Mouse.MiddleButton);
        Assert.Equal(State(mask, 8), Mouse.XButton1);
        Assert.Equal(State(mask, 16), Mouse.XButton2);
    }

    private sealed class Fixture : IDisposable
    {
        internal Border Target { get; } = new() { Background = Brushes.White };
        internal PopupRoot Root { get; }
        private readonly Window _owner;
        private readonly PopupWindow _popup;
        private bool _disposed;
        internal Fixture()
        {
            Mouse.Capture(null);
            Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            _owner = new DisplayedTestWindow { Width = 320, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native };
            typeof(Window).GetField("_dpiScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_owner, 2d);
            Root = new PopupRoot(new Popup(), Target, isLightDismiss: false);
            _popup = new PopupWindow(_owner, Root);
            // The callback checks for a live bridge. This placeholder owns no
            // native handle; real AppKit integration is checked in HostSmoke.
            typeof(PopupWindow).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_popup, RuntimeHelpers.GetUninitializedObject(typeof(NativePlatformWindow)));
            _popup.Measure(new Size(200, 120));
            _popup.Arrange(new Rect(0, 0, 200, 120));
            _popup.SetVisualBounds(new Rect(0, 0, 200, 120));
            Root.SetVisualBounds(new Rect(0, 0, 200, 120));
            Target.SetVisualBounds(new Rect(0, 0, 200, 120));
        }
        internal void Wheel(uint mask, bool available, float x = 100, bool precise = true) => Send(new PlatformEvent
        {
            Type = PlatformEventType.MouseWheel, MouseX = x, MouseY = 100,
            MouseButtons = mask, HasMouseButtonStates = available, Modifiers = 5,
            WheelDeltaX = .125f, WheelDeltaY = -.25f, WheelHasPreciseScrollingDeltas = precise,
            WheelPhase = MouseWheelPhase.Changed, WheelMomentumPhase = MouseWheelPhase.Began,
        });
        internal void Send(PlatformEvent evt) => typeof(PopupWindow)
            .GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_popup, [evt]);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Mouse.Capture(null);
            typeof(PopupWindow).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_popup, null);
            _popup.Dispose();
            _owner.Close();
            Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
        }
    }
}
