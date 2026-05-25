using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class ManipulationBoundaryFeedbackTests
{
    [Fact]
    public void ReportBoundaryFeedback_SetsUnusedManipulation()
    {
        var args = new ManipulationDeltaEventArgs
        {
            RoutedEvent = UIElement.ManipulationDeltaEvent
        };
        var unused = new ManipulationDelta { Translation = new Vector(3, 0), Scale = new Vector(1, 1) };

        args.ReportBoundaryFeedback(unused);

        Assert.NotNull(args.UnusedManipulation);
        Assert.Equal(3, args.UnusedManipulation!.Translation.X);
    }

    [Fact]
    public void InertiaProcessor_RaisesBoundaryFeedback_WhenDeltaHandlerReports()
    {
        var target = new Border();
        int boundaryCount = 0;
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
        {
            var d = (ManipulationDeltaEventArgs)e;
            d.ReportBoundaryFeedback(new ManipulationDelta
            {
                Translation = new Vector(2, 0),
                Scale = new Vector(1, 1)
            });
        }));
        target.AddHandler(UIElement.ManipulationBoundaryFeedbackEvent, new RoutedEventHandler((_, _) => boundaryCount++));

        var processor = new ManipulationInertiaProcessor(
            target, new Point(0, 0),
            new ManipulationDelta { Translation = Vector.Zero, Scale = new Vector(1, 1), Expansion = Vector.Zero, Rotation = 0 },
            Dispatcher.GetForCurrentThread());

        Assert.True(processor.Start(new Vector(0.5, 0), 0, Vector.Zero, null, null, null));
        processor.TickForTesting(16);

        Assert.True(boundaryCount >= 1, $"Expected boundary feedback raised, got {boundaryCount}");
    }

    [Fact]
    public void Complete_TerminatesInertia_RaisesCompletedExactlyOnce()
    {
        var target = new Border();
        int completedCount = 0;
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
        {
            ((ManipulationDeltaEventArgs)e).Complete();
        }));
        target.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, _) => completedCount++));

        var processor = new ManipulationInertiaProcessor(
            target, new Point(0, 0),
            new ManipulationDelta { Scale = new Vector(1, 1) },
            Dispatcher.GetForCurrentThread());
        Assert.True(processor.Start(new Vector(0.5, 0), 0, Vector.Zero, null, null, null));
        processor.TickForTesting(16);

        Assert.False(processor.IsRunning);
        Assert.Equal(1, completedCount);
    }
}
