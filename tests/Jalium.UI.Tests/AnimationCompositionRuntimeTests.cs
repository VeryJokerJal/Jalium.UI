using System.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class AnimationCompositionRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColorComposition_HonorsAdditiveAndCumulativeValues(bool keyFrames)
    {
        var endpoint = Color.FromScRgb(.25f, .2f, .1f, 0f);
        AnimationTimeline animation;
        if (keyFrames)
        {
            var frames = new ColorAnimationUsingKeyFrames { IsAdditive = true, IsCumulative = true };
            frames.KeyFrames.Add(new DiscreteColorKeyFrame(endpoint, KeyTime.FromPercent(1)));
            animation = frames;
        }
        else
        {
            animation = new ColorAnimation { From = Color.FromScRgb(0, 0, 0, 0), To = endpoint, IsAdditive = true, IsCumulative = true };
        }
        var origin = Color.FromScRgb(.1f, .1f, .1f, .1f);
        var value = (Color)Sample(animation, 2).GetCurrentValue(origin, origin);
        Assert.Equal(.6f, value.ScA, 5);
        Assert.Equal(.5f, value.ScR, 5);
        Assert.Equal(.3f, value.ScG, 5);
        Assert.Equal(.1f, value.ScB, 5);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RotationComposition_HonorsAdditiveAndCumulativeValues(bool keyFrames, bool rotationObject)
    {
        var axis = new Vector3D(0, 0, 1);
        var origin = new Quaternion(axis, 15);
        var endpoint = new Quaternion(axis, 30);
        AnimationTimeline animation;
        object baseValue;
        if (rotationObject)
        {
            baseValue = new QuaternionRotation3D(origin);
            if (keyFrames)
            {
                var frames = new Rotation3DAnimationUsingKeyFrames { IsAdditive = true, IsCumulative = true };
                frames.KeyFrames.Add(new LinearRotation3DKeyFrame(new QuaternionRotation3D(endpoint), KeyTime.FromPercent(1)));
                animation = frames;
            }
            else
                animation = new Rotation3DAnimation { From = Rotation3D.Identity, To = new QuaternionRotation3D(endpoint), IsAdditive = true, IsCumulative = true };
        }
        else
        {
            baseValue = origin;
            if (keyFrames)
            {
                var frames = new QuaternionAnimationUsingKeyFrames { IsAdditive = true, IsCumulative = true };
                frames.KeyFrames.Add(new LinearQuaternionKeyFrame(endpoint, KeyTime.FromPercent(1)));
                animation = frames;
            }
            else
                animation = new QuaternionAnimation { From = Quaternion.Identity, To = endpoint, IsAdditive = true, IsCumulative = true };
        }
        object value = Sample(animation, 1.5).GetCurrentValue(baseValue, baseValue);
        var actual = value is Rotation3D rotation ? Rotation3DAnimation.ToQuaternion(rotation) : (Quaternion)value;
        Assert.Equal(60d, actual.Angle, 5);
        Assert.Equal(1d, actual.Axis.Z, 5);
    }

    [Fact]
    public void RectComposition_HonorsAdditiveAndCumulativeValues()
    {
        var animation = new RectAnimation { From = new Rect(), To = new Rect(10, 20, 30, 40), IsAdditive = true, IsCumulative = true };
        var origin = new Rect(1, 2, 3, 4);
        Assert.Equal(new Rect(16, 32, 48, 64), Sample(animation, 1.5).GetCurrentValue(origin, origin));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Size3DComposition_HonorsAdditiveAndCumulativeValues(bool keyFrames)
    {
        AnimationTimeline animation;
        if (keyFrames)
        {
            var frames = new Size3DAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new LinearSize3DKeyFrame(new Size3D(10, 20, 30), KeyTime.FromPercent(1)));
            animation = frames;
        }
        else
            animation = new Size3DAnimation { From = new Size3D(), To = new Size3D(10, 20, 30) };
        animation.SetValue(AnimationTimeline.IsAdditiveProperty, true);
        animation.SetValue(AnimationTimeline.IsCumulativeProperty, true);
        var origin = new Size3D(1, 2, 3);
        Assert.Equal(new Size3D(16, 32, 48), Sample(animation, 1.5).GetCurrentValue(origin, origin));
    }

    [Fact]
    public void ReusingAPathAnimation_RecalculatesItsCumulativeDistance()
    {
        var animation = new DoubleAnimationUsingPath { PathGeometry = Path(100), IsCumulative = true };
        var clock = Sample(animation, 1.5);
        Assert.Equal(150d, clock.GetCurrentValue(0d, 0d));
        animation.PathGeometry = Path(200);
        Assert.Equal(300d, clock.GetCurrentValue(0d, 0d));
    }

    [Fact]
    public void EditingATimeline_NotifiesItsParentAndDetachesReplacedChildren()
    {
        var animation = new DoubleAnimationUsingPath { PathGeometry = Path(100) };
        var group = new ParallelTimeline();
        group.Children.Add(animation);
        int changes = 0;
        group.Changed += (_, _) => changes++;
        animation.Duration = TimeSpan.FromSeconds(2);
        Assert.Equal(1, changes);
        var previous = animation.PathGeometry!;
        animation.PathGeometry = Path(200);
        Assert.Equal(2, changes);
        previous.Figures.Add(new PathFigure());
        Assert.Equal(2, changes);
        animation.PathGeometry.Figures.Add(new PathFigure());
        Assert.Equal(3, changes);
    }

    private static PathGeometry Path(double length)
    {
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point() };
        figure.Segments.Add(new LineSegment(new Point(length, 0)));
        path.Figures.Add(figure);
        return path;
    }

    private static AnimationClock Sample(AnimationTimeline animation, double seconds)
    {
        animation.Duration = TimeSpan.FromSeconds(1);
        animation.RepeatBehavior = new RepeatBehavior(2);
        var clock = animation.CreateClock();
        const long start = 1_000_000_000;
        clock.BeginAt(start);
        clock.Tick(start + (long)(seconds * Stopwatch.Frequency));
        return clock;
    }
}
