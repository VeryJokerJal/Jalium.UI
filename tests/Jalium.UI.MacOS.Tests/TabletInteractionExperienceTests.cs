using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TabletInteractionExperienceTests
{
    [Theory]
    [InlineData(MouseWheelPhase.Began)]
    [InlineData(MouseWheelPhase.MayBegin)]
    [InlineData(MouseWheelPhase.Cancelled)]
    public void ZeroDistanceTrackpadBoundaryStopsPendingWheelAnimation(MouseWheelPhase phase)
    {
        var viewer = Viewer();
        viewer.RaiseEvent(Wheel(-120, precise: false));
        Assert.True(Field<bool>(viewer, "_isSmoothScrolling"));
        double offset = viewer.VerticalOffset;
        // A descendant may handle the bubble, so interruption must occur in preview.
        var boundary = Wheel(0, phase: phase);
        boundary.RoutedEvent = UIElement.PreviewMouseWheelEvent;
        viewer.RaiseEvent(boundary);
        Assert.False(Field<bool>(viewer, "_isSmoothScrolling"));
        Assert.Equal(offset, viewer.VerticalOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledTrackpadPacketCannotMoveContent(bool momentum)
    {
        var viewer = Viewer();
        var input = Wheel(-50, phase: momentum ? MouseWheelPhase.None : MouseWheelPhase.Cancelled,
            momentum: momentum ? MouseWheelPhase.Cancelled : MouseWheelPhase.None);
        viewer.RaiseEvent(input);
        Assert.Equal(200, viewer.VerticalOffset);
        Assert.False(Field<bool>(viewer, "_isSmoothScrolling"));
    }

    [Theory]
    [InlineData(MouseWheelPhase.Began)]
    [InlineData(MouseWheelPhase.Cancelled)]
    public void EditorZeroDistanceTrackpadBoundaryStopsPendingAnimation(MouseWheelPhase phase)
    {
        var editor = new EditControl
        {
            Text = string.Join('\n', Enumerable.Repeat(new string('x', 80), 60)), ShowMinimap = false
        };
        editor.Measure(new Size(240, 160)); editor.Arrange(new Rect(0, 0, 240, 160));
        editor.UpdateScrollBarsForTesting(new Size(240, 160));
        editor.RaiseEvent(Wheel(-120, precise: false));
        Assert.True(Field<bool>(editor, "_isScrollAnimating"));
        var view = Field<EditorView>(editor, "_view");
        double offset = view.VerticalOffset;
        editor.RaiseEvent(Wheel(0, phase: phase));
        Assert.False(Field<bool>(editor, "_isScrollAnimating"));
        Assert.Equal(offset, view.VerticalOffset);
    }

    [Fact]
    public void VirtualizedLeafReplacementKeepsMomentumInItsOriginalViewer()
    {
        var root = new Grid(); var oldBranch = new Grid(); var otherBranch = new Border();
        var oldLeaf = new Border(); oldBranch.Children.Add(oldLeaf);
        root.Children.Add(oldBranch); root.Children.Add(otherBranch);
        var routing = new MouseWheelGestureRouting();
        Assert.Same(oldLeaf, routing.ResolveTarget(oldLeaf, root, MouseWheelPhase.Began, MouseWheelPhase.None));
        oldBranch.Children.Remove(oldLeaf);
        Assert.Same(oldBranch, routing.ResolveTarget(otherBranch, root,
            MouseWheelPhase.None, MouseWheelPhase.Began));
    }

    [Fact]
    public void CancelledWheelSequenceCannotDonateMomentumToAnotherControl()
    {
        var root = new Grid(); var first = new Border(); var second = new Border();
        root.Children.Add(first); root.Children.Add(second);
        var routing = new MouseWheelGestureRouting();
        routing.ResolveTarget(first, root, MouseWheelPhase.Began, MouseWheelPhase.None);
        routing.Cancel();
        Assert.Null(routing.ResolveTarget(second, root, MouseWheelPhase.None, MouseWheelPhase.Changed));
        Assert.Same(second, routing.ResolveTarget(second, root, MouseWheelPhase.Began, MouseWheelPhase.None));
    }

    [Theory]
    [InlineData(PointerDeviceType.Touch)]
    [InlineData(PointerDeviceType.Pen)]
    public void HoldingStillBeforeReleaseDoesNotStartAStaleFling(PointerDeviceType type)
    {
        var viewer = Viewer(); viewer.PanningMode = PanningMode.VerticalOnly;
        Pan(viewer, type, 0, 100, down: true);
        Pan(viewer, type, 10, 70);
        // Pen pressure/hover drivers may keep reporting an unchanged position.
        Pan(viewer, type, 240, 70);
        Pan(viewer, type, 250, 70, up: true);
        Assert.Equal(230, viewer.VerticalOffset, 6);
        Assert.False(Field<bool>(viewer, "_isSmoothScrolling"));
    }

    [Theory]
    [InlineData(PointerDeviceType.Touch)]
    [InlineData(PointerDeviceType.Pen)]
    public void ReversingBeforeReleaseCannotFlingInTheOldDirection(PointerDeviceType type)
    {
        var viewer = Viewer(); viewer.PanningMode = PanningMode.VerticalOnly;
        Pan(viewer, type, 0, 100, down: true);
        Pan(viewer, type, 10, 40);
        Pan(viewer, type, 20, 55);
        Pan(viewer, type, 30, 55, up: true);
        Assert.True(Field<bool>(viewer, "_isSmoothScrolling"));
        Assert.True(Field<double>(viewer, "_smoothTargetY") < viewer.VerticalOffset);
    }

    [Theory]
    [InlineData(PointerDeviceType.Touch)]
    [InlineData(PointerDeviceType.Pen)]
    public void ReleasePauseSurvivesTheNativeTimestampRollover(PointerDeviceType type)
    {
        var viewer = Viewer(); viewer.PanningMode = PanningMode.VerticalOnly;
        Pan(viewer, type, int.MaxValue - 40, 100, down: true);
        Pan(viewer, type, int.MaxValue - 30, 70);
        Pan(viewer, type, unchecked(int.MaxValue + 200), 70, up: true);
        Assert.Equal(230, viewer.VerticalOffset, 6);
        Assert.False(Field<bool>(viewer, "_isSmoothScrolling"));
    }

    [Theory]
    [InlineData(PointerDeviceType.Touch)]
    [InlineData(PointerDeviceType.Pen)]
    public void ReleaseCommitsItsFinalPositionBeforeStartingInertia(PointerDeviceType type)
    {
        var viewer = Viewer(); viewer.PanningMode = PanningMode.VerticalOnly;
        viewer.IsScrollInertiaEnabled = false;
        Pan(viewer, type, 0, 100, down: true);
        Pan(viewer, type, 10, 80);
        Pan(viewer, type, 20, 65, up: true);
        Assert.Equal(235, viewer.VerticalOffset, 6);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PenFlickUsesReleaseTimingAndFinalPosition(bool paused, bool rollover)
    {
        var window = new Window { Width = 300, Height = 200, IsManipulationEnabled = true };
        window.Measure(new Size(300, 200)); window.Arrange(new Rect(0, 0, 300, 200));
        var input = new WindowInputDispatcher(window);
        ((IInputDispatcherHost)window).RealTimeStylus.UseRealTimeThread = false;
        var gestures = new List<SystemGesture>();
        Vector? releaseVelocity = null;
        double translation = 0;
        window.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
            translation = ((ManipulationDeltaEventArgs)e).CumulativeManipulation.Translation.X));
        window.AddHandler(UIElement.ManipulationInertiaStartingEvent, new RoutedEventHandler((_, e) =>
        {
            var inertia = (ManipulationInertiaStartingEventArgs)e;
            releaseVelocity = inertia.InitialVelocities.LinearVelocity;
            inertia.Cancel();
        }));
        window.AddHandler(UIElement.StylusSystemGestureEvent,
            new StylusSystemGestureEventHandler((_, e) => gestures.Add(e.SystemGesture)), true);
        void Send(int time, float x, bool down = false, bool up = false)
        {
            time = unchecked(time + (rollover ? int.MaxValue - 15 : 0));
            var snapshot = new MacOSTabletSnapshot
            {
                Size = 96, Type = down ? 50 : up ? 51 : 52, PointerId = 71,
                PointerType = 2, X = x, Y = 50, Pressure = up ? 0 : .5f,
                Flags = 1u | (up ? 0u : 2u), Buttons = up ? 0u : 1u, TimestampMillis = time
            };
            var data = window.BuildPointerInputData(snapshot.ToPointerEvent());
            input.HandleMacOSTabletInput(data, down, up, time, () => true, null);
        }
        try
        {
            Send(1, 20, down: true);
            Send(11, paused ? 80 : 20);
            Send(paused ? 250 : 21, 80, up: true);
            Assert.Equal(!paused, gestures.Contains(SystemGesture.Flick));
            Assert.Equal(60, translation, 6);
            Assert.NotNull(releaseVelocity);
            Assert.Equal(paused, releaseVelocity.Value.Length == 0);
        }
        finally { input.HandleMouseCancel(); window.Close(); }
    }

    [Fact]
    public void ManipulationInertiaDistanceDoesNotDependOnFrameRate()
    {
        (double Distance, double Rotation, double Expansion) Simulate(int interval)
        {
            var target = new Border();
            var cumulative = new ManipulationDelta { Scale = new Vector(1, 1) };
            var processor = new ManipulationInertiaProcessor(target, new Point(), cumulative,
                Dispatcher.GetForCurrentThread());
            try
            {
                Assert.True(processor.Start(new Vector(1, 0), .4, new Vector(.2, 0), null, null, null));
                for (int elapsed = 0; elapsed < 240; elapsed += interval) processor.TickForTesting(interval);
                return (processor.Cumulative.Translation.X, processor.Cumulative.Rotation,
                    processor.Cumulative.Expansion.X);
            }
            finally { processor.Cancel(); }
        }
        var fast = Simulate(8); var slow = Simulate(40);
        Assert.Equal(fast.Distance, slow.Distance, 8);
        Assert.Equal(fast.Rotation, slow.Rotation, 8);
        Assert.Equal(fast.Expansion, slow.Expansion, 8);
    }

    private static ScrollViewer Viewer()
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 1000, Height = 2000 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        viewer.Measure(new Size(240, 160)); viewer.Arrange(new Rect(0, 0, 240, 160));
        viewer.ScrollToVerticalOffset(200);
        return viewer;
    }

    private static void Pan(ScrollViewer viewer, PointerDeviceType type, int time, double y,
        bool down = false, bool up = false)
    {
        var point = new PointerPoint(7, new Point(30, y), type, !up,
            new PointerPointProperties { IsPrimary = true, Pressure = up ? 0 : .5f }, (ulong)time);
        RoutedEventArgs args = down ? new PointerDownEventArgs(point, ModifierKeys.None, time)
            { RoutedEvent = UIElement.PointerDownEvent } : up ? new PointerUpEventArgs(point, ModifierKeys.None, time)
            { RoutedEvent = UIElement.PointerUpEvent } : new PointerMoveEventArgs(point, ModifierKeys.None, time)
            { RoutedEvent = UIElement.PointerMoveEvent };
        viewer.RaiseEvent(args);
    }

    private static MouseWheelEventArgs Wheel(double y, bool precise = true,
        MouseWheelPhase phase = MouseWheelPhase.None, MouseWheelPhase momentum = MouseWheelPhase.None) =>
        new(UIElement.MouseWheelEvent, new Point(20, 20), 0, y, precise,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1, phase, momentum);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
