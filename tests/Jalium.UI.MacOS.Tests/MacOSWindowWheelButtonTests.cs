using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWindowWheelButtonTests
{
    [Theory]
    [InlineData(0x80000000u, 0u, true)]
    [InlineData(0x80000001u, 1u, true)]
    [InlineData(0x80000002u, 2u, true)]
    [InlineData(0x80000004u, 4u, true)]
    [InlineData(0x80000008u, 8u, true)]
    [InlineData(0x80000010u, 16u, true)]
    [InlineData(0xffffffffu, 31u, true)]
    [InlineData(0u, 0u, false)]
    public void NativeWheelDecoderRetainsSnapshotWithoutChangingEventLayout(uint snapshot, uint mask, bool available)
    {
        var nativeType = typeof(NativePlatformWindow).GetNestedType("NativePlatformEvent", BindingFlags.NonPublic)!;
        Assert.Equal(72, Marshal.SizeOf(nativeType));
        Assert.Equal(16, Marshal.OffsetOf(nativeType, "Data0").ToInt32());
        Assert.Equal(48, Marshal.OffsetOf(nativeType, "Data8").ToInt32());
        // Feed the real callback the complete public ABI, including its unused
        // union tail. No AppKit window or constructor is needed to decode it.
        var window = (NativePlatformWindow)RuntimeHelpers.GetUninitializedObject(typeof(NativePlatformWindow));
        PlatformEvent? received = null;
        typeof(NativePlatformWindow).GetField("_eventHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, (Action<PlatformEvent>)(evt => received = evt));
        var handle = GCHandle.Alloc(window);
        nint packet = Marshal.AllocHGlobal(72);
        try
        {
            Marshal.Copy(new byte[72], 0, packet, 72);
            Marshal.WriteInt32(packet, (int)PlatformEventType.MouseWheel);
            float[] values = [100, 120, .125f, -.25f];
            Marshal.Copy(values, 0, packet + 16, values.Length);
            Marshal.WriteInt32(packet, 32, 5); // Shift + Alt
            Marshal.WriteInt32(packet, 36, 1);
            Marshal.WriteInt32(packet, 40, (int)MouseWheelPhase.Changed);
            Marshal.WriteInt32(packet, 44, (int)MouseWheelPhase.Began);
            Marshal.WriteInt32(packet, 48, unchecked((int)snapshot));
            typeof(NativePlatformWindow).GetMethod("OnNativeEventStatic", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [packet, GCHandle.ToIntPtr(handle)]);
            var actual = Assert.IsType<PlatformEvent>(received);
            Assert.Equal(mask, actual.MouseButtons);
            Assert.Equal(available, actual.HasMouseButtonStates);
            Assert.Equal(100, actual.MouseX);
            Assert.Equal(120, actual.MouseY);
            Assert.Equal(.125f, actual.WheelDeltaX);
            Assert.Equal(-.25f, actual.WheelDeltaY);
            Assert.Equal(5, actual.Modifiers);
            Assert.True(actual.WheelHasPreciseScrollingDeltas);
            Assert.Equal(MouseWheelPhase.Changed, actual.WheelPhase);
            Assert.Equal(MouseWheelPhase.Began, actual.WheelMomentumPhase);
        }
        finally { Marshal.FreeHGlobal(packet); handle.Free(); }
    }

    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, false)]
    [InlineData(4u, false)]
    [InlineData(8u, false)]
    [InlineData(16u, false)]
    [InlineData(31u, false)]
    [InlineData(31u, true)]
    public void NativeWheelRetainsAllButtonsInPreviewBubblePointerAndGlobalState(uint mask, bool capture)
    {
        using var fixture = new WheelFixture();
        if (capture) Assert.True(fixture.Target.CaptureMouse());
        var seen = new List<string>();
        fixture.Window.PreviewMouseWheel += (_, e) => { AssertMask(mask, e); seen.Add("preview"); };
        fixture.Target.MouseWheel += (_, e) =>
        {
            AssertMask(mask, e);
            Assert.Equal(15, e.HorizontalDelta);
            Assert.Equal(-30, e.VerticalDelta);
            Assert.True(e.HasPreciseScrollingDeltas);
            Assert.Equal(MouseWheelPhase.Changed, e.Phase);
            Assert.Equal(MouseWheelPhase.None, e.MomentumPhase);
            seen.Add("bubble");
        };
        fixture.Target.AddHandler(PointerEvents.PointerWheelChangedEvent, new PointerWheelChangedEventHandler((_, e) =>
        {
            var p = e.Pointer.Properties;
            Assert.Equal((mask & 1) != 0, p.IsLeftButtonPressed);
            Assert.Equal((mask & 2) != 0, p.IsRightButtonPressed);
            Assert.Equal((mask & 4) != 0, p.IsMiddleButtonPressed);
            Assert.Equal((mask & 8) != 0, p.IsXButton1Pressed);
            Assert.Equal((mask & 16) != 0, p.IsXButton2Pressed);
            seen.Add("pointer");
        }));
        fixture.Send(mask, true, capture ? 400 : 50);
        Assert.Equal(["preview", "bubble", "pointer"], seen);
        AssertGlobalMask(mask);
    }

    [Fact]
    public void AvailableReleasedSnapshotClearsStalePressWithoutSynthesizingMouseUp()
    {
        using var fixture = new WheelFixture();
        Mouse.UpdateState(new Point(50, 50), fixture.Target, new MouseButtonStates
        { Left = MouseButtonState.Pressed, Right = MouseButtonState.Pressed, XButton2 = MouseButtonState.Pressed });
        int ups = 0;
        fixture.Target.MouseUp += (_, _) => ups++;
        fixture.Target.MouseWheel += (_, e) => AssertMask(0, e);
        fixture.Send(0, true);
        AssertGlobalMask(0);
        Assert.Equal(0, ups);
    }

    [Fact]
    public void LegacyWheelWithoutSnapshotPreservesLastKnownButtons()
    {
        using var fixture = new WheelFixture();
        Mouse.UpdateState(new Point(50, 50), fixture.Target, new MouseButtonStates
        { Middle = MouseButtonState.Pressed, XButton1 = MouseButtonState.Pressed });
        fixture.Target.MouseWheel += (_, e) => AssertMask(12, e);
        fixture.Send(31, false); // Ignore bits when the availability marker is absent.
        AssertGlobalMask(12);
    }

    private static void AssertMask(uint mask, MouseEventArgs e)
    {
        Assert.Equal(State(mask, 1), e.LeftButton);
        Assert.Equal(State(mask, 2), e.RightButton);
        Assert.Equal(State(mask, 4), e.MiddleButton);
        Assert.Equal(State(mask, 8), e.XButton1);
        Assert.Equal(State(mask, 16), e.XButton2);
        AssertGlobalMask(mask);
    }

    private static MouseButtonState State(uint mask, uint bit) =>
        (mask & bit) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released;

    private static void AssertGlobalMask(uint mask)
    {
        Assert.Equal(State(mask, 1), Mouse.LeftButton);
        Assert.Equal(State(mask, 2), Mouse.RightButton);
        Assert.Equal(State(mask, 4), Mouse.MiddleButton);
        Assert.Equal(State(mask, 8), Mouse.XButton1);
        Assert.Equal(State(mask, 16), Mouse.XButton2);
    }

    private sealed class WheelFixture : IDisposable
    {
        internal Border Target { get; } = new() { Background = Brushes.White };
        internal DisplayedTestWindow Window { get; }
        internal WheelFixture()
        {
            Mouse.Capture(null);
            Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Window = new DisplayedTestWindow
            { Width = 240, Height = 180, TitleBarStyle = WindowTitleBarStyle.Native, Content = Target };
            Window.Measure(new Size(240, 180));
            Window.Arrange(new Rect(0, 0, 240, 180));
        }
        internal void Send(uint mask, bool available, float x = 50) => typeof(Window)
            .GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Window, [new PlatformEvent
            {
                Type = PlatformEventType.MouseWheel, MouseX = x, MouseY = 50,
                MouseButtons = mask, HasMouseButtonStates = available,
                WheelDeltaX = .125f, WheelDeltaY = -.25f, WheelHasPreciseScrollingDeltas = true,
                WheelPhase = MouseWheelPhase.Changed,
            }]);
        public void Dispose()
        {
            Mouse.Capture(null);
            Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Window.Close();
        }
    }
}
