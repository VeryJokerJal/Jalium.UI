using System.Reflection;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class AnimationSmallSurfaceParityTests
{
    [Fact]
    public void AnimatableCloneAndSerializationHelperUseWpfContracts()
    {
        var original = new ProbeAnimatable();
        Assert.IsType<ProbeAnimatable>(original.Clone());
        Assert.False(Animatable.ShouldSerializeStoredWeakReference(original));
        Assert.False(Animatable.ShouldSerializeStoredWeakReference(null!));
    }

    [Fact]
    public void ClockGroupExposesTypedTimelineAndReadOnlyClockCollection()
    {
        var timeline = new ParallelTimeline();
        timeline.Children.Add(new ProbeAnimation());

        ClockGroup group = timeline.CreateClock();

        Assert.Same(timeline, group.Timeline);
        Assert.Equal(
            typeof(TimelineGroup),
            typeof(ClockGroup).GetProperty(
                nameof(ClockGroup.Timeline),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.PropertyType);
        Assert.Equal(
            typeof(ClockCollection),
            typeof(ClockGroup).GetProperty(
                nameof(ClockGroup.Children),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.PropertyType);
        Assert.Single(group.Children);
        Assert.True(group.Children.IsReadOnly);
        Assert.True(ClockCollection.Equals(group.Children, group.Children));
        Assert.False(ClockCollection.Equals(group.Children, null!));
        Assert.Throws<NotSupportedException>(() => group.Children.Add(group.Children[0]));
        Assert.Throws<NotSupportedException>(() => group.Children.Remove(group.Children[0]));
        Assert.Throws<NotSupportedException>(() => group.Children.Clear());

        ConstructorInfo constructor = typeof(ClockGroup).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(TimelineGroup)],
            null)!;
        Assert.True(constructor.IsFamilyOrAssembly);
    }

    [Fact]
    public void AnimationClockAndKeyTimeStaticMembersEvaluateRealValues()
    {
        var animation = new ProbeAnimation();
        var clock = new AnimationClock(animation);

        Assert.Equal(7d, clock.GetCurrentValue(3d, 4d));

        KeyTime first = KeyTime.FromPercent(0.25);
        KeyTime second = KeyTime.FromPercent(0.25);
        Assert.True(KeyTime.Equals(first, second));
        Assert.False(KeyTime.Equals(first, KeyTime.FromPercent(0.5)));
        Assert.False(KeyTime.Equals(first, KeyTime.FromPercent(0.25005)));
    }

    private sealed class ProbeAnimatable : Animatable
    {
        protected override Freezable CreateInstanceCore() => new ProbeAnimatable();
    }

    private sealed class ProbeAnimation : TypedAnimationTimeline<double>
    {
        protected override double GetCurrentValueCore(
            double defaultOriginValue,
            double defaultDestinationValue,
            AnimationClock animationClock) => defaultOriginValue + defaultDestinationValue;
    }
}
