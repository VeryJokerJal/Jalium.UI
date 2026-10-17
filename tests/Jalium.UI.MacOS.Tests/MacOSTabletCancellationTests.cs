using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTabletCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CancellationCompletesRangeAndPluginBoundaries(bool customTitleBar, bool contact)
    {
        using var host = new Host(customTitleBar);
        int rangeOut = 0, canceled = 0, up = 0;
        host.Ink.AddHandler(UIElement.StylusOutOfRangeEvent,
            new StylusEventHandler((_, e) => { rangeOut++; Assert.False(e.StylusDevice.InRange); }), true);
        host.Ink.AddHandler(UIElement.PointerCancelEvent, new RoutedEventHandler((_, _) => canceled++));
        host.Ink.AddHandler(UIElement.StylusUpEvent, new StylusEventHandler((_, _) => up++), true);
        host.Send(contact);
        Assert.Equal(1, host.PlugIn.Entered);
        host.Cancel();
        Assert.Equal(1, rangeOut);
        Assert.Equal(1, host.PlugIn.Left);
        Assert.False(host.PlugIn.LastLeaveInRange);
        Assert.Equal(1, canceled);
        Assert.Equal(0, up);
        host.Cancel();
        Assert.Equal(1, rangeOut);
        Assert.Equal(1, host.PlugIn.Left);
        Assert.Equal(1, canceled);
        host.Send(false);
        Assert.Equal(2, host.PlugIn.Entered);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CanceledInvertedPenRestoresOrdinaryEditingMode(bool customTitleBar, bool contact)
    {
        using var host = new Host(customTitleBar);
        host.Send(contact);
        Assert.Equal(InkCanvasEditingMode.EraseByStroke, host.Ink.ActiveEditingMode);
        host.Cancel();
        Assert.Equal(InkCanvasEditingMode.Ink, host.Ink.ActiveEditingMode);
        Assert.Null(Tablet.CurrentStylusDevice);
        Assert.Empty(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CancellationDiscardsWetInkEvenAfterTheSurfaceIsDisabled(bool customTitleBar, bool disabled)
    {
        using var host = new Host(customTitleBar);
        host.Send(true, inverted: false);
        host.Send(true, inverted: false, move: true);
        Assert.True(host.Preview.HasActiveSessions);
        Assert.NotNull(host.Renderer.CurrentPreviewStroke);
        host.Ink.IsEnabled = !disabled;
        host.Cancel();
        Assert.False(host.Preview.HasActiveSessions);
        Assert.Null(host.Renderer.CurrentPreviewStroke);
        Assert.Empty(host.Ink.Strokes);
        host.Ink.IsEnabled = true;
        host.Send(true, inverted: false);
        host.Send(true, inverted: false, move: true);
        host.Up();
        Assert.Single(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeaveReentryPreservesTheNewContact(bool customTitleBar)
    {
        using var host = new Host(customTitleBar);
        host.Send(true, inverted: false);
        host.PlugIn.Leaving = () =>
        {
            host.PlugIn.Leaving = null;
            host.Send(true, inverted: false);
            host.Send(true, inverted: false, move: true);
        };
        host.Cancel();
        Assert.True(host.Preview.HasActiveSessions);
        Assert.NotNull(host.Renderer.CurrentPreviewStroke);
        Assert.True(Tablet.CurrentStylusDevice?.InRange);
        host.Up();
        Assert.Single(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneFailingLeaveHandlerDoesNotKeepOtherPluginPreviews(bool customTitleBar)
    {
        using var host = new Host(customTitleBar);
        host.Ink.GetStylusPlugIns(createIfMissing: true)!.Insert(0, new ThrowingPlugIn());
        host.Send(true, inverted: false);
        host.Send(true, inverted: false, move: true);
        Assert.Throws<InvalidOperationException>(() => host.Cancel());
        Assert.False(host.Preview.HasActiveSessions);
        Assert.Null(host.Renderer.CurrentPreviewStroke);
        Assert.Null(Tablet.CurrentStylusDevice);
        Assert.Empty(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeExitReentryOnAnotherSurfaceStillClearsTheOldPreview(bool customTitleBar)
    {
        using var host = new Host(customTitleBar);
        host.Send(true, inverted: false);
        host.Send(true, inverted: false, move: true);
        var next = new InkCanvas();
        host.Ink.AddHandler(UIElement.StylusOutOfRangeEvent, new StylusEventHandler((_, _) =>
        {
            host.Window.Content = next;
            host.Window.Measure(new Size(240, 180));
            host.Window.Arrange(new Rect(0, 0, 240, 180));
            host.Send(true, inverted: false);
            host.Send(true, inverted: false, move: true);
        }), true);
        host.Cancel();
        Assert.False(host.Preview.HasActiveSessions);
        Assert.Null(host.Renderer.CurrentPreviewStroke);
        host.Up();
        Assert.Single(next.Strokes);
        Assert.Empty(host.Ink.Strokes);
    }

    private sealed class ThrowingPlugIn : StylusPlugIn
    {
        protected override void OnStylusLeave(RawStylusInput input, bool confirmed)
        {
            if (input.IsCanceled) throw new InvalidOperationException("cancellation observer");
        }
    }

    private sealed class RecordingPlugIn : StylusPlugIn
    {
        internal int Entered, Left;
        internal bool LastLeaveInRange = true;
        internal Action? Leaving;
        protected override void OnStylusEnter(RawStylusInput input, bool confirmed) => Entered++;
        protected override void OnStylusLeave(RawStylusInput input, bool confirmed)
        {
            Left++;
            LastLeaveInRange = input.InRange;
            Leaving?.Invoke();
        }
    }

    private sealed class Host : IDisposable
    {
        internal readonly InkCanvas Ink = new() { EditingMode = InkCanvasEditingMode.Ink,
            EditingModeInverted = InkCanvasEditingMode.EraseByStroke };
        internal readonly Window Window;
        internal readonly RecordingPlugIn PlugIn = new();
        internal RealTimeInkPreviewStylusPlugIn Preview => (RealTimeInkPreviewStylusPlugIn)typeof(InkCanvas)
            .GetField("_realTimePreviewPlugIn", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Ink)!;
        internal DynamicRenderer Renderer => (DynamicRenderer)typeof(InkCanvas)
            .GetField("_dynamicRenderer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Ink)!;
        private readonly WindowInputDispatcher _input;
        internal Host(bool custom)
        {
            Window = new Window { Width = 240, Height = 180, Content = Ink,
                TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Window, [Visibility.Visible]);
            Window.Measure(new Size(240, 180));
            Window.Arrange(new Rect(0, 0, 240, 180));
            _input = new WindowInputDispatcher(Window);
            ((IInputDispatcherHost)Window).RealTimeStylus.UseRealTimeThread = false;
            Ink.GetStylusPlugIns(createIfMissing: true)!.Add(PlugIn);
        }
        private PointerInputData Data(bool contact, bool cancel = false, bool inverted = true, bool move = false, bool up = false) =>
            Window.BuildPointerInputData(new MacOSTabletSnapshot
            {
                Size = 96, Type = cancel ? 53 : up ? 51 : contact && !move ? 50 : 52, PointerId = 73,
                PointerType = 2, X = move ? 100 : 40, Y = 80,
                Flags = (cancel ? 0u : 1u | (contact ? 2u : 0u)) | (inverted ? 24u : 0u),
                Pressure = contact ? .7f : 0, Buttons = contact ? 1u : 0, TimestampMillis = 100
            }.ToPointerEvent());
        internal void Send(bool contact, bool inverted = true, bool move = false) =>
            _input.HandleMacOSTabletInput(Data(contact, inverted: inverted, move: move),
                contact && !move, false, 100, () => true, null);
        internal void Up() =>
            _input.HandleMacOSTabletInput(Data(false, inverted: false, up: true), false, true, 130, () => true, null);
        internal void Cancel() => _input.HandleMacOSTabletCancel(Data(false, true), 120);
        public void Dispose()
        {
            _input.HandleMacOSTabletCancel(Data(false, true), 140);
            Window.Close();
        }
    }
}
