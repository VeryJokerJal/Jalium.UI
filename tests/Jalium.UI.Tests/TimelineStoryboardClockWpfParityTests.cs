using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Animation;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public class TimelineStoryboardClockWpfParityTests
{
    private sealed class ProbeContentElement : FrameworkContentElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(ProbeContentElement),
            new PropertyMetadata(0d));

        public double Value
        {
            get => (double)GetValue(ValueProperty)!;
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class ProbeTimeline : Timeline
    {
        public ProbeTimeline()
        {
        }

        public ProbeTimeline(TimeSpan? beginTime)
            : base(beginTime)
        {
        }

        public ProbeTimeline(TimeSpan? beginTime, Duration duration)
            : base(beginTime, duration)
        {
        }

        public ProbeTimeline(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
            : base(beginTime, duration, repeatBehavior)
        {
        }
    }

    private sealed class ProbeElement : FrameworkElement
    {
    }

    [Fact]
    public void Timeline_IsAnimatable_AndDeclaresWpfTimingDependencyProperties()
    {
        Assert.True(typeof(Timeline).IsAbstract);
        Assert.True(typeof(Animatable).IsAssignableFrom(typeof(Timeline)));

        AssertDp(Timeline.AccelerationRatioProperty, typeof(double), 0d);
        AssertDp(Timeline.DecelerationRatioProperty, typeof(double), 0d);
        AssertDp(Timeline.NameProperty, typeof(string), null);
        AssertDp(Timeline.RepeatBehaviorProperty, typeof(RepeatBehavior), new RepeatBehavior(1d));
        AssertDp(Timeline.DesiredFrameRateProperty, typeof(int?), null);

        var timeline = new ProbeTimeline(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3),
            new RepeatBehavior(4d));
        Assert.Equal(TimeSpan.FromSeconds(2), timeline.BeginTime);
        Assert.Equal(new Duration(TimeSpan.FromSeconds(3)), timeline.Duration);
        Assert.Equal(new RepeatBehavior(4d), timeline.RepeatBehavior);

        timeline.AccelerationRatio = 0.4;
        timeline.DecelerationRatio = 0.6;
        Assert.Throws<ArgumentException>(() => timeline.DecelerationRatio = 0.7);
        Assert.Throws<ArgumentException>(() => timeline.SpeedRatio = 0d);
        Assert.Throws<ArgumentException>(() => Timeline.SetDesiredFrameRate(timeline, 0));

        Timeline.SetDesiredFrameRate(timeline, 120);
        Assert.Equal(120, Timeline.GetDesiredFrameRate(timeline));
    }

    [Fact]
    public void Timeline_ClockCreation_TransfersHandlersAndHonorsControllability()
    {
        var timeline = new ProbeTimeline { Duration = TimeSpan.FromSeconds(1) };
        int stateChanges = 0;
        int speedChanges = 0;
        int removeRequests = 0;
        object? sender = null;
        timeline.CurrentStateInvalidated += (s, _) =>
        {
            sender = s;
            stateChanges++;
        };
        timeline.CurrentGlobalSpeedInvalidated += (_, _) => speedChanges++;
        timeline.RemoveRequested += (_, _) => removeRequests++;

        Clock uncontrollable = timeline.CreateClock();
        Assert.Null(uncontrollable.Controller);
        Assert.False(uncontrollable.HasControllableRoot);

        Clock clock = timeline.CreateClock(hasControllableRoot: true);
        Assert.NotNull(clock.Controller);
        Assert.True(clock.HasControllableRoot);
        clock.Controller!.Begin();
        Assert.Same(clock, sender);
        Assert.Equal(ClockState.Active, clock.CurrentState);
        Assert.Equal(1d, clock.CurrentGlobalSpeed);

        clock.Controller.SpeedRatio = 2d;
        Assert.Equal(2d, clock.CurrentGlobalSpeed);
        clock.Controller.Pause();
        Assert.Equal(0d, clock.CurrentGlobalSpeed);
        clock.Controller.Resume();
        clock.Controller.Remove();

        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Equal(1, removeRequests);
        Assert.True(stateChanges >= 2);
        Assert.True(speedChanges >= 3);
    }

    [Fact]
    public void TimelineCollection_DeepClones_Freezes_AndInvalidatesEnumerators()
    {
        var child = new ParallelTimeline { Name = "child" };
        child.Children.Add(new ParallelTimeline { Duration = TimeSpan.FromSeconds(2) });
        var collection = new TimelineCollection(2) { child };

        TimelineCollection clone = collection.Clone();
        Assert.NotSame(collection, clone);
        Assert.NotSame(collection[0], clone[0]);
        Assert.NotSame(((ParallelTimeline)collection[0]).Children, ((ParallelTimeline)clone[0]).Children);

        TimelineCollection.Enumerator enumerator = collection.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        collection.Add(new ParallelTimeline());
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());

        collection.Freeze();
        Assert.True(collection.IsFrozen);
        Assert.True(child.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => collection.Add(new ParallelTimeline()));
    }

    [Fact]
    public void TimelineGroup_AllocatesAClockTree_AndControllerCascades()
    {
        var group = new ParallelTimeline();
        group.Children.Add(new ProbeTimeline { Duration = TimeSpan.FromSeconds(1) });
        group.Children.Add(new ParallelTimeline { Duration = TimeSpan.FromSeconds(2) });

        ClockGroup clock = group.CreateClock();
        Assert.Equal(2, clock.Children.Count);
        Assert.Same(clock, clock.Children[0].Parent);
        Assert.True(clock.HasControllableRoot);

        clock.Controller!.Begin();
        Assert.All(clock.Children, child => Assert.Equal(ClockState.Active, child.CurrentState));
        clock.Controller.Pause();
        Assert.All(clock.Children, child => Assert.True(child.IsPaused));
        clock.Controller.Stop();
        Assert.All(clock.Children, child => Assert.Equal(ClockState.Stopped, child.CurrentState));
    }

    [Fact]
    public void Storyboard_IsSubclassableParallelTimeline_WithTypedClone()
    {
        Assert.False(typeof(Storyboard).IsSealed);
        Assert.Equal(typeof(ParallelTimeline), typeof(Storyboard).BaseType);

        var storyboard = new Storyboard { Name = "fade" };
        storyboard.Children.Add(new DoubleAnimation { From = 0, To = 1 });
        Storyboard clone = storyboard.Clone();

        Assert.Equal("fade", clone.Name);
        Assert.NotSame(storyboard.Children, clone.Children);
        Assert.NotSame(storyboard.Children[0], clone.Children[0]);
    }

    [Fact]
    public void Storyboard_InheritsTargetsThroughNestedGroups_AndExposesLiveControlState()
    {
        var element = new ProbeElement();
        element.SetValue(UIElement.OpacityProperty, 0.25d);
        var animation = new DoubleAnimation
        {
            From = 0d,
            To = 1d,
            Duration = TimeSpan.FromSeconds(1),
            FillBehavior = FillBehavior.HoldEnd,
        };
        var nested = new ParallelTimeline();
        Storyboard.SetTarget(nested, element);
        Storyboard.SetTargetProperty(nested, new PropertyPath(UIElement.OpacityProperty));
        nested.Children.Add(animation);

        var storyboard = new Storyboard();
        storyboard.Children.Add(nested);

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin(element, isControllable: true));
        AnimationManager.ProcessFrame(t0 + Stopwatch.Frequency / 4);

        Assert.Equal(ClockState.Active, storyboard.GetCurrentState(element));
        Assert.Equal(0.25d, storyboard.GetCurrentProgress(element)!.Value, 3);
        Assert.Equal(TimeSpan.FromSeconds(0.25), storyboard.GetCurrentTime(element));
        Assert.Equal(1, storyboard.GetCurrentIteration(element));

        storyboard.SetSpeedRatio(element, 2d);
        Assert.Equal(2d, storyboard.GetCurrentGlobalSpeed(element));
        storyboard.Pause(element);
        Assert.True(storyboard.GetIsPaused(element));
        Assert.Equal(0d, storyboard.GetCurrentGlobalSpeed(element));
        storyboard.Resume(element);
        Assert.False(storyboard.GetIsPaused(element));

        AnimationEngineTests.RunInsideFrame(
            t0 + Stopwatch.Frequency / 2,
            _ => storyboard.Seek(element, TimeSpan.FromSeconds(0.75), TimeSeekOrigin.BeginTime));
        AnimationManager.ProcessFrame(t0 + Stopwatch.Frequency / 2);
        Assert.Equal(0.75d, storyboard.GetCurrentProgress(element)!.Value, 3);

        storyboard.SkipToFill(element);
        Assert.Equal(ClockState.Filling, storyboard.GetCurrentState(element));
        Assert.Equal(1d, storyboard.GetCurrentProgress(element));

        storyboard.Begin(element, isControllable: true);
        storyboard.Remove(element);
        Assert.Null(storyboard.GetCurrentProgress(element));
        Assert.Equal(0.25d, element.GetValue(UIElement.OpacityProperty));
    }

    [Fact]
    public void Storyboard_FrameworkElementAndTemplateOverloadsMatchTheVerifierSurface()
    {
        Type type = typeof(Storyboard);
        Type fe = typeof(FrameworkElement);
        Type template = typeof(FrameworkTemplate);
        Type handoff = typeof(Jalium.UI.Media.Animation.HandoffBehavior);
        Type origin = typeof(TimeSeekOrigin);

        AssertMethod(type, nameof(Storyboard.Begin), fe);
        AssertMethod(type, nameof(Storyboard.Begin), fe, typeof(bool));
        AssertMethod(type, nameof(Storyboard.Begin), fe, handoff);
        AssertMethod(type, nameof(Storyboard.Begin), fe, handoff, typeof(bool));
        AssertMethod(type, nameof(Storyboard.Begin), fe, template);
        AssertMethod(type, nameof(Storyboard.Begin), fe, template, typeof(bool));
        AssertMethod(type, nameof(Storyboard.Begin), fe, template, handoff);
        AssertMethod(type, nameof(Storyboard.Begin), fe, template, handoff, typeof(bool));
        AssertMethod(type, nameof(Storyboard.Seek), typeof(TimeSpan), origin);
        AssertMethod(type, nameof(Storyboard.Seek), fe, typeof(TimeSpan), origin);
        AssertMethod(type, nameof(Storyboard.SeekAlignedToLastTick), typeof(TimeSpan), origin);
        AssertMethod(type, nameof(Storyboard.SeekAlignedToLastTick), fe, typeof(TimeSpan), origin);
        AssertMethod(type, nameof(Storyboard.SetSpeedRatio), fe, typeof(double));
        AssertMethod(type, nameof(Storyboard.SkipToFill), fe);
        AssertMethod(type, nameof(Storyboard.Remove), fe);
    }

    [Fact]
    public void Storyboard_FrameworkContentElementOverloadsControlTheSameLiveClock()
    {
        var content = new ProbeContentElement { Value = 0.25d };
        var animation = new DoubleAnimation
        {
            From = 0d,
            To = 1d,
            Duration = TimeSpan.FromSeconds(1),
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(animation, content);
        Storyboard.SetTargetProperty(animation, new PropertyPath(ProbeContentElement.ValueProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin(content, isControllable: true));
        AnimationManager.ProcessFrame(t0 + Stopwatch.Frequency / 4);

        Assert.Equal(ClockState.Active, storyboard.GetCurrentState(content));
        Assert.Equal(0.25d, storyboard.GetCurrentProgress(content)!.Value, 3);
        Assert.Equal(TimeSpan.FromSeconds(0.25), storyboard.GetCurrentTime(content));
        Assert.Equal(1, storyboard.GetCurrentIteration(content));

        storyboard.SetSpeedRatio(content, 2d);
        storyboard.Pause(content);
        Assert.True(storyboard.GetIsPaused(content));
        storyboard.Resume(content);
        storyboard.Seek(content, TimeSpan.FromSeconds(0.5), TimeSeekOrigin.BeginTime);
        storyboard.SeekAlignedToLastTick(content, TimeSpan.FromSeconds(0.75), TimeSeekOrigin.BeginTime);
        storyboard.SkipToFill(content);
        Assert.Equal(ClockState.Filling, storyboard.GetCurrentState(content));

        storyboard.Remove(content);
        Assert.Null(storyboard.GetCurrentProgress(content));
        Assert.Equal(0.25d, content.Value);
    }

    private static void AssertDp(DependencyProperty property, Type propertyType, object? defaultValue)
    {
        Assert.Equal(propertyType, property.PropertyType);
        Assert.Equal(typeof(Timeline), property.OwnerType);
        Assert.Equal(defaultValue, property.DefaultMetadata.DefaultValue);
    }

    private static void AssertMethod(Type type, string name, params Type[] parameters) =>
        Assert.NotNull(type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: parameters,
            modifiers: null));
}
