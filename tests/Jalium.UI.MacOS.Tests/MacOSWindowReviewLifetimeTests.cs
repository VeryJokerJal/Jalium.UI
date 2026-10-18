using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWindowReviewLifetimeTests
{
    public static IEnumerable<object[]> CoordinateCases()
    {
        // Physical client origins, independent source/destination DPI, source
        // local point, and the expected destination-client point.
        yield return [200d, 140d, 2d, 700d, 200d, 1d, 20d, 30d, 260d, 45d];
        yield return [100d, 70d, 1d, 1400d, 400d, 2d, 20d, 30d, 1340d, 390d];
        yield return [-1600d, -900d, 2d, -1200d, -500d, 1d, 40d, 60d, 220d, 230d];
        yield return [-100d, 300d, 1.25d, 500d, -200d, 1.5d, 10d, 20d, 492d, -376d];
        yield return [120d, 80d, 2d, 320d, 480d, 2d, 20d, 30d, 120d, 230d];
        yield return [120d, 80d, 1d, 320d, 480d, 1d, 20d, 30d, 220d, 430d];
    }

    [Theory, MemberData(nameof(CoordinateCases))]
    public void PopupPreviewHooksUseTheOwnersClientSpace(double ownerX, double ownerY, double ownerScale,
        double popupX, double popupY, double popupScale, double x, double y, double expectedX, double expectedY)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new ProbeWindow(); using var popup = new ProbeWindow();
        Position(owner, ownerX, ownerY, ownerScale); Position(popup, popupX, popupY, popupScale);
        typeof(Window).GetField("_macOSPopupParent", Private)!.SetValue(popup, owner);
        var host = (IInputDispatcherHost)popup;
        host.OnPreviewWindowMouseDown(MouseButton.Right, new(x, y), 1);
        host.OnPreviewWindowMouseUp(MouseButton.Right, new(x, y));
        host.OnPreviewWindowMouseMove(new(x, y)); host.OnPreviewWindowMouseWheel(120, new(x, y));
        Assert.Equal(4, owner.Points.Count);
        foreach (var point in owner.Points) { Assert.Equal(expectedX, point.X, 9); Assert.Equal(expectedY, point.Y, 9); }
    }

    public static IEnumerable<object[]> TransformCases() => CoordinateCases().Select(row => row.Take(6).ToArray());

    [Theory, MemberData(nameof(TransformCases))]
    public void PopupTextGeometryMapsTheWholeTransformIntoTheOwner(double ownerX, double ownerY, double ownerScale,
        double popupX, double popupY, double popupScale)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var owner = new ProbeWindow(); using var popup = new ProbeWindow();
        Position(owner, ownerX, ownerY, ownerScale); Position(popup, popupX, popupY, popupScale);
        var target = new Control { Width = 100, Height = 60,
            RenderTransform = new MatrixTransform(new Matrix(1.2, .3, -.2, .8, 4, 5)) };
        var panel = new Canvas(); panel.Children.Add(target); Canvas.SetLeft(target, 12); Canvas.SetTop(target, 10);
        popup.Content = panel; popup.Measure(new Size(300, 200)); popup.Arrange(new Rect(0, 0, 300, 200));
        Matrix local = target.GetRenderMatrix();
        Matrix converted = (Matrix)typeof(Window).GetMethod("GetMacOSInputTransform", Private)!.Invoke(owner, [target])!;
        foreach (var sample in new[] { new Point(0, 0), new Point(10, 20), new Point(70, 40) })
        {
            Point source = local.Transform(sample), destination = converted.Transform(sample);
            Assert.Equal((popupX + source.X * popupScale - ownerX) / ownerScale, destination.X, 8);
            Assert.Equal((popupY + source.Y * popupScale - ownerY) / ownerScale, destination.Y, 8);
        }
        Matrix unchanged = (Matrix)typeof(Window).GetMethod("GetMacOSInputTransform", Private)!.Invoke(popup, [target])!;
        Assert.Equal(local, unchanged);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void NestedWheelEndRetiresTheOuterPacket(bool canceled, bool momentum)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new InputHost(); int outerLater = 0, outerBubble = 0, pointer = 0, innerBubble = 0;
        h.Target.PreviewMouseWheel += (_, e) =>
        {
            if (e.Timestamp == 100) h.Wheel(101, canceled ? MouseWheelPhase.Cancelled : MouseWheelPhase.Ended, momentum);
        };
        h.Target.PreviewMouseWheel += (_, e) => { if (e.Timestamp == 100) outerLater++; };
        h.Target.MouseWheel += (_, e) => { if (e.Timestamp == 100) outerBubble++; else innerBubble++; };
        h.Target.AddHandler(PointerEvents.PointerWheelChangedEvent,
            new PointerWheelChangedEventHandler((_, _) => pointer++));
        h.Wheel(100, MouseWheelPhase.Changed, momentum);
        Assert.Equal(0, outerLater); Assert.Equal(0, outerBubble); Assert.Equal(1, innerBubble); Assert.Equal(1, pointer);
    }

    [Fact]
    public void AWindowHookCannotResumeTheOuterWheelAfterNestedInput()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new InputHost(); bool nested = false; int previews = 0;
        h.Window.WheelHook = () => { if (!nested) { nested = true; h.Wheel(101, MouseWheelPhase.Ended, false); } };
        h.Target.PreviewMouseWheel += (_, _) => previews++;
        h.Wheel(100, MouseWheelPhase.Changed, false); Assert.Equal(1, previews);
    }

    [Fact]
    public void OrdinaryWheelStillCompletesItsPreviewBubbleAndPointerStages()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new InputHost(); int previews = 0, bubbles = 0, pointers = 0;
        h.Target.PreviewMouseWheel += (_, _) => previews++; h.Target.MouseWheel += (_, _) => bubbles++;
        h.Target.AddHandler(PointerEvents.PointerWheelChangedEvent, new PointerWheelChangedEventHandler((_, _) => pointers++));
        h.Wheel(100, MouseWheelPhase.Changed, false); Assert.Equal((1, 1, 1), (previews, bubbles, pointers));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void AnEndedNestedWheelCannotExecuteTheOlderCommand(bool canceled, bool momentum)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new InputHost(); var command = new CountingCommand();
        h.Target.InputBindings.Add(new MouseBinding(command, new MouseGesture(MouseAction.WheelClick)));
        h.Target.PreviewMouseWheel += (_, e) =>
        {
            if (e.Timestamp == 100) h.Wheel(101, canceled ? MouseWheelPhase.Cancelled : MouseWheelPhase.Ended, momentum);
            else if (e.Timestamp == 101) e.Handled = true;
        };
        h.Wheel(100, MouseWheelPhase.Changed, momentum);
        Assert.Equal((0, 0), (command.Queries, command.Executions));
        h.Wheel(102, MouseWheelPhase.Began, momentum);
        Assert.Equal((1, 1), (command.Queries, command.Executions));
    }

    private sealed class CountingCommand : System.Windows.Input.ICommand
    {
        internal int Queries, Executions;
        public bool CanExecute(object? parameter) { Queries++; return true; }
        public void Execute(object? parameter) => Executions++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    public static IEnumerable<object[]> TabletCases()
    {
        for (int phase = 0; phase < 3; phase++)
            foreach (bool threaded in new[] { false, true })
                foreach (string mutation in new[] { "lifetime", "replacement", "cancel" })
                    foreach (bool bubble in new[] { false, true }) yield return [phase, threaded, mutation, bubble];
    }

    [Theory, MemberData(nameof(TabletCases))]
    public void RetiredTabletPacketsStopInsidePointerPromotion(int phase, bool threaded, string mutation, bool bubble)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new InputHost(threaded); bool alive = true; int later = 0, legacy = 0, replacement = 0;
        if (phase != 0) h.Tablet(0, 10, () => true);
        RoutedEvent routed = phase switch
        {
            0 => bubble ? UIElement.PointerDownEvent : UIElement.PreviewPointerDownEvent,
            1 => bubble ? UIElement.PointerMoveEvent : UIElement.PreviewPointerMoveEvent,
            _ => bubble ? UIElement.PointerUpEvent : UIElement.PreviewPointerUpEvent
        };
        void Change(int time)
        {
            if (time != 100) return;
            if (mutation == "lifetime") alive = false;
            else if (mutation == "replacement") h.Tablet(0, 101, () => true);
            else h.CancelTablet();
        }
        Delegate mutate = phase switch
        {
            0 => new PointerDownEventHandler((_, e) => Change(e.Timestamp)),
            1 => new PointerMoveEventHandler((_, e) => Change(e.Timestamp)),
            _ => new PointerUpEventHandler((_, e) => Change(e.Timestamp))
        };
        Delegate observe = phase switch
        {
            0 => new PointerDownEventHandler((_, e) => { if (e.Timestamp == 100) later++; }),
            1 => new PointerMoveEventHandler((_, e) => { if (e.Timestamp == 100) later++; }),
            _ => new PointerUpEventHandler((_, e) => { if (e.Timestamp == 100) later++; })
        };
        h.Target.AddHandler(routed, mutate); h.Target.AddHandler(routed, observe);
        h.Target.PointerPressed += (_, e) => { if (((PointerPressedEventArgs)e).Timestamp == 100) legacy++; else if (((PointerPressedEventArgs)e).Timestamp == 101) replacement++; };
        h.Target.PointerMoved += (_, e) => { if (((PointerMovedEventArgs)e).Timestamp == 100) legacy++; };
        h.Target.PointerReleased += (_, e) => { if (((PointerReleasedEventArgs)e).Timestamp == 100) legacy++; };
        h.Tablet(phase, 100, () => alive);
        Assert.Equal(0, later); Assert.Equal(0, legacy);
        if (mutation == "replacement") Assert.Equal(1, replacement);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OrdinaryTabletPacketsStillReachLegacyPointerEvents(int phase)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new InputHost(); if (phase != 0) h.Tablet(0, 10, () => true);
        int delivered = 0;
        h.Target.PointerPressed += (_, e) => { if (((PointerPressedEventArgs)e).Timestamp == 100) delivered++; };
        h.Target.PointerMoved += (_, e) => { if (((PointerMovedEventArgs)e).Timestamp == 100) delivered++; };
        h.Target.PointerReleased += (_, e) => { if (((PointerReleasedEventArgs)e).Timestamp == 100) delivered++; };
        h.Tablet(phase, 100, () => true); Assert.Equal(1, delivered);
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Position(Window window, double physicalX, double physicalY, double scale)
    {
        typeof(Window).GetField("_dpiScale", Private)!.SetValue(window, scale);
        window.Left = physicalX / scale; window.Top = physicalY / scale;
    }

    private sealed class ProbeWindow : Window, IDisposable
    {
        internal readonly List<Point> Points = [];
        internal Action? WheelHook;
        protected override bool OnPreviewWindowMouseDown(MouseButton button, Point point, int clicks) { Points.Add(point); return false; }
        protected override bool OnPreviewWindowMouseUp(MouseButton button, Point point) { Points.Add(point); return false; }
        protected override bool OnPreviewWindowMouseMove(Point point) { Points.Add(point); return false; }
        protected override bool OnPreviewWindowMouseWheel(int delta, Point point) { Points.Add(point); WheelHook?.Invoke(); return false; }
        public void Dispose() => Close();
    }

    private sealed class InputHost : IDisposable
    {
        internal readonly ProbeWindow Window = new() { Width = 300, Height = 200, TitleBarStyle = WindowTitleBarStyle.Native };
        internal readonly Control Target = new() { Width = 300, Height = 200, Background = Brushes.White };
        private readonly WindowInputDispatcher _input;
        internal InputHost(bool threaded = false)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus(); Mouse.Capture(null);
            Window.Content = Target;
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!.Invoke(Window, [Visibility.Visible]);
            Window.Measure(new Size(300, 200)); Window.Arrange(new Rect(0, 0, 300, 200));
            _input = new WindowInputDispatcher(Window);
            ((IInputDispatcherHost)Window).RealTimeStylus.UseRealTimeThread = threaded;
        }
        internal void Wheel(int time, MouseWheelPhase phase, bool momentum) =>
            _input.HandleMouseWheel(new Point(40, 50), 0, time == 101 ? 0 : 11, true,
                MouseButtonStates.AllReleased, ModifierKeys.None, time,
                momentum ? MouseWheelPhase.None : phase, momentum ? phase : MouseWheelPhase.None);
        private PointerInputData Data(int phase, int time) => Window.BuildPointerInputData(new MacOSTabletSnapshot
        {
            Size = 96, Type = phase == 0 ? 50 : phase == 1 ? 52 : 51, PointerType = 2, PointerId = 917,
            X = 40, Y = 50, Flags = phase == 2 ? 1u : 3u, Pressure = phase == 2 ? 0 : .6f,
            Buttons = phase == 2 ? 0u : 1u, TimestampMillis = time
        }.ToPointerEvent());
        internal void Tablet(int phase, int time, Func<bool> current) =>
            _input.HandleMacOSTabletInput(Data(phase, time), phase == 0, phase == 2, time, current, null);
        internal void CancelTablet() => _input.HandleMacOSTabletCancel(Data(2, 120), 120);
        public void Dispose() { CancelTablet(); Mouse.Capture(null); Keyboard.ClearFocus(); Window.Close(); }
    }
}
