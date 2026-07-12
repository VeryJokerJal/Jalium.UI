using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class AnimationExpandedFamiliesWpfParityTests
{
    [Fact]
    public void NamedAnimationBasesExposeTheWpfBridgeShape()
    {
        Type[] baseTypes =
        [
            typeof(BooleanAnimationBase), typeof(ByteAnimationBase), typeof(CharAnimationBase),
            typeof(ColorAnimationBase), typeof(DecimalAnimationBase), typeof(DoubleAnimationBase),
            typeof(Int16AnimationBase), typeof(Int32AnimationBase), typeof(Int64AnimationBase),
            typeof(MatrixAnimationBase), typeof(ObjectAnimationBase), typeof(Point3DAnimationBase),
            typeof(PointAnimationBase), typeof(QuaternionAnimationBase), typeof(RectAnimationBase),
            typeof(Rotation3DAnimationBase), typeof(SingleAnimationBase), typeof(SizeAnimationBase),
            typeof(StringAnimationBase), typeof(ThicknessAnimationBase), typeof(Vector3DAnimationBase),
            typeof(VectorAnimationBase),
        ];

        foreach (Type type in baseTypes)
        {
            Assert.True(type.IsAbstract, type.FullName);
            Assert.Equal(typeof(AnimationTimeline), type.BaseType);
            Assert.True(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single().IsFamily);

            MethodInfo targetGetter = type.GetProperty(nameof(AnimationTimeline.TargetPropertyType))!.GetMethod!;
            MethodInfo objectBridge = type.GetMethod(
                nameof(AnimationTimeline.GetCurrentValue),
                [typeof(object), typeof(object), typeof(AnimationClock)])!;
            Assert.True(targetGetter.IsFinal);
            Assert.True(objectBridge.IsFinal);
        }

        Assert.Equal(typeof(ByteAnimationBase), typeof(ByteAnimation).BaseType);
        Assert.Equal(typeof(ByteAnimationBase), typeof(ByteAnimationUsingKeyFrames).BaseType);
        Assert.Equal(typeof(MatrixAnimationBase), typeof(MatrixAnimationUsingKeyFrames).BaseType);
        Assert.Equal(typeof(RectAnimationBase), typeof(RectAnimationUsingKeyFrames).BaseType);
        Assert.Equal(typeof(Vector3DAnimationBase), typeof(Vector3DAnimation).BaseType);

        Assert.Null(typeof(MatrixAnimationUsingKeyFrames).GetProperty(
            "IsAdditive",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.NotNull(typeof(RectAnimationUsingKeyFrames).GetProperty(
            "IsAdditive",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void KeyTimeAndSimpleAnimationSurfaceUseWpfValues()
    {
        Assert.Equal(0, (int)KeyTimeType.Uniform);
        Assert.Equal(1, (int)KeyTimeType.Paced);
        Assert.Equal(2, (int)KeyTimeType.TimeSpan);
        Assert.Equal(3, (int)KeyTimeType.Percent);

        var point = new PointAnimation(new Point(1, 2), new Point(3, 4), TimeSpan.FromSeconds(2), FillBehavior.Stop);
        var thickness = new ThicknessAnimation(new Thickness(1), new Thickness(4), TimeSpan.FromSeconds(2), FillBehavior.Stop);
        var integer = new Int32Animation(1, 9, TimeSpan.FromSeconds(2), FillBehavior.Stop);
        var vector = new VectorAnimation(new Vector(1, 2), new Vector(3, 4), TimeSpan.FromSeconds(2), FillBehavior.Stop);

        Assert.Equal(FillBehavior.Stop, point.FillBehavior);
        Assert.Equal(FillBehavior.Stop, thickness.FillBehavior);
        Assert.Equal(FillBehavior.Stop, integer.FillBehavior);
        Assert.Equal(FillBehavior.Stop, vector.FillBehavior);
        Assert.IsType<PointAnimation>(point.Clone());
        Assert.IsType<ThicknessAnimation>(thickness.Clone());
        Assert.IsType<Int32Animation>(integer.Clone());
        Assert.IsType<VectorAnimation>(vector.Clone());
    }

    [Fact]
    public void BooleanAndCharFamiliesAreTypedCloneableAndMarkupFriendly()
    {
        var booleans = new BooleanAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1) };
        ((IAddChild)booleans).AddChild(new DiscreteBooleanKeyFrame(true, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        var chars = new CharAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1) };
        ((IAddChild)chars).AddChild(new DiscreteCharKeyFrame('z', KeyTime.FromTimeSpan(TimeSpan.Zero)));

        Assert.IsType<BooleanKeyFrameCollection>(booleans.KeyFrames);
        Assert.IsType<CharKeyFrameCollection>(chars.KeyFrames);
        Assert.True(booleans.ShouldSerializeKeyFrames());
        Assert.True(chars.ShouldSerializeKeyFrames());
        Assert.True(Evaluate(booleans, false, false, 0.5));
        Assert.Equal('z', Evaluate(chars, 'a', 'a', 0.5));

        BooleanAnimationUsingKeyFrames booleanClone = booleans.Clone();
        CharAnimationUsingKeyFrames charClone = chars.Clone();
        Assert.NotSame(booleans.KeyFrames, booleanClone.KeyFrames);
        Assert.NotSame(chars.KeyFrames, charClone.KeyFrames);
        Assert.Throws<InvalidOperationException>(() => ((IAddChild)booleans).AddText("text"));
    }

    [Fact]
    public void PathAnimationsExposeDependencyPropertiesAndCloneTheirState()
    {
        var geometry = new PathGeometry();
        var doublePath = new DoubleAnimationUsingPath
        {
            PathGeometry = geometry,
            Source = PathAnimationSource.Y,
        };
        var matrixPath = new MatrixAnimationUsingPath
        {
            PathGeometry = geometry,
            DoesRotateWithTangent = true,
            IsAngleCumulative = true,
            IsOffsetCumulative = true,
        };

        Assert.Equal(typeof(PathGeometry), DoubleAnimationUsingPath.PathGeometryProperty.PropertyType);
        Assert.Equal(typeof(PathAnimationSource), DoubleAnimationUsingPath.SourceProperty.PropertyType);
        Assert.Equal(typeof(PathGeometry), PointAnimationUsingPath.PathGeometryProperty.PropertyType);
        Assert.Equal(typeof(bool), MatrixAnimationUsingPath.DoesRotateWithTangentProperty.PropertyType);

        DoubleAnimationUsingPath doubleClone = doublePath.Clone();
        MatrixAnimationUsingPath matrixClone = matrixPath.Clone();
        Assert.NotNull(doubleClone.PathGeometry);
        Assert.NotSame(geometry, doubleClone.PathGeometry);
        Assert.Equal(PathAnimationSource.Y, doubleClone.Source);
        Assert.True(matrixClone.DoesRotateWithTangent);
        Assert.True(matrixClone.IsAngleCumulative);
        Assert.True(matrixClone.IsOffsetCumulative);
    }

    [Fact]
    public void ScalarAndValueKeyFramesCoverDiscreteLinearSplineAndEasing()
    {
        Assert.Equal((byte)5, new LinearByteKeyFrame(10).InterpolateValue(0, 0.5));
        Assert.Equal(5m, new LinearDecimalKeyFrame(10m).InterpolateValue(0m, 0.5));
        Assert.Equal((short)5, new LinearInt16KeyFrame(10).InterpolateValue(0, 0.5));
        Assert.Equal(5, new LinearInt32KeyFrame(10).InterpolateValue(0, 0.5));
        Assert.Equal(5L, new LinearInt64KeyFrame(10).InterpolateValue(0, 0.5));
        Assert.Equal(5f, new LinearSingleKeyFrame(10).InterpolateValue(0, 0.5));

        var defaultSpline = new SplineByteKeyFrame();
        Assert.True(defaultSpline.KeySpline.IsFrozen);
        Assert.Throws<ArgumentNullException>(() => defaultSpline.KeySpline = null!);

        var spline = new KeySpline(0, 0, 1, 1);
        Assert.Equal(new Rect(5, 10, 20, 30),
            new SplineRectKeyFrame(new Rect(10, 20, 30, 40), KeyTime.Uniform, spline)
                .InterpolateValue(new Rect(0, 0, 10, 20), 0.5));
        Assert.Equal(new Size(5, 10),
            new EasingSizeKeyFrame(new Size(10, 20), KeyTime.Uniform, new LinearEase())
                .InterpolateValue(new Size(0, 0), 0.5));
        Assert.Equal(new Vector(5, 10),
            new LinearVectorKeyFrame(new Vector(10, 20)).InterpolateValue(default, 0.5));

        Matrix matrix = new(1, 2, 3, 4, 5, 6);
        var matrixFrame = new DiscreteMatrixKeyFrame(matrix);
        Assert.Equal(Matrix.Identity, matrixFrame.InterpolateValue(Matrix.Identity, 0.5));
        Assert.Equal(matrix, matrixFrame.InterpolateValue(Matrix.Identity, 1));
    }

    [Fact]
    public void ExpandedUsingKeyFramesSupportMarkupCloneFreezeAndNaturalDuration()
    {
        var animation = new Int32AnimationUsingKeyFrames();
        ((IAddChild)animation).AddChild(new LinearInt32KeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));

        Assert.True(animation.ShouldSerializeKeyFrames());
        Assert.Equal(TimeSpan.FromSeconds(2), GetNaturalDuration(animation).TimeSpan);

        Int32AnimationUsingKeyFrames clone = animation.Clone();
        Assert.NotSame(animation.KeyFrames, clone.KeyFrames);
        Assert.NotSame(animation.KeyFrames[0], clone.KeyFrames[0]);

        animation.Freeze();
        Assert.True(animation.KeyFrames.IsFrozen);
        Assert.True(animation.KeyFrames[0].IsFrozen);
    }

    [Fact]
    public void AnimationExceptionCarriesItsAnimationContext()
    {
        var target = new DoubleAnimation();
        var clock = new AnimationClock(target);
        var inner = new InvalidOperationException("inner");
        var exception = new AnimationException(clock, DoubleAnimation.FromProperty, target, "animation", inner);

        Assert.Same(clock, exception.Clock);
        Assert.Same(DoubleAnimation.FromProperty, exception.Property);
        Assert.Same(target, exception.Target);
        Assert.Same(inner, exception.InnerException);
    }

    private static Duration GetNaturalDuration(AnimationTimeline animation)
    {
        MethodInfo method = animation.GetType().GetMethod(
            "GetNaturalDurationCore",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        return (Duration)method.Invoke(animation, [animation.CreateClock()])!;
    }

    private static T Evaluate<T>(AnimationTimeline animation, T origin, T destination, double seconds)
    {
        const long start = 1_000_000_000;
        var clock = new AnimationClock(animation);
        clock.BeginAt(start);
        clock.Tick(start + (long)(seconds * Stopwatch.Frequency));
        return (T)animation.GetCurrentValue(origin!, destination!, clock);
    }
}
