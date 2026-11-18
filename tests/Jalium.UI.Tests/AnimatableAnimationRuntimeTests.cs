using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimatableAnimationRuntimeTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    [Fact]
    public void BeginAnimation_AdvancesTransformAndNotifiesItsOwner()
    {
        var transform = new RotateTransform();
        int changes = 0;
        transform.Changed += (_, _) => changes++;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => transform.BeginAnimation(
                RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))));
            for (int frame = 1; frame <= 4; frame++)
            {
                int previousChanges = changes;
                AnimationManager.ProcessFrame(start + Ticks(frame * .25));
                Assert.Equal(90d * frame, transform.Angle, 5);
                Assert.True(changes > previousChanges);
            }
        }
        finally
        {
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Theory]
    [InlineData(FillBehavior.HoldEnd)]
    [InlineData(FillBehavior.Stop)]
    public void FillAndRemoval_PreserveTheLatestBaseValue(FillBehavior fillBehavior)
    {
        var transform = new RotateTransform(10);
        var animation = new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1)) { FillBehavior = fillBehavior };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => transform.BeginAnimation(RotateTransform.AngleProperty, animation));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(50d, transform.Angle, 5);
            Assert.Equal(10d, transform.GetAnimationBaseValue(RotateTransform.AngleProperty));

            transform.SetValue(RotateTransform.AngleProperty, 42d);
            Assert.Equal(50d, transform.Angle, 5);
            Assert.Equal(42d, transform.GetAnimationBaseValue(RotateTransform.AngleProperty));
            AnimationManager.ProcessFrame(start + Ticks(1.5));
            Assert.Equal(fillBehavior == FillBehavior.HoldEnd ? 100d : 42d, transform.Angle, 5);

            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            Assert.Equal(42d, transform.Angle);
            Assert.False(transform.HasAnimatedProperties);
        }
        finally
        {
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void DelayedAnimation_LeavesTheBaseValueVisibleUntilItsBeginTime()
    {
        var transform = new RotateTransform(10);
        var animation = new DoubleAnimation(100, 200, TimeSpan.FromSeconds(1)) { BeginTime = TimeSpan.FromSeconds(1) };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => transform.BeginAnimation(RotateTransform.AngleProperty, animation));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(10d, transform.Angle);
            AnimationManager.ProcessFrame(start + Ticks(1.5));
            Assert.Equal(150d, transform.Angle, 5);
        }
        finally
        {
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void ApplyAnimationClock_RespondsToPauseResumeSeekAndRemove()
    {
        var transform = new RotateTransform(10);
        var animation = new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1));
        var clock = (AnimationClock)((Timeline)animation).CreateClock(hasControllableRoot: true);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => transform.ApplyAnimationClock(RotateTransform.AngleProperty, clock));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            Assert.Equal(25d, transform.Angle, 5);
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => clock.Controller.Pause());
            AnimationManager.ProcessFrame(start + Ticks(1));
            Assert.Equal(25d, transform.Angle, 5);
            AnimationEngineTests.RunInsideFrame(start + Ticks(1), _ => clock.Controller.Resume());
            AnimationManager.ProcessFrame(start + Ticks(1.25));
            Assert.Equal(50d, transform.Angle, 5);
            AnimationEngineTests.RunInsideFrame(start + Ticks(1.25), _ => clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(.75), TimeSeekOrigin.BeginTime));
            AnimationManager.ProcessFrame(start + Ticks(1.25));
            Assert.Equal(75d, transform.Angle, 5);
            clock.Controller.Remove();
            Assert.Equal(10d, transform.Angle);
            Assert.False(transform.HasAnimatedProperties);
        }
        finally
        {
            transform.ApplyAnimationClock(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void Compose_SamplesBothClocksAndRemovalRevealsTheBaseValue()
    {
        var transform = new RotateTransform(10);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => transform.BeginAnimation(
                RotateTransform.AngleProperty, new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1))));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => transform.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation { By = 20, Duration = TimeSpan.FromSeconds(1) }, HandoffBehavior.Compose));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(55d, transform.Angle, 5);
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            Assert.Equal(10d, transform.Angle);
        }
        finally
        {
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void SnapshotAndReplace_StartsFromTheDisplayedValue()
    {
        var transform = new RotateTransform(10);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => transform.BeginAnimation(
                RotateTransform.AngleProperty, new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1))));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => transform.BeginAnimation(
                RotateTransform.AngleProperty, new DoubleAnimation { To = 200, Duration = TimeSpan.FromSeconds(1) }));
            AnimationManager.ProcessFrame(start + Ticks(.75));
            Assert.Equal(112.5d, transform.Angle, 5);
            Assert.Equal(10d, transform.GetAnimationBaseValue(RotateTransform.AngleProperty));
        }
        finally
        {
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void ColorAnimation_UpdatesTheBrushWithoutMouseInput()
    {
        var brush = new SolidColorBrush(Colors.Black);
        var animation = new ColorAnimation { From = Colors.Black, To = Colors.White, Duration = TimeSpan.FromSeconds(1) };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => brush.BeginAnimation(SolidColorBrush.ColorProperty, animation));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.InRange(brush.Color.R, (byte)127, (byte)128);
            Assert.Equal(Colors.Black, brush.GetAnimationBaseValue(SolidColorBrush.ColorProperty));
        }
        finally
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void StoryboardOnTransform_PreservesBaseValuesAndReleasesThePreviousOwner()
    {
        var transform = new RotateTransform(10);
        var storyboard = CreateStoryboard(transform, new PropertyPath(RotateTransform.AngleProperty));
        long start = Stopwatch.GetTimestamp();
        int completed = 0;
        storyboard.Completed += (_, _) => completed++;
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(50d, transform.Angle, 5);
            Assert.Equal(10d, transform.GetAnimationBaseValue(RotateTransform.AngleProperty));
            transform.SetValue(RotateTransform.AngleProperty, 42d);
            Assert.Equal(50d, transform.Angle, 5);
            AnimationEngineTests.RunInsideFrame(start + Ticks(.5), _ => transform.BeginAnimation(
                RotateTransform.AngleProperty, new DoubleAnimation { To = 200, Duration = TimeSpan.FromSeconds(1) }));
            storyboard.Stop();
            AnimationManager.ProcessFrame(start + Ticks(1));
            Assert.Equal(125d, transform.Angle, 5);
            Assert.Equal(0, completed);
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            Assert.Equal(42d, transform.Angle);
        }
        finally
        {
            storyboard.Stop();
            transform.BeginAnimation(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Theory]
    [InlineData("RenderTransform.Angle")]
    [InlineData("(UIElement.RenderTransform).(RotateTransform.Angle)")]
    [InlineData("(0).(1)")]
    [InlineData("RenderTransform.Children[0].Angle")]
    public void Storyboard_ResolvesNestedAndParameterizedPropertyPaths(string path)
    {
        var transform = new RotateTransform(10);
        var target = new Border
        {
            RenderTransform = path.Contains("Children") ? new TransformGroup(transform) : transform
        };
        var propertyPath = new PropertyPath(path, UIElement.RenderTransformProperty, RotateTransform.AngleProperty);
        var storyboard = CreateStoryboard(target, propertyPath);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(50d, transform.Angle, 5);
            storyboard.Stop();
            Assert.Equal(10d, transform.Angle);
        }
        finally
        {
            storyboard.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void Storyboard_FailedBeginDoesNotLeaveEarlierChildrenRunning()
    {
        var target = new Border { Opacity = .8 };
        var storyboard = CreateStoryboard(target, new PropertyPath(UIElement.OpacityProperty));
        var invalid = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));
        Storyboard.SetTarget(invalid, target);
        Storyboard.SetTargetProperty(invalid, new PropertyPath("MissingProperty"));
        storyboard.Children.Add(invalid);
        try
        {
            Assert.Throws<InvalidOperationException>(() => storyboard.Begin());
            Assert.False(target.HasAnimation(UIElement.OpacityProperty));
            Assert.Equal(.8, target.Opacity);
        }
        finally
        {
            storyboard.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    private static Storyboard CreateStoryboard(DependencyObject target, PropertyPath property)
    {
        var animation = new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }

    [Fact]
    public void InvalidAnimationRequests_LeaveTheTargetUnchanged()
    {
        var transform = new RotateTransform(10);
        Assert.Throws<ArgumentNullException>(() => transform.BeginAnimation(null!, new DoubleAnimation()));
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(), (HandoffBehavior)42));
        Assert.Throws<ArgumentException>(() => transform.BeginAnimation(RotateTransform.AngleProperty, new ColorAnimation()));
        transform.Freeze();
        Assert.Throws<InvalidOperationException>(() => transform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation()));
        Assert.Equal(10d, transform.Angle);
    }
}
