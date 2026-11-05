using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ManipulationReleaseInertiaTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SequentialFingerReleaseKeepsRecentPinchVelocityAndAgesItOnce(bool paused, bool rollover)
    {
        using var gesture = new Gesture(rollover);
        gesture.Move();
        var before = gesture.LastDelta!;
        Assert.True(before.Velocities.ExpansionVelocity.X > 0);
        Assert.True(Math.Abs(before.Velocities.AngularVelocity) > 0);
        gesture.Send(1, new Point(50, 90), 22, up: true);
        gesture.Send(2, new Point(180, 110), paused ? 222 : 23, up: true);
        var released = Assert.IsType<ManipulationVelocities>(gesture.Released);
        double weight = paused ? 0 : .98;
        Assert.Equal(before.Velocities.ExpansionVelocity.X * weight, released.ExpansionVelocity.X, 8);
        Assert.Equal(before.Velocities.AngularVelocity * weight, released.AngularVelocity, 8);
        Assert.Equal(before.Velocities.LinearVelocity.X * weight, released.LinearVelocity.X, 8);
        Assert.Equal(before.CumulativeManipulation.Scale.X, gesture.Total!.Scale.X, 8);
        Assert.Equal(before.CumulativeManipulation.Rotation, gesture.Total.Rotation, 8);
    }

    [Fact]
    public void ContinuingWithOneFingerDoesNotReuseThePreviousPinchVelocity()
    {
        using var gesture = new Gesture(false);
        gesture.Move();
        gesture.Send(1, new Point(50, 90), 22, up: true);
        gesture.Send(2, new Point(185, 110), 30);
        gesture.Send(2, new Point(185, 110), 31, up: true);
        Assert.NotNull(gesture.Released);
        Assert.Equal(Vector.Zero, gesture.Released.ExpansionVelocity);
        Assert.Equal(0, gesture.Released.AngularVelocity);
        Assert.True(gesture.Released.LinearVelocity.X > 0);
    }

    private sealed class Gesture : IDisposable
    {
        private readonly Window _window = new() { IsManipulationEnabled = true };
        private readonly WindowInputDispatcher _input;
        private readonly bool _rollover;
        internal ManipulationDeltaEventArgs? LastDelta;
        internal ManipulationVelocities? Released;
        internal ManipulationDelta? Total;

        internal Gesture(bool rollover)
        {
            _rollover = rollover;
            _input = new WindowInputDispatcher(_window);
            _window.AddHandler(UIElement.ManipulationStartingEvent, new RoutedEventHandler((_, e) =>
                ((ManipulationStartingEventArgs)e).IsSingleTouchEnabled = false));
            _window.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
                LastDelta = (ManipulationDeltaEventArgs)e));
            _window.AddHandler(UIElement.ManipulationInertiaStartingEvent, new RoutedEventHandler((_, e) =>
            {
                var args = (ManipulationInertiaStartingEventArgs)e;
                Released = args.InitialVelocities;
                args.Cancel();
            }));
            _window.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, e) =>
                Total = ((ManipulationCompletedEventArgs)e).TotalManipulation));
            Send(1, new Point(50, 100), 1, down: true);
            Send(2, new Point(150, 100), 2, down: true);
        }

        internal void Move()
        {
            Send(1, new Point(50, 90), 11);
            Send(2, new Point(180, 110), 21);
        }

        internal void Send(uint id, Point position, int time, bool down = false, bool up = false)
        {
            time = unchecked(time + (_rollover ? int.MaxValue - 15 : 0));
            var point = new PointerPoint(id, position, PointerDeviceType.Touch, !up,
                new PointerPointProperties { IsPrimary = id == 1 }, unchecked((ulong)time));
            var data = new PointerInputData(id, PointerInputKind.Touch, point, position, ModifierKeys.None,
                true, false, new StylusPointCollection());
            typeof(WindowInputDispatcher).GetMethod("DispatchManipulationPipeline", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_input, [_window, data, down, up, false, time]);
        }

        public void Dispose() { _input.HandleMouseCancel(); _window.Close(); }
    }
}
