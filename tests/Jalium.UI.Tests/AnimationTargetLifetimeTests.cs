using System.Diagnostics;
using System.Runtime.CompilerServices;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationTargetLifetimeTests
{
    [Fact]
    public void FrozenBrushPath_ClonesTheResourceAndPreservesItsBinding()
    {
        var shared = new SolidColorBrush(Colors.Black);
        shared.Freeze();
        var source = new Border { Background = shared };
        var target = new Border();
        var binding = target.SetBinding(Border.BackgroundProperty, new Binding(nameof(Border.Background)) { Source = source });
        var animation = new ColorAnimation { From = Colors.Black, To = Colors.White, Duration = TimeSpan.FromSeconds(1) };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath("Background.Color"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            var animated = Assert.IsType<SolidColorBrush>(target.Background);
            Assert.NotSame(shared, animated);
            Assert.InRange(animated.Color.R, (byte)127, (byte)128);
            Assert.Equal(Colors.Black, shared.Color);
            Assert.Same(binding, target.GetBindingExpression(Border.BackgroundProperty));
            storyboard.Stop();
            source.Background = Brushes.Red;
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(target.Background).Color);
            Assert.Same(binding, target.GetBindingExpression(Border.BackgroundProperty));
        }
        finally
        {
            storyboard.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void ReplacingAPlainDependencyObjectStoryboard_StopsThePreviousWriter()
    {
        var target = new PlainTarget();
        target.SetValue(PlainTarget.ValueProperty, 10d);
        var first = Create(target, 0, 100);
        var second = Create(target, 25, 200);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => first.Begin());
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.Equal(25d, target.GetValue(PlainTarget.ValueProperty));
            AnimationEngineTests.RunInsideFrame(start + Stopwatch.Frequency / 4, _ => second.Begin());
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Equal(68.75d, target.GetValue(PlainTarget.ValueProperty));
            first.Stop();
            Assert.Equal(68.75d, target.GetValue(PlainTarget.ValueProperty));
            target.SetValue(PlainTarget.ValueProperty, 42d);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency * 3 / 4);
            Assert.Equal(112.5d, target.GetValue(PlainTarget.ValueProperty));
            second.Stop();
            Assert.Equal(42d, target.GetValue(PlainTarget.ValueProperty));
        }
        finally
        {
            first.Stop();
            second.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("element")]
    [InlineData("animatable")]
    public void FillStopRestoresTheLatestBaseValueWhileTheParentContinues(string targetKind)
    {
        DependencyObject target = targetKind == "plain" ? new PlainTarget() :
            targetKind == "element" ? new Border() : new TranslateTransform();
        var property = targetKind == "plain" ? PlainTarget.ValueProperty :
            targetKind == "element" ? FrameworkElement.WidthProperty : TranslateTransform.XProperty;
        target.SetValue(property, 10d);
        Storyboard CreateForTarget(double from, double to)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(1));
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            return storyboard;
        }
        var first = CreateForTarget(0, 100);
        var second = CreateForTarget(25, 200);
        second.Children[0].FillBehavior = FillBehavior.Stop;
        second.Duration = TimeSpan.FromSeconds(3);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => first.Begin());
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            AnimationEngineTests.RunInsideFrame(start + Stopwatch.Frequency / 4, _ => second.Begin());
            target.SetValue(property, 42d);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency * 3 / 2);
            Assert.Equal(42d, target.GetValue(property));
        }
        finally
        {
            first.Stop();
            second.Stop();
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void AnimatedRotationChild_NotifiesTheThreeDimensionalTransformGroup()
    {
        var rotation = new QuaternionRotation3D();
        var transform = new RotateTransform3D { Rotation = rotation };
        var group = new Transform3DGroup();
        group.Children.Add(transform);
        int changes = 0;
        group.Changed += (_, _) => changes++;
        var animation = new QuaternionAnimation
        {
            From = Quaternion.Identity,
            To = new Quaternion(new Vector3D(0, 0, 1), 90),
            Duration = TimeSpan.FromSeconds(1)
        };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => rotation.BeginAnimation(QuaternionRotation3D.QuaternionProperty, animation));
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.True(changes > 0);
            Assert.Equal(Math.Sqrt(.5), group.Value.M11, 6);
        }
        finally
        {
            rotation.BeginAnimation(QuaternionRotation3D.QuaternionProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void StoppingAStoryboard_ReleasesItWithoutWaitingForAnotherFrame()
    {
        var reference = CreateStoppedStoryboard();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateStoppedStoryboard()
    {
        var target = new PlainTarget();
        var storyboard = Create(target, 0, 100);
        storyboard.RepeatBehavior = RepeatBehavior.Forever;
        storyboard.Begin();
        storyboard.Stop();
        return new WeakReference(storyboard);
    }

    private static Storyboard Create(PlainTarget target, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(1));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(PlainTarget.ValueProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }

    private sealed class PlainTarget : DependencyObject
    {
        internal static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(PlainTarget), new PropertyMetadata(0d));
    }
}
