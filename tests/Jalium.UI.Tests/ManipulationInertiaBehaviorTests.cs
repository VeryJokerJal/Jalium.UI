using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ManipulationInertiaBehaviorTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(37)]
    [InlineData(400)]
    public void DesiredDisplacementStopsAtTheRequestedDistance(int frame)
    {
        var target = new Border();
        int completions = 0;
        target.AddHandler(UIElement.ManipulationCompletedEvent, new RoutedEventHandler((_, _) => completions++));
        var processor = New(target);
        try
        {
            Assert.True(processor.Start(new Vector(-.6, .8), 0, Vector.Zero,
                new InertiaTranslationBehavior { DesiredDisplacement = 100 }, null, null));
            Finish(processor, frame);
            Assert.Equal(-60, processor.Cumulative.Translation.X, 8);
            Assert.Equal(80, processor.Cumulative.Translation.Y, 8);
            Assert.Equal(Vector.Zero, processor.CurrentVelocities.LinearVelocity);
            Assert.Equal(1, completions);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void ExplicitDecelerationHasTheSpecifiedStoppingTimeAndDistance()
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(new Vector(1, 0), 0, Vector.Zero,
                new InertiaTranslationBehavior { DesiredDeceleration = .01 }, null, null));
            processor.TickForTesting(50);
            Assert.Equal(37.5, processor.Cumulative.Translation.X, 8);
            Assert.Equal(.5, processor.CurrentVelocities.LinearVelocity.X, 8);
            processor.TickForTesting(500);
            Assert.Equal(50, processor.Cumulative.Translation.X, 8);
            Assert.False(processor.IsRunning);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void ZeroDecelerationPreservesVelocityUntilTheOperationIsStopped()
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(new Vector(.5, 0), 0, Vector.Zero,
                new InertiaTranslationBehavior { DesiredDeceleration = 0 }, null, null));
            processor.TickForTesting(200);
            Assert.Equal(100, processor.Cumulative.Translation.X, 8);
            Assert.Equal(.5, processor.CurrentVelocities.LinearVelocity.X, 8);
        }
        finally { processor.Cancel(); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void DesiredRotationPreservesTheOriginalDirection(int direction)
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(Vector.Zero, .5 * direction, Vector.Zero, null,
                new InertiaRotationBehavior { DesiredRotation = 90 }, null));
            Finish(processor, 29);
            Assert.Equal(90 * direction, processor.Cumulative.Rotation, 8);
            Assert.Equal(0, processor.CurrentVelocities.AngularVelocity);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void ExpansionUsesItsRadiusForFrameAndCumulativeScale()
    {
        var target = new Border();
        var scales = new List<Vector>();
        target.AddHandler(UIElement.ManipulationDeltaEvent, new RoutedEventHandler((_, e) =>
            scales.Add(((ManipulationDeltaEventArgs)e).DeltaManipulation.Scale)));
        var processor = New(target, new ManipulationDelta { Scale = new Vector(2, 3) });
        try
        {
            Assert.True(processor.Start(Vector.Zero, 0, new Vector(.5, .25), null, null,
                new InertiaExpansionBehavior { InitialRadius = 100, DesiredExpansion = new Vector(40, 20) }));
            Finish(processor, 17);
            Assert.Equal(40, processor.Cumulative.Expansion.X, 8);
            Assert.Equal(20, processor.Cumulative.Expansion.Y, 8);
            Assert.Equal(2.8, processor.Cumulative.Scale.X, 8);
            Assert.Equal(3.6, processor.Cumulative.Scale.Y, 8);
            Assert.Equal(1.4, scales.Aggregate(1.0, (value, scale) => value * scale.X), 8);
            Assert.Equal(1.2, scales.Aggregate(1.0, (value, scale) => value * scale.Y), 8);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void ContractionStopsAtAPositiveRadiusWithoutFlippingTheContent()
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(Vector.Zero, 0, new Vector(-1, -1), null, null,
                new InertiaExpansionBehavior { InitialRadius = 10, DesiredExpansion = new Vector(100, 100) }));
            Finish(processor, 50);
            Assert.Equal(-9, processor.Cumulative.Expansion.X, 8);
            Assert.Equal(.1, processor.Cumulative.Scale.X, 8);
            Assert.Equal(.1, processor.Cumulative.Scale.Y, 8);
            Assert.Equal(Vector.Zero, processor.CurrentVelocities.ExpansionVelocity);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void ZeroRequestedDistanceDoesNotStartAnUnwantedAnimation()
    {
        var processor = New();
        try
        {
            Assert.False(processor.Start(new Vector(1, 0), .5, new Vector(.5, .5),
                new InertiaTranslationBehavior { DesiredDisplacement = 0 },
                new InertiaRotationBehavior { DesiredRotation = 0 },
                new InertiaExpansionBehavior { DesiredExpansion = Vector.Zero }));
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void ExplicitFineMovementStillReachesItsRequestedDistance()
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(new Vector(.001, 0), 0, Vector.Zero,
                new InertiaTranslationBehavior { DesiredDisplacement = .03 }, null, null));
            Finish(processor, 7);
            Assert.Equal(.03, processor.Cumulative.Translation.X, 10);
        }
        finally { processor.Cancel(); }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteVelocityCannotStartAnEndlessAnimation(double velocity)
    {
        var processor = New();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => processor.Start(new Vector(velocity, 0), 0,
                Vector.Zero, null, null, null));
            Assert.False(processor.IsRunning);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void InvalidRestartStopsThePreviousTimerBeforeRejectingParameters()
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(new Vector(1, 0), 0, Vector.Zero, null, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => processor.Start(new Vector(1, 0), 0,
                Vector.Zero, new InertiaTranslationBehavior { DesiredDisplacement = -1 }, null, null));
            Assert.False(processor.IsRunning);
        }
        finally { processor.Cancel(); }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(37)]
    [InlineData(400)]
    public void DefaultCurvesHaveTheSameEndpointAcrossRefreshRates(int frame)
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(new Vector(1, 0), .4, new Vector(.2, 0), null, null,
                new InertiaExpansionBehavior { InitialRadius = 100 }));
            Finish(processor, frame);
            Assert.Equal(497.5, processor.Cumulative.Translation.X, 8);
            Assert.Equal(191, processor.Cumulative.Rotation, 8);
            Assert.Equal(97.5, processor.Cumulative.Expansion.X, 8);
            Assert.Equal(1.975, processor.Cumulative.Scale.X, 8);
        }
        finally { processor.Cancel(); }
    }

    [Fact]
    public void DiagonalExpansionKeepsItsVectorMagnitudeThreshold()
    {
        var processor = New();
        try
        {
            Assert.True(processor.Start(Vector.Zero, 0, new Vector(.004, .004), null, null,
                new InertiaExpansionBehavior { InitialRadius = 100 }));
            Finish(processor, 16);
            Assert.True(processor.Cumulative.Expansion.X > 0);
            Assert.Equal(processor.Cumulative.Expansion.X, processor.Cumulative.Expansion.Y, 8);
        }
        finally { processor.Cancel(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonFiniteRotationOrExpansionNeverReachesConsumers(bool expansion)
    {
        var processor = New();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => processor.Start(Vector.Zero,
                expansion ? 0 : double.NaN, expansion ? new Vector(double.PositiveInfinity, 0) : Vector.Zero,
                null, null, null));
            Assert.False(processor.IsRunning);
        }
        finally { processor.Cancel(); }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidRadiusCannotProduceInvalidScale(double radius)
    {
        var processor = New();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => processor.Start(Vector.Zero, 0, new Vector(.1, .1),
                null, null, new InertiaExpansionBehavior { InitialRadius = radius }));
            Assert.False(processor.IsRunning);
        }
        finally { processor.Cancel(); }
    }

    private static ManipulationInertiaProcessor New(Border? target = null, ManipulationDelta? cumulative = null) =>
        new(target ?? new Border(), new Point(), cumulative ?? new ManipulationDelta { Scale = new Vector(1, 1) },
            Dispatcher.GetForCurrentThread());

    private static void Finish(ManipulationInertiaProcessor processor, int frame)
    {
        for (int index = 0; index < 2000 && processor.IsRunning; index++) processor.TickForTesting(frame);
        Assert.False(processor.IsRunning);
    }
}
