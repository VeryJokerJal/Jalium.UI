using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ManipulationInertiaProcessorTests
{
    [Fact]
    public void Start_BelowStopThreshold_ReturnsFalse_AndRaisesNothing()
    {
        var target = new Border();
        int deltaCount = 0;
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, _) => deltaCount++));

        var processor = new ManipulationInertiaProcessor(
            target,
            origin: new Point(0, 0),
            cumulativeBefore: NewDelta(),
            dispatcher: Dispatcher.GetForCurrentThread());

        bool started = processor.Start(
            linearVelocity: new Vector(0.0001, 0.0001),
            angularVelocity: 0,
            expansionVelocity: Vector.Zero,
            translationBehavior: null,
            rotationBehavior: null,
            expansionBehavior: null);

        Assert.False(started);
        Assert.Equal(0, deltaCount);
    }

    [Fact]
    public void TickForTesting_RaisesInertialDelta_AndAccumulates()
    {
        var target = new Border();
        var capturedDeltas = new List<ManipulationDeltaEventArgs>();
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) => capturedDeltas.Add((ManipulationDeltaEventArgs)e)));

        var processor = new ManipulationInertiaProcessor(
            target,
            origin: new Point(0, 0),
            cumulativeBefore: NewDelta(),
            dispatcher: Dispatcher.GetForCurrentThread());

        Assert.True(processor.Start(
            linearVelocity: new Vector(0.5, 0), // 500 DIP/sec
            angularVelocity: 0,
            expansionVelocity: Vector.Zero,
            translationBehavior: null,
            rotationBehavior: null,
            expansionBehavior: null));

        // Drive several frames manually.
        for (int i = 0; i < 5; i++)
        {
            processor.TickForTesting(16);
        }

        Assert.True(capturedDeltas.Count >= 1, $"Expected at least one delta, got {capturedDeltas.Count}");
        Assert.All(capturedDeltas, d => Assert.True(d.IsInertial));
        // Cumulative translation must monotonically grow until it asymptotes.
        var firstCum = capturedDeltas[0].CumulativeManipulation!.Translation.X;
        var lastCum = capturedDeltas[^1].CumulativeManipulation!.Translation.X;
        Assert.True(lastCum >= firstCum, $"Expected cumulative growth, got {firstCum} → {lastCum}");
    }

    [Fact]
    public void TickForTesting_EventuallyRaisesCompletedWithIsInertialTrue()
    {
        var target = new Border();
        int deltaCount = 0;
        var completedArgs = new List<ManipulationCompletedEventArgs>();
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, _) => deltaCount++));
        target.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, e) => completedArgs.Add((ManipulationCompletedEventArgs)e)));

        var processor = new ManipulationInertiaProcessor(
            target, new Point(0, 0), NewDelta(), Dispatcher.GetForCurrentThread());
        Assert.True(processor.Start(
            linearVelocity: new Vector(0.5, 0),
            angularVelocity: 0,
            expansionVelocity: Vector.Zero,
            translationBehavior: null,
            rotationBehavior: null,
            expansionBehavior: null));

        // Drive enough ticks for exponential decay to fall below the stop threshold.
        for (int i = 0; i < 4000 && processor.IsRunning; i++)
        {
            processor.TickForTesting(64);
        }

        Assert.False(processor.IsRunning);
        Assert.Single(completedArgs);
        Assert.True(completedArgs[0].IsInertial);
    }

    [Fact]
    public void Cancel_StopsImmediatelyWithoutRaisingCompleted()
    {
        var target = new Border();
        int completedCount = 0;
        target.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, _) => completedCount++));

        var processor = new ManipulationInertiaProcessor(
            target, new Point(0, 0), NewDelta(), Dispatcher.GetForCurrentThread());
        Assert.True(processor.Start(
            new Vector(0.5, 0), 0, Vector.Zero, null, null, null));

        processor.Cancel();

        Assert.False(processor.IsRunning);
        Assert.Equal(0, completedCount);
    }

    [Fact]
    public void Tick_InertialDeltaCannotBeCancelledAndProcessorContinues()
    {
        var target = new Border();
        int completedCount = 0;
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
        {
            ((ManipulationDeltaEventArgs)e).Cancel();
        }));
        target.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, _) => completedCount++));

        var processor = new ManipulationInertiaProcessor(
            target, new Point(0, 0), NewDelta(), Dispatcher.GetForCurrentThread());
        Assert.True(processor.Start(
            new Vector(0.5, 0), 0, Vector.Zero, null, null, null));
        processor.TickForTesting(16);

        Assert.True(processor.IsRunning);
        Assert.Equal(0, completedCount);
    }

    private static ManipulationDelta NewDelta() =>
        new() { Translation = Vector.Zero, Rotation = 0, Scale = new Vector(1, 1), Expansion = Vector.Zero };
}
