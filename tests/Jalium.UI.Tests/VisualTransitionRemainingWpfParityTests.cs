using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class VisualTransitionRemainingWpfParityTests
{
    [Fact]
    public void GeneratedDurationAndEasingFunctionUseWpfTypes()
    {
        Assert.Equal(
            typeof(Duration),
            typeof(VisualTransition).GetProperty(nameof(VisualTransition.GeneratedDuration))!.PropertyType);
        Assert.Equal(
            typeof(IEasingFunction),
            typeof(VisualTransition).GetProperty(nameof(VisualTransition.GeneratedEasingFunction))!.PropertyType);

        var easing = new ProbeEasingFunction();
        var transition = new VisualTransition
        {
            GeneratedDuration = new Duration(TimeSpan.FromMilliseconds(250)),
            GeneratedEasingFunction = easing,
        };

        Assert.Equal(TimeSpan.FromMilliseconds(250), transition.GeneratedDuration.TimeSpan);
        Assert.Same(easing, transition.GeneratedEasingFunction);
        Assert.Equal(0.25, easing.Ease(0.5));
    }

    private sealed class ProbeEasingFunction : IEasingFunction
    {
        public double Ease(double normalizedTime) => normalizedTime * normalizedTime;
    }
}
