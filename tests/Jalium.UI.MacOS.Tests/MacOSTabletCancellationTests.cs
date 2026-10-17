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

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void PluginCancellationEndsTheRangeWithoutRoutingOrCommitting(bool custom, bool threaded, int phase)
    {
        using var host = new Host(custom, threaded);
        var plugIn = new CallbackPlugIn();
        host.Ink.GetStylusPlugIns(createIfMissing: true)!.Add(plugIn);
        int rangeOut = 0, pointerCancel = 0, stylus = 0;
        host.Ink.AddHandler(UIElement.StylusOutOfRangeEvent, new StylusEventHandler((_, _) => rangeOut++), true);
        host.Ink.AddHandler(UIElement.PointerCancelEvent, new RoutedEventHandler((_, _) => pointerCancel++));
        foreach (var routedEvent in new[] { UIElement.PreviewStylusDownEvent, UIElement.PreviewStylusMoveEvent,
            UIElement.PreviewStylusUpEvent, UIElement.StylusDownEvent, UIElement.StylusMoveEvent, UIElement.StylusUpEvent })
            host.Ink.AddHandler(routedEvent, new StylusEventHandler((_, _) => stylus++), true);
        if (phase > 0) host.Send(true, inverted: false);
        if (phase == 2) host.Send(true, inverted: false, move: true);
        int before = stylus;
        plugIn.Input = input => input.Cancel();
        if (phase == 0) host.Send(true, inverted: false);
        else if (phase == 1) host.Send(true, inverted: false, move: true);
        else host.Up();
        Assert.Equal(1, rangeOut);
        Assert.Equal(1, pointerCancel);
        Assert.Equal(before, stylus);
        Assert.False(host.Preview.HasActiveSessions);
        Assert.Null(host.Renderer.CurrentPreviewStroke);
        Assert.Null(Tablet.CurrentStylusDevice);
        Assert.Empty(host.Ink.Strokes);
        plugIn.Input = null;
        host.Send(true, inverted: false);
        host.Send(true, inverted: false, move: true);
        host.Up();
        Assert.Single(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CancellationInsideBoundaryOrInputCannotStartLaterPreviews(bool custom, bool threaded, bool boundary)
    {
        using var host = new Host(custom, threaded);
        if (boundary) host.PlugIn.Entering = host.Cancel;
        else host.Ink.GetStylusPlugIns(createIfMissing: true)!.Add(new CallbackPlugIn { Input = _ => host.Cancel() });
        host.Send(true, inverted: false);
        Assert.False(host.Preview.HasActiveSessions);
        Assert.Null(host.Renderer.CurrentPreviewStroke);
        Assert.Null(Tablet.CurrentStylusDevice);
        Assert.Empty(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InputFailureRetiresPreviewAndDeviceAsCancellation(bool custom, bool threaded)
    {
        using var host = new Host(custom, threaded);
        host.Ink.GetStylusPlugIns(createIfMissing: true)!.Add(new CallbackPlugIn
        {
            Input = _ => throw new InvalidOperationException("input observer")
        });
        host.Send(true, inverted: false);
        Assert.False(host.Preview.HasActiveSessions);
        Assert.Null(host.Renderer.CurrentPreviewStroke);
        Assert.Null(Tablet.CurrentStylusDevice);
        Assert.Empty(host.Ink.Strokes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OlderCanceledInputCannotRemoveAReentrantNewSession(bool custom, bool threaded)
    {
        using var host = new Host(custom, threaded);
        int ranges = 0;
        host.Ink.AddHandler(UIElement.StylusInRangeEvent, new StylusEventHandler((_, _) => ranges++), true);
        var plugIn = new CallbackPlugIn();
        host.Ink.GetStylusPlugIns(createIfMissing: true)!.Add(plugIn);
        plugIn.Input = input =>
        {
            plugIn.Input = null;
            host.Send(true, inverted: false);
            host.Send(true, inverted: false, move: true);
            input.Cancel();
        };
        host.Send(true, inverted: false);
        Assert.True(host.Preview.HasActiveSessions);
        Assert.True(Tablet.CurrentStylusDevice?.InRange);
        Assert.Equal(1, ranges);
        host.Up();
        Assert.Equal(1, ranges);
        Assert.Single(host.Ink.Strokes);
    }

    private sealed class CallbackPlugIn : StylusPlugIn
    {
        internal Action<RawStylusInput>? Input;
        protected override void OnStylusDown(RawStylusInput input) => Input?.Invoke(input);
        protected override void OnStylusMove(RawStylusInput input) => Input?.Invoke(input);
        protected override void OnStylusUp(RawStylusInput input) => Input?.Invoke(input);
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
        internal Action? Entering;
        protected override void OnStylusEnter(RawStylusInput input, bool confirmed)
        {
            Entered++;
            Entering?.Invoke();
        }
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
        internal Host(bool custom, bool threaded = false)
        {
            Window = new Window { Width = 240, Height = 180, Content = Ink,
                TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Window, [Visibility.Visible]);
            Window.Measure(new Size(240, 180));
            Window.Arrange(new Rect(0, 0, 240, 180));
            _input = new WindowInputDispatcher(Window);
            ((IInputDispatcherHost)Window).RealTimeStylus.UseRealTimeThread = threaded;
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
