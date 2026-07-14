using System.Globalization;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class AnimationKeyFrameCoreParityTests
{
    [Fact]
    public void LinearAndDiscreteKeyFramesHonorEndpointsAndMidpoint()
    {
        var linear = new LinearDoubleKeyFrame(20.0, KeyTime.FromPercent(0.5));

        Assert.Equal(10.0, linear.InterpolateValue(10.0, 0.0));
        Assert.Equal(15.0, linear.InterpolateValue(10.0, 0.5));
        Assert.Equal(20.0, linear.InterpolateValue(10.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => linear.InterpolateValue(10.0, -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => linear.InterpolateValue(10.0, 1.01));

        var boolean = new DiscreteBooleanKeyFrame(true, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1)));
        Assert.False(boolean.InterpolateValue(false, 0.999));
        Assert.True(boolean.InterpolateValue(false, 1.0));

        var character = new DiscreteCharKeyFrame('z');
        Assert.Equal('a', character.InterpolateValue('a', 0.5));
        Assert.Equal('z', character.InterpolateValue('a', 1.0));

        var text = new DiscreteStringKeyFrame("target");
        Assert.Equal("base", text.InterpolateValue("base", 0.5));
        Assert.Equal("target", text.InterpolateValue("base", 1.0));
    }

    [Fact]
    public void SplineKeyFrameUsesDependencyPropertyAndTracksControlPointChanges()
    {
        var spline = new KeySpline(0.25, 0.1, 0.25, 1.0);
        var keyFrame = new SplinePointKeyFrame(
            new Point(10.0, 20.0),
            KeyTime.FromPercent(0.75),
            spline);

        Assert.Same(spline, keyFrame.GetValue(SplinePointKeyFrame.KeySplineProperty));
        Assert.Equal(typeof(KeySpline), SplinePointKeyFrame.KeySplineProperty.PropertyType);

        Point firstMidpoint = keyFrame.InterpolateValue(new Point(0.0, 0.0), 0.5);
        spline.ControlPoint1 = new Point(0.8, 0.0);
        Point secondMidpoint = keyFrame.InterpolateValue(new Point(0.0, 0.0), 0.5);

        Assert.NotEqual(firstMidpoint, secondMidpoint);
        Assert.Equal(new Point(0.0, 0.0), keyFrame.InterpolateValue(new Point(0.0, 0.0), 0.0));
        Assert.Equal(new Point(10.0, 20.0), keyFrame.InterpolateValue(new Point(0.0, 0.0), 1.0));
    }

    [Fact]
    public void KeyFrameCloneAndFreezeIncludeValueKeyTimeAndNestedSpline()
    {
        var source = new SplineDoubleKeyFrame(
            42.0,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)),
            new KeySpline(0.2, 0.3, 0.7, 0.8));

        var clone = Assert.IsType<SplineDoubleKeyFrame>(source.Clone());
        Assert.Equal(source.Value, clone.Value);
        Assert.Equal(source.KeyTime, clone.KeyTime);
        Assert.NotSame(source.KeySpline, clone.KeySpline);
        Assert.Equal(source.KeySpline!.ControlPoint1, clone.KeySpline!.ControlPoint1);
        Assert.Equal(source.KeySpline.ControlPoint2, clone.KeySpline.ControlPoint2);

        source.Freeze();
        Assert.True(source.IsFrozen);
        Assert.True(source.KeySpline!.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => source.Value = 1.0);
        Assert.Throws<InvalidOperationException>(() => source.KeySpline = new KeySpline());
        Assert.Throws<InvalidOperationException>(() => source.KeySpline!.ControlPoint1 = new Point(0.1, 0.1));
    }

    [Fact]
    public void QuaternionKeyFramesRespectShortestPathDependencyProperties()
    {
        Quaternion from = Quaternion.Identity;
        Quaternion to = new(new Vector3D(0.0, 0.0, 1.0), 270.0);
        var keyFrame = new LinearQuaternionKeyFrame(to);

        Assert.Same(
            LinearQuaternionKeyFrame.UseShortestPathProperty,
            typeof(LinearQuaternionKeyFrame).GetField(
                nameof(LinearQuaternionKeyFrame.UseShortestPathProperty),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.GetValue(null));
        Assert.True(keyFrame.UseShortestPath);

        Quaternion shortestMidpoint = keyFrame.InterpolateValue(from, 0.5);
        keyFrame.UseShortestPath = false;
        Quaternion longMidpoint = keyFrame.InterpolateValue(from, 0.5);

        Assert.True(shortestMidpoint.Z < 0.0);
        Assert.True(longMidpoint.Z > 0.0);
        Assert.Equal(to, keyFrame.InterpolateValue(from, 1.0));
    }

    [Fact]
    public void KeySplineAndRepeatBehaviorExposeWpfFormattingContracts()
    {
        var spline = new KeySpline(0.25, 0.5, 0.75, 1.0);

        Assert.IsAssignableFrom<IFormattable>(spline);
        Assert.Equal("0.25,0.5,0.75,1", spline.ToString(CultureInfo.InvariantCulture));
        Assert.Contains(';', spline.ToString(CultureInfo.GetCultureInfo("de-DE")));
        Assert.Throws<ArgumentException>(() => spline.ControlPoint1 = new Point(-0.01, 0.0));
        Assert.Throws<ArgumentException>(() => spline.ControlPoint2 = new Point(1.01, 0.0));

        RepeatBehavior count = new(2.5);
        Assert.True(RepeatBehavior.Equals(count, new RepeatBehavior(2.5)));
        Assert.False(RepeatBehavior.Equals(count, new RepeatBehavior(TimeSpan.FromSeconds(2.5))));
        Assert.Equal("2.5x", count.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("Forever", RepeatBehavior.Forever.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConcreteKeyFramesDeclareWpfCoreOverrides()
    {
        AssertCoreOverrides<DiscreteBooleanKeyFrame, bool>();
        AssertCoreOverrides<DiscreteColorKeyFrame, Color>();
        AssertCoreOverrides<LinearDoubleKeyFrame, double>();
        AssertCoreOverrides<SplinePointKeyFrame, Point>();
        AssertCoreOverrides<SplineQuaternionKeyFrame, Quaternion>();
        AssertCoreOverrides<SplineRotation3DKeyFrame, Rotation3D>();
        AssertCoreOverrides<SplineVector3DKeyFrame, Vector3D>();
    }

    private static void AssertCoreOverrides<TKeyFrame, TValue>()
    {
        const BindingFlags declaredNonPublic = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        MethodInfo? create = typeof(TKeyFrame).GetMethod("CreateInstanceCore", declaredNonPublic);
        MethodInfo? interpolate = typeof(TKeyFrame).GetMethod(
            "InterpolateValueCore",
            declaredNonPublic,
            null,
            [typeof(TValue), typeof(double)],
            null);

        Assert.NotNull(create);
        Assert.Equal(typeof(Freezable), create!.ReturnType);
        Assert.NotNull(interpolate);
        Assert.Equal(typeof(TValue), interpolate!.ReturnType);
    }
}
